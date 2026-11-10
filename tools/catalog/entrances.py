"""Fallback map points for the entrance of every raid and dungeon in the catalog, from the game's own tables.

    python3 tools/catalog/entrances.py [--cache <folder>] [--write] [--debug <journalInstanceID> ...]

The addon asks the game for entrances (C_EncounterJournal.GetDungeonEntrancesForMap), but the game does not always give
them: a character who has never been there, an entrance with no record. Ctrl + click then had nothing to show. This
script takes every catalog row of the raid and dungeon sheets, finds its journal instance (JournalEncounter by
"ID в журнале", JournalInstance by "ID места"), its entrance (JournalInstanceEntrance, world position) and the map the
entrance lies on (UiMapAssignment), and writes Entrances.lua: ns.ENTRANCES[journalInstanceID] = { map, x, y }, and
tools/catalog/entrances.json (which instances each catalog row leads to, for the test).

Entrance: JournalInstanceEntrance, else the entrance icon (AreaPOI with PoiDataType 1) - newer places and entrances on a
phased copy of a continent have only the icon. Map: the smallest zone whose region holds the entrance; when the entrance
is in an area of its own inside the zone and a map inside the zone has that area's name, that map (Dragon Soul inside
the Caverns of Time, as the game shows it). MANUAL overrides places with no usable record. The script fails when a
catalog place is left without a point.
"""
import csv, io, json, os, re, sys, tempfile, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
CATALOG = os.path.join(ROOT, "program", "catalog.json")
OUT = os.path.join(ROOT, "Entrances.lua")
ROWS = os.path.join(HERE, "entrances.json")
SHEETS = ("raids", "dungeons")
ZONE, DUNGEON, MICRO, ORPHAN = 3, 4, 5, 6
# journalInstanceID -> point, where the game's tables have no entrance record
MANUAL = {
    1178: (1462, 0.736, 0.360, "Operation: Mechagon, Mechagon Island (no record; the point is from guides)"),
    1292: (23, 0.4345, 0.1959, "Stratholme Service Entrance: its record is the main gate; the point is its AreaTrigger"),
}


def table(name, cache):
    path = os.path.join(cache, "%s-enUS.csv" % name)
    if not os.path.exists(path) or os.path.getsize(path) < 50:
        url = "https://wago.tools/db2/%s/csv?locale=enUS" % name
        req = urllib.request.Request(url, headers={"User-Agent": "mount-sheet-sync catalog tool"})
        with urllib.request.urlopen(req, timeout=180) as r, open(path, "wb") as f:
            f.write(r.read())
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(io.StringIO(f.read())))


def ids(value):
    return [int(x) for x in re.findall(r"\d+", str(value or ""))]


def row_key(je, im):
    """A catalog row's place, as tools/tests/test_entrances.py builds it too."""
    return "je:%s|im:%s" % (",".join(map(str, je)), ",".join(map(str, im)))


def catalog_places():
    """journalInstanceID set per catalog row: [(sheet, place, je list, im list)]."""
    with open(CATALOG, encoding="utf-8") as f:
        data = json.load(f)
    out = []
    for sheet in data["sheets"]:
        if sheet["key"] not in SHEETS:
            continue
        cols = sheet["columns"]
        je, im = cols.index("ID в журнале"), cols.index("ID места")
        place = 1
        for row in sheet["rows"]:
            out.append((sheet["key"], row[place], ids(row[je]), ids(row[im])))
    return out


def rare_spots():
    """mountID -> (uiMap, x%, y%) or (uiMap,) from the rare sheet's «Точка»."""
    with open(CATALOG, encoding="utf-8") as f:
        data = json.load(f)
    out = {}
    for sheet in data["sheets"]:
        if sheet["key"] != "rare" or "Точка" not in sheet["columns"]:
            continue
        ci, cp = sheet["columns"].index("ID в игре"), sheet["columns"].index("Точка")
        for row in sheet["rows"]:
            parts = str(row[cp] or "").split()
            if parts and str(row[ci]).isdigit():
                out[int(row[ci])] = tuple([int(parts[0])] + [float(p) for p in parts[1:3]])
    return out


