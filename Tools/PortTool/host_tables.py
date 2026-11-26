#!/usr/bin/env python3
"""Generate the host tables that mirror lists the mod keeps in its own asset libraries.

The mod registers its world laws and its kinds of world news with the base game's
asset libraries (Scripts/GameLibrary, not ported). The core needs only the lists:
which laws exist and whether they start on, which news kinds exist and where
their texts go. This reads those from the mod at HEAD and writes

  Assets/EmpireCraft/Core/Host/WorldLaws.cs
  Assets/EmpireCraft/Core/Host/NewsKinds.cs

Run it again when the mod adds a law or a kind of news:

  python Tools/PortTool/host_tables.py [--mod <mod repo>]
"""
import argparse
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import port_tool as tool  # noqa: E402

HOST = os.path.join(tool.ROOT, "Assets", "EmpireCraft", "Core", "Host")
BASE_LAWS = ("civ_army", "rebellions", "diplomacy", "kingdom_expansion", "old_age")


def write(name, lines):
    path = os.path.join(HOST, name)
    content = "\n".join(lines)
    if os.path.exists(path) and io.open(path, encoding="utf-8").read() == content:
        print("unchanged:", name)
        return
    io.open(path, "w", encoding="utf-8", newline="\n").write(content)
    print("wrote:", name)


def laws(mod, commit):
    source = tool.git(mod, "show", commit + ":Scripts/GameLibrary/EmpireCraftWorldLawLibrary.cs").stdout.decode("utf-8")
    found = []
    for block in re.finditer(r"(\w+)\s*=\s*new WorldLawAsset\(\)\s*\{(.*?)\}\)", source, re.S):
        state = re.search(r"\bdefault_state\s*=\s*(true|false)", block.group(2))
        if state and block.group(1).startswith("empirecraft_law_"):
            found.append((block.group(1)[len("empirecraft_law_"):], state.group(1)))
    if len(found) < 25:
        raise SystemExit("only %d laws parsed" % len(found))
    lines = [
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace EmpireCraft.Core",
        "{",
        "    // A switch over how a world plays (\"world law\"): a named on/off the systems ask about.",
        "    // The switches are static for now, as the mod's other settings are; they move onto",
        "    // the world (and into its save) when the client gets a place to change them.",
        "    public sealed class WorldLaw",
        "    {",
        "        public readonly string Id;",
        "        public readonly bool Default;",
        "        public bool Enabled;",
        "        internal WorldLaw(string id, bool on) { Id = id; Default = on; Enabled = on; }",
        "        public bool IsEnabled() => Enabled;",
        "        public void Toggle(bool on) { Enabled = on; }",
        "    }",
        "",
        "    public static class WorldLaws",
        "    {",
        "        public static readonly List<WorldLaw> All = new List<WorldLaw>();",
        "        static WorldLaw Law(string id, bool on) { var law = new WorldLaw(id, on); All.Add(law); return law; }",
        "",
        "        // The mod's own laws with its defaults (EmpireCraftWorldLawLibrary @ %s)." % commit[:8],
    ]
    for name, state in found:
        lines.append("        public static readonly WorldLaw %s = Law(\"%s\", %s);" % (tool.pascal(name), name, state))
    lines += ["", "        // Switches the mod read from the base game. Here they are this game's own, all on."]
    for name in BASE_LAWS:
        lines.append("        public static readonly WorldLaw %s = Law(\"%s\", true);" % (tool.pascal(name), name))
    lines += [
        "",
        "        public static WorldLaw Get(string id) { foreach (var law in All) if (string.Equals(law.Id, id, StringComparison.Ordinal)) return law; return null; }",
        "        public static void Reset() { foreach (var law in All) law.Enabled = law.Default; }",
        "    }",
        "",
        "    // How a person died, as the ported systems record it. People here die of age or by a",
        "    // system's hand; the rest are labels those systems may write into a biography.",
        "    public enum DeathCause : byte { Other, Age, Weapon, Fire, Water, Starvation, Poison, Plague, Infection, Tumor, Explosion, Eaten }",
        "}",
        "",
    ]
    write("WorldLaws.cs", lines)
    return len(found)


