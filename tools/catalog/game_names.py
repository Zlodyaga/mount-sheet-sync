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

# Names the game's tables don't carry: creatures and chests (official names from Wowhead tooltips).
NAMED = {
    "Осквернитель из рода Бесконечности": {"de": "Ewiger Verderber", "fr": "Corrupteur infini", "es": "Corruptor Infinito", "pt": "Corruptor Infinito"},
    "Исхак Повелитель Четырех Ветров": {"de": "Ishak von den Vier Winden", "fr": "Ishak des Quatre vents", "es": "Ishak de los Cuatro Vientos", "pt": "Ishak dos Quatro Ventos"},
    "Ночная Погибель": {"de": "Schrecken der Nacht", "fr": "Plaie-de-Nuit", "es": "Nocturno", "pt": "Nocturno"},
    "Сундук хранителя силовых линий": {"de": "Eregos' Lager", "fr": "Cache d'Eregos", "es": "Alijo de Eregos", "pt": "Baú de Éregos"},
    "Мешок Каши": {"de": "Kashas Tasche", "fr": "Sac de Kasha", "es": "Bolsa de Kasha", "pt": "Sacola da Kasha"},
    # descriptions of a row, not names in the game: translated by hand
    "Обычные NPC внутри AQ40": {"de": "Normale NPCs in AQ40", "fr": "PNJ ordinaires dans AQ40", "es": "PNJ normales en AQ40", "pt": "PNJs comuns em AQ40"},
    "Старый тайм-ран в Зул'Амане": {"de": "Alter Zeitlauf in Zul'Aman", "fr": "Ancienne course contre la montre à Zul'Aman", "es": "Antigua contrarreloj de Zul'Aman", "pt": "Antiga corrida contra o tempo em Zul'Aman"},
    "Любой босс подземелий путешествий во времени": {"de": "Beliebiger Boss in Zeitwanderungsdungeons", "fr": "N'importe quel boss des donjons des Marcheurs du temps", "es": "Cualquier jefe de mazmorras de Paseo en el tiempo", "pt": "Qualquer chefe de masmorras de Caminhada Temporal"},
}
ANY_BOSS = {"de": "Beliebiger Boss: %s", "fr": "N'importe quel boss : %s", "es": "Cualquier jefe: %s", "pt": "Qualquer chefe: %s"}
TASTY = {"de": "Leckerer Sporenfresser (%s)", "fr": "Brouteur de spores savoureux (%s)", "es": "Comeesporas sabroso (%s)", "pt": "Pastador de esporos saboroso (%s)"}
ANY_DUNGEON = {"ru": "Любое подземелье путешествий во времени", "de": "Beliebiger Zeitwanderungsdungeon", "fr": "N'importe quel donjon des Marcheurs du temps", "es": "Cualquier mazmorra de Paseo en el tiempo", "pt": "Qualquer masmorra de Caminhada Temporal"}
ZONES = {"Kun-Lai Summit": 379, "Valley of the Four Winds": 376, "Isle of Giants": 507, "Isle of Thunder": 504, "Spires of Arak": 542, "Vol'dun": 864, "Uldum": 1527}
ARCHAVON, TORGHAST_AREA, RAZUVIOUS, ROTMIRE = 753, 10472, 1607, 2711


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

    out = {"mounts": {}, "bosses": {}, "places": {}, "rewards": {}, "conditions": {}, "texts": {}}
    titles = {}
    extra = {"zone": {}, "archavon": {}, "torghast": {}, "razuvious": {}, "rotmire": {}}
    for lang, loc in LOCALES.items():
        for t in table("UiMap", loc, cache):
            for place, i in ZONES.items():
                if int(t["ID"]) == i:
                    extra["zone"].setdefault(place, {})[lang] = t["Name_lang"]
        for t in table("AreaTable", loc, cache):
            if int(t["ID"]) == TORGHAST_AREA:
                extra["torghast"][lang] = t["AreaName_lang"]
        for t in table("Mount", loc, cache):
            i = int(t["ID"])
            if i in want_mounts and t["Name_lang"]:
                out["mounts"].setdefault(str(i), {})[lang] = t["Name_lang"]
        for t in table("JournalEncounter", loc, cache):
            i = int(t["ID"])
            if i in want_bosses and t["Name_lang"]:
                out["bosses"].setdefault(str(i), {})[lang] = t["Name_lang"]
            if i == RAZUVIOUS:
                extra["razuvious"][lang] = t["Name_lang"]
            if i == ROTMIRE:
                extra["rotmire"][lang] = t["Name_lang"]
        for t in table("JournalInstance", loc, cache):
            if int(t["ID"]) == ARCHAVON:
                extra["archavon"][lang] = t["Name_lang"]
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
    # the achievement's requirement: the game gives it only in the client's language
    wanted = {r["id"] for r in out["rewards"].values()} | ({out["saddle"]["id"]} if "saddle" in out else set())
    for a in wanted:
        out["conditions"][str(a)] = {lang: titles[lang][a]["Description_lang"] for lang in LOCALES if titles[lang].get(a) and titles[lang][a]["Description_lang"]}

    texts = out["texts"]
    texts.update(NAMED)
    four = ("de", "fr", "es", "pt")
    short = lambda name: name.split(",")[0].split("،")[0].strip()
    texts["Любой босс Склепа Аркавона"] = {l: ANY_BOSS[l] % extra["archavon"][l] for l in four}
    texts["Боссы Торгаста"] = {l: ANY_BOSS[l] % short(extra["torghast"][l]) for l in four}
    texts["Вкусный споролакомец из Гнилотопи"] = {l: TASTY[l] % extra["rotmire"][l] for l in four}
    texts["Инструктор Разувий (секрет с конём Храбрецом)"] = {l: extra["razuvious"][l] for l in four}
    serpent = next(r for r in rows if r["boss"] == "Любопытный слизневый змей")
    texts["Любопытный слизневый змей"] = {l: out["mounts"][str(serpent["id"])][l] for l in four}
    texts.update(extra["zone"])
    texts["Vault of Archavon"] = extra["archavon"]
    texts["Torghast, Tower of the Damned"] = extra["torghast"]
    texts["Any Timewalking Dungeon"] = ANY_DUNGEON
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
