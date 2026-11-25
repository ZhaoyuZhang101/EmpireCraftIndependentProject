#!/usr/bin/env python3
"""Port bookkeeping between the EmpireCraft WorldBox mod and this repository.

Every ported file is copied from a *committed* mod revision, run through a
deterministic text transform (namespace, using lines, engine aliases) and
recorded in Port/manifest.json. The body is kept line-for-line so later mod
changes can be merged in three-way:

  base   = transform(mod file at the recorded commit)
  theirs = transform(mod file at the new commit)
  ours   = the ported file with whatever was edited by hand here

Commands (run from the repository root):
  python Tools/PortTool/port_tool.py add     <mod paths...> [--dest Rules] [--note text] [--force]
  python Tools/PortTool/port_tool.py extract <mod path> <Type[,Outer.Inner...]> [--dest Rules] [--note text]
  python Tools/PortTool/port_tool.py data    <mod paths...> [--dest sub-folder] [--force]
  python Tools/PortTool/port_tool.py remove  <manifest keys...>
  python Tools/PortTool/port_tool.py note    <manifest key> "what was changed by hand"
  python Tools/PortTool/port_tool.py status
  python Tools/PortTool/port_tool.py diff    <manifest key>
  python Tools/PortTool/port_tool.py sync    [<manifest keys...>] [--all]

`extract` lifts single declarations into a file of their own: a type (an enum
that lives inside a large system file, say) or, written Outer.Member, a nested
type, constant or pure function of a class, wrapped in a partial declaration of
that class. A rule that only needs those does not have to wait for the whole
system. Its manifest key is
"<mod path>#<types>". Drop the entry once the owning file is ported whole.

The mod repository is found through --mod or the EMPIRECRAFT_MOD_REPO
environment variable. Nothing is ever written to the mod repository.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MANIFEST = os.path.join(ROOT, "Port", "manifest.json")
CORE = "Assets/EmpireCraft/Core"
DATA = "Assets/EmpireCraft/Data"
NAMESPACE = "EmpireCraft.Core"

# Engine and loader names the mod uses, mapped onto the host layer in
# Assets/EmpireCraft/Core/Host by a using alias so the body stays untouched.
ALIASES = [
    (r"\bMathf\.", "Mathf", "EmpireCraft.Core.EcMath"),
    (r"\bTime\.", "Time", "EmpireCraft.Core.FrameClock"),
    (r"\bColor\b", "Color", "EmpireCraft.Core.EcColor"),
    (r"\bColorUtility\.", "ColorUtility", "EmpireCraft.Core.EcColorUtility"),
]
# Fixed text swaps applied to every line (the same on both sides of a sync).
REWRITES = [
    ("ModClass._declare.FolderPath", "GameData.Folder"),
    ("NeoModLoader.General.LM.", "LM."),
    ("NeoModLoader.services.LogService.", "LogService."),
    ('PlayerConfig.dict["language"].stringVal', "LM.Language"),
    ("Config.time_scale_asset?.multiplier ?? 1f", "FrameClock.SpeedFactor"),
    ("UnityEngine.Random.", "EcRandom."),
    ("UnityEngine.Mathf.", "Mathf."),
    ("UnityEngine.Time.", "Time."),
]
# Identity rewrite (opt-in per file, `--identity`): the WorldBox polity objects become this
# repository's entities, one identifier for one identifier so the line structure survives.
# The target members live on the entities (Core/Entities) and on GameClock (Core/Host).
# Only names WorldBox alone uses are rewritten as text. Plain member names (id, data, king,
# capital, cities ...) are renamed by the type of their receiver instead, see semantic_fix;
# `--keep` remains for the odd case text rules still get wrong.
IDENTITY = [
    # A few mod files declare a helper type of their own called Realm; it gives way to the entity.
    (r"(?<![.\w])Realm\b", "ModRealm"),
    (r"\bWorld\.world\.getCurWorldTime\(\)", "GameClock.Now"),
    (r"\bWorld\.world\b", "EmpireWorld.Current"),
    (r"(?<![.\w])Kingdom\b", "Realm"),
    (r"(?<![.\w])Actor\b", "Person"),
    (r"(?<![.\w])MapBox\b", "EmpireWorld"),
    # The mod's own layer objects derive from these; see Core/Entities/Records.cs.
    (r"(?<![.\w])MetaObject<", "RecordObject<"),
    (r"(?<![.\w])MetaObjectData\b", "RecordData"),
    (r"(?<![.\w])MetaSystemManager<", "RecordStore<"),
    (r"(?<![.\w])NanoObject\b", "Entity"),
    (r"\bEmpireCraft\.Core\.StackGuard\b", "StackGuard"),
    (r"\.isRekt\(\)", ".IsGone()"),
    (r"\.isAlive\(\)", ".Alive"),
    (r"\.hasKing\(\)", ".HasRuler()"),
    (r"\.hasCapital\(\)", ".HasCapital()"),
    (r"\.getID\(\)", ".Key"),
    (r"\bDate\.getMonthsSince\b", "GameClock.MonthsSince"),
    (r"\bDate\.getYearsSince\b", "GameClock.YearsSince"),
    (r"\bDate\.getCurrentYear\(\)", "GameClock.Year"),
    (r"\bDate\.getCurrentMonth\(\)", "GameClock.Month"),
]
IDENTITY = [(re.compile(pattern), replacement) for pattern, replacement in IDENTITY]
DROPPED_USINGS = ("UnityEngine", "NeoModLoader", "HarmonyLib", "ai", "EpPathFinding")


def git(mod, *args, check=True):
    result = subprocess.run(["git", "-C", mod] + list(args), capture_output=True)
    if check and result.returncode != 0:
        raise SystemExit("git %s failed: %s" % (" ".join(args), result.stderr.decode("utf-8", "replace")))
    return result


def mod_repo(args):
    mod = args.mod or os.environ.get("EMPIRECRAFT_MOD_REPO")
    if not mod or not os.path.exists(os.path.join(mod, ".git")):
        raise SystemExit("Pass --mod <path to the EmpireCraft mod repository> or set EMPIRECRAFT_MOD_REPO.")
    return mod


def show(mod, commit, path):
    data = git(mod, "show", "%s:%s" % (commit, path)).stdout
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return data.decode("utf-8").replace("\r\n", "\n").replace("\r", "\n")


_namespace_cache = {}


def mod_namespaces(mod, commit):
    if commit not in _namespace_cache:
        out = git(mod, "grep", "-h", "-E", r"^\s*namespace\s+EmpireCraft\.Scripts", commit, "--", "Scripts").stdout.decode("utf-8", "replace")
        names = {"EmpireCraft.Scripts"}
        for line in out.splitlines():
            m = re.match(r"\s*namespace\s+([\w.]+)", line)
            if m:
                names.add(m.group(1))
        _namespace_cache[commit] = sorted(names, key=len, reverse=True)
    return _namespace_cache[commit]


# ---- type extraction -------------------------------------------------------

def _matching_brace(text, start):
    """Index just past the brace block opening at or after start, skipping comments and strings."""
    i, depth, n = start, 0, len(text)
    while i < n:
        c = text[i]
        two = text[i:i + 2]
        if two == "//":
            i = text.find("\n", i)
            if i < 0:
                break
            continue
        if two == "/*":
            i = text.find("*/", i) + 2
            continue
        if c == '"' or c == "'":
            verbatim = c == '"' and i > 0 and text[i - 1] == "@"
            i += 1
            while i < n:
                if verbatim and text[i:i + 2] == '""':
                    i += 2
                    continue
                if not verbatim and text[i] == "\\":
                    i += 2
                    continue
                if text[i] == c:
                    break
                i += 1
            i += 1
            continue
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    raise SystemExit("Unbalanced braces while extracting a type.")


def _declaration(text, name, lo, hi):
    """(start, header end, block end) of the declaration of `name` inside text[lo:hi]."""
    pattern = re.compile(r"^[ \t]*(?:public|internal)[\w \t]*\b(?:enum|class|struct|interface)[ \t]+%s\b[^\n{]*" % re.escape(name), re.M)
    m = pattern.search(text, lo, hi)
    if not m:
        raise SystemExit("Type %s not found." % name)
    start = m.start()
    # Take the attribute and comment lines that sit directly above the declaration.
    while start > lo:
        previous = text.rfind("\n", lo, start - 1) + 1
        line = text[previous:start].strip()
        if line.startswith("[") or line.startswith("//"):
            start = previous
        else:
            break
    return start, m.end(), _matching_brace(text, m.end())


def _member(text, name, lo, hi):
    """(start, end) of a field, constant, property or method named `name` declared inside text[lo:hi]."""
    pattern = re.compile(r"^[ \t]*(?:public|internal|private|protected)[^\n;{=(]*\b%s\b[ \t]*(?=[(={;,])" % re.escape(name), re.M)
    m = pattern.search(text, lo, hi)
    if not m:
        raise SystemExit("Member %s not found." % name)
    start = m.start()
    while start > lo:
        previous = text.rfind("\n", lo, start - 1) + 1
        line = text[previous:start].strip()
        if line.startswith("[") or line.startswith("//"):
            start = previous
        else:
            break
    i, depth, assigned, n = m.end(), 0, False, hi
    while i < n:
        c = text[i]
        two = text[i:i + 2]
        if two == "//":
            i = text.find("\n", i)
            continue
        if two == "/*":
            i = text.find("*/", i) + 2
            continue
        if c == '"' or c == "'":
            i += 1
            while i < n and text[i] != c:
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if c == "}" and depth == 0 and not assigned:
                return start, i + 1
        elif c == "=" and depth == 0:
            assigned = True
        elif c == ";" and depth == 0:
            return start, i + 1
        i += 1
    raise SystemExit("Could not find the end of member %s." % name)


def extract_source(text, types):
    """A stand-alone pseudo source file holding only the named type declarations."""
    namespace = re.search(r"^namespace\s+[\w.]+\s*;?\s*$", text, re.M)
    parts = ["using System;", "using System.Collections.Generic;", "", namespace.group(0).strip() if namespace else "namespace EmpireCraft.Scripts;", ""]
    for name in types:
        if "." in name:
            outer, inner = name.split(".", 1)
            _, header_end, block_end = _declaration(text, outer, 0, len(text))
            header = text[text.rfind("\n", 0, header_end) + 1:header_end].strip()
            if " partial " not in " " + header + " ":
                header = re.sub(r"\b(class|struct)\b", r"partial \1", header, count=1)
            type_pattern = re.compile(r"\b(?:enum|class|struct|interface)[ \t]+%s\b" % re.escape(inner))
            if type_pattern.search(text, header_end, block_end):
                start, _, end = _declaration(text, inner, header_end, block_end)
            else:
                start, end = _member(text, inner, header_end, block_end)
            parts += [header, "{", text[start:end].rstrip(), "}", ""]
        else:
            start, _, end = _declaration(text, name, 0, len(text))
            parts += [text[start:end].rstrip(), ""]
    return "\n".join(parts)


def source_of(mod, commit, key):
    """Mod source text for a manifest key (a path, or path#Type,Type for an extract)."""
    path, _, types = key.partition("#")
    text = show(mod, commit, path)
    return extract_source(text, types.split(",")) if types else text


# ---- transform -------------------------------------------------------------

def transform(text, key, commit, namespaces, identity=False, keep=()):
    """Deterministic rewrite of one mod source file. Returns (text, change list)."""
    changes = []
    lines = text.split("\n")
    while lines and lines[-1] == "":
        lines.pop()
    out = []
    file_scoped = False
    dropped = set()
    rewritten = set()
    qualified = re.compile("|".join(re.escape(n) + r"\." for n in namespaces))
    # `Layer.Empire`, `Regimes.Regime`: a namespace segment used as a relative qualifier.
    segments = sorted({s for n in namespaces for s in n.split(".")[2:] if re.match(r"^[A-Za-z]\w*$", s)} - {"System"})
    relative = re.compile(r"(?<![.\w])(?:%s)\.(?=[A-Z])" % "|".join(segments or ["__none__"]))
    for line in lines:
        m = re.match(r"^using\s+(static\s+)?([\w.]+)\s*;\s*$", line)
        if m:
            name = m.group(2)
            if name.startswith("EmpireCraft.Scripts") and not m.group(1):
                continue
            if any(name == d or name.startswith(d + ".") for d in DROPPED_USINGS):
                dropped.add(name.split(".")[0])
                continue
        if re.match(r"^namespace\s+[\w.]+\s*;\s*$", line):
            out.append("namespace " + NAMESPACE)
            out.append("{")
            file_scoped = True
            continue
        if re.match(r"^namespace\s+[\w.]+\s*$", line):
            out.append("namespace " + NAMESPACE)
            continue
        if re.match(r"^namespace\s+[\w.]+\s*\{", line):
            out.append(re.sub(r"^namespace\s+[\w.]+", "namespace " + NAMESPACE, line))
            continue
        line = qualified.sub(NAMESPACE + ".", line)
        for old, new in REWRITES:
            if old in line:
                line = line.replace(old, new)
                rewritten.add("%s -> %s" % (old, new))
        if identity and not line.startswith("using "):
            line = relative.sub("", line)
            for index, kept in enumerate(keep):
                line = line.replace(kept, "\x00%d\x00" % index)
            for pattern, replacement in IDENTITY:
                line = pattern.sub(replacement, line)
            for index, kept in enumerate(keep):
                line = line.replace("\x00%d\x00" % index, kept)
        out.append(line)
    if not any(line.startswith("namespace ") for line in out):
        # A file in the global namespace: open the core namespace after its using lines.
        last_using = max([i for i, line in enumerate(out) if line.startswith("using ")] or [-1])
        out[last_using + 1:last_using + 1] = ["namespace " + NAMESPACE, "{"]
        out.append("}")
        changes.append("global namespace wrapped into " + NAMESPACE)
    elif file_scoped:
        out.append("}")
        changes.append("file-scoped namespace opened as a block (body not re-indented)")
    changes.append("namespace " + NAMESPACE)
    if identity:
        changes.append("identity rewrite (Kingdom -> Realm, Actor -> Person, members and clock)")
    body = "\n".join(out)
    aliases = []
    for pattern, alias, target in ALIASES:
        if re.search(pattern, body):
            aliases.append("using %s = %s;" % (alias, target))
            changes.append("%s -> %s (using alias)" % (alias, target))
    for name in sorted(dropped):
        changes.append("dropped using " + name)
    changes += sorted(rewritten)
    header = ["// Ported from the EmpireCraft mod: %s @ %s. Body kept line-for-line for three-way sync;" % (key, commit[:8]),
              "// see Port/manifest.json and Tools/PortTool."]
    return "\n".join(header + aliases + out) + "\n", changes


MEMBERS = os.path.join(ROOT, "Port", "identity-members.json")


def semantic_fix(sources, core):
    """Rename members by the type of their receiver (Port/identity-members.json), in place, in every file below `sources`.

    The second half of the identity rewrite: a plain member name such as `id` or `data` cannot be
    renamed by text, since mod classes use the same names. Validation/PortSurvey binds each access
    against `core` and renames only those asked of an entity that do not resolve.
    """
    orbis = os.path.join(ROOT, "Assets", "ORBIS", "Runtime", "FlatWorld", "Core")
    result = subprocess.run(["dotnet", "run", "--project", os.path.join(ROOT, "Validation", "PortSurvey"), "-c", "Release", "--",
                             "fix", sources, MEMBERS, core, orbis], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise SystemExit(result.stdout[-2000:] + result.stderr[-4000:])
    return result.stdout.strip().splitlines()[-1]


# ---- manifest --------------------------------------------------------------

def load_manifest():
    with open(MANIFEST, encoding="utf-8") as f:
        return json.load(f)


def save_manifest(manifest):
    manifest["files"].sort(key=lambda e: e["mod"])
    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write("\n")


def find(manifest, key):
    key = key.replace("\\", "/")
    for entry in manifest["files"]:
        if entry["mod"] == key:
            return entry
    return None


def write(dest, text):
    full = os.path.join(ROOT, dest)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def read(dest):
    with open(os.path.join(ROOT, dest), encoding="utf-8") as f:
        return f.read().replace("\r\n", "\n")


def head(mod):
    return git(mod, "rev-parse", "HEAD").stdout.decode().strip()


def record_source(manifest, mod, commit, key, dest, kind, note, force, identity=None, keep=None):
    entry = find(manifest, key)
    if entry and not force:
        print("skip (already ported, use --force to regenerate): " + key)
        return
    if entry:
        dest = entry["dest"]
    if identity is None:
        identity = bool(entry and entry.get("identity"))
    if keep is None:
        keep = entry.get("keep", []) if entry else []
    source = source_of(mod, commit, key)
    text, changes = transform(source, key, commit, mod_namespaces(mod, commit), identity, keep)
    write(dest, text)
    record = {"mod": key, "commit": commit, "ported": datetime.date.today().isoformat(), "dest": dest,
              "lines": source.count("\n"), "sha256": hashlib.sha256(source.encode("utf-8")).hexdigest(),
              "kind": kind, "changes": changes}
    if identity:
        record["identity"] = True
    if keep:
        record["keep"] = list(keep)
    if note:
        record["notes"] = [note]
    elif entry and entry.get("notes"):
        record["notes"] = entry["notes"]
    if entry:
        manifest["files"].remove(entry)
    manifest["files"].append(record)
    print("ported %s -> %s (%d lines)" % (key, dest, record["lines"]))


def cmd_add(args):
    mod = mod_repo(args)
    commit = git(mod, "rev-parse", args.commit).stdout.decode().strip()
    manifest = load_manifest()
    for path in args.paths:
        path = path.replace("\\", "/")
        dest = "/".join((CORE, args.dest or "Rules", os.path.basename(path)))
        record_source(manifest, mod, commit, path, dest, "source", args.note, args.force, args.identity or None, args.keep)
    save_manifest(manifest)


def cmd_extract(args):
    mod = mod_repo(args)
    commit = git(mod, "rev-parse", args.commit).stdout.decode().strip()
    manifest = load_manifest()
    key = args.path.replace("\\", "/") + "#" + args.types
    dest = "/".join((CORE, args.dest or "Rules", args.types.split(",")[0] + ".cs"))
    record_source(manifest, mod, commit, key, dest, "extract", args.note, args.force, args.identity or None, args.keep)
    save_manifest(manifest)


def cmd_data(args):
    mod = mod_repo(args)
    commit = git(mod, "rev-parse", args.commit).stdout.decode().strip()
    manifest = load_manifest()
    for path in args.paths:
        path = path.replace("\\", "/")
        entry = find(manifest, path)
        if entry and not args.force:
            print("skip (already ported, use --force to regenerate): " + path)
            continue
        # Keep the mod's own folder layout below Data/ unless told otherwise.
        sub = args.dest if args.dest is not None else os.path.dirname(path)
        dest = entry["dest"] if entry else "/".join(p for p in (DATA, sub, os.path.basename(path)) if p)
        raw = git(mod, "show", "%s:%s" % (commit, path)).stdout
        full = os.path.join(ROOT, dest)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "wb") as f:
            f.write(raw)
        if entry:
            manifest["files"].remove(entry)
        manifest["files"].append({"mod": path, "commit": commit, "ported": datetime.date.today().isoformat(), "dest": dest,
                                  "lines": raw.count(b"\n"), "sha256": hashlib.sha256(raw).hexdigest(), "kind": "data",
                                  "changes": ["copied unchanged"]})
        print("copied %s -> %s" % (path, dest))
    save_manifest(manifest)


def cmd_remove(args):
    manifest = load_manifest()
    for key in args.paths:
        entry = find(manifest, key)
        if not entry:
            print("not in the manifest: " + key)
            continue
        for name in (entry["dest"], entry["dest"] + ".meta"):
            full = os.path.join(ROOT, name)
            if os.path.exists(full):
                os.remove(full)
        manifest["files"].remove(entry)
        print("removed " + entry["dest"])
    save_manifest(manifest)


def cmd_note(args):
    manifest = load_manifest()
    entry = find(manifest, args.path)
    if not entry:
        raise SystemExit("Not in the manifest: " + args.path)
    notes = entry.setdefault("notes", [])
    if args.text not in notes:
        notes.append(args.text)
    save_manifest(manifest)


def changed_since(mod, entry, now):
    path = entry["mod"].partition("#")[0]
    if git(mod, "diff", "--quiet", entry["commit"], now, "--", path, check=False).returncode == 0:
        return False
    if entry.get("kind") != "extract":
        return True
    return source_of(mod, entry["commit"], entry["mod"]) != source_of(mod, now, entry["mod"])


def cmd_status(args):
    mod = mod_repo(args)
    manifest = load_manifest()
    now = head(mod)
    behind = lines = 0
    for entry in manifest["files"]:
        if entry.get("kind") != "data":
            lines += entry["lines"]
        path = entry["mod"].partition("#")[0]
        changed = changed_since(mod, entry, now)
        dirty = git(mod, "status", "--porcelain", "--", path).stdout.strip() != b""
        if changed:
            behind += 1
            stat = git(mod, "diff", "--shortstat", entry["commit"], now, "--", path).stdout.decode().strip()
            print("BEHIND %s  %s" % (entry["mod"], stat))
        elif dirty:
            print("note   %s  has uncommitted edits in the mod working tree" % entry["mod"])
    sources = sum(1 for e in manifest["files"] if e.get("kind") != "data")
    print("%d entries (%d source files, %d source lines, %d data files), %d behind mod HEAD %s" %
          (len(manifest["files"]), sources, lines, len(manifest["files"]) - sources, behind, now[:8]))


def cmd_diff(args):
    mod = mod_repo(args)
    entry = find(load_manifest(), args.path)
    if not entry:
        raise SystemExit("Not in the manifest: " + args.path)
    sys.stdout.buffer.write(git(mod, "diff", entry["commit"], head(mod), "--", entry["mod"].partition("#")[0]).stdout)


def cmd_sync(args):
    mod = mod_repo(args)
    manifest = load_manifest()
    now = head(mod)
    targets = manifest["files"] if args.all else [find(manifest, p) for p in args.paths]
    conflicts = 0
    for entry in targets:
        if entry is None:
            raise SystemExit("Not in the manifest; add it first.")
        if entry["commit"] == now:
            continue
        if not changed_since(mod, entry, now):
            entry["commit"] = now
            continue
        if entry.get("kind") == "data":
            raw = git(mod, "show", "%s:%s" % (now, entry["mod"])).stdout
            with open(os.path.join(ROOT, entry["dest"]), "wb") as f:
                f.write(raw)
            entry.update(commit=now, lines=raw.count(b"\n"), sha256=hashlib.sha256(raw).hexdigest())
            print("updated data " + entry["mod"])
            continue
        rewrite = (bool(entry.get("identity")), entry.get("keep", []))
        base, _ = transform(source_of(mod, entry["commit"], entry["mod"]), entry["mod"], entry["commit"], mod_namespaces(mod, entry["commit"]), *rewrite)
        source = source_of(mod, now, entry["mod"])
        theirs, _ = transform(source, entry["mod"], now, mod_namespaces(mod, now), *rewrite)
        with tempfile.TemporaryDirectory() as tmp:
            names = {}
            for label, text in (("ours", read(entry["dest"])), ("base", base), ("theirs", theirs)):
                names[label] = os.path.join(tmp, label + ".cs")
                with open(names[label], "w", encoding="utf-8", newline="\n") as f:
                    f.write(text)
            result = subprocess.run(["git", "merge-file", "-p", "-L", "ported", "-L", "mod@" + entry["commit"][:8],
                                     "-L", "mod@" + now[:8], names["ours"], names["base"], names["theirs"]], capture_output=True)
        if result.returncode < 0 or result.returncode > 127:
            raise SystemExit("merge-file failed for " + entry["mod"])
        write(entry["dest"], result.stdout.decode("utf-8"))
        if result.returncode:
            conflicts += 1
            print("CONFLICT %s (%d hunks) -> resolve in %s" % (entry["mod"], result.returncode, entry["dest"]))
        else:
            print("merged " + entry["mod"])
        entry.update(commit=now, lines=source.count("\n"), sha256=hashlib.sha256(source.encode("utf-8")).hexdigest(),
                     ported=datetime.date.today().isoformat())
    save_manifest(manifest)
    if conflicts:
        raise SystemExit("%d file(s) need manual resolution." % conflicts)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mod", help="path to the EmpireCraft mod repository")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("add", "data"):
        p = sub.add_parser(name)
        p.add_argument("paths", nargs="+")
        p.add_argument("--dest", help="sub-folder below Core/ (add) or Data/ (data)")
        p.add_argument("--commit", default="HEAD", help="mod revision to copy from (default HEAD)")
        p.add_argument("--note")
        p.add_argument("--force", action="store_true")
        p.add_argument("--identity", action="store_true", help="rewrite WorldBox polity objects to this repository's entities")
        p.add_argument("--keep", nargs="*", help="with --identity: text such as title.capital to leave alone")
    p = sub.add_parser("extract")
    p.add_argument("path")
    p.add_argument("types", help="comma separated: Type or Outer.Inner")
    p.add_argument("--dest")
    p.add_argument("--commit", default="HEAD")
    p.add_argument("--note")
    p.add_argument("--force", action="store_true")
    p.add_argument("--identity", action="store_true")
    p.add_argument("--keep", nargs="*")
    p = sub.add_parser("remove")
    p.add_argument("paths", nargs="+")
    sub.add_parser("status")
    p = sub.add_parser("note")
    p.add_argument("path")
    p.add_argument("text")
    p = sub.add_parser("diff")
    p.add_argument("path")
    p = sub.add_parser("sync")
    p.add_argument("paths", nargs="*")
    p.add_argument("--all", action="store_true")
    args = parser.parse_args()
    {"add": cmd_add, "extract": cmd_extract, "data": cmd_data, "remove": cmd_remove,
     "status": cmd_status, "note": cmd_note, "diff": cmd_diff, "sync": cmd_sync}[args.command](args)


if __name__ == "__main__":
    main()
