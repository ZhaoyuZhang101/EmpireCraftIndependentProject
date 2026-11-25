#!/usr/bin/env python3
"""What the entity layer still has to offer before the rest of the mod can be ported.

All unported mod files are run through the identity rewrite and handed, together
with the ported core, to Validation/PortSurvey, which binds every member access
with the compiler's semantic model (nothing has to compile) and counts the ones
that do not resolve:

  Realm.GetEmpire      an entity member to provide (or an extension that has to come over)
  TileZone.city        a WorldBox concept this repository has no type for yet
  Date, AssetManager   a name nothing here declares

  python Tools/PortTool/survey.py [--mod <repo>] [--dirs ...] [--report survey.json]

The ranking is the to-do list of the entity layer and the world operations.
Run from the repository root.
"""
import argparse
import collections
import json
import os
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import closure
import port_tool as tool

DIRS = closure.DEFAULT_DIRS + ["Scripts/GameClassExtensions"]
ENTITIES = ("Realm", "City", "Person", "Clan", "EmpireWorld")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mod")
    parser.add_argument("--dirs", nargs="+", default=DIRS)
    parser.add_argument("--report", default="survey.json")
    parser.add_argument("--work")
    args = parser.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    mod = tool.mod_repo(args)
    commit = tool.git(mod, "rev-parse", "HEAD").stdout.decode().strip()
    manifest = tool.load_manifest()
    ported = {e["mod"] for e in manifest["files"] if e.get("kind") == "source"}
    listed = tool.git(mod, "ls-tree", "-r", "--name-only", commit, "--", *args.dirs).stdout.decode("utf-8").splitlines()
    candidates = [p for p in listed if p.endswith(".cs") and p not in ported]
    work = os.path.abspath(args.work or os.path.join(tempfile.gettempdir(), "ec_port_survey"))
    if os.path.isdir(work):
        shutil.rmtree(work)
    os.makedirs(os.path.join(work, "src"))
    namespaces = tool.mod_namespaces(mod, commit)
    for path in candidates:
        text, _ = tool.transform(tool.show(mod, commit, path), path, commit, namespaces, True, [])
        full = os.path.join(work, "src", *path.split("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    # The core, minus the declarations lifted out of files that are surveyed whole here.
    core = os.path.join(work, "core")
    shutil.copytree(os.path.join(tool.ROOT, "Assets", "EmpireCraft", "Core"), core, ignore=shutil.ignore_patterns("*.meta"))
    for entry in manifest["files"]:
        if entry.get("kind") == "extract" and entry["mod"].partition("#")[0] in candidates:
            os.remove(os.path.join(core, *entry["dest"].split("/")[3:]))
    orbis = os.path.join(tool.ROOT, "Assets", "ORBIS", "Runtime", "FlatWorld", "Core")
    print("by-type member renames: " + tool.semantic_fix(os.path.join(work, "src"), core))
    raw = os.path.join(work, "raw.json")
    result = subprocess.run(["dotnet", "run", "--project", os.path.join(tool.ROOT, "Validation", "PortSurvey"), "-c", "Release", "--",
                             "survey", os.path.join(work, "src"), raw, core, orbis], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise SystemExit(result.stdout[-2000:] + result.stderr[-4000:])
    print("mod %s: %s" % (commit[:8], result.stdout.strip().splitlines()[-1]))
    with open(raw, encoding="utf-8") as f:
        report = json.load(f)
    report["commit"] = commit
    members, names = report["members"], report["names"]
    by_type = collections.Counter()
    distinct = collections.Counter()
    for key, n in members.items():
        owner = key.split(".")[0]
        by_type[owner] += n
        distinct[owner] += 1
    clean = [p for p, n in report["unresolved_per_file"].items() if n == 0]
    report["clean_files"] = sorted(clean)
    report["clean_lines"] = sum(report["file_lines"][p] for p in clean)
    with open(args.report, "w", encoding="utf-8", newline="\n") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print("%d files (%d lines) resolve completely" % (len(clean), report["clean_lines"]))
    print("unresolved members by receiver type:")
    for name, n in by_type.most_common(30):
        print("  %-26s %6d uses, %4d distinct%s" % (name, n, distinct[name], "   <- entity" if name in ENTITIES else ""))
    for entity in ENTITIES:
        top = [(k, n) for k, n in members.items() if k.split(".")[0] == entity]
        top.sort(key=lambda kv: -kv[1])
        if top:
            print("%s (%d distinct): %s" % (entity, len(top), ", ".join("%s %d" % (k.split(".", 1)[1], n) for k, n in top[:45])))
    print("unresolved names: " + ", ".join("%s %d" % kv for kv in sorted(names.items(), key=lambda kv: -kv[1])[:50]))
    print("Report: " + args.report)


if __name__ == "__main__":
    main()
