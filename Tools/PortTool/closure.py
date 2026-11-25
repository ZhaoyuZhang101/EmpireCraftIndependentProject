#!/usr/bin/env python3
"""Find the mod files that can be ported right now, by compiling them.

Every candidate file is run through the port_tool transform and compiled together
with what is already in Assets/EmpireCraft/Core. Files with errors are thrown out
and the rest compiled again, until what is left compiles cleanly: the largest set
that needs nothing but itself and the code already ported.

  python Tools/PortTool/closure.py [--mod <repo>] [--dirs Scripts/GeneralSystems ...] [--report closure.json]
  python Tools/PortTool/closure.py --apply closure.json      (port the set found by an earlier run)

The report lists the files that pass and, for each file thrown out, the round it
fell in, how many errors it had and the names it was missing. A file that fell in
round 1 over one or two names is a candidate for `extract` or an #if EC_PENDING.
"""
import argparse
import collections
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import port_tool as tool

DEFAULT_DIRS = ["Scripts/GeneralSystems", "Scripts/Data", "Scripts/Layer", "Scripts/Regimes", "Scripts/HelperFunc", "Scripts/Enums"]
ERROR = re.compile(r"^\s*(?:\d+(?::\d+)?>)?(.*?\.cs)\((\d+),(\d+)\): error (CS\d+): (.*?)(?: \[[^\]]*\])?\s*$")
MISSING = re.compile(r"The (?:type or namespace )?name '([^']+)'|does not contain a definition for '([^']+)'")

PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Library</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable><WarningLevel>0</WarningLevel>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors><NoWarn>$(NoWarn);CS0649;CS0169;CS0414;CS8632;CS0162;CS0219;CS0168</NoWarn>
    <DefineConstants>$(DefineConstants);EC_SERVER_NEVER</DefineConstants></PropertyGroup>
  <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3"/></ItemGroup>
  <ItemGroup>
    <Compile Include="{orbis}/*.cs"/>
    <Compile Include="{core}/**/*.cs" Exclude="{excluded}"/>
    <Compile Include="src/**/*.cs"/>
  </ItemGroup>
