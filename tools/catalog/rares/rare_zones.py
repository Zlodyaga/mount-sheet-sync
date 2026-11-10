"""Zone (area) and the first few spawn points of NPCs from Wowhead's map data. Output: rare_zones.json."""
import csv, io, json, os, re, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
areas = {}
with open(os.path.join(sys.argv[2], "AreaTable-enUS.csv"), encoding="utf-8") as f:
    for r in csv.DictReader(io.StringIO(f.read())):
        areas[r["ID"]] = r["AreaName_lang"]
out = {}
for i in sys.argv[1].split(","):
    req = urllib.request.Request("https://www.wowhead.com/npc=%s" % i, headers={"User-Agent": "mount-sheet-sync catalog tool"})
    try:
        html = urllib.request.urlopen(req, timeout=30).read().decode("utf-8", "replace")
    except Exception as e:
        print(i, "fail", e)
        continue
    m = re.search(r"g_mapperData\s*=\s*(\{.*?\});", html, re.S)
    if not m:
        print(i, "no map data")
        continue
    data = json.loads(m.group(1))
    zones = []
    for area, floors in data.items():
        for fl in floors:
            pts = fl.get("coords") or []
            zones.append({"area": int(area), "name": areas.get(area, "?"), "count": fl.get("count", len(pts)), "first": pts[:1]})
    out[i] = zones
    print(i, [(z["name"], z["count"], z["first"]) for z in zones][:3])
    time.sleep(0.3)
json.dump(out, open(os.path.join(HERE, "rare_zones.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
