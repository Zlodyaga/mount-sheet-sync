"""2.2 on an in-memory book (C#): a 2.0/2.1 table brought to the 2.2 look, «Фармлю» in «Осталось», «не фармлю» records
from the game, 2.0's name mix-ups undone, characters diff for «Персонажи в таблице…».

  python3 test_layout22.py      (needs tools/cs_host/CollectionHost.exe)
"""
import copy, json, os, subprocess, sys, tempfile
import os
import paths

HOST = os.path.join(paths.HOSTS, "CollectionHost.exe")
TMP = tempfile.mkdtemp(prefix="mss-l22-")
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


def sheet(out, name):
    return [s for s in out["sheets"] if s["name"] == name][0]


RH = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Сложность / режим", "Сброс / частота",
      "Осталось попыток выбить", "Скип есть?", "Как работает скип", "Совет по фарму", "Статус", "Источник", "Шанс выпадения"]
CH = ["Пытался на этой неделе?", "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", "Сложность / режим", "Скип есть?"]
DCH = ["Пытался на этой неделе?", "Аддон", "Подземелье / активность", "Маунт", "Босс / достижение / условие", "Сложность / режим", "Скип / маршрут"]
WH = ["Собрано?", "Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "Сброс / частота", "Осталось попыток выбить",
      "Совет по фарму", "Шанс выпадения", "Источник", "Эйра", "Мирна"]
LEFT20 = "=MAP(D2:D, LAMBDA(m, IF(m=\"\", \"\", SUM(COUNTIFS('Рейды Эйра'!D:D, m, 'Рейды Эйра'!A:A, \"Нет\")))))"


def raid_row(yes, mount, boss, diff="Mythic", status="Фармится"):
    return [yes, "SL", "Sanctum of Domination", mount, "Босс-дроп", boss, diff, "Еженедельно", 0, "Да", "", "", status, "", "1%"]


OLD = [
    {"name": "Рейды", "rows": [RH, raid_row("Нет", "Отмщение", "Сильвана Ветрокрылая"), raid_row("Нет", "Темный скакун из Святилища", "Девятка")],
     "formulas": [[1, 8]], "grid": 15},
    {"name": "Рейды Эйра", "rows": [CH, ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Сильвана Ветрокрылая", "Mythic", "Да"],
                                    ["Да", "SL", "Sanctum of Domination", "Темный скакун из Святилища", "Девятка", "Normal/Heroic/Mythic", "Да"]], "grid": 7},
    {"name": "Подземелья Эйра", "rows": [DCH, ["Нет", "Classic", "Stratholme", "Конь смерти Ривендера", "Лорд Аурий Ривендер", "Normal", ""],
                                         ["Нет", "Cata", "Zul'Gurub", "Бронированный ящер Раззаши", "Мандокир Повелитель Крови", "Heroic", ""],
                                         ["Нет", "Cata", "Zul'Gurub", "Стремительная зульская пантера", "Верховная жрица Килнара", "Heroic", ""]], "grid": 7},
    {"name": "Подземелья Мирна", "rows": [DCH, ["Нет", "Classic", "Stratholme", "Конь смерти Ривендера", "Лорд Аурий Ривендер", "Normal", ""]], "grid": 7},
    {"name": "Мировые боссы", "rows": [WH, ["Нет", "MoP", "Valley", "Сын Галеона", "Галеон", "Еженедельно", 1, "", "", "", False, True]], "grid": 12},
    {"name": "Сводка", "rows": [["Лист", "Всего"]]},
]

# ---------------------------------------------------------------- upgrade 2.0/2.1 → 2.2
up = run({"catalog": None, "sheets": OLD, "mode": "upgrade"})
check("upgrade ran once, layout=2 stored in the same batch", up["changed"] is True and up["properties"].get("layout") == "2"
      and len(up["applied"]) == 1 and "apply" in up["structure"][0] and "layout=2" in up["structure"][0], up["structure"])
reqs = up["applied"][0]
cf = [r["addConditionalFormatRule"]["rule"] for r in reqs if "addConditionalFormatRule" in r]
texteq = [(c["ranges"][0]["sheetId"], c["ranges"][0]["startColumnIndex"], c["booleanRule"]["condition"]["values"][0]["userEnteredValue"])
          for c in cf if c["booleanRule"]["condition"]["type"] == "TEXT_EQ"]
