"""Build the catalog rows of rare-monster mounts from Rarity (rare_scan.json), Wowhead names (npc_names.json) and zones
(rare_zones.json); with --write put them into the repository: program/catalog.json (sheet "rare"),
tools/i18n/sheet_content.txt (ru -> en, and the farming tips in de/fr/es/pt from tip_l10n.py), tools/i18n/mounts_en.txt,
tools/catalog/rare_names.json (names and zones for game_names.py); then it runs game_names.py and build_sheet.py in that
order (sheet-i18n.json is built from game_names.json).

    python3 tools/catalog/rares/rare_rows.py [--wago <mss-wago cache>] [--write]

Inputs: rare_scan.py (Rarity's database, a local copy — not in the repository) -> rare_scan.json; npc_names.py;
rare_zones.py. The wago.tools tables (UiMap, Mount…) are the cache that entrances.py and game_names.py use."""
import csv, io, json, os, re, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
WAGO = sys.argv[sys.argv.index("--wago") + 1] if "--wago" in sys.argv else os.path.join(tempfile.gettempdir(), "mss-wago")
WRITE = "--write" in sys.argv
sys.path.insert(0, HERE)
from tip_l10n import Tips  # noqa: E402
scan = json.load(open(os.path.join(HERE, "rare_scan.json"), encoding="utf-8"))
names = json.load(open(os.path.join(HERE, "npc_names.json"), encoding="utf-8"))
zones_wh = json.load(open(os.path.join(HERE, "rare_zones.json"), encoding="utf-8"))


def table(name, loc):
    with open(os.path.join(WAGO, "%s-%s.csv" % (name, loc)), encoding="utf-8") as f:
        return list(csv.DictReader(io.StringIO(f.read())))


mount = {lang: {int(r["ID"]): r["Name_lang"] for r in table("Mount", loc)} for lang, loc in (("ru", "ruRU"), ("en", "enUS"))}
uimap = {lang: {int(r["ID"]): r["Name_lang"] for r in table("UiMap", loc)} for lang, loc in (("ru", "ruRU"), ("en", "enUS"))}

