"""Where players have seen a rare: the points of Wowhead's map on the NPC page (g_mapperData), by creature number.

    python3 tools/catalog/rares/spawn_points.py 50985 50981 ...   # prints and merges into spawn_points.json

For a rare that stands still the points are its spawn spots; for one that flies or runs they lie along its route.
A source for the research in docs/rare-routes.md, not used by the addon yet.
"""
import io, json, os, re, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "spawn_points.json")


def fetch(npc):
    req = urllib.request.Request("https://www.wowhead.com/npc=%d" % npc, headers={"User-Agent": "Mozilla/5.0"})
    html = urllib.request.urlopen(req, timeout=60).read().decode("utf-8", "replace")
    name = re.search(r"<title>([^<]*?) - NPC", html)
    m = re.search(r"g_mapperData = (\{.*?\});", html)
    maps = []
    if m:
        for zone in json.loads(m.group(1)).values():
            for floor in zone:
                maps.append({"map": floor.get("uiMapId"), "zone": floor.get("uiMapName"), "points": floor.get("coords", [])})
    return {"name": name.group(1) if name else "", "maps": maps}


def main():
    data = json.load(io.open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
    for arg in sys.argv[1:]:
        npc = int(arg)
        data[str(npc)] = fetch(npc)
        print(npc, data[str(npc)]["name"], [(m["zone"], len(m["points"])) for m in data[str(npc)]["maps"]])
        time.sleep(1.5)
    io.open(OUT, "w", encoding="utf-8", newline="\n").write(json.dumps(data, ensure_ascii=False, indent=1, sort_keys=True) + "\n")


if __name__ == "__main__":
    main()