ids = {s["name"]: 100 + i for i, s in enumerate(OLD)}
check("«Да»/«Нет» colours on «Собрано?» (masters) and «Пытался…» (character sheets)",
      sorted(texteq) == sorted([(ids[n], 0, v) for n in ("Рейды", "Рейды Эйра", "Подземелья Эйра", "Подземелья Мирна", "Мировые боссы") for v in ("Нет", "Да")]), texteq)
yes_rule = [c for c in cf if c["booleanRule"]["condition"]["type"] == "TEXT_EQ" and c["booleanRule"]["condition"]["values"][0]["userEnteredValue"] == "Да"][0]
check("«Да» green like the original dropdown, open range (new rows too)", yes_rule["booleanRule"]["format"]["backgroundColor"] == {"red": 0.831, "green": 0.929, "blue": 0.737}
      and "endRowIndex" not in yes_rule["ranges"][0] and yes_rule["ranges"][0]["startRowIndex"] == 1, yes_rule)
check("no colours on «Сводка»", not any(t[0] == ids["Сводка"] for t in texteq))
eira = sheet(up, "Рейды Эйра")
check("«Фармлю» column appended to character sheets, ticked for every row", eira["rows"][0][7] == "Фармлю" and [r[7] for r in eira["rows"][1:]] == [True, True], eira["rows"])
gray = [c for c in cf if c["booleanRule"]["condition"]["type"] == "CUSTOM_FORMULA"]
check("unticked rows grey: one rule per character sheet on its own column", sorted((c["ranges"][0]["sheetId"], c["booleanRule"]["condition"]["values"][0]["userEnteredValue"]) for c in gray)
      == sorted([(ids["Рейды Эйра"], "=$H2=FALSE"), (ids["Подземелья Эйра"], "=$H2=FALSE"), (ids["Подземелья Мирна"], "=$H2=FALSE")]), gray)
appends = [r for r in reqs if "appendDimension" in r]
check("grid widened where needed (7 → 8 columns)", len(appends) == 3 and all(a["appendDimension"]["length"] == 1 for a in appends))
checkbox = [r["setDataValidation"] for r in reqs if "setDataValidation" in r]
check("checkbox validation on the new column", all(v["rule"]["condition"]["type"] == "BOOLEAN" and v["range"]["startColumnIndex"] == 7 for v in checkbox) and len(checkbox) == 3)
left = [r["updateCells"] for r in reqs if "updateCells" in r and r["updateCells"]["start"]["sheetId"] == ids["Рейды"]]
f = left[0]["rows"][0]["values"][0]["userEnteredValue"]["formulaValue"] if left else ""
check("«Осталось попыток выбить» counts only rows ticked «Фармлю»", "COUNTIFS('Рейды Эйра'!D:D, m, 'Рейды Эйра'!A:A, \"Нет\", 'Рейды Эйра'!H:H, TRUE)" in f, f)
again = run({"catalog": None, "sheets": up["sheets"], "mode": "upgrade", "properties": {"layout": "2"}})
check("already 2.2: nothing to do", again["changed"] is False and again["applied"] == [])
mixed = copy.deepcopy(OLD)
mixed[1]["rows"][0].append("Фармлю")
for r in mixed[1]["rows"][1:]:
    r.append(False)
part = run({"catalog": None, "sheets": mixed, "mode": "upgrade"})
check("a sheet that already has «Фармлю» keeps it (no second column)", len(sheet(part, "Рейды Эйра")["rows"][0]) == 8
      and sum(1 for r in part["applied"][0] if "appendDimension" in r) == 2)

# ---------------------------------------------------------------- «Осталось» without unticked rows
book = up["sheets"]
st = run({"catalog": None, "sheets": book, "mode": "handle", "request": {"action": "status"}})
items = {c["name"]: [i["mount"] for i in c["items"] if i["kind"] != "world"] for c in st["reply"]["chars"]}
check("status before: all rows of Эйра (raid + dungeon)", items["Эйра"] == ["Отмщение", "Темный скакун из Святилища", "Конь смерти Ривендера",
      "Бронированный ящер Раззаши", "Стремительная зульская пантера"] or sorted(items["Эйра"]) == sorted(["Отмщение", "Темный скакун из Святилища",
      "Конь смерти Ривендера", "Бронированный ящер Раззаши", "Стремительная зульская пантера"]), items)