# (mount ID, [(npc, faction)], expansion, zone uiMap, (x, y) or None, extra ru, extra en)
# x, y: Rarity's point, else Wowhead's when the rare has one spawn point; None: many spawn points
A, H = "Alliance", "Horde"
# ([(npc, faction)], expansion, zone uiMap, (x, y) or None, extra ru, extra en); the mount comes from Rarity by the NPC
SPEC = [
    ([(142692, "")], "Battle for Azeroth", 14, (67.48, 60.58), "", ""),
    ([(142423, "")], "Battle for Azeroth", 14, (33.44, 36.94), "Вход в пещеру — у точки.", "The cave entrance is at the point."),
    ([(142437, "")], "Battle for Azeroth", 14, (57.15, 45.75), "", ""),
    ([(142709, "")], "Battle for Azeroth", 14, (67.06, 65.89), "", ""),
    ([(142741, A)], "Battle for Azeroth", 14, None, "Добыча только для Альянса.", "Alliance only."),
    ([(142739, H)], "Battle for Azeroth", 14, None, "Добыча только для Орды.", "Horde only."),
    ([(148787, "")], "Battle for Azeroth", 62, (56.4, 30.8), "", ""),
    ([(149652, A), (149660, H)], "Battle for Azeroth", 62, None, "", ""),
    ([(149655, A), (149663, H)], "Battle for Azeroth", 62, None, "", ""),
    ([(147701, A), (148037, H)], "Battle for Azeroth", 62, None, "", ""),
    ([(152182, "")], "Battle for Azeroth", 1462, (65.8, 79.6), "", ""),
    ([(154342, "")], "Battle for Azeroth", 1462, (52.0, 41.4), "", ""),
    ([(152290, "")], "Battle for Azeroth", 1355, None, "", ""),
    ([(157466, "")], "Battle for Azeroth", 1530, (34.15, 68.05), "Появляется во время нападений Н'Зота.", "Appears during N'Zoth assaults."),
    ([(157153, "")], "Battle for Azeroth", 1530, (36.6, 37.6), "Появляется во время нападений Н'Зота.", "Appears during N'Zoth assaults."),
    ([(157160, "")], "Battle for Azeroth", 1530, (10.0, 33.0), "Появляется во время нападений Н'Зота.", "Appears during N'Zoth assaults."),
    ([(162147, "")], "Battle for Azeroth", 1527, (30.85, 49.71), "Появляется во время нападений Н'Зота.", "Appears during N'Zoth assaults."),
    ([(157146, "")], "Battle for Azeroth", 1527, (68.23, 31.97), "Появляется во время нападений Н'Зота.", "Appears during N'Zoth assaults."),
    ([(126867, "")], "Legion", 882, (34.01, 47.83), "", ""),
    ([(126852, "")], "Legion", 882, (55.65, 59.95), "", ""),
    ([(126912, "")], "Legion", 882, (49.79, 9.40), "", ""),
    ([(127288, "")], "Legion", 885, (62.96, 24.86), "", ""),
    ([(122958, "")], "Legion", 885, (61.78, 36.97), "", ""),
    ([(126040, ""), (126199, "")], "Legion", 885, (64.42, 20.35), "", ""),
    ([(73167, "")], "Mists of Pandaria", 554, None, "", ""),
    ([(165290, "")], "Shadowlands", 1525, (46.0, 78.5), "Его можно и купить у торговца.", "Can also be bought from a vendor."),
    ([(166679, "")], "Shadowlands", 1525, (51.98, 51.80), "", ""),
    ([(166521, "")], "Shadowlands", 1525, (62.0, 47.0), "", ""),
    ([(162586, "")], "Shadowlands", 1536, (44.2, 51.2), "Падает, только если вашим спутником сейчас конструкт Нина (Некролорды).", "Drops only while the construct Neena is your active companion (Necrolords)."),
    ([(168147, "")], "Shadowlands", 1536, (50.67, 47.37), "Его можно и купить у торговца.", "Can also be bought from a vendor."),
    ([(162741, "")], "Shadowlands", 1536, (31.4, 35.6), "", ""),
    ([(157309, "")], "Shadowlands", 1536, (58.6, 74.2), "", ""),
    ([(162690, "")], "Shadowlands", 1536, (65.8, 35.3), "", ""),
    ([(162819, "")], "Shadowlands", 1536, (33.6, 80.6), "", ""),
    ([(168647, "")], "Shadowlands", 1565, (30.4, 55.2), "Его можно и купить у торговца.", "Can also be bought from a vendor."),
    ([(174861, "")], "Shadowlands", 1543, (53.5, 79.5), "", ""),
    ([(179460, "")], "Shadowlands", 1543, None, "", ""),
    ([(179472, "")], "Shadowlands", 1961, None, "", ""),
    ([(180160, "")], "Shadowlands", 1961, (56.3, 66.3), "", ""),
    ([(179684, "")], "Shadowlands", 1961, (45.35, 30.34), "", ""),
    ([(195353, "")], "Dragonflight", 2024, None, "", ""),
    ([(208029, "")], "Dragonflight", 2024, (27.8, 46.0), "", ""),
    ([(203625, "")], "Dragonflight", 2133, None, "", ""),
    ([(207802, "")], "The War Within", 2215, None, "", ""),
    ([(234621, "")], "The War Within", 2346, (32.2, 21.8), "", ""),
    ([(231310, "")], "The War Within", 2346, (41.2, 41.2), "", ""),
    ([(234845, "")], "The War Within", 2371, None, "", ""),
    ([(232195, "")], "The War Within", 2371, None, "", ""),
]
by_npc = {}
for e in scan:
    if e["mount"]:
        for n in e["npcs"]:
            by_npc.setdefault(n, set()).add(e["mount"][0])
ROWS = []
for npcs, exp, ui, xy, ru, en in SPEC:
    ids = set()
    for n, _ in npcs:
        assert len(by_npc.get(n, ())) == 1, (n, by_npc.get(n))
        ids |= by_npc[n]
    assert len(ids) == 1, (npcs, ids)
    ROWS.append((ids.pop(), npcs, exp, ui, xy, ru, en))
