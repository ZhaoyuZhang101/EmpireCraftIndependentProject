"""Follow each suspended entry point down to the round-1 member it hangs on, and count those root reasons."""
import collections
import io
import json
import re
import sys

ENTRY = re.compile(r"^(Tick|Update|Monthly|Yearly|MonthlyTick|YearlyTick|MonthlyUpdate|YearlyUpdate|OnMonth|OnYear|Daily|DailyTick)$")
report = json.load(io.open(sys.argv[1], encoding="utf-8"))
by_name = collections.defaultdict(list)     # "Class.Member" -> items
entries = []
for path, entry in report["per_file"].items():
    for item in entry.get("why", []):
        name = str(item.get("member", ""))
        by_name[name].append(item)
        if ENTRY.search(name.split(".")[-1]):
            entries.append(item)


def clean(error):
    error = re.sub(r" \(are you missing.*$", "", error)
    return re.sub(r" and no accessible extension method.*$", "", error)


def missing(item):
    """The suspended member this one's first error points at, if any."""
    error = item.get("error", "")
    owner = item["member"].rsplit(".", 1)[0]
    found = re.search(r"CS0103: The name '(\w+)' does not exist", error)
    if found:
        return [owner + "." + found.group(1)]
    found = re.search(r"CS0117: '(\w+)' does not contain a definition for '(\w+)'", error)
    if found:
        return [found.group(1) + "." + found.group(2)]
    found = re.search(r"CS1061: '(\w+)' does not contain a definition for '(\w+)'", error)
    if found:
        return [found.group(1) + "." + found.group(2), found.group(1) + "Extension." + found.group(2)]
    return []


def root(item, depth=0, seen=None):
    seen = seen or set()
    if item.get("round") == 1 or depth > 12:
        return item, depth
    for name in missing(item):
        candidates = [name] + [key for key in by_name if key.endswith("." + name.split(".")[-1]) and key.split(".")[-2:-1] == name.split(".")[-2:-1]]
        for key in candidates:
            for nxt in by_name.get(key, []):
                if id(nxt) in seen or nxt.get("round", 99) >= item.get("round", 0):
                    continue
                seen.add(id(nxt))
                return root(nxt, depth + 1, seen)
    return item, depth


roots = collections.Counter()
examples = {}
unresolved = 0
depths = collections.Counter()
for item in entries:
    found, depth = root(item)
    if found.get("round") != 1:
        unresolved += 1
    text = clean(found.get("error", ""))[:105]
    roots[text] += 1
    depths[depth] += 1
    examples.setdefault(text, item["member"] + " <- " + found["member"])
print("entries:", len(entries), "not traced to round 1:", unresolved, "depths:", sorted(depths.items()))
for text, count in roots.most_common(60):
    print("%4d %-105s e.g. %s" % (count, text, examples[text][:70]))