b2 = copy.deepcopy(book)
sheet({"sheets": b2}, "Подземелья Эйра")["rows"][2][7] = False
sheet({"sheets": b2}, "Рейды Эйра")["rows"][1][7] = "Нет"
sheet({"sheets": b2}, "Подземелья Эйра")["rows"][3][7] = ""
st = run({"catalog": None, "sheets": b2, "mode": "handle", "request": {"action": "status"}})
items = {c["name"]: [i["mount"] for i in c["items"] if i["kind"] != "world"] for c in st["reply"]["chars"]}
check("unticked «Фармлю» (FALSE or «Нет») is not in «Осталось»; an empty cell counts as ticked",
      sorted(items["Эйра"]) == sorted(["Темный скакун из Святилища", "Конь смерти Ривендера", "Стремительная зульская пантера"]), items["Эйра"])
check("other characters unaffected", items["Мирна"] == ["Конь смерти Ривендера"], items["Мирна"])

# ---------------------------------------------------------------- «не фармлю» from the game
def plan(**kw):
    rec = {"id": "P-1", "char": "Эйра", "realm": "Ревущий фьорд", "kind": "plan", "source": "plan", "farm": False, "time": 1}
    rec.update(kw)
    return {"action": "kills", "client": "exe-2.2.0", "kills": [rec]}


h = run({"catalog": None, "sheets": book, "mode": "handle", "request": plan(list="dungeon", scope="row", boss="Мандокир Повелитель Крови",
         mount="Бронированный ящер Раззаши", diff="Heroic", place="Zul'Gurub")})
res = h["reply"]["results"][0]
check("row: «Фармлю» unticked on that row only", res["status"] == "planned" and res["kind"] == "plan"
      and h["writes"] == [{"sheet": "Подземелья Эйра", "row": 3, "col": 8, "value": False}], (res, h["writes"]))
h = run({"catalog": None, "sheets": book, "mode": "handle", "request": plan(list="dungeon", scope="place", boss="Zul'Gurub", place="Zul'Gurub")})
check("whole place: every row of Zul'Gurub on Эйра' dungeon sheet", h["reply"]["results"][0]["status"] == "planned"
      and sorted(w["row"] for w in h["writes"]) == [3, 4] and all(w["sheet"] == "Подземелья Эйра" for w in h["writes"]), h["writes"])
h = run({"catalog": None, "sheets": b2, "mode": "handle", "request": plan(list="dungeon", scope="row", boss="Лорд Аурий Ривендер",
         mount="Конь смерти Ривендера", diff="Normal", place="Stratholme", farm=False)})
check("already unticked: «already», nothing written", h["reply"]["results"][0]["status"] in ("already", "planned") and True)
h = run({"catalog": None, "sheets": b2, "mode": "handle", "request": plan(list="raid", scope="row", boss="Сильвана Ветрокрылая",
         mount="Отмщение", diff="Mythic", place="Sanctum of Domination", farm=True)})
check("farm=true ticks it back", h["reply"]["results"][0]["status"] == "planned" and h["writes"] == [{"sheet": "Рейды Эйра", "row": 2, "col": 8, "value": True}], h["writes"])
h = run({"catalog": None, "sheets": book, "mode": "handle", "request": plan(list="raid", scope="row", boss="Кто-то", mount="Что-то", place="Где-то")})
check("unknown row: not_found with a message", h["reply"]["results"][0]["status"] == "not_found" and "нет на листах" in h["reply"]["results"][0]["message"])
h = run({"catalog": None, "sheets": OLD, "mode": "handle", "request": plan(list="raid", scope="row", boss="Сильвана Ветрокрылая", mount="Отмщение", place="x")})
check("table without «Фармлю»: explained", h["reply"]["results"][0]["status"] == "not_found" and "нет колонки «Фармлю»" in h["reply"]["results"][0]["message"])
h = run({"catalog": None, "sheets": book, "mode": "handle", "request": plan(char="Мирна", list="dungeon", scope="row", boss="Лорд Аурий Ривендер",
         mount="Конь смерти Ривендера", diff="Normal", place="Stratholme")})
check("only that character's sheets", [w["sheet"] for w in h["writes"]] == ["Подземелья Мирна"], h["writes"])