chance_of = {}
quest_of = {}
for e in scan:
    if e["mount"]:
        for n in e["npcs"]:
            chance_of[(e["mount"][0], n)] = e["chance"]
            quest_of[(e["mount"][0], n)] = bool(e["quest"])


def pct(c):
    return round(1.0 / c, 4)


def num(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


rows, content, named, zones = [], [], {}, {}
seen = set()
for mid, npcs, exp, ui, xy, ru_x, en_x in ROWS:
    assert mid not in seen, mid
    seen.add(mid)
    chances = {chance_of.get((mid, n)) for n, _ in npcs}
    assert len(chances) == 1 and None not in chances, (mid, chances)
    c = chances.pop()
    daily = any(quest_of.get((mid, n)) for n, _ in npcs) or exp == "Legion"
    zone_en, zone_ru = uimap["en"][ui], uimap["ru"][ui]
    zones[zone_en] = ui
    boss_ru = " / ".join(names[str(n)]["ru"] for n, _ in npcs)
    boss = {lang: " / ".join(names[str(n)][lang] for n, _ in npcs) for lang in ("en", "de", "fr", "es", "pt")}
    named[boss_ru] = {l: boss[l] for l in ("de", "fr", "es", "pt")}
    ru = "Рар: %s" % zone_ru
    en = "A rare in %s" % zone_en
    if xy:
        ru += ", точка %s, %s" % (num(xy[0]), num(xy[1]))
        en += ", at %s, %s" % (num(xy[0]), num(xy[1]))
    else:
        ru += ", точек появления несколько"
        en += ", several spawn points"
    ru += "."
    en += "."
    if len(npcs) == 2 and npcs[0][1]:
        a = names[str(npcs[0][0])]
        h = names[str(npcs[1][0])]
        ru += " Альянсу — %s, Орде — %s." % (a["ru"], h["ru"])
        en += " Alliance: %s, Horde: %s." % (a["en"], h["en"])
    if ru_x:
        ru += " " + ru_x
        en += " " + en_x
    if daily:
        ru += " Лут раз в день на персонажа."
        en += " Loot once a day per character."
    rows.append([exp, zone_en, mount["ru"][mid], boss_ru, "Ежедневно", ru, pct(c),
                 "https://www.wowhead.com/npc=%d" % npcs[0][0], mid, "", "", "", "", ", ".join(str(n) for n, _ in npcs),
                 ("%d %s %s" % (ui, num(xy[0]), num(xy[1]))) if xy else str(ui), ""])
    content.append((boss_ru, boss["en"]))
    content.append((ru, en))

# ---- rows the addon tells apart by the creature's number, not its name
LANGS = ("en", "de", "fr", "es", "pt")
NO_CD = "Без отката"
content.append((NO_CD, "No cooldown"))
uimap.update({lang: {int(r["ID"]): r["Name_lang"] for r in table("UiMap", loc)} for lang, loc in
              (("de", "deDE"), ("fr", "frFR"), ("es", "esES"), ("pt", "ptBR"))})
scan_by_mount = {}
for e in scan:
    if e["mount"]:
        scan_by_mount.setdefault(e["mount"][0], e)


def special(mid, exp, ui, boss_ru, boss_tr, every, ru, en, npcs, group="", chance=None):
    zones[uimap["en"][ui]] = ui
    named[boss_ru] = {l: boss_tr[l] for l in ("de", "fr", "es", "pt")}
    if chance is None:
        chance = pct(scan_by_mount[mid]["chance"])
    rows.append([exp, uimap["en"][ui], mount["ru"][mid], boss_ru, every, ru, chance,
                 "https://www.wowhead.com/npc=%d" % npcs[0], mid, "", "", "", "", ", ".join(str(n) for n in npcs), str(ui), group])
    content.append((boss_ru, boss_tr["en"]))
    content.append((ru, en))


# Zandalari Warbringers: three look the same, each has its own direhorn; the number is for the sheet
NO = {"ru": "№%d", "en": "#%d", "de": "Nr. %d", "fr": "n° %d", "es": "n.º %d", "pt": "nº %d"}
for k, (mid, npc) in enumerate([(535, 69769), (534, 69841), (536, 69842)], 1):
    tr = {l: "%s %s" % (names[str(npc)][l], NO[l] % k) for l in LANGS}
    special(mid, "Mists of Pandaria", 424, "%s %s" % (names[str(npc)]["ru"], NO["ru"] % k), tr, NO_CD,
            "Появляется в разных зонах Пандарии. Три завоевателя на вид одинаковы, у каждого свой ящер; номер — только для таблицы, аддон различает их сам. Отката добычи нет.",
            "Appears in several zones of Pandaria. The three Warbringers look the same, each has its own direhorn; the number is only for the sheet, the addon tells them apart. No loot cooldown.",
            [npc])

# Tanaan Jungle: any of the four rares drops any of the three mounts; each of them gives loot once a day
TANAAN = [95053, 95044, 95056, 95054]
t_ru = " / ".join(names[str(n)]["ru"] for n in TANAAN)
t_tr = {l: " / ".join(names[str(n)][l] for n in TANAAN) for l in LANGS}
for mid in (622, 611, 643):
    special(mid, "Warlords of Draenor", 534, t_ru, t_tr, NO_CD,
            "Четыре рара Танаанских джунглей, маунт падает с любого из них. Добыча с каждого — раз в день на персонажа, поэтому общего отката у строки нет.",
            "Four rares of Tanaan Jungle; the mount drops from any of them. Each one gives loot once a day per character, so the row has no cooldown of its own.",
            TANAAN)

# Ol' Mole Rufus: the Lurker of the Deeps has to be summoned
special(2205, "The War Within", 2214, names["220285"]["ru"], {l: names["220285"][l] for l in LANGS}, "Ежедневно",
        "Рара нужно призвать: пять игроков почти одновременно (за ~10 секунд) дёргают пять неприметных рычагов по Звенящим глубинам, рар появляется у озера через минуту. Лут раз в день на персонажа.",
        "It has to be summoned: five players pull the five Inconspicuous Levers around the Ringing Deeps within about 10 seconds; it appears at the lake a minute later. Loot once a day per character.",
        [220285])

# Midnight: any rare of the zone
ANY = {"ru": "Любой редкий монстр: %s", "en": "Any rare: %s", "de": "Beliebiger seltener Gegner: %s",
       "fr": "N'importe quel monstre rare : %s", "es": "Cualquier monstruo raro: %s", "pt": "Qualquer monstro raro: %s"}
# rares Rarity does not list, from Wowhead's "dropped by" of the mount's item (checked 10.11.2026: the player saw
# Hardin Steellock in Voidstorm, and the row did not know him)
VOIDSTORM_MORE = [257199, 248459, 248700, 257231, 241443, 248068, 248791, 248823]
MORE_NPCS = {2827: VOIDSTORM_MORE, 2751: VOIDSTORM_MORE}
for mid in (2827, 2751, 2760, 2775, 2762, 2758, 2708, 2615):
    e = scan_by_mount[mid]
    ui = [c[1] for c in e["coords"] if c[1]][0]
    npcs = sorted(set(e["npcs"]) | set(MORE_NPCS.get(mid, [])))
    special(mid, "Midnight", ui, ANY["ru"] % uimap["ru"][ui], {l: ANY[l] % uimap[l][ui] for l in LANGS}, NO_CD,
            "Падает с любого редкого монстра зоны (их здесь %d). Добыча с каждого — раз в день на персонажа, поэтому общего отката у строки нет." % len(npcs),
            "Drops from any rare of the zone (%d of them). Each one gives loot once a day per character, so the row has no cooldown of its own." % len(npcs),
            npcs)

# Forbidden Reach rares and the Theater of Pain champions: any of them (like the Midnight zones)
e = scan_by_mount[1619]
fr_npcs = sorted(set(e["npcs"]))
special(1619, "Dragonflight", 2151, ANY["ru"] % uimap["ru"][2151], {l: ANY[l] % uimap[l][2151] for l in LANGS}, NO_CD,
        "Падает с любого редкого монстра зоны (их здесь %d). Добыча с каждого — раз в день на персонажа, поэтому общего отката у строки нет." % len(fr_npcs),
        "Drops from any rare of the zone (%d of them). Each one gives loot once a day per character, so the row has no cooldown of its own." % len(fr_npcs),
        fr_npcs)
THEATER = {"ru": "Любой боец Театра Боли", "en": "Any Theater of Pain champion", "de": "Beliebiger Champion des Theaters der Schmerzen",
           "fr": "N'importe quel champion du Théâtre de la Souffrance", "es": "Cualquier campeón del Teatro del Dolor", "pt": "Qualquer campeão do Teatro da Dor"}
special(1437, "Shadowlands", 1536, THEATER["ru"], THEATER, NO_CD,
        "Бойцы Театра Боли в Малдраксусе выходят на арену по очереди; маунт падает с любого из них.",
        "The Theater of Pain champions in Maldraxxus come out to the arena in turn; the mount drops from any of them.",
        sorted(set(scan_by_mount[1437]["npcs"])))

# common mobs (about 1/3000): no cooldown, every kill is an attempt — the «Фарм мобов» group of the window
MOBS = {"ru": "Обычные мобы: %s", "en": "Common mobs: %s", "de": "Gewöhnliche Gegner: %s", "fr": "Monstres communs : %s",
        "es": "Enemigos comunes: %s", "pt": "Inimigos comuns: %s"}
for mid, ui in ((928, 864), (1018, 896), (956, 863), (1019, 942)):
    e = scan_by_mount[mid]
    npcs = sorted(set(e["npcs"]))
    some_ru = ", ".join(names[str(n)]["ru"] for n in npcs[:3] if str(n) in names)
    some_en = ", ".join(names[str(n)]["en"] for n in npcs[:3] if str(n) in names)
    special(mid, "Battle for Azeroth", ui, MOBS["ru"] % uimap["ru"][ui], {l: MOBS[l] % uimap[l][ui] for l in LANGS}, NO_CD,
            "Падает с обычных мобов зоны (видов %d), например: %s. Отката нет: каждое убийство — попытка." % (len(npcs), some_ru),
            "Drops from the zone's common mobs (%d kinds), e.g. %s. No cooldown: every kill is an attempt." % (len(npcs), some_en),
            npcs, group="моб")

# rares with a guaranteed drop: rare to find, not to loot — the «100 %» group, no attempts (the chance is a word)
GUARANTEED = [
    (265, 32491, "Wrath of the Lich King", 120,
     "Летает по Грозовой Гряде, появляется раз в 6–24 часа (вместо него может появиться Вирагоса). Маунт падает всегда.",
     "Flies around the Storm Peaks, appears every 6-24 hours (Vyragosa may come instead). The mount always drops."),
    (393, 50062, "Cataclysm", 207,
     "Летает по Подземью; появляется раз в несколько часов, его нужно оседлать и победить. Маунт падает всегда.",
     "Flies around Deepholm; appears every few hours, you have to mount it and win. The mount always drops."),
    (657, 81001, "Warlords of Draenor", 525,
     "Хребет Ледяного Огня, появляется редко. Маунт падает всегда.",
     "Frostfire Ridge, appears rarely. The mount always drops."),
    (627, 50992, "Warlords of Draenor", 525,
     "Хребет Ледяного Огня, появляется редко. Маунт падает всегда.",
     "Frostfire Ridge, appears rarely. The mount always drops."),
    (655, 50985, "Warlords of Draenor", 543,
     "Горгронд, появляется раз в много часов. Маунт падает всегда.",
     "Gorgrond, appears once in many hours. The mount always drops."),
    (517, 64403, "Mists of Pandaria", 390,
     "Вечноцветущий дол (фаза Пандарии): щит снимает Небесный кристалл из 10 небесных осколков, они падают с мобов дола. Маунт падает всегда.",
     "Vale of Eternal Blossoms (the Pandaria phase): a Sky Crystal made of 10 Skyshards, dropped by the Vale's mobs, takes down her shield. The mount always drops."),
    # 10.11.2026: not in Rarity (it skips sure drops); Wowhead loot: Poseidus 99 %, the Draenor four 100 %, Night Mare 86 %, Dormus 99.9 %
    (420, 50005, "Cataclysm", 203,
     "Плавает по Вайш'иру (%s, %s), появляется раз в несколько часов. Маунт падает всегда." % (uimap["ru"][205], uimap["ru"][204]),
     "Swims around Vashj'ir (Shimmering Expanse, Abyssal Depths), appears every few hours. The mount always drops."),
    (612, 50990, "Warlords of Draenor", 550,
     "%s (Дренор), появляется раз в много часов. Маунт падает всегда." % uimap["ru"][550],
     "Nagrand (Draenor), appears once in many hours. The mount always drops."),
    (614, 50981, "Warlords of Draenor", 550,
     "%s (Дренор), появляется раз в много часов. Маунт падает всегда." % uimap["ru"][550],
     "Nagrand (Draenor), appears once in many hours. The mount always drops."),
    (630, 51015, "Warlords of Draenor", 535,
     "%s, появляется раз в много часов. Маунт падает всегда." % uimap["ru"][535],
     "Talador, appears once in many hours. The mount always drops."),
    (636, 50883, "Warlords of Draenor", 539,
     "%s (Дренор), появляется раз в много часов. Маунт падает всегда." % uimap["ru"][539],
     "Shadowmoon Valley (Draenor), appears once in many hours. The mount always drops."),
    (1306, 168135, "Shadowlands", 1565,
     "%s: появляется после короткой цепочки заданий (подробности — на Wowhead). Маунт падает почти всегда." % uimap["ru"][1565],
     "Ardenweald: appears after a short quest chain (details on Wowhead). The mount almost always drops."),
    (400, 50245, "Cataclysm", 249,
     "Сначала найти фигурку верблюда (%s; появляется редко и в разных местах): она перенесёт к Дормусу. Маунт падает всегда." % uimap["ru"][249],
     "First find a camel figurine in Uldum (it appears rarely, in different places): it takes you to Dormus. The mount always drops."),
]
for mid, npc, exp, ui, ru, en in GUARANTEED:
    special(mid, exp, ui, names[str(npc)]["ru"], {l: names[str(npc)][l] for l in LANGS}, NO_CD, ru, en, [npc],
            group="100", chance="Гарантированно")

# 10.11.2026: rares that Rarity does not track (found from the game's mount table + Wowhead loot: count / outof)
OUTSIDE = [
    (1434, [180978], "Shadowlands", 1970, 620 / 13508,
     "Появляется, только если призвать приманкой (Aurelid Lure; подробности — на Wowhead).",
     "Comes only when summoned with a lure (Aurelid Lure; details on Wowhead)."),
    (2176, [219281], "The War Within", 2248, 410 / 636,
     "Появляется на Острове Дорн нечасто (подробности — на Wowhead).",
     "Appears on the Isle of Dorn now and then (details on Wowhead)."),
    (1057, [140474], "Battle for Azeroth", 942, 1326 / 2345,
     "Его призывают (подробности — на Wowhead).",
     "It has to be summoned (details on Wowhead)."),
    (1415, [164112, 164122], "Shadowlands", 1565, 3702 / 5178,
     "Вырастает из Быстрорастущего гриба (подробности — на Wowhead).",
     "Grows out of a Rapidly Growing Mushroom (details on Wowhead)."),
]
for mid, npcs, exp, ui, chance, ru, en in OUTSIDE:
    special(mid, exp, ui, names[str(npcs[0])]["ru"], {l: names[str(npcs[0])][l] for l in LANGS}, "Ежедневно", ru, en, npcs,
            chance=round(chance, 4))
# The Coiled Isle (Midnight): two mounts from any of its rares, a very rare drop
COILED = sorted({257863, 257906, 263456, 268049, 261142, 255088, 255087, 265237, 268090,
                 255927, 256631, 258920, 261109, 265262, 258916})
for mid in (3051, 3061):
    special(mid, "Midnight", 2512, ANY["ru"] % uimap["ru"][2512], {l: ANY[l] % uimap[l][2512] for l in LANGS}, NO_CD,
            "Падает с любого редкого монстра острова (их здесь %d), очень редко. Добыча с каждого — раз в день на персонажа, поэтому общего отката у строки нет." % len(COILED),
            "Drops from any rare of the isle (%d of them), very rarely. Each one gives loot once a day per character, so the row has no cooldown of its own." % len(COILED),
            COILED, chance=round({3051: 9 / 14349, 3061: 11 / 14013}[mid], 4))

order = ["Classic", "The Burning Crusade", "Wrath of the Lich King", "Cataclysm", "Mists of Pandaria", "Warlords of Draenor", "Legion", "Battle for Azeroth", "Shadowlands", "Dragonflight", "The War Within", "Midnight"]
rows.sort(key=lambda r: (order.index(r[0]), r[1], r[3]))
for r in rows:
    print(r[0][:6], "|", r[1], "|", r[2], "|", r[3][:50], "|", r[4], "|", r[6], "|", r[13][:30])
print(len(rows), "rows")

if WRITE:
    p = os.path.join(REPO, "program", "catalog.json")
    s = open(p, encoding="utf-8").read()
    cat = json.loads(s)
    rare = [x for x in cat["sheets"] if x["key"] == "rare"][0]
    cols = rare["columns"]
    base = cols.index("ID статистики") + 1
    ishak = [r for r in rare["rows"] if r[3] == "Исхак Повелитель Четырех Ветров"][0][:base] + ["157134", "1527 73.96 83.52", ""]
    cols = cols[:base] + ["ID существ", "Точка", "Вид"]
    a = s.index('    {"sheet": "Редкие монстры"')
    b = s.index("]}", a) + 2
    block = ('    {"sheet": "Редкие монстры", "key": "rare", "columns": ' + json.dumps(cols, ensure_ascii=False) + ', "rows": [\n'
             + ",\n".join("      " + json.dumps(r, ensure_ascii=False) for r in [ishak] + rows) + "\n    ]}")
    s = s[:a] + block + s[b:]
    s = s.replace('"version": "2026-11-02"', '"version": "2026-11-10"', 1)
    json.loads(s)
    open(p, "w", encoding="utf-8", newline="\n").write(s)

    p = os.path.join(REPO, "tools", "i18n", "sheet_content.txt")
    s = open(p, encoding="utf-8").read()
    have = set(re.findall(r"^@ (.*)$", s, re.M))
    extra = ""
    for ru, en in content:
        if ru not in have:
            have.add(ru)
            extra += "\n@ %s\nen: %s\n" % (ru, en)
    s = s.rstrip("\n") + "\n" + extra
    # the farming tips in German, French, Spanish and Portuguese (other texts there stay English)
    i_tip = cols.index("Совет по фарму")
    en_of = dict(content)
    tips = Tips(uimap, names)
    for r in [ishak] + rows:
        ru = r[i_tip]
        m = re.search(r"^@ " + re.escape(ru) + r"\n((?:[a-z]{2}: .*\n?)*)", s, re.M)
        assert m, ru
        en = re.search(r"^en: (.*)$", m.group(1), re.M).group(1)
        lines = "en: %s\n" % en + "".join("%s: %s\n" % (l, t) for l, t in tips.translate(en).items())
        s = s[:m.start(1)] + lines + s[m.end(1):]
    open(p, "w", encoding="utf-8", newline="\n").write(s.rstrip("\n") + "\n")

    p = os.path.join(REPO, "tools", "i18n", "mounts_en.txt")
    s = open(p, encoding="utf-8").read()
    have = set(int(x) for x in re.findall(r"^(\d+)\t", s, re.M))
    extra = "".join("%d\t%s\n" % (r[8], mount["en"][r[8]]) for r in rows if r[8] not in have)
    open(p, "w", encoding="utf-8", newline="\n").write(s + extra)

    out = {"note": "Rare monsters: official names from Wowhead tooltips (npc ID -> name), zones -> UiMap. Loaded by game_names.py.",
           "names": dict(sorted(named.items())), "zones": dict(sorted(zones.items()))}
    json.dump(out, open(os.path.join(REPO, "tools", "catalog", "rare_names.json"), "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
    for tool in (["catalog", "game_names.py", "--cache", WAGO, "--write"], ["i18n", "build_sheet.py"]):
        subprocess.run([sys.executable, "-X", "utf8", os.path.join(REPO, "tools", tool[0], tool[1])] + tool[2:], check=True)
    print("written")
