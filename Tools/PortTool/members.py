#!/usr/bin/env python3
"""Port by member instead of by file.

A mod system file rarely compiles as a whole: one method that names something this
repository does not have yet (a war, a map zone) used to keep the whole file out, and
every file that calls into it. Here every unported file goes through the identity
rewrite and the by-type renames, and then Validation/PortSurvey `suspend` wraps exactly
the members that cannot compile yet in `#if EC_PENDING`. What is left compiles and can
come over; each round of new entity members lets more of it out of suspension.

  python Tools/PortTool/members.py [--mod <repo>] [--dirs ...] [--report members.json]   find and check
  python Tools/PortTool/members.py --apply members.json                                   bring the live files over

Files brought over this way are generated: do not edit them by hand, change the rules
(port_tool.py, Port/identity-members.json) or the entity layer and run this again.
Run from the repository root.
"""
import argparse
import datetime
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import closure
import port_tool as tool

DIRS = closure.DEFAULT_DIRS + ["Scripts/GameClassExtensions", "Scripts/AI"]


def generated_header(text, path):
    """The header of a generated file names its source but not the commit, which the manifest records."""
    first, _, rest = text.partition("\n")
    if first.startswith("// Ported from the EmpireCraft mod: "):
        first = "// Generated from the EmpireCraft mod: %s. Do not edit by hand: Tools/PortTool/members.py rewrites it;" % path
    return first + "\n" + rest


def find(args):
    mod = tool.mod_repo(args)
    commit = tool.git(mod, "rev-parse", "HEAD").stdout.decode().strip()
    manifest = tool.load_manifest()
    done = {e["mod"] for e in manifest["files"] if e.get("kind") == "source" and not e.get("generated")}
    listed = tool.git(mod, "ls-tree", "-r", "--name-only", commit, "--", *args.dirs).stdout.decode("utf-8").splitlines()
    candidates = [p for p in listed if p.endswith(".cs") and p not in done]
    work = os.path.abspath(args.work or os.path.join(tempfile.gettempdir(), "ec_port_members"))
    if os.path.isdir(work):
        shutil.rmtree(work)
    os.makedirs(os.path.join(work, "src"))
    namespaces = tool.mod_namespaces(mod, commit)
    sources = {}
    for path in candidates:
        source = tool.show(mod, commit, path)
        sources[path] = hashlib.sha256(source.encode("utf-8")).hexdigest()
        text, _ = tool.transform(source, path, commit, namespaces, True, [])
        text = generated_header(text, path)
        full = os.path.join(work, "src", *path.split("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    # The core as it would be without the files generated earlier and without declarations
    # lifted out of files that are candidates as a whole now.
    core = os.path.join(work, "core")
    shutil.copytree(os.path.join(tool.ROOT, "Assets", "EmpireCraft", "Core"), core, ignore=shutil.ignore_patterns("*.meta"))
    for entry in manifest["files"]:
        replaced = entry.get("generated") or (entry.get("kind") == "extract" and entry["mod"].partition("#")[0] in sources)
        if replaced:
            path = os.path.join(core, *entry["dest"].split("/")[3:])
            if os.path.exists(path):
                os.remove(path)
    orbis = os.path.join(tool.ROOT, "Assets", "ORBIS", "Runtime", "FlatWorld", "Core")
    print("%d candidates from mod %s" % (len(candidates), commit[:8]))
    print("by-type member renames: " + tool.semantic_fix(os.path.join(work, "src"), core))
    raw = os.path.join(work, "suspend.json")
    result = subprocess.run(["dotnet", "run", "--project", os.path.join(tool.ROOT, "Validation", "PortSurvey"), "-c", "Release", "--",
                             "suspend", os.path.join(work, "src"), raw, core, orbis], capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(result.stdout.strip())
    if result.returncode != 0:
        raise SystemExit(result.stderr[-4000:])
    # The proof: everything together under the compiler and language version Unity uses.
    with open(os.path.join(work, "closure.csproj"), "w", encoding="utf-8") as f:
        f.write(closure.PROJECT.format(orbis=orbis, core=core, excluded=""))
    log = os.path.join(work, "build.log")
    subprocess.run(["dotnet", "build", os.path.join(work, "closure.csproj"), "-nologo", "-v:q", "-noconlog",
                    "-flp:logfile=%s;errorsonly;encoding=utf-8" % log], capture_output=True)
    errors = []
    with open(log, encoding="utf-8-sig", errors="replace") as f:
        for line in f:
            if ": error " in line and line.strip() not in errors:
                errors.append(line.strip())
    with open(raw, encoding="utf-8") as f:
        report = json.load(f)
    report.update(commit=commit, work=work, build_errors=len(errors), sha256=sources)
    with open(args.report, "w", encoding="utf-8", newline="\n") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print("check build: %d errors" % len(errors))
    for line in errors[:12]:
        print("  " + line[:260])
    print("Report: " + args.report)


def apply(args):
    with open(args.apply, encoding="utf-8") as f:
        report = json.load(f)
    if report.get("build_errors"):
        raise SystemExit("The check build of this report had errors; nothing is brought over.")
    manifest = tool.load_manifest()
    work = report["work"]
    today = datetime.date.today().isoformat()
    by_mod = {e["mod"]: e for e in manifest["files"]}
    taken = {e["dest"]: e["mod"] for e in manifest["files"]}
    count = lines = 0
    for path, info in sorted(report["per_file"].items()):
        entry = by_mod.get(path)
        if not info["live"]:
            if entry and entry.get("generated"):
                tool.cmd_remove(argparse.Namespace(paths=[path]))
                manifest = tool.load_manifest()
            continue
        folder, name = closure.destination(path)
        dest = entry["dest"] if entry else "/".join((tool.CORE, folder, name))
        if taken.get(dest, path) != path:
            raise SystemExit("destination already taken: %s (for %s)" % (dest, path))
        with open(os.path.join(work, "src", *path.split("/")), encoding="utf-8") as f:
            text = f.read()
        tool.write(dest, text if text.endswith("\n") else text + "\n")
        record = {"mod": path, "commit": report["commit"], "ported": today, "dest": dest, "lines": info["lines"],
                  "sha256": report["sha256"][path], "kind": "source", "identity": True, "generated": True,
                  "suspended_lines": info["suspended_lines"], "suspended_members": info["suspended_members"],
                  "changes": ["generated by Tools/PortTool/members.py: identity rewrite, by-type renames, members that cannot compile yet inside #if EC_PENDING"]}
        manifest["files"] = [e for e in manifest["files"] if e["mod"] != path]
        manifest["files"].append(record)
        taken[dest] = path
        count += 1
        lines += info["lines"] - info["suspended_lines"]
    # A declaration lifted out of a file earlier gives way to the file itself.
    for entry in list(manifest["files"]):
        if entry.get("kind") == "extract" and report["per_file"].get(entry["mod"].partition("#")[0], {}).get("live"):
            for name in (entry["dest"], entry["dest"] + ".meta"):
                full = os.path.join(tool.ROOT, name)
                if os.path.exists(full):
                    os.remove(full)
            manifest["files"].remove(entry)
            print("dropped the lifted declaration " + entry["mod"])
    tool.save_manifest(manifest)
    print("%d generated files in the core, %d live lines" % (count, lines))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mod")
    parser.add_argument("--dirs", nargs="+", default=DIRS)
    parser.add_argument("--report", default="members.json")
    parser.add_argument("--work")
    parser.add_argument("--apply")
    args = parser.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if args.apply:
        apply(args)
    else:
        find(args)


if __name__ == "__main__":
    main()