class World:
    def __init__(self, cache):
        self.encounter = {int(r["ID"]): int(r["JournalInstanceID"]) for r in table("JournalEncounter", cache)}
        self.instances = {}
        self.by_map = {}
        for r in table("JournalInstance", cache):
            i = int(r["ID"])
            self.instances[i] = r["Name_lang"]
            self.by_map.setdefault(int(r["MapID"]), []).append(i)
        self.entrances = {}
        for r in table("JournalInstanceEntrance", cache):
            self.entrances.setdefault(int(r["JournalInstanceID"]), []).append(r)
        # the entrance icons the game shows (PoiDataType 1: PoiData is the journal instance); newer places have only these
        self.icons = {}
        for r in table("AreaPOI", cache):
            if r["PoiDataType"] == "1" and r["PoiData"].isdigit():
                self.icons.setdefault(int(r["PoiData"]), []).append({
                    "ID": r["ID"], "Faction": "-1", "MapID": r["ContinentID"] or "0", "AreaTableID": r["AreaID"] or "0",
                    "Position_0": r["Pos_0"], "Position_1": r["Pos_1"], "Position_2": r["Pos_2"]})
        self.areas, self.area_parent = {}, {}
        for r in table("AreaTable", cache):
            self.areas[int(r["ID"])] = r["AreaName_lang"]
            self.area_parent[int(r["ID"])] = int(r["ParentAreaID"] or 0)
        self.maps = {int(r["ID"]): r for r in table("UiMap", cache)}
        self.inner = []   # (instance, uiMap) put on a map inside a zone, for the report
        self.guessed = []   # (instance, uiMap) whose zone the area table does not confirm: the smallest zone holding it
        self.assign = {}
        for a in table("UiMapAssignment", cache):
            self.assign.setdefault(int(a["MapID"]), []).append(a)

    def instances_of(self, je, im):
        found = []
        for e in je:
            if e in self.encounter and self.encounter[e] not in found:
                found.append(self.encounter[e])
        for m in im:
            for i in self.by_map.get(m, []):
                if i not in found:
                    found.append(i)
        return found

    def candidates(self, entrance):
        """UiMaps whose region holds the entrance: [(type, area, uiMap, x, y)]."""
        x, y, z = (float(entrance["Position_%d" % k]) for k in range(3))
        res = []
        for a in self.assign.get(int(entrance["MapID"]), []):
            x0, y0, z0 = float(a["Region_0"]), float(a["Region_1"]), float(a["Region_2"])
            x1, y1, z1 = float(a["Region_3"]), float(a["Region_4"]), float(a["Region_5"])
            if not (x0 <= x <= x1 and y0 <= y <= y1 and z0 <= z <= z1) or x1 <= x0 or y1 <= y0:
                continue
            ui = int(a["UiMapID"])
            m = self.maps.get(ui)
            if not m:
                continue
            u0, v0, u1, v1 = float(a["UiMin_0"]), float(a["UiMin_1"]), float(a["UiMax_0"]), float(a["UiMax_1"])
            u = u0 + (y1 - y) / (y1 - y0) * (u1 - u0)
            v = v0 + (x1 - x) / (x1 - x0) * (v1 - v0)
            res.append((int(m["Type"]), (x1 - x0) * (y1 - y0), ui, round(u, 4), round(v, 4),
                        int(a["AreaID"]), int(m["ParentUiMapID"]), z0 > -100000))
        return res

    def point(self, instance):
        if instance in MANUAL:
            ui, x, y, _ = MANUAL[instance]
            return ui, x, y
        rows = sorted(self.entrances.get(instance, []), key=lambda r: (r["Faction"] != "-1", int(r["ID"])))
        rows += self.icons.get(instance, [])   # entrance on a phased copy of the continent, or no record: the icon
        for r in rows:
            cands = sorted(set(self.candidates(r)), key=lambda c: (c[0], c[1], c[2]))
            zones = [c for c in cands if c[0] == ZONE]
            if not zones:
                other = [c for c in cands if c[0] in (DUNGEON, MICRO, ORPHAN)]
                if other:   # a place with a map of its own type (Tazavesh)
                    self.inner.append((instance, other[0][2]))
                    return other[0][2], other[0][3], other[0][4]
                continue
            area = int(r["AreaTableID"])
            # the zone the entrance's area belongs to; zone rectangles overlap, so the smallest one may be a neighbour
            # (Zul'Gurub's entrance lies in Blasted Lands' rectangle too)
            chain, a = [], area
            while a > 0 and a not in chain:
                chain.append(a)
                a = self.area_parent.get(a, 0)
            own = [c for c in zones if c[5] in chain]
            zone = (own or zones)[0]
            if not own:
                self.guessed.append((instance, zone[2]))
            if area > 0 and area != zone[5]:
                # the entrance is in an area of its own inside the zone (a cave): the map of that cave, as the game shows
                # Dragon Soul inside the Caverns of Time. Only a map named as that area: Karazhan's cellars are not it.
                name = self.areas.get(area)
                inner = [c for c in cands if c[0] in (DUNGEON, MICRO, ORPHAN) and c[7] and c[6] == zone[2]
                         and name and self.maps[c[2]]["Name_lang"] == name]
                if inner:
                    best = max(inner, key=lambda c: (c[1], -c[2]))
                    self.inner.append((instance, best[2]))
                    return best[2], best[3], best[4]
            return zone[2], zone[3], zone[4]
        return None


