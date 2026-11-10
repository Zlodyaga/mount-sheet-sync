"""Official rare names in six languages from Wowhead tooltips: npc id -> {lang: name}. Output: npc_names.json.

    python3 tools/catalog/rares/npc_names.py <npc ID>,<npc ID>,…"""
import json, os, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
LOC = {"en": 0, "fr": 2, "de": 3, "es": 6, "ru": 7, "pt": 8}
ids = [int(x) for x in sys.argv[1].split(",")]
path = os.path.join(HERE, "npc_names.json")
out = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
for i in ids:
    for lang, n in LOC.items():
        if out.get(str(i), {}).get(lang):
            continue
        url = "https://nether.wowhead.com/tooltip/npc/%d?locale=%d" % (i, n)
        req = urllib.request.Request(url, headers={"User-Agent": "mount-sheet-sync catalog tool"})
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                data = json.loads(r.read().decode("utf-8"))
            name = data.get("name")
            if isinstance(name, str) and name:
                out.setdefault(str(i), {})[lang] = name
        except Exception as e:
            print("fail", i, lang, e)
        time.sleep(0.15)
    json.dump(out, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
for i in ids:
    print(i, out.get(str(i), {}).get("en"), "|", out.get(str(i), {}).get("ru"), "|", len(out.get(str(i), {})))