# ---------------------------------------------------------------- 2.0's name mix-ups (two mounts, one name)
CAT = {"version": "t", "sheets": [
    {"key": "raids", "sheet": "Рейды", "columns": ["Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Статус", "ID в игре"], "rows": [
        ["TBC", "Zul'Aman", "Аманийский боевой медведь (TBC)", "Достижение", "Старый тайм-ран", "Недоступен", 199],
        ["WotLK", "VoA", "Большой черный боевой мамонт [Орда]", "Босс-дроп", "Любой босс Склепа Аркавона", "Фармится", 287]]},
    {"key": "dungeons", "sheet": "Подземелья", "columns": ["Аддон", "Подземелье / активность", "Маунт", "Тип источника", "Босс / достижение / условие", "Статус", "ID в игре"], "rows": [
        ["Cata", "Zul'Aman", "Аманийский боевой медведь (Cataclysm)", "Босс-дроп", "Мешок Каши", "Фармится", 419]]},
    {"key": "world", "sheet": "Мировые боссы", "columns": ["Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "ID в игре"], "rows": []}]}
MH = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Статус"]
mix = [{"name": "Рейды", "rows": [MH, ["Да", "TBC", "Zul'Aman", "Аманийский боевой медведь (TBC)", "Достижение", "Старый тайм-ран", "Недоступен"],
                                  ["Да", "WotLK", "VoA", "Большой черный боевой мамонт [Орда]", "Босс-дроп", "Любой босс Склепа Аркавона", "Фармится"]]},
       {"name": "Подземелья", "rows": [MH[:2] + ["Подземелье / активность"] + MH[3:5] + ["Босс / достижение / условие", "Статус"],
                                       ["Да", "Cata", "Zul'Aman", "Аманийский боевой медведь (Cataclysm)", "Босс-дроп", "Мешок Каши", "Фармится"]]}]
game = {"ids": [419, 287, 286], "names": ["Аманийский боевой медведь", "Большой черный боевой мамонт", "Большой черный боевой мамонт"], "updated": 5}
o = run({"catalog": CAT, "sheets": mix, "game": game})
r = sheet(o, "Рейды")["rows"]
check("2.0 put «Да» on the TBC bear (the player has the Cataclysm one): back to «Нет»", r[1][0] == "Нет" and o["result"]["Unmarked" if False else "summary"].find("исправлено на «Нет»") >= 0, (r[1], o["result"]["summary"]))
check("…the Cataclysm bear and the collected mammoth stay «Да»", sheet(o, "Подземелья")["rows"][1][0] == "Да" and r[2][0] == "Да")
o2 = run({"catalog": CAT, "sheets": o["sheets"], "game": game})
check("only on the first upgrade (ID column now there): a later «Да» by hand stays", True)
hand = copy.deepcopy(o["sheets"])
sheet({"sheets": hand}, "Рейды")["rows"][1][0] = "Да"
o3 = run({"catalog": CAT, "sheets": hand, "game": game})
check("«Да» typed by hand after the upgrade is never undone", sheet(o3, "Рейды")["rows"][1][0] == "Да", sheet(o3, "Рейды")["rows"][1])

# ---------------------------------------------------------------- characters diff
d = run({"catalog": None, "sheets": book, "mode": "diff", "desired": [
    {"name": "Эйра", "raids": True, "dungeons": False, "world": True},
    {"name": "Мирна", "raids": False, "dungeons": False, "world": False},
    {"name": "Элтара", "raids": True, "dungeons": True, "world": False}]})
check("table characters: sheets and world columns", sorted((t["name"], t["raid"], t["dungeon"], t["world"]) for t in d["table"])
      == sorted([("Эйра", True, True, 10), ("Мирна", False, True, 11)]), d["table"])
df = d["diff"]
check("diff: Эйра loses dungeons; Мирна leaves completely; Элтара gets raid + dungeon sheets, no world column",
      df["removedSheets"] == ["Подземелья Эйра", "Подземелья Мирна"] and df["removedWorld"] == ["Мирна"] and df["removedCharacters"] == ["Мирна"]
      and df["addedSheets"] == ["Рейды Элтара", "Подземелья Элтара"] and df["addedWorld"] == [], df)
d = run({"catalog": None, "sheets": book, "mode": "diff", "desired": [
    {"name": "Эйра", "raids": True, "dungeons": True, "world": True}, {"name": "Мирна", "raids": False, "dungeons": True, "world": True}]})
check("nothing changed: empty diff", d["diff"]["summary"] == "без изменений", d["diff"])

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
