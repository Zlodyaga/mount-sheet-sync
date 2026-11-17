"""Names of the places next to the rares' spawn points, in the six languages of the addon: rares/place_names.json.

    python3 tools/catalog/place_names.py [--cache <folder>]

tools/catalog/rares/routes.json gives a spawn point an area of the game (`places` next to `spawns`, `place` in a route,
AreaTable IDs). The names come from the game's own AreaTable (wago.tools) in enUS, ruRU, deDE, frFR, esES, ptBR, so no
name is translated by hand. routes.py puts them into Routes.lua. Rerun after new `places` in routes.json.
"""
import csv, io, json, os, sys, tempfile, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "rares", "routes.json")
OUT = os.path.join(HERE, "rares", "place_names.json")
LOCALES = ["enUS", "ruRU", "deDE", "frFR", "esES", "ptBR"]


def table(name, locale, cache):
    path = os.path.join(cache, "%s-%s.csv" % (name, locale))
    if not os.path.exists(path) or os.path.getsize(path) < 50:
        url = "https://wago.tools/db2/%s/csv?locale=%s" % (name, locale)
        req = urllib.request.Request(url, headers={"User-Agent": "mount-sheet-sync catalog tool"})
        with urllib.request.urlopen(req, timeout=180) as r, open(path, "wb") as f:
            f.write(r.read())
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(io.StringIO(f.read())))


def wanted():
    ids = set()
    for e in json.load(io.open(DATA, encoding="utf-8")):
        ids.update(p for p in e.get("places", []) if p)
        ids.update(r["place"] for r in e.get("routes", []) if r.get("place"))
    return ids


def main():
    cache = sys.argv[sys.argv.index("--cache") + 1] if "--cache" in sys.argv else tempfile.mkdtemp(prefix="mss-areas-")
    ids = wanted()
    out = {}
    for loc in LOCALES:
        for r in table("AreaTable", loc, cache):
            i = int(r["ID"])
            if i in ids and r["AreaName_lang"]:
                out.setdefault(str(i), {})[loc] = r["AreaName_lang"]
    missing = sorted(i for i in ids if str(i) not in out)
    if missing:
        raise SystemExit("no names for areas %s" % missing)
    io.open(OUT, "w", encoding="utf-8", newline="\n").write(json.dumps(out, ensure_ascii=False, indent=1, sort_keys=True) + "\n")
    print("%d places, written %s" % (len(out), OUT))


if __name__ == "__main__":
    main()
