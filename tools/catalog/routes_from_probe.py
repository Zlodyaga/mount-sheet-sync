"""Routes recorded in the game by the probe addon (/mssprobe route), for the world map: tools/catalog/rares/routes.json.

    python3 tools/catalog/routes_from_probe.py <WTF\\Account\\…\\SavedVariables\\MountSheetSyncProbe.lua> [--write]

The probe writes db.routes[creature] = { { map, points = { x, y, … } (0-1), started, name }, … } — where the player was
while following the rare. Every run becomes a route of that creature on its map: the points in 0-100, thinned so that
two neighbours are at least 0.5 apart, the first point its spawn. A run already taken (same creature and start time) is
skipped. Then run tools/catalog/routes.py --write. Without --write it only prints what it would add.
"""
import io, json, math, os, sys

from lupa import lua51 as lupa

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "rares", "routes.json")
MIN_STEP = 0.5


def py(v):
    if lupa.lua_type(v) == "table":
        keys = list(v.keys())
        if keys and all(isinstance(k, int) for k in keys) and sorted(keys) == list(range(1, len(keys) + 1)):
            return [py(v[k]) for k in sorted(keys)]
        return {str(k): py(v[k]) for k in keys}
    return v


def thin(flat):
    pts = [(round(flat[i] * 100, 1), round(flat[i + 1] * 100, 1)) for i in range(0, len(flat) - 1, 2)]
    out = []
    for p in pts:
        if not out or math.hypot(p[0] - out[-1][0], p[1] - out[-1][1]) >= MIN_STEP:
            out.append(p)
    if len(pts) > 1 and out[-1] != pts[-1]:
        out.append(pts[-1])
    return [list(p) for p in out]


def merge(data, db):
    added = []
    for npc, runs in sorted((db.get("routes") or {}).items()):
        for run in runs or []:
            line = thin(run.get("points") or [])
            if len(line) < 2 or not run.get("map"):
                continue
            tag = "recorded in the game %s" % run.get("started")
            entry = next((e for e in data if e["npc"] == int(npc) and e["map"] == int(run["map"])), None)
            if entry is None:
                entry = {"npc": int(npc), "map": int(run["map"]), "name": run.get("name") or str(npc),
                         "source": "routes recorded in the game with the probe addon (/mssprobe route)", "routes": []}
                data.append(entry)
            entry.setdefault("routes", [])
            if any(r.get("recorded") == tag for r in entry["routes"]):
                continue
            entry["routes"].append({"name": "run %d" % (len(entry["routes"]) + 1), "recorded": tag, "spawn": line[0], "line": line})
            added.append((int(npc), int(run["map"]), len(line)))
    return added


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__)
        return 2
    L = lupa.LuaRuntime(unpack_returned_tuples=True)
    L.execute(io.open(args[0], encoding="utf-8").read())
    db = py(L.globals().MountSheetSyncProbeDB) or {}
    data = json.load(io.open(OUT, encoding="utf-8"))
    added = merge(data, db)
    for npc, m, n in added:
        print("creature %d, map %d: a route of %d points" % (npc, m, n))
    if not added:
        print("no new routes")
    if "--write" in sys.argv and added:
        io.open(OUT, "w", encoding="utf-8", newline="\n").write(json.dumps(data, ensure_ascii=False, indent=1) + "\n")
        print("written; now run tools/catalog/routes.py --write")
    return 0


if __name__ == "__main__":
    sys.exit(main())