def main():
    args = sys.argv[1:]
    cache = os.path.join(tempfile.gettempdir(), "mss-wago")
    if "--cache" in args:
        cache = args[args.index("--cache") + 1]
    os.makedirs(cache, exist_ok=True)
    world = World(cache)

    if "--debug" in args:
        for i in [int(a) for a in args[args.index("--debug") + 1:] if a.isdigit()]:
            print(i, world.instances.get(i), "->", world.point(i))
            for r in world.entrances.get(i, []):
                print("   entrance", r["ID"], "faction", r["Faction"], "map", r["MapID"], "area", r["AreaTableID"])
                for c in sorted(set(world.candidates(r))):
                    if c[0] >= ZONE:
                        print("      type", c[0], "ui", c[2], world.maps[c[2]]["Name_lang"], c[3], c[4], "area", c[5],
                              "parent", c[6], "bounded z", c[7], "size", int(c[1]))
        return

    points, missing, rowless, rows = {}, [], [], {}
    for sheet, place, je, im in catalog_places():
        found = world.instances_of(je, im)
        if je or im:
            rows[row_key(je, im)] = found
        if not found:
            rowless.append("%s: %s" % (sheet, place))
            continue
        for i in found:
            if i in points:
                continue
            p = world.point(i)
            if p:
                points[i] = p
            else:
                missing.append("%s: %s (journal %d %s)" % (sheet, place, i, world.instances.get(i)))
    print("instances with a point:", len(points))
    for i, ui in world.inner:
        print("   on a map inside a zone: %d %s -> %d %s" % (i, world.instances.get(i), ui, world.maps[ui]["Name_lang"]))
    for i, ui in sorted(set(world.guessed)):
        if i in points and points[i][0] == ui:
            print("   zone not confirmed by the area: %d %s -> %d %s %s" % (i, world.instances.get(i), ui, world.maps[ui]["Name_lang"], points[i][1:]))
    for i, (ui, x, y) in sorted(points.items()):
        if not (0.03 < x < 0.97 and 0.03 < y < 0.97):
            print("   CHECK, at the edge of the map: %d %s -> %d %s %s %s" % (i, world.instances.get(i), ui, world.maps[ui]["Name_lang"], x, y))
    if rowless:
        print("catalog rows with no journal instance (no entrance needed or not a place):")
        for r in sorted(set(rowless)):
            print("   ", r)
    if missing:
        print("NO ENTRANCE POINT:")
        for m in missing:
            print("   ", m)
        sys.exit(1)
    if "--write" in args:
        lines = ["-- Generated by tools/catalog/entrances.py from the game's tables (JournalInstanceEntrance, UiMapAssignment).",
                 "-- Do not edit by hand. Fallback map point of every catalog raid and dungeon entrance: [journalInstanceID] = { map, x, y }.",
                 "local _, ns = ...",
                 "ns.ENTRANCES = {"]
        for i in sorted(points):
            ui, x, y = points[i]
            lines.append("  [%d] = { map = %d, x = %s, y = %s },   -- %s" % (i, ui, x, y, world.instances.get(i, "?")))
        lines.append("}")
        # rare monsters: the catalog's «Точка» (uiMap x y in percent, or the zone alone when they have several spawn points)
        lines.append("-- Rare monsters: [mountID] = { map, x, y } from the catalog's column «Точка»; no x, y — several spawn points, the zone's map.")
        lines.append("ns.RARE_SPOTS = {")
        for mid, spot in sorted(rare_spots().items()):
            if len(spot) == 3:
                lines.append("  [%d] = { map = %d, x = %s, y = %s }," % (mid, spot[0], round(spot[1] / 100, 4), round(spot[2] / 100, 4)))
            else:
                lines.append("  [%d] = { map = %d }," % (mid, spot[0]))
        lines.append("}")
        with open(OUT, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        # which journal instances each catalog row leads to: tools/tests/test_entrances.py checks the catalog against it
        # without the game's tables, and fails when a new place has no point (run this script again then)
        with open(ROWS, "w", encoding="utf-8", newline="\n") as f:
            json.dump({"rows": dict(sorted(rows.items()))}, f, ensure_ascii=False, indent=1)
            f.write("\n")
        print("wrote", OUT, "and", ROWS)


if __name__ == "__main__":
    main()
