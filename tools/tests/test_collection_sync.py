"""CollectionSync (C#) on an in-memory book: hidden «ID в игре» column, names «как в игре», «Собрано?» by mountID,
rows of collected mounts removed from character sheets; MarkCollected with mountID.

  python3 test_collection_sync.py      (needs tools/cs_host/CollectionHost.exe)
"""
import copy, json, os, subprocess, sys, tempfile
import os
import paths

HOST = os.path.join(paths.HOSTS, "CollectionHost.exe")
TMP = tempfile.mkdtemp(prefix="mss-cs-")
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1800]))


def run(job):
    path = os.path.join(TMP, "job.json")
    json.dump(job, open(path, "w", encoding="utf-8"), ensure_ascii=False)
    p = subprocess.run(["mono", HOST, path], capture_output=True)
    if p.returncode != 0:
        print(p.stderr.decode("utf-8", "replace"))
    return json.loads(p.stdout.decode("utf-8"))


ID = "ID в игре"
RC = ["Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Статус", ID]
DC = ["Аддон", "Подземелье / активность", "Маунт", "Тип источника", "Босс / достижение / условие", "Статус", ID]
WC = ["Аддон", "Локация", "Маунт", "Босс / достижение / сундук", ID]
CATALOG = {
    "version": "test",
    "sheets": [
        {"key": "raids", "sheet": "Рейды", "columns": RC, "rows": [
            ["TBC", "Tempest Keep", "Пепел Ал'ара", "Босс-дроп", "Кель'тас Солнечный Скиталец", "Фармится", 69],
            ["WotLK", "Ulduar", "Разрушитель-железноног", "Босс-дроп", "Мимирон", "Фармится", 613],
            ["TBC", "Zul'Aman", "Аманийский боевой медведь", "Достижение", "Старинное", "Недоступен", 199],
            ["DF", "Amirdrassil", "Без ID в каталоге", "Босс-дроп", "Фиракк", "Фармится", ""],
        ]},
        {"key": "dungeons", "sheet": "Подземелья", "columns": DC, "rows": [
            ["Cata", "Zul'Aman", "Аманийский боевой медведь (Cataclysm)", "Босс-дроп", "Мешок Каши", "Фармится", 419],
            ["Classic", "Stratholme", "Конь смерти Ривендера", "Босс-дроп", "Лорд Аурий Ривендер", "Фармится", 1001],
            ["WotLK", "Utgarde Pinnacle", "Синий протодракон", "Босс-дроп", "Скади Безжалостный", "Фармится", 363],
        ]},
        {"key": "world", "sheet": "Мировые боссы", "columns": WC, "rows": [
            ["MoP", "Valley", "Сын Галлеона", "Галлеон", 515],
            ["MoP", "Kun-Lai", "Небесный ониксовый облачный змей", "Ша Злости", 517],
        ]},
    ],
    "aliases": {"613": ["Железнокопытный разрушитель"], "1001": ["Поводья коня смерти"]},
}
# a table made by 2.0 (no ID column, old names); master columns as the program writes them (subset)
RH = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Статус"]
DH = ["Собрано?", "Аддон", "Подземелье / активность", "Маунт", "Тип источника", "Босс / достижение / условие", "Статус"]
CH = ["Пытался на этой неделе?", "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", "Сложность / режим", "Скип есть?"]
SHEETS = [
    {"name": "Рейды", "rows": [RH,
        ["Нет", "TBC", "Tempest Keep", "Пепел Ал'ара", "Босс-дроп", "Кель'тас Солнечный Скиталец", "Фармится"],
        ["Нет", "WotLK", "Ulduar", "Железнокопытный разрушитель", "Босс-дроп", "Мимирон", "Фармится"],
        ["Нет", "TBC", "Zul'Aman", "Аманийский боевой медведь", "Достижение", "Старинное", "Недоступен"],
        ["Нет", "DF", "Amirdrassil", "Без ID в каталоге", "Босс-дроп", "Фиракк", "Фармится"],
        ["Недоступно", "WotLK", "Ulduar", "Мимиронова голова", "Босс-дроп", "Йогг-Сарон", "Фармится"],
        ["Нет", "WotLK", "Ulduar", "Гнедой конь", "Босс-дроп", "Кто-то", "Фармится"],
    ], "formulas": [[6, 0]]},
    {"name": "Рейды Эйра", "rows": [CH,
        ["Нет", "TBC", "Tempest Keep", "Пепел Ал'ара", "Кель'тас Солнечный Скиталец", "Любая", "Нет"],
        ["Да", "WotLK", "Ulduar", "Железнокопытный разрушитель", "Мимирон", "25", "Нет"],
        ["Нет", "SL", "Sepulcher", "Смотритель Зерета", "Тюремщик", "Эпохальный", "Да"],
    ]},
    {"name": "Рейды Мирна", "rows": [CH,
        ["Нет", "TBC", "Tempest Keep", "Пепел Ал'ара", "Кель'тас Солнечный Скиталец", "Любая", "Нет"],
        ["Нет", "SL", "Sepulcher", "Смотритель Зерета", "Тюремщик", "Эпохальный", "Да"],
    ], "formulas": [[2, 0]]},
    {"name": "Подземелья", "rows": [DH,
        ["Нет", "Cata", "Zul'Aman", "Аманийский боевой медведь (Cataclysm)", "Босс-дроп", "Мешок Каши", "Фармится"],
        ["Нет", "Classic", "Stratholme", "Поводья коня смерти", "Босс-дроп", "Лорд Аурий Ривендер", "Фармится"],
        ["Нет", "WotLK", "Utgarde Pinnacle", "Синий протодракон", "Босс-дроп", "Скади Безжалостный", "Фармится"],
    ]},
    {"name": "Подземелья Эйра", "rows": [
        ["Пытался на этой неделе?", "Аддон", "Подземелье / активность", "Маунт", "Босс / достижение / условие", "Сложность / режим", "Скип / маршрут"],
        ["Нет", "Classic", "Stratholme", "Поводья коня смерти", "Лорд Аурий Ривендер", "Normal", ""],
        ["Нет", "WotLK", "Utgarde Pinnacle", "Синий протодракон", "Скади Безжалостный", "Heroic", ""],
        ["Нет", "Cata", "Zul'Aman", "Аманийский боевой медведь (Cataclysm)", "Мешок Каши", "Heroic", ""],
        ["Нет", "TBC", "Zul'Aman", "Аманийский боевой медведь", "Старинное", "", ""],
    ]},
    {"name": "Мировые боссы", "rows": [
        ["Собрано?", "Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "Эйра"],
        ["Нет", "MoP", "Valley", "Сын Галлеона", "Галлеон", False],
        ["Нет", "MoP", "Kun-Lai", "Небесный ониксовый облачный змей", "Ша Злости", True],
    ]},
    {"name": "Мои маунты", "rows": [["Собрано?", "Маунт"], [False, "Гнедой конь"], [False, "Чей-то конь"]]},
    {"name": "Заметки", "rows": [["Маунт", "Пытался"], ["Пепел Ал'ара", "Нет"]]},
]
GAME = {"ids": [69, 613, 419, 1001, 517, 6], "updated": 1792657686,
        "names": ["Пепел Ал'ара", "Разрушитель-железноног", "Аманийский боевой медведь", "Конь смерти Ривендера",
                  "Небесный ониксовый облачный змей", "Гнедой конь"]}


def sheet(out, name):
    return [s for s in out["sheets"] if s["name"] == name][0]


def col(rows, title):
    return rows[0].index(title)


out = run({"catalog": CATALOG, "sheets": SHEETS, "game": GAME})
res = out["result"]
raids = sheet(out, "Рейды")["rows"]
dung = sheet(out, "Подземелья")["rows"]

# 1. hidden ID column on old masters
check("ID column added to «Рейды» and «Подземелья», hidden; not to «Мировые боссы» or user sheets",
      out["structure"][:2] == ["column Рейды hidden", "column Подземелья hidden"] and res["idColumns"] == ["Рейды", "Подземелья"],
      out["structure"])
ci = col(raids, ID)
check("raid IDs from the catalog: by name, by old name, unknown left empty",
      [r[ci] for r in raids[1:]] == [69, 613, 199, "", "", ""], [r[ci] for r in raids])
check("dungeon IDs: the «(Cataclysm)» bear is 419, the renamed Deathcharger 1001",
      [r[col(dung, ID)] for r in dung[1:]] == [419, 1001, 363], [r[col(dung, ID)] for r in dung])

# 2. names «как в игре»
check("old catalog names replaced on the master sheets",
      raids[2][3] == "Разрушитель-железноног" and dung[2][3] == "Конь смерти Ривендера", (raids[2], dung[2]))
check("…and on character sheets (before their rows go away)", any(w["value"] == "Конь смерти Ривендера" and w["sheet"] == "Подземелья Эйра" for w in out["writes"]),
      [w for w in out["writes"] if "Эйра" in w["sheet"]])
check("rename count: 2 master cells + 2 character cells", res["renamed"] == 4, res)

# 3. «Собрано?» by mountID
yes = {r[3]: r[0] for r in raids[1:]}
check("collected by ID → «Да»", yes["Пепел Ал'ара"] == "Да" and yes["Разрушитель-железноног"] == "Да", yes)
check("same game name, other ID (old TBC bear 199) stays «Нет»", yes["Аманийский боевой медведь"] == "Нет", yes)
check("«Недоступно» and a formula cell are not touched", yes["Мимиронова голова"] == "Недоступно" and yes["Гнедой конь"] == "Нет", yes)
check("row without ID in the catalog: by name (not collected → «Нет»)", yes["Без ID в каталоге"] == "Нет")
dyes = {r[3]: r[0] for r in dung[1:]}
check("dungeons: Cataclysm bear (419) and the Deathcharger «Да», blue proto-drake «Нет»",
      dyes == {"Аманийский боевой медведь (Cataclysm)": "Да", "Конь смерти Ривендера": "Да", "Синий протодракон": "Нет"}, dyes)
world = sheet(out, "Мировые боссы")["rows"]
check("world bosses: by catalog ID without an ID column", world[1][0] == "Нет" and world[2][0] == "Да", world)
mine = sheet(out, "Мои маунты")["rows"]
check("user's own sheet: by name, checkbox stays a checkbox", mine[1][0] is True and mine[2][0] is False, mine)
check("summary lists the marked mounts", len(res["marked"]) == 6 and res["summary"].startswith("«Собрано?» = «Да»: 6"), res)

# 4. character sheets cleaned
eira = sheet(out, "Рейды Эйра")["rows"]
mim = sheet(out, "Рейды Мирна")
check("«Рейды Эйра»: collected mounts removed, header and the rest kept",
      [r[3] for r in eira] == ["Маунт", "Смотритель Зерета"], eira)
check("«Рейды Мирна»: formula row moved up with its row", [r[3] for r in mim["rows"]] == ["Маунт", "Смотритель Зерета"]
      and mim["formulas"] == [[1, 0]], mim)
de = sheet(out, "Подземелья Эйра")["rows"]
check("«Подземелья Эйра»: exact names only — the note «(Cataclysm)» tells the two bears apart",
      [r[3] for r in de[1:]] == ["Синий протодракон", "Аманийский боевой медведь"], de)
check("sheets that are not character sheets are not cleaned", sheet(out, "Заметки")["rows"][1][0] == "Пепел Ал'ара")
check("removed rows counted, deletes recorded per sheet", res["removed"] == 5
      and "delete Рейды Эйра 2,3" in out["structure"] and "delete Подземелья Эйра 2,4" in out["structure"], (res, out["structure"]))

# 5. second run: nothing to do
again = run({"catalog": CATALOG, "sheets": out["sheets"], "game": GAME})
check("second run changes nothing", not again["result"]["changed"] and again["writes"] == [] and again["structure"] == []
      and again["result"]["summary"] == "всё уже совпадает с коллекцией", again["result"])

# 6. never back to «Нет»
g2 = dict(GAME, ids=[], names=[])
back = run({"catalog": CATALOG, "sheets": out["sheets"], "game": g2})
check("empty collection: no «Нет» written back", back["writes"] == [] and not back["result"]["changed"], back["result"])

# 7. a «Да» typed by hand on the master → rows removed (remove only)
manual = copy.deepcopy(out["sheets"])
r = [s for s in manual if s["name"] == "Подземелья"][0]["rows"]
r[3][0] = "да"
rm = run({"catalog": CATALOG, "sheets": manual, "mode": "remove"})
check("hand-made «да» on the master removes the mount from character sheets",
      rm["removed"] == 1 and [x[3] for x in sheet(rm, "Подземелья Эйра")["rows"][1:]] == ["Аманийский боевой медведь"], rm["structure"])

# 8. new mount from the game: the ID column decides between two mounts with the same name
req = {"action": "kills", "client": "exe-2.1.0", "kills": [
    {"id": "C-1", "char": "Эйра", "realm": "Ревущий фьорд", "kind": "mount", "boss": "Аманийский боевой медведь",
     "mount": "Аманийский боевой медведь", "mountID": 199, "from": "Старинное", "time": 1792657700}]}
h = run({"catalog": CATALOG, "sheets": out["sheets"], "mode": "handle", "request": req})
cells = h["reply"]["results"][0]["cells"]
check("mount record with mountID marks only the row with that ID", h["reply"]["results"][0]["status"] == "collected"
      and [(c["sheet"], c["row"]) for c in cells] == [("Рейды", 4)], cells)
req["kills"][0]["mountID"] = 419
h = run({"catalog": CATALOG, "sheets": out["sheets"], "mode": "handle", "request": req})
cells = h["reply"]["results"][0]["cells"]
check("…and the other ID finds the dungeon row (already «Да»)", h["reply"]["results"][0]["status"] == "already"
      and [(c["sheet"], c["row"]) for c in cells] == [("Подземелья", 2)], cells)
del req["kills"][0]["mountID"]
h = run({"catalog": CATALOG, "sheets": SHEETS, "mode": "handle", "request": req})
check("without mountID (or ID column) — by name, as before", h["reply"]["results"][0]["status"] == "collected")

# 9. a 2.0 table that 2.1 already gave the ID column: the bear mix-up is fixed once
up21 = copy.deepcopy(out["sheets"])
rr = [x for x in up21 if x["name"] == "Рейды"][0]["rows"]
bear = [r for r in rr if r[3] == "Аманийский боевой медведь"][0]
bear[0] = "Да"   # 2.0 marked it by the name (the player has the other bear, 419)
fixed = run({"catalog": CATALOG, "sheets": up21, "game": GAME, "creator": "2.0.0"})
fr = sheet(fixed, "Рейды")["rows"]
check("2.0 table upgraded by 2.1: «Да» of the other same-name mount back to «Нет»",
      [r for r in fr if r[3] == "Аманийский боевой медведь"][0][0] == "Нет"
      and "исправлено на «Нет»" in fixed["result"]["summary"], fixed["result"])
again = run({"catalog": CATALOG, "sheets": up21, "game": GAME, "creator": "2.1.0"})
check("a table made by 2.1: such «Да» is the player's own — kept",
      [r for r in sheet(again, "Рейды")["rows"] if r[3] == "Аманийский боевой медведь"][0][0] == "Да", again["result"])

once = run({"catalog": CATALOG, "sheets": up21, "game": GAME, "creator": "2.0.0", "properties": {"nameMixFixed": "1"}})
check("…and only once: after the fix a «Да» typed by hand stays",
      [r for r in sheet(once, "Рейды")["rows"] if r[3] == "Аманийский боевой медведь"][0][0] == "Да", once["result"])

# 10. «Попыток» from the game's kill statistics (addon 2.9+): sum of all characters, only upwards
ST = "ID статистики"
SCAT = {"version": "test", "aliases": {}, "sheets": [
    {"key": "raids", "sheet": "Рейды", "columns": ["Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", ID, ST], "rows": [
        ["Cata", "Firelands", "Пламенное Крыло Алисразор", "Алисразор", 425, "5970/5971"],
        ["TBC", "Tempest Keep", "Пепел Ал'ара", "Кель'тас Солнечный Скиталец", 183, "1088"],
        ["WotLK", "Eye of Eternity", "Синий дракон", "Малигос", 247, "1391/1394"],
        ["Cata", "Dragon Soul", "Эксперимент 12-Б", "Ультраксион", 445, "6161/6162"],
        ["TBC", "Karazhan", "Огненный боевой конь", "Ловчий Аттумен", 168, ""],
        ["Cata", "Dragon Soul", "Пылающий дракон", "Безумие Смертокрыла", 442, "6167/6168"],
        ["Cata", "Dragon Soul", "Сумеречный предвестник", "Слава рейдеру", 443, "6168"],
    ]},
    {"key": "dungeons", "sheet": "Подземелья", "columns": ["Аддон", "Подземелье / активность", "Маунт", "Босс / достижение / условие", ID, ST], "rows": []},
    {"key": "world", "sheet": "Мировые боссы", "columns": ["Аддон", "Локация", "Маунт", "Босс / достижение / сундук", ID, ST], "rows": [
        ["MoP", "Kun-Lai", "Небесный ониксовый облачный змей", "Ша Злости", 473, "6989"],
    ]},
]}
AH = ["Собрано?", "Маунт", "Босс / достижение / сундук", "Шанс выпадения", "Попыток", "Шанс за все попытки", ID]
SSHEETS = [
    {"name": "Рейды", "rows": [AH,
        ["Нет", "Пламенное Крыло Алисразор", "Алисразор", "1%", 3, "", 425],
        ["Да", "Пепел Ал'ара", "Кель'тас Солнечный Скиталец", "1%", 5, "", 183],
        ["Да", "Синий дракон", "Малигос", "1%", 0, "", 247],
        ["Нет", "Эксперимент 12-Б", "Ультраксион", "1%", 40, "", 445],
        ["Нет", "Огненный боевой конь", "Ловчий Аттумен", "1%", 2, "", 168],
        ["Нет", "Пылающий дракон", "Безумие Смертокрыла", "1%", 0, "", 442],
        ["Нет", "Сумеречный предвестник", "Слава рейдеру", "Достижение", "", "", 443],
    ], "formulas": [[6, 4]]},
    {"name": "Мировые боссы", "rows": [
        ["Собрано?", "Маунт", "Босс / достижение / сундук", "Шанс выпадения", "Попыток", "Шанс за все попытки", "Источник", "Эйра"],
        ["Нет", "Небесный ониксовый облачный змей", "Ша Злости", 0.01, 0, "", "", False],
    ]},
]
SGAME = {"ids": [], "names": [], "updated": 1792657686, "chars": [
    {"name": "Эйра", "realm": "Ревущий фьорд", "statsAt": 1792657600,
     "stats": {"5970": 10, "5971": 2, "1088": 50, "6161": 7, "6167": 9, "6168": 4, "6989": 6, "1391": 20, "1394": 5}},
    {"name": "Мирна", "realm": "Ревущий фьорд", "statsAt": 1792657000, "stats": {"5970": 4, "6989": 1}},
    {"name": "Старый", "realm": "Ревущий фьорд"},
]}
st = run({"catalog": SCAT, "sheets": SSHEETS, "game": SGAME})
sr = {r[1]: r[4] for r in sheet(st, "Рейды")["rows"][1:]}
check("statistics: Alysrazor (Normal + Heroic) of both characters added up, more than the sheet had → 16",
      sr["Пламенное Крыло Алисразор"] == 16, sr)
check("collected mount with 0: filled once from the statistics (how many attempts it took) → 25",
      sr["Синий дракон"] == 25, sr)
check("collected mount that already has a number, a bigger number in the sheet, a mount without statistics: untouched",
      sr["Пепел Ал'ара"] == 5 and sr["Эксперимент 12-Б"] == 40 and sr["Огненный боевой конь"] == 2, sr)
check("formula in «Попыток» and an empty cell (no counter) are not touched",
      sr["Пылающий дракон"] == 0 and sr["Сумеречный предвестник"] == "", sr)
sw = sheet(st, "Мировые боссы")["rows"]
check("world bosses: found by name and boss (no ID column), 6 + 1 kills", sw[1][4] == 7, sw[1])
check("summary names the attempts", st["result"]["attempts"] == ["Пламенное Крыло Алисразор", "Синий дракон", "Небесный ониксовый облачный змей"]
      and "«Попыток» по статистике игры: 3" in st["result"]["summary"], st["result"])
st2 = run({"catalog": SCAT, "sheets": st["sheets"], "game": SGAME})
check("statistics: second run changes nothing", st2["writes"] == [] and st2["result"]["attempts"] == [], st2["result"])
nost = run({"catalog": SCAT, "sheets": SSHEETS, "game": dict(SGAME, chars=[{"name": "Эйра", "realm": "Ревущий фьорд"}])})
check("no character sent statistics (old addon): nothing changes", nost["result"]["attempts"] == [] and nost["writes"] == [], nost["result"])
emb = run({"catalog": None, "sheets": SSHEETS, "game": SGAME})
check("embedded catalog knows the statistics of Alysrazor and the Sha of Anger",
      emb["result"]["attempts"] == ["Пламенное Крыло Алисразор", "Синий дракон", "Небесный ониксовый облачный змей"], emb["result"])

# 11. the embedded catalog parses and every ID row is unique per sheet
emb = run({"catalog": None, "sheets": [], "game": GAME})
check("embedded catalog loads", "result" in emb)

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
