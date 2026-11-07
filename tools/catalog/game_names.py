"""Names of the catalog's mounts, bosses, instances and reward achievements in the six languages, from the game's own tables.

    python3 tools/catalog/game_names.py [--cache <folder>] [--write]

The game itself gives names only in the client's language, and the catalog has Russian and English. So an addon set to
German on a Russian client would show Russian. This script takes the official names from the game's DB2 tables as
wago.tools publishes them (Mount, JournalEncounter, JournalInstance, Achievement) for every catalog ID and writes
tools/catalog/game_names.json; tools/i18n/build_sheet.py puts it into program/sheet-i18n.json ("names").

Reward rows (achievements, season rewards) get their achievement: the Russian title equals the catalog's boss text, or
the achievement's reward text names the row's mount. Rows without a match are listed; MANUAL below fixes them.
"""
import csv, io, json, os, re, sys, tempfile, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
CATALOG = os.path.join(ROOT, "program", "catalog.json")
OUT = os.path.join(HERE, "game_names.json")
LOCALES = {"ru": "ruRU", "en": "enUS", "de": "deDE", "fr": "frFR", "es": "esES", "pt": "ptBR"}
# the program's rule for rows that are not a boss kill (SheetAttempts.RewardSource)
REWARD = re.compile(r"achievement|достижен|ahead of the curve|mythic\+|challenge mode|timed / chest", re.I)
# mount ID -> achievement ID where the title and the reward text don't tell (0: no achievement, e.g. a chest)
MANUAL = {
    558: 8398,     # Kor'kron War Wolf: Ahead of the Curve: Garrosh Hellscream (the reward text has the mount in another case)
    933: 12812,    # Obsidian Krolusk: Glory of the Wartime Hero
    1520: 15078,   # Corrupted Deathwalker: Shadowlands Keystone Master, season 2
    1801: 19011,   # Dragonflight Keystone Master, season 3
    518: 6375, 503: 6375, 519: 6375, 520: 6375,   # the four Pandaren phoenixes: Challenge Conqueror: Silver
    342: 0, 343: 0,   # Trial of the Grand Crusader: a chest, not an achievement
    764: 0,        # Grove Warden: no single achievement names it
    3005: 0, 3006: 0, 3007: 0, 3008: 0, 3009: 0, 3010: 0,   # bought with the Time-Lost Saddle; the saddle comes from a season achievement
}


def table(name, locale, cache):
    path = os.path.join(cache, "%s-%s.csv" % (name, locale))
    if not os.path.exists(path):
        url = "https://wago.tools/db2/%s/csv?locale=%s" % (name, locale)
        req = urllib.request.Request(url, headers={"User-Agent": "mount-sheet-sync catalog tool"})
        with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
            f.write(r.read())
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(io.StringIO(f.read())))


def ids(value):
    return [int(x) for x in re.findall(r"\d+", str(value or ""))]


def norm(s):
    return re.sub(r"[\W_]+", "", str(s or "").lower().replace("ё", "е"))


def main():
    args = sys.argv[1:]
    cache = args[args.index("--cache") + 1] if "--cache" in args else os.path.join(tempfile.gettempdir(), "mss-wago")
    os.makedirs(cache, exist_ok=True)
    catalog = json.load(open(CATALOG, encoding="utf-8"))
    rows = []
    for sheet in catalog["sheets"]:
        cols = sheet["columns"]
        col = lambda name: cols.index(name) if name in cols else -1
        c_mount, c_id, c_journal, c_map, c_src = col("Маунт"), col("ID в игре"), col("ID в журнале"), col("ID места"), col("Тип источника")
        c_boss = next(i for i, c in enumerate(cols) if c.startswith("Босс"))
        for r in sheet["rows"]:
            rows.append({"mount": r[c_mount], "boss": r[c_boss], "id": (ids(r[c_id]) or [0])[0],
                         "journal": ids(r[c_journal]) if c_journal >= 0 else [], "map": ids(r[c_map]) if c_map >= 0 else [],
                         "reward": c_src >= 0 and bool(REWARD.search(str(r[c_src] or "")))})
    want_mounts = {r["id"] for r in rows if r["id"]}
    want_bosses = {j for r in rows for j in r["journal"]}
    want_maps = {m for r in rows for m in r["map"]}

    out = {"mounts": {}, "bosses": {}, "places": {}, "rewards": {}}
    titles = {}
    for lang, loc in LOCALES.items():
        for t in table("Mount", loc, cache):
            i = int(t["ID"])
            if i in want_mounts and t["Name_lang"]:
                out["mounts"].setdefault(str(i), {})[lang] = t["Name_lang"]
        for t in table("JournalEncounter", loc, cache):
            i = int(t["ID"])
            if i in want_bosses and t["Name_lang"]:
                out["bosses"].setdefault(str(i), {})[lang] = t["Name_lang"]
        for t in table("JournalInstance", loc, cache):
            m = int(t.get("MapID") or 0)
            if m in want_maps and t["Name_lang"] and lang not in out["places"].get(str(m), {}):
                out["places"].setdefault(str(m), {})[lang] = t["Name_lang"]
        titles[lang] = {int(t["ID"]): t for t in table("Achievement", loc, cache)}

    missing = []
    for r in (r for r in rows if r["reward"] and r["id"]):
        mount_ru = out["mounts"].get(str(r["id"]), {}).get("ru") or r["mount"]
        if r["id"] in MANUAL:
            found = [MANUAL[r["id"]]] if MANUAL[r["id"]] else []
        else:
            by_title = [i for i, t in titles["ru"].items() if norm(t["Title_lang"]) == norm(r["boss"])]
            by_reward = [i for i, t in titles["ru"].items() if t["Reward_lang"] and norm(mount_ru) in norm(t["Reward_lang"])]
            both = [i for i in by_title if i in by_reward]
            found = both or (by_reward if len(by_reward) == 1 else []) or (by_title if len(by_title) == 1 else [])
            if len(found) > 1:
                found = [max(found)]   # the same achievement remade: the newest one is in the game's UI
        if not found:
            missing.append("%s | %s" % (r["boss"], r["mount"]))
            continue
        a = found[0]
        out["rewards"][str(r["id"])] = {"id": a, "names": {lang: titles[lang][a]["Title_lang"] for lang in LOCALES if titles[lang].get(a)}}

    # past seasons' mounts are bought with the Timelost Saddle; the newest achievement giving it is this season's
    saddle = [i for i, t in titles["ru"].items() if "Затерянное во времени седло" in (t["Reward_lang"] or "")]
    if saddle:
        a = max(saddle)
        out["saddle"] = {"id": a, "names": {lang: titles[lang][a]["Title_lang"] for lang in LOCALES if titles[lang].get(a)}}
        print("saddle: %d %s" % (a, titles["ru"][a]["Title_lang"]))
    print("mounts %d/%d, bosses %d/%d, places %d/%d, achievements %d/%d"
          % (len(out["mounts"]), len(want_mounts), len(out["bosses"]), len(want_bosses), len(out["places"]), len(want_maps),
             len(out["rewards"]), sum(1 for r in rows if r["reward"] and r["id"])))
    for m in missing:
        print("  no achievement: " + m)
    text = json.dumps(out, ensure_ascii=False, indent=1, sort_keys=True) + "\n"
    if "--write" in args:
        open(OUT, "w", encoding="utf-8", newline="\n").write(text)
        print("written to " + OUT)


if __name__ == "__main__":
    main()
