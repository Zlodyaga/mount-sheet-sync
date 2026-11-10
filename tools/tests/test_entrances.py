"""Every raid and dungeon of the catalog has a fallback map point for its entrance (Entrances.lua, ns.ENTRANCES).

Ctrl + click in the addon's window asks the game for the entrance, and when the game gives none it pins this point. The
points come from tools/catalog/entrances.py (the game's tables); tools/catalog/entrances.json records which journal
instances each catalog row leads to. A new catalog place makes this test fail until the script is run again.
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
failures = 0
passes = 0


def check(name, ok, detail=""):
    global failures, passes
    if ok:
        passes += 1
    else:
        failures += 1
        print("FAIL:", name, detail)


def ids(value):
    return [int(x) for x in re.findall(r"\d+", str(value or ""))]


with open(os.path.join(ROOT, "Entrances.lua"), encoding="utf-8") as f:
    lua = f.read()
entrance_lua = lua.split("ns.RARE_SPOTS")[0]
points = {int(m[0]): (int(m[1]), float(m[2]), float(m[3]))
          for m in re.findall(r"\[(\d+)\] = \{ map = (\d+), x = ([\d.]+), y = ([\d.]+) \}", entrance_lua)}
entries = len(re.findall(r"^\s*\[\d+\] =", entrance_lua, re.M))
check("every line of Entrances.lua is a point", entries == len(points) and points, (entries, len(points)))
check("Entrances.lua fills ns.ENTRANCES", "ns.ENTRANCES = {" in lua and "local _, ns = ..." in lua)
for i, (ui, x, y) in sorted(points.items()):
    check("point %d is inside its map" % i, ui > 0 and 0 < x < 1 and 0 < y < 1, (ui, x, y))

with open(os.path.join(ROOT, "tools", "catalog", "entrances.json"), encoding="utf-8") as f:
    rows = json.load(f)["rows"]
with open(os.path.join(ROOT, "program", "catalog.json"), encoding="utf-8") as f:
    catalog = json.load(f)
places = 0
for sheet in catalog["sheets"]:
    if sheet["key"] not in ("raids", "dungeons"):
        continue
    cols = sheet["columns"]
    je_col, im_col = cols.index("ID в журнале"), cols.index("ID места")
    for row in sheet["rows"]:
        je, im = ids(row[je_col]), ids(row[im_col])
        if not (je or im):
            continue   # achievements and rewards: no place, no path
        places += 1
        key = "je:%s|im:%s" % (",".join(map(str, je)), ",".join(map(str, im)))
        found = rows.get(key)
        check("catalog row %r is known to entrances.json (run tools/catalog/entrances.py --write)" % row[1],
              found is not None, key)
        check("catalog row %r leads to a journal instance" % row[1], found != [], key)
        for i in found or []:
            check("instance %d (%s) has a fallback point" % (i, row[1]), i in points)
check("the catalog has raid and dungeon places", places > 50, places)

# points checked against the game earlier, and the cave the game shows Dragon Soul in
check("Dragon Soul is on the Caverns of Time map, not projected onto Tanaris", points.get(187, (0,))[0] == 75, points.get(187))
check("Freehold", points.get(1001) == (895, 0.8382, 0.7925), points.get(1001))
check("Return to Karazhan", points.get(860) == (42, 0.4637, 0.7066), points.get(860))
check("Operation: Mechagon", points.get(1178) == (1462, 0.736, 0.36), points.get(1178))
check("Stratholme Service Entrance: its own door, not the main gate", points.get(1292) == (23, 0.4345, 0.1959), points.get(1292))
check("Zul'Gurub is in Northern Stranglethorn, not at the edge of the Blasted Lands", points.get(76, (0,))[0] == 50, points.get(76))

# 3.0.5: rare monsters — the catalog's «Точка» (uiMap x y, or the zone alone) in ns.RARE_SPOTS by mount ID
spots_lua = lua[lua.index("ns.RARE_SPOTS = {"):] if "ns.RARE_SPOTS = {" in lua else ""
spots = {int(m[0]): (int(m[1]), m[2], m[3]) for m in re.findall(r"\[(\d+)\] = \{ map = (\d+)(?:, x = ([\d.]+), y = ([\d.]+))? \}", spots_lua)}
rare = [s for s in catalog["sheets"] if s["key"] == "rare"][0]
ci, cp = rare["columns"].index("ID в игре"), rare["columns"].index("Точка")
for row in rare["rows"]:
    parts = str(row[cp] or "").split()
    got = spots.get(int(row[ci]))
    want = (int(parts[0]), "%g" % round(float(parts[1]) / 100, 4), "%g" % round(float(parts[2]) / 100, 4)) if len(parts) == 3 else (int(parts[0]), "", "") if parts else None
    check("rare %r: its point in ns.RARE_SPOTS (run tools/catalog/entrances.py --write)" % row[3],
          want is not None and got is not None and (got[0], got[1] and "%g" % float(got[1]), got[2] and "%g" % float(got[2])) == want, (want, got))
check("every rare of the catalog has a point or a zone", len(spots) == len(rare["rows"]) > 50, (len(spots), len(rare["rows"])))

toc = [ln.strip() for ln in open(os.path.join(ROOT, "MountSheetSync.toc"), encoding="utf-8") if ln.strip() and not ln.startswith("#")]
check("Entrances.lua loads before UI.lua", "Entrances.lua" in toc and toc.index("Entrances.lua") < toc.index("UI.lua"), toc)

print("%d passed, %d failed" % (passes, failures))
sys.exit(1 if failures else 0)
