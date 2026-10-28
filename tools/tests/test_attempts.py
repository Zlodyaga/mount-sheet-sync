"""2.8: the attempt counter on the main sheets (C#, in-memory book and the sheet builder).

A new spreadsheet has «Попыток» and «Шанс за все попытки» right after «Шанс выпадения»; an older one gets them on
upgrade (layout 3, insertDimension — on «Мировые боссы» before the character columns). Every kill that could drop a
mount not collected yet adds one attempt; «Сброс недели» re-sends («again») do not; the status for the addon lists
those rows with n and p.

  python3 test_attempts.py      (needs tools/cs_host/CollectionHost.exe and BuilderHost.exe)
"""
import json, os, subprocess, tempfile, time
import paths

COLL = os.path.join(paths.HOSTS, "CollectionHost.exe")
BUILD = os.path.join(paths.HOSTS, "BuilderHost.exe")
TMP = tempfile.mkdtemp(prefix="mss-att-")
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1800]))


def run(job):
    path = os.path.join(TMP, "job.json")
    json.dump(job, open(path, "w", encoding="utf-8"), ensure_ascii=False)
    p = subprocess.run(["mono", COLL, path], capture_output=True)
    if p.returncode != 0:
        print(p.stderr.decode("utf-8", "replace"))
    return json.loads(p.stdout.decode("utf-8"))


def sheet(out, name):
    return [s for s in out["sheets"] if s["name"] == name][0]


# ---------------------------------------------------------------- a new spreadsheet
p = subprocess.run(["mono", BUILD], input=json.dumps({"title": "T", "chars": [{"name": "Эйра", "raids": True, "dungeons": True}]}).encode(),
                   capture_output=True)
reqs = json.loads(p.stdout.decode("utf-8"))["requests"]


def table(sheet_id):
    for r in reqs:
        u = r.get("updateCells")
        if u and "start" in u and u["start"]["sheetId"] == sheet_id and u["start"]["rowIndex"] == 0 and u["start"]["columnIndex"] == 0:
            return [[(v or {}).get("userEnteredValue") for v in row.get("values", [])] for row in u["rows"]]
    return None


def text(v):
    if not v:
        return ""
    return v.get("stringValue", v.get("numberValue", v.get("formulaValue", v.get("boolValue", ""))))


raids = table(100)
head = [text(v) for v in raids[0]]
check("new «Рейды»: «Попыток» and «Шанс за все попытки» right after «Шанс выпадения», «ID в игре» last",
      head[14:] == ["Шанс выпадения", "Попыток", "Шанс за все попытки", "ID в игре"], head)
rows = {text(r[3]): (i, r) for i, r in enumerate(raids) if i > 0}
i, r = rows["Отмщение"]
check("1% mount: 0 attempts and the chance formula in integers (any locale)",
      r[15] == {"numberValue": 0} and r[16] == {"formulaValue": '=IF(N(P%d)>0, 1-(1-1/100)^P%d, "")' % (i + 1, i + 1)}, r[14:17])
ach = [(i, r) for i, r in rows.values() if text(r[14]) in ("Достижение", "Недоступно", "100%")]
check("no counter for achievements, unavailable or guaranteed rows", ach and all(r[15] is None and r[16] is None for i, r in ach), ach[:2])
fmt = [x["updateCells"] for x in reqs if "updateCells" in x and "range" in x["updateCells"] and x["updateCells"]["range"]["sheetId"] == 100
       and x["updateCells"]["range"]["startColumnIndex"] == 16 and "numberFormat" in x["updateCells"]["fields"]]
check("chance shown in percent (0.0%), every cell", any(all(row["values"][0]["userEnteredFormat"]["numberFormat"] == {"type": "PERCENT", "pattern": "0.0%"}
      for row in f["rows"]) for f in fmt), fmt[:1])
dung = table(200)
dh = [text(v) for v in dung[0]]
check("new «Подземелья»: the two columns after «Шанс выпадения» too", dh[9:12] == ["Шанс выпадения", "Попыток", "Шанс за все попытки"], dh)
world = table(400)
wh = [text(v) for v in world[0]]
check("new «Мировые боссы»: the two columns before «Источник» and the characters", wh[8:12] == ["Шанс выпадения", "Попыток", "Шанс за все попытки", "Источник"], wh)
wr = [r for r in world[1:] if text(r[3])]
check("world bosses (1% as a number): 0 and the formula", wr and all(r[9] == {"numberValue": 0} and "1-(1-1/100)^J" in text(r[10]) for r in wr
                                                                 if text(r[8]) == 0.01), [r[8:11] for r in wr[:2]])