def news(mod, commit):
    source = tool.git(mod, "show", commit + ":Scripts/GameLibrary/EmpireCraftWorldLogLibrary.cs").stdout.decode("utf-8-sig")
    feed = tool.git(mod, "show", commit + ":Scripts/GamePatches/ScreenFeedPatch.cs").stdout.decode("utf-8-sig")
    declared = re.findall(r"public static WorldLogAsset\s+(\w+)\s*;", source)
    # The kinds the mod flashes on screen whoever they are about.
    body = re.search(r"HashSet<string> Major\(\)(.*?)\n\s*return _major;", feed, re.S)
    major = set(re.findall(r"EmpireCraftWorldLogLibrary\.(\w+)", body.group(1))) if body else set()
    if len(major) < 5:
        raise SystemExit("major kinds not found in ScreenFeedPatch")
    kinds = {}
    odd = []
    starts = [m for m in re.finditer(r"(\w+)\s*=\s*(?:wl|AssetManager\.world_log_library)\.add\(new WorldLogAsset\b", source)]
    for index, start in enumerate(starts):
        end = starts[index + 1].start() if index + 1 < len(starts) else len(source)
        block = source[start.end():end]
        field = start.group(1)
        literal = re.search(r"\bid\s*=\s*\"([^\"]+)\"", block)
        named = re.search(r"\bid\s*=\s*nameof\((\w+)\)", block)
        kind_id = literal.group(1) if literal else named.group(1) if named else field
        group = re.search(r"\bgroup\s*=\s*\"([^\"]*)\"", block)
        slots = re.findall(r"updateText\(ref pText,\s*pMessage,\s*\"([^\"]+)\",\s*(\d)\)", block)
        replacer = re.search(r"text_replacer\s*=\s*delegate[^{]*\{(.*?)\n\s*\}\s*\n", block, re.S)
        if replacer:
            rest = re.sub(r"\s*wl\.updateText\([^;]*;", "", replacer.group(1)).strip()
            if rest:
                odd.append(field)
        if field in declared:
            kinds[field] = (kind_id, group.group(1) if group else "", slots)
            continue
        # Kinds registered in a loop over their ids: the same definition for each id listed.
        loops = list(re.finditer(r"foreach\s*\(string\s+\w+\s+in\s+new\[\]\s*\{([^}]*)\}\)", source[:start.start()]))
        for name in re.findall(r"\"(\w+)\"", loops[-1].group(1)) if loops else []:
            if name in declared:
                kinds[name] = (name, group.group(1) if group else "", slots)
    # Declared but never registered in the mod: the field stays empty there, and here.
    missing = [name for name in declared if name not in kinds]
    if len(missing) > 5 or len(kinds) < 80:
        raise SystemExit("news kinds: %d parsed, declared but not parsed: %s" % (len(kinds), missing))
    lines = [
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace EmpireCraft.Core",
        "{",
        "    // The kinds of world news the ported systems post, with where their texts go in the template",
        "    // (\"1:$empire$\": the first text replaces $empire$). Field names are the mod's.",
        "    // Generated by Tools/PortTool/host_tables.py from the mod's EmpireCraftWorldLogLibrary @ %s; do not edit by hand." % commit[:8],
        "    public static class NewsKinds",
        "    {",
        "        public static readonly List<NewsKind> All = new List<NewsKind>();",
        "        static NewsKind Kind(string id, string group, bool major, params string[] slots) { var kind = new NewsKind(id, group, major, slots); All.Add(kind); return kind; }",
        "",
    ]
    for field in declared:
        if field not in kinds:
            lines.append("        public static readonly NewsKind %s = null;   // declared by the mod, never registered" % field)
            continue
        kind_id, group, slots = kinds[field]
        arguments = "".join(", \"%s:%s\"" % (index, placeholder) for placeholder, index in slots)
        lines.append("        public static readonly NewsKind %s = Kind(\"%s\", \"%s\", %s%s);"
                     % (field, kind_id, group, "true" if field in major else "false", arguments))
    lines += [
        "",
        "        public static NewsKind Get(string id) { foreach (var kind in All) if (string.Equals(kind.Id, id, StringComparison.Ordinal)) return kind; return null; }",
        "    }",
        "}",
        "",
    ]
    write("NewsKinds.cs", lines)
    if odd:
        print("note: %d kinds have more than plain placeholders in their text: %s" % (len(odd), ", ".join(odd)))
    return len(kinds), len([name for name in kinds if name in major])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mod")
    args = parser.parse_args()
    mod = tool.mod_repo(args)
    commit = tool.git(mod, "rev-parse", "HEAD").stdout.decode().strip()
    print("%d laws" % laws(mod, commit))
    print("%d news kinds, %d major" % news(mod, commit))


if __name__ == "__main__":
    main()
