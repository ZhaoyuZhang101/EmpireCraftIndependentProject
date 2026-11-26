#!/usr/bin/env python3
"""Everything each entry point of the ported systems still needs before it can compile.

Reads the member graph written by `PortSurvey graph` (each member's own errors and the
members it refers to) and, for every entry point (Tick, Update, Monthly, Yearly ...),
gathers the errors of everything it reaches. An entry point compiles only when that
whole set is gone, so the sets say how far each one really is, and which missing
things are worth providing first.

  python Tools/PortTool/entry_needs.py <graph.json> [--entries 40] [--needs 60] [--show Class.Member]

A "need" is an error boiled down to what is missing: a type, a name, or a member of a
type. The same need behind many entry points is counted once per entry point.
"""
import argparse
import collections
import io
import json
import re

ENTRY = re.compile(r"^(Tick|Update|Monthly|Yearly|MonthlyTick|YearlyTick|MonthlyUpdate|YearlyUpdate|OnMonth|OnYear|Daily|DailyTick)$")


def need(error):
    found = re.match(r"CS0246: The type or namespace name '([^']+)'", error) or re.match(r"CS0234: The type or namespace name '([^']+)'", error)
    if found:
        return "type " + found.group(1).split("<")[0]
    found = re.match(r"CS0103: The name '([^']+)'", error)
    if found:
        return "name " + found.group(1)
    found = re.match(r"CS(?:1061|0117): '([^']+)' does not contain a definition for '([^']+)'", error)
    if found:
        return found.group(1) + "." + found.group(2)
    found = re.match(r"CS0115: '([^']+)': no suitable method found to override", error)
    if found:
        return "override " + found.group(1)
    return re.sub(r" \(are you missing.*$", "", error)[:110]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("graph")
    parser.add_argument("--entries", type=int, default=40, help="how many of the nearest entry points to list")
    parser.add_argument("--needs", type=int, default=60, help="how many needs to list")
    parser.add_argument("--show", help="list every need of one member, with the member each comes from")
    parser.add_argument("--path", nargs=2, metavar=("FROM", "TO"), help="the shortest chain of references from one member to another")
    parser.add_argument("--users", help="the members that refer to this one, with how many entry points reach each")
    args = parser.parse_args()
    units = json.load(io.open(args.graph, encoding="utf-8"))["units"]
    own = [sorted({need(error) for error in unit["errors"]}) for unit in units]

    def reach(start):
        seen = {start}
        stack = [start]
        while stack:
            for other in units[stack.pop()]["refs"]:
                if other not in seen:
                    seen.add(other)
                    stack.append(other)
        return seen

    if args.path:
        starts = [index for index, unit in enumerate(units) if unit["name"] == args.path[0]]
        goals = {index for index, unit in enumerate(units) if unit["name"] == args.path[1]}
        parent = {index: None for index in starts}
        queue = collections.deque(starts)
        found = None
        while queue and found is None:
            current = queue.popleft()
            if current in goals:
                found = current
                break
            for other in units[current]["refs"]:
                if other not in parent:
                    parent[other] = current
                    queue.append(other)
        chain = []
        while found is not None:
            chain.append(found)
            found = parent[found]
        for index in reversed(chain):
            print("%s  (%s:%d)" % (units[index]["name"], units[index]["file"], units[index]["line"]))
        if not chain:
            print("no chain")
        return
    if args.users:
        targets = {index for index, unit in enumerate(units) if unit["name"] == args.users}
        entries = [index for index, unit in enumerate(units) if ENTRY.match(unit["name"].split(".")[-1])]
        reached = collections.Counter()
        for index in entries:
            for other in reach(index):
                reached[other] += 1
        for index, unit in enumerate(units):
            if targets & set(unit["refs"]):
                print("%4d entry points reach  %s  (%s:%d)" % (reached.get(index, 0), unit["name"], unit["file"], unit["line"]))
        return
    if args.show:
        for index, unit in enumerate(units):
            if unit["name"] == args.show:
                print("%s (%s:%d)" % (unit["name"], unit["file"], unit["line"]))
                for other in sorted(reach(index), key=lambda i: units[i]["name"]):
                    for item in own[other]:
                        print("   %-60s <- %s" % (item, units[other]["name"]))
        return

    entries = [index for index, unit in enumerate(units) if ENTRY.match(unit["name"].split(".")[-1])]
    needs = {}
    for index in entries:
        gathered = set()
        for other in reach(index):
            gathered.update(own[other])
        needs[index] = gathered
    sizes = collections.Counter(len(found) for found in needs.values())
    print("%d entry points; needs each: %s" % (len(entries), ", ".join("%d:%d" % (size, count) for size, count in sorted(sizes.items())[:14])))
    print("clear now: %d" % sizes.get(0, 0))

    print("\n-- nearest entry points")
    for index in sorted(entries, key=lambda i: (len(needs[i]), units[i]["name"])):
        if not needs[index]:
            continue
        if args.entries <= 0:
            break
        args.entries -= 1
        print("%3d  %-48s %s" % (len(needs[index]), units[index]["name"][:48], "; ".join(sorted(needs[index]))[:230]))

    print("\n-- needs by how many entry points want them (and how many of those are within 5 of compiling)")
    wanted = collections.Counter()
    close = collections.Counter()
    for index, found in needs.items():
        for item in found:
            wanted[item] += 1
            if len(found) <= 5:
                close[item] += 1
    for item, count in wanted.most_common(args.needs):
        print("%4d %4d  %s" % (count, close.get(item, 0), item))


if __name__ == "__main__":
    main()
