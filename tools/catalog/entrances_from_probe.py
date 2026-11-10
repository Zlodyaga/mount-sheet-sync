"""Entrance points the game itself showed in a session, for entrances.py: tools/catalog/entrances_game.json.

    python3 tools/catalog/entrances_from_probe.py <WTF\\Account\\…\\SavedVariables\\MountSheetSyncProbe.lua>

The probe addon (tools/probe/MountSheetSyncProbe) writes what C_EncounterJournal.GetDungeonEntrancesForMap returns on
every map for each character. For every journalInstanceID the point is the one on the map entrances.py would pick when
the game has it there, otherwise on a zone map, the one most characters saw. Only coordinates go to the file (no names
of characters). Ny'alotha is left out: its entrance moves between Uldum and the Vale every week."""
import json, os, re, sys

import lupa

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "entrances_game.json")
SKIP = {1180}
ZONE = 3


def py(v):
    if lupa.lua_type(v) == "table":
        keys = list(v.keys())
        if keys and all(isinstance(k, int) for k in keys) and sorted(keys) == list(range(1, len(keys) + 1)):
            return [py(v[k]) for k in sorted(keys)]
        return {str(k): py(v[k]) for k in keys}
    return v


def main():
    L = lupa.LuaRuntime(unpack_returned_tuples=True)
    L.execute(open(sys.argv[1], encoding="utf-8").read())
    db = py(L.globals().MountSheetSyncProbeDB)
    seen = {}   # inst -> {(map, x, y): [characters, mapType]}
    for c in (db.get("chars") or {}).values():
        for mp, e in (c.get("entrances") or {}).items():
            for r in e.get("list") or []:
                if r.get("inst") is None or r.get("x") is None:
                    continue
                k = (int(mp), round(r["x"], 4), round(r["y"], 4))
                s = seen.setdefault(int(r["inst"]), {}).setdefault(k, [0, e.get("mapType")])
                s[0] += 1
    # the maps Entrances.lua uses now: the game's point on the same map wins
    src = open(os.path.join(HERE, "..", "..", "Entrances.lua"), encoding="utf-8").read().split("ns.RARE_SPOTS")[0]
    old = {int(m.group(1)): (int(m.group(2)),) for m in re.finditer(r"\[(\d+)\] = \{ map = (\d+),", src)}
    out = {}
    for inst, pts in sorted(seen.items()):
        if inst in SKIP:
            continue
        ours = old.get(inst)
        same = [k for k in pts if ours and k[0] == ours[0]]
        zones = [k for k in pts if pts[k][1] == ZONE]
        pick = max(same or zones or list(pts), key=lambda k: pts[k][0])
        out[str(inst)] = [pick[0], pick[1], pick[2]]
    json.dump({"note": "journalInstanceID -> [UiMap, x, y] as the game showed them (entrances_from_probe.py)", "points": out},
              open(OUT, "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
    print("%d instances -> %s" % (len(out), OUT))


main()