# ---------------------------------------------------------------- upgrade of a 2.2 spreadsheet (layout 2 → 3)
RH = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Сложность / режим", "Сброс / частота",
      "Осталось попыток выбить", "Скип есть?", "Как работает скип", "Совет по фарму", "Статус", "Источник", "Шанс выпадения", "ID в игре"]
DH = ["Собрано?", "Аддон", "Подземелье / активность", "Маунт", "Тип источника", "Босс / достижение / условие", "Сложность / режим",
      "Сброс / частота", "Скип / маршрут", "Шанс выпадения", "Совет по фарму", "Статус", "Источник", "ID в игре"]
WH = ["Собрано?", "Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "Сброс / частота", "Осталось попыток выбить",
      "Совет по фарму", "Шанс выпадения", "Источник", "Эйра", "Мирна"]
CH = ["Пытался на этой неделе?", "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", "Сложность / режим", "Скип есть?", "Фармлю"]
DCH = ["Пытался на этой неделе?", "Аддон", "Подземелье / активность", "Маунт", "Босс / достижение / условие", "Сложность / режим", "Скип / маршрут", "Фармлю"]
OLD = [
    {"name": "Рейды", "rows": [RH,
        ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Босс-дроп", "Сильвана Ветрокрылая", "Mythic", "Еженедельно", "", "Да", "", "", "Фармится", "", "1%", 1471],
        ["Да", "SL", "Sanctum of Domination", "Темный скакун из Святилища", "Босс-дроп", "Девятка", "Normal/Heroic/Mythic", "", "", "", "", "", "Фармится", "", "1%", 1500],
        ["Нет", "Legion", "Antorus", "Кругомер", "Достижение", "Слава герою", "Любая", "", "", "", "", "", "Фармится", "", "Достижение", ""],
        ["", "", "", "", "", "", "", "", "", "", "", "", "", "", "", ""]], "grid": 16},
    {"name": "Рейды Эйра", "rows": [CH, ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Сильвана Ветрокрылая", "Mythic", "Да", True]], "grid": 8},
    {"name": "Подземелья", "rows": [DH,
        ["Нет", "WotLK", "Utgarde Pinnacle", "Синий протодракон", "Босс-дроп", "Скади Безжалостный", "Heroic", "Ежедневно", "", "~1%", "", "Фармится", "", 363]], "grid": 14},
    {"name": "Подземелья Эйра", "rows": [DCH, ["Нет", "WotLK", "Utgarde Pinnacle", "Синий протодракон", "Скади Безжалостный", "Heroic", "", True]], "grid": 8},
    {"name": "Сводка", "rows": [["Лист", "Всего"]]},
    {"name": "Мировые боссы", "rows": [WH, ["Нет", "MoP", "Valley of the Four Winds", "Сын Галеона", "Галеон", "Еженедельно", "", "", 0.01, "", False, True]], "grid": 12},
]
up = run({"catalog": None, "sheets": OLD, "mode": "upgrade", "properties": {"layout": "2"}})
check("upgrade to layout 4 in one batch", up["changed"] is True and up["properties"].get("layout") == "4" and len(up["applied"]) == 1, up["structure"])
batch = up["applied"][0]
ins = [r["insertDimension"] for r in batch if "insertDimension" in r]
ids = {s["name"]: 100 + i for i, s in enumerate(OLD)}
check("two columns inserted after «Шанс выпадения» on the three main sheets (formats taken from the left)",
      sorted((x["range"]["sheetId"], x["range"]["startIndex"], x["range"]["endIndex"], x["inheritFromBefore"]) for x in ins)
      == sorted([(ids["Рейды"], 15, 17, True), (ids["Подземелья"], 10, 12, True), (ids["Мировые боссы"], 9, 11, True)]), ins)
ur = sheet(up, "Рейды")
check("«Рейды» in memory: header, 0 attempts on the 1% rows, empty on the achievement and the blank row",
      ur["rows"][0][14:18] == ["Шанс выпадения", "Попыток", "Шанс за все попытки", "ID в игре"]
      and ur["rows"][1][15] == 0 and ur["rows"][2][15] == 0 and ur["rows"][3][15] == "" and ur["rows"][4][15] == "", ur["rows"])
check("…chance cells are formulas, «ID в игре» moved right", [1, 16] in ur["formulas"] and [2, 16] in ur["formulas"] and [3, 16] not in ur["formulas"]
      and ur["rows"][1][17] == 1471, ur["formulas"])
uw = sheet(up, "Мировые боссы")
check("«Мировые боссы»: the counter before the character columns (Эйра, Мирна now at 12, 13, ticks kept)",
      uw["rows"][0][8:14] == ["Шанс выпадения", "Попыток", "Шанс за все попытки", "Источник", "Эйра", "Мирна"]
      and uw["rows"][1][12] is False and uw["rows"][1][13] is True and uw["rows"][1][9] == 0, uw["rows"])
wf = [r["updateCells"] for r in batch if "updateCells" in r and r["updateCells"].get("start", {}).get("sheetId") == ids["Мировые боссы"]]
fmt = [r["updateCells"] for r in batch if "updateCells" in r and "range" in r["updateCells"] and r["updateCells"]["range"]["sheetId"] == ids["Рейды"]]
check("formats go cell by cell (updateCells), not repeatCell — repeatCell skips rows hidden by the filter",
      not any("repeatCell" in r for r in batch) and any(f["fields"] == "userEnteredFormat.numberFormat" and f["range"]["startColumnIndex"] == 16
      and len(f["rows"]) == f["range"]["endRowIndex"] - 1 and f["rows"][0]["values"][0]["userEnteredFormat"]["numberFormat"]["pattern"] == "0.0%" for f in fmt), fmt[:1])
check("…world chance formula from the number 0.01", wf and "1-(1-1/100)^J2" in wf[0]["rows"][1]["values"][1]["userEnteredValue"]["formulaValue"], wf)
d = run({"catalog": None, "sheets": up["sheets"], "mode": "diff", "desired": [{"name": "Эйра", "raids": True, "dungeons": True, "world": True}]})
check("characters of the upgraded table found where they are now", sorted((t["name"], t["world"]) for t in d["table"]) == [("Мирна", 13), ("Эйра", 12)], d["table"])
again = run({"catalog": None, "sheets": up["sheets"], "mode": "upgrade", "properties": {"layout": "3"}})
ab = again["applied"][0] if again["applied"] else []
check("layout 3 (a 2.8 table): formats of both columns again, cell by cell, on the three sheets, no new columns",
      again["changed"] is True and again["properties"].get("layout") == "4" and not any("insertDimension" in r for r in ab)
      and sorted({r["updateCells"]["range"]["sheetId"] for r in ab if "updateCells" in r}) == sorted([ids["Рейды"], ids["Подземелья"], ids["Мировые боссы"]]), again)
again = run({"catalog": None, "sheets": up["sheets"], "mode": "upgrade", "properties": {"layout": "4"}})
check("layout 4: nothing more to do", again["changed"] is False)
# a 2.0/2.1 table goes 0 → 3 in one go
up0 = run({"catalog": None, "sheets": OLD, "mode": "upgrade"})
check("2.0/2.1 table: colours, «Фармлю» and the counter in one batch, layout 4", up0["properties"].get("layout") == "4" and len(up0["applied"]) == 1
      and any("insertDimension" in r for r in up0["applied"][0]), up0["structure"])

# ---------------------------------------------------------------- kills
SHEETS = [{"name": s["name"], "rows": s["rows"], "formulas": s["formulas"]} for s in up["sheets"]]
WEEK = NOW + 3 * 86400


def kill(i, boss, kind, diff, **kw):
    k = {"id": "K-Player-1-A-%d" % i, "time": NOW - 100 + i, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A",
         "boss": boss, "kind": kind, "difficultyID": diff, "weekEnd": WEEK}
    k.update(kw)
    return k


def handle(sheets, kills):
    out = run({"catalog": None, "sheets": sheets, "mode": "handle", "properties": {"layout": "4"},
               "request": {"action": "kills", "client": "exe-2.8.0", "kills": kills}})
    return out, [{"name": s["name"], "rows": s["rows"], "formulas": s["formulas"]} for s in out["sheets"]]


out, s1 = handle(SHEETS, [kill(1, "Сильвана Ветрокрылая", "raid", 16)])
res = out["reply"]["results"][0]
check("Mythic Sylvanas: «Да» on Эйра's sheet and attempt 1 of «Отмщение» (p = 1%)", res["status"] == "marked"
      and res.get("attempts") == [{"mount": "Отмщение", "n": 1, "p": 0.01, "sheet": "Рейды", "row": 2}], res)
check("…written as a number into «Попыток»", {"sheet": "Рейды", "row": 2, "col": 16, "value": 1} in out["writes"], out["writes"])
out, s2 = handle(s1, [kill(2, "Сильвана Ветрокрылая", "raid", 16), kill(3, "Сильвана Ветрокрылая", "raid", 15),
                      kill(4, "Девятка", "raid", 16), kill(5, "Сильвана Ветрокрылая", "raid", 16, again=True),
                      kill(6, "Сильвана Ветрокрылая", "manual", 0)])
rr = out["reply"]["results"]
check("second Mythic kill (alt or next week): attempt 2", rr[0].get("attempts", [{}])[0].get("n") == 2, rr[0])
check("Heroic kill: the Mythic-only mount is not an attempt", "attempts" not in rr[1], rr[1])
check("collected mount (Девятка): no attempt", "attempts" not in rr[2], rr[2])
check("a re-send after «Сброс недели» (again) is not counted twice", "attempts" not in rr[3] and rr[3]["status"] in ("already", "marked"), rr[3])
check("a manual mark (no difficulty) counts only world bosses", "attempts" not in rr[4], rr[4])
out, s3 = handle(s2, [kill(7, "Скади Безжалостный", "party", 2), kill(8, "Галеон", "world", 0),
                      kill(9, "Сильвана Ветрокрылая", "raid", 16, weekEnd=NOW - 3600)])
rr = out["reply"]["results"]
check("Heroic Skadi: «Синий протодракон» attempt 1 (~1%)", rr[0].get("attempts") == [{"mount": "Синий протодракон", "n": 1, "p": 0.01, "sheet": "Подземелья", "row": 2}], rr[0])
check("Galleon (world boss): attempt 1 on «Мировые боссы»", rr[1].get("attempts", [{}])[0].get("n") == 1 and rr[1]["attempts"][0]["sheet"] == "Мировые боссы", rr[1])
check("a kill from before the weekly reset: no «Да» any more, but still an attempt (3)", rr[2]["status"] == "stale" and rr[2].get("attempts", [{}])[0].get("n") == 3, rr[2])
# a hand-typed count is continued
hand = json.loads(json.dumps(s3))
[s for s in hand if s["name"] == "Рейды"][0]["rows"][1][15] = "40"
out, _ = handle(hand, [kill(10, "Сильвана Ветрокрылая", "raid", 16)])
check("a number typed by hand (40) goes on: 41", out["reply"]["results"][0].get("attempts", [{}])[0].get("n") == 41, out["reply"]["results"][0])

# ---------------------------------------------------------------- status for the addon
st = run({"catalog": None, "sheets": s3, "mode": "handle", "properties": {"layout": "4"}, "request": {"action": "status"}})["reply"]
mounts = {m["mount"]: m for m in st.get("mounts", [])}
check("status lists the counted rows not collected yet (Отмщение, Синий протодракон, Сын Галеона)",
      sorted(mounts) == ["Отмщение", "Синий протодракон", "Сын Галеона"], sorted(mounts))
o = mounts.get("Отмщение", {})
check("…with n, p, the boss names and the Mythic difficulty IDs (like a «Remaining» row)", o.get("n") == 3 and o.get("p") == 0.01
      and "сильвана ветрокрылая" in o.get("names", []) and 16 in o.get("d", []) and 15 not in o.get("d", []) and o.get("id") == 1471 and o.get("kind") == "raid", o)
check("…world row without difficulties", mounts.get("Сын Галеона", {}).get("kind") == "world" and mounts["Сын Галеона"].get("d") == [], mounts.get("Сын Галеона"))
old = run({"catalog": None, "sheets": OLD, "mode": "handle", "properties": {"layout": "2"}, "request": {"action": "status"}})["reply"]
check("a table without the counter: no «mounts» in the status (the addon simply shows nothing)", "mounts" not in old, list(old))

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
raise SystemExit(1 if failed else 0)