</Project>
"""


def destination(path):
    """Where a mod file goes below Assets/EmpireCraft/Core."""
    parts = path.split("/")            # Scripts/<Top>/[sub/...]/File.cs
    top, middle, name = parts[1], parts[2:-1], parts[-1]
    if top == "Data":
        folder = "State"
    elif top == "Enums":
        folder = "Enums"
    elif top == "HelperFunc":
        folder = "Util"
    else:
        stem = name[:-3]
        folder = "Systems" if stem.endswith("System") or "System." in stem else "Rules"
    return "/".join([folder] + middle), name


def find_set(args):
    mod = tool.mod_repo(args)
    commit = tool.git(mod, "rev-parse", "HEAD").stdout.decode().strip()
    manifest = tool.load_manifest()
    ported = {e["mod"].partition("#")[0] for e in manifest["files"]}
    listed = tool.git(mod, "ls-tree", "-r", "--name-only", commit, "--", *args.dirs).stdout.decode("utf-8").splitlines()
    candidates = [p for p in listed if p.endswith(".cs") and p not in ported]
    work = os.path.abspath(args.work or os.path.join(tempfile.gettempdir(), "ec_port_closure"))
    if os.path.isdir(work):
        shutil.rmtree(work)
    os.makedirs(os.path.join(work, "src"))
    namespaces = tool.mod_namespaces(mod, commit)
    lines = {}
    for path in candidates:
        source = tool.show(mod, commit, path)
        lines[path] = source.count("\n")
        text, _ = tool.transform(source, path, commit, namespaces)
        full = os.path.join(work, "src", *path.split("/"))
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    with open(os.path.join(work, "closure.csproj"), "w", encoding="utf-8") as f:
        f.write(PROJECT.format(orbis=os.path.join(tool.ROOT, "Assets", "ORBIS", "Runtime", "FlatWorld", "Core"),
                               core=os.path.join(tool.ROOT, "Assets", "EmpireCraft", "Core"), excluded=""))
    print("%d candidates (%d lines) from %s @ %s" % (len(candidates), sum(lines.values()), ", ".join(args.dirs), commit[:8]))

    alive = set(candidates)
    rejected = {}
    source_root = os.path.join(work, "src") + os.sep
    for round_number in range(1, 60):
        log = os.path.join(work, "build.log")
        subprocess.run(["dotnet", "build", os.path.join(work, "closure.csproj"), "-nologo", "-v:q", "-noconlog",
                        "-flp:logfile=%s;errorsonly;encoding=utf-8" % log], capture_output=True)
        errors = collections.defaultdict(list)
        foreign = []
        seen = set()
        with open(log, encoding="utf-8-sig", errors="replace") as f:
            for line in f:
                m = ERROR.match(line)
                if not m:
                    if "error" in line and line.strip() not in seen and len(foreign) < 10 and ".cs(" not in line:
                        seen.add(line.strip())
                        foreign.append(line.strip())
                    continue
                key = (m.group(1), m.group(2), m.group(3), m.group(4))
                if key in seen:
                    continue
                seen.add(key)
                full = os.path.normpath(m.group(1))
                if full.startswith(source_root):
                    errors[full[len(source_root):].replace(os.sep, "/")].append((m.group(4), m.group(5)))
                else:
                    foreign.append(line.strip())
        if not errors:
            if foreign:
                print("round %d: errors outside the candidates; stopping:" % round_number)
                for line in foreign[:10]:
                    print("  " + line[:300])
                break
            print("round %d: clean" % round_number)
            break
        for path, found in errors.items():
            missing = collections.Counter()
            for code, message in found:
                for m in MISSING.finditer(message):
                    missing[m.group(1) or m.group(2)] += 1
            rejected[path] = {"round": round_number, "errors": len(found), "lines": lines[path],
                              "codes": dict(collections.Counter(code for code, _ in found)),
                              "missing": dict(missing.most_common(12)), "first": found[0][1][:200]}
            alive.discard(path)
            os.remove(os.path.join(work, "src", *path.split("/")))
        print("round %d: %d files out, %d left" % (round_number, len(errors), len(alive)))
    report = {"commit": commit, "dirs": args.dirs, "pass": sorted(alive), "pass_lines": sum(lines[p] for p in alive), "rejected": rejected}
    with open(args.report, "w", encoding="utf-8", newline="\n") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print("%d files (%d lines) compile on their own; %d thrown out. Report: %s" % (len(alive), report["pass_lines"], len(rejected), args.report))
    near = sorted((p for p, r in rejected.items() if r["round"] == 1 and r["errors"] <= 4), key=lambda p: (rejected[p]["errors"], p))
    if near:
        print("near misses (fell in round 1 with at most 4 errors):")
        for path in near[:60]:
            r = rejected[path]
            print("  %-70s %4d lines  %d errors  missing %s" % (path, r["lines"], r["errors"], ", ".join(r["missing"]) or r["first"][:60]))


def apply_set(args):
    mod = tool.mod_repo(args)
    with open(args.apply, encoding="utf-8") as f:
        report = json.load(f)
    skip = set(args.skip or [])
    manifest = tool.load_manifest()
    taken = {e["dest"] for e in manifest["files"]}
    count = 0
    for path in report["pass"]:
        if path in skip or tool.find(manifest, path):
            continue
        folder, name = destination(path)
        dest = "/".join((tool.CORE, folder, name))
        if dest in taken or os.path.exists(os.path.join(tool.ROOT, dest)):
            raise SystemExit("destination already taken: %s (for %s)" % (dest, path))
        taken.add(dest)
        tool.record_source(manifest, mod, report["commit"], path, dest, "source", None, False)
        count += 1
    tool.save_manifest(manifest)
    print("ported %d files" % count)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mod")
    parser.add_argument("--dirs", nargs="+", default=DEFAULT_DIRS)
    parser.add_argument("--report", default="closure.json")
    parser.add_argument("--work", help="scratch folder (default: the system temp folder)")
    parser.add_argument("--apply", help="port the passing files of this report")
    parser.add_argument("--skip", nargs="*", help="with --apply: mod paths to leave out")
    args = parser.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if args.apply:
        apply_set(args)
    else:
        find_set(args)


if __name__ == "__main__":
    main()
