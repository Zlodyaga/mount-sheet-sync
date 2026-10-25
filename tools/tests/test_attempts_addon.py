"""2.8 in the game: the attempt line in chat after a kill, the boss tooltip / Adventure Guide hint and the bug-report
text in the settings. The «Remaining» list is the real status of the C# program (CollectionHost) for a small table,
turned into SheetData.lua like SheetData.Build does.

  python3 test_attempts_addon.py      (needs tools/cs_host/CollectionHost.exe)
"""
import json, os, subprocess, tempfile, time
from lupa import lua51
import paths

ADDON_DIR = paths.ADDON_DIR
HERE = os.path.dirname(os.path.abspath(__file__))
COLL = os.path.join(paths.HOSTS, "CollectionHost.exe")
TMP = tempfile.mkdtemp(prefix="mss-atta-")
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


def host(job):
    path = os.path.join(TMP, "job.json")
    json.dump(job, open(path, "w", encoding="utf-8"), ensure_ascii=False)
    p = subprocess.run(["mono", COLL, path], capture_output=True)
    return json.loads(p.stdout.decode("utf-8"))


RH = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Сложность / режим", "Сброс / частота",
      "Осталось попыток выбить", "Скип есть?", "Как работает скип", "Совет по фарму", "Статус", "Источник", "Шанс выпадения", "ID в игре"]
CH = ["Пытался на этой неделе?", "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", "Сложность / режим", "Скип есть?", "Фармлю"]
WH = ["Собрано?", "Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "Сброс / частота", "Осталось попыток выбить",
      "Совет по фарму", "Шанс выпадения", "Источник", "Эйра", "Мирна"]
SHEETS = [
    {"name": "Рейды", "rows": [RH,
        ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Босс-дроп", "Сильвана Ветрокрылая", "Mythic", "", "", "", "", "", "Фармится", "", "1%", 1471],
        ["Нет", "Legion", "Antorus", "Кругомер", "Достижение", "Аргус Освободитель", "Любая", "", "", "", "", "", "Фармится", "", "Достижение", ""]]},
    {"name": "Рейды Эйра", "rows": [CH, ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Сильвана Ветрокрылая", "Mythic", "Да", True]]},
    {"name": "Рейды Мирна", "rows": [CH, ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Сильвана Ветрокрылая", "Mythic", "Да", True]]},
    {"name": "Мировые боссы", "rows": [WH, ["Нет", "MoP", "Valley of the Four Winds", "Сын Галеона", "Галеон", "", "", "", 0.01, "", False, False]]},
]
up = host({"catalog": None, "sheets": SHEETS, "mode": "upgrade", "properties": {"layout": "2"}})
sheets = [{"name": s["name"], "rows": s["rows"], "formulas": s["formulas"]} for s in up["sheets"]]
[s for s in sheets if s["name"] == "Рейды"][0]["rows"][1][15] = 36   # 36 attempts so far
status = host({"catalog": None, "sheets": sheets, "mode": "handle", "properties": {"layout": "3"}, "request": {"action": "status"}})["reply"]
status["generated"] = NOW - 3600
check("the program's status carries the attempt rows", [m["mount"] for m in status.get("mounts", [])] == ["Отмщение", "Сын Галеона"], status.get("mounts"))


def lua_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


def to_lua(v):
    if v is None: return "nil"
    if isinstance(v, bool): return "true" if v else "false"
    if isinstance(v, (int, float)): return repr(v)
    if isinstance(v, str): return lua_str(v)
    if isinstance(v, list): return "{" + ", ".join(to_lua(x) for x in v) + "}"
    return "{" + ", ".join("[%s] = %s" % (lua_str(k), to_lua(x)) for k, x in v.items() if x is not None and k != "ok") + "}"


SHEET_SRC = "MountSheetSyncSheet = " + to_lua(status) + "\n"
PROGRAM_SRC = 'MountSheetSyncProgram = { seen = %d, sheet = true, version = "2.8.0" }\n' % (NOW - 30)


def new_game(quiet=False):
    L = lua51.LuaRuntime(unpack_returned_tuples=True)
    L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
    L.execute(r"""
    MOCK = { now = %d, inst = { "Святилище Господства", "raid", 16, 2450 } }
    MOCK.now0 = MOCK.now
    function issecretvalue(v) return v == "SECRET" end
    C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return MOCK.now0 + 3 * 86400 - MOCK.now end }
    function time() return MOCK.now end
    function GetTime() return 1000 + MOCK.now end
    date = os.date
    function UnitName() return "Эйра" end
    function GetRealmName() return "Ревущий фьорд" end
    function UnitGUID() return "Player-1602-0ABCDEF1" end
    function GetDifficultyInfo() return "Эпохальный" end
    function GetInstanceInfo() local i = MOCK.inst; return i[1], i[2], i[3], "", 20, 0, false, i[4] end
    function RequestRaidInfo() end
    function ReloadUI() end
    function GetNumSavedInstances() return 0 end
    function GetNumSavedWorldBosses() return 0 end
    C_MountJournal = { GetMountInfoByID = function(id)
      if id == 1471 then return "Отмщение", 351195, 1, false, true, 0, false, false, nil, false, false, id, false end
    end }
    Enum = { TooltipDataType = { Unit = 2 } }
    TooltipDataProcessor = { AddTooltipPostCall = function(kind, fn) if kind == 2 then UNIT_POSTCALL = fn end end }
    TIP_UNIT = { "Сильвана Ветрокрылая", "target" }
    function GameTooltip:GetUnit() return TIP_UNIT[1], TIP_UNIT[2] end
    function UnitIsPlayer(u) return u == "player" end
    -- the Adventure Guide (load-on-demand)
    function EncounterJournal_DisplayEncounter(id) EJ_SHOWN = id end
    function EJ_GetEncounterInfo(id) if id == 2435 then return "Сильвана Ветрокрылая" end return "Кто-то" end
    MountSheetSyncDB = { version = 1, kills = {}, quiet = %s }
    """ % (NOW, "true" if quiet else "false"))
    EJ = L.eval("CreateFrame('Frame', nil, UIParent)")
    L.globals().EncounterJournal = L.table_from({"encounter": L.table_from({"info": EJ})})
    EJ.encounterTitle = L.eval("UIParent:CreateFontString()")
    g = L.globals()
    toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
    files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
    ns = L.table()
    loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
    for name in files:
        src = SHEET_SRC if name == "SheetData.lua" else PROGRAM_SRC if name == "Program.lua" else open(os.path.join(ADDON_DIR, name), encoding="utf-8").read()
        loader(src, name)("MountSheetSync", ns)
    g.Fire("ADDON_LOADED", "MountSheetSync")
    g.Fire("PLAYER_LOGIN")
    g.RunTimers()
    return L, g, ns, files, EJ


def chat(g, since):
    return [str(g.CHAT[i]) for i in range(since + 1, len(g.CHAT) + 1)]


def kill(L, g, boss, diff, enc, inst=("Святилище Господства", "raid")):
    L.execute("MOCK.now = MOCK.now + 300")
    L.execute('MOCK.inst = { "%s", "%s", %d, 2450 }' % (inst[0], inst[1], diff))
    n = len(g.CHAT)
    g.Fire("ENCOUNTER_END", enc, boss, diff, 20, 1)
    g.RunTimers()
    return chat(g, n)


L, g, ns, files, EJ = new_game()
check("Tooltip.lua is loaded after the window", files.index("Tooltip.lua") > files.index("UI.lua"), files)
out = kill(L, g, "Сильвана Ветрокрылая", 16, 2435)
check("Mythic Sylvanas: «Отмщение мог выпасть: попытка 37 · шанс за все попытки 31%» (36 in the sheet + this kill)",
      any("Отмщение" in c and "мог выпасть" in c and "попытка 37" in c and "шанс за все попытки 31%" in c for c in out), out)
out = kill(L, g, "Сильвана Ветрокрылая", 16, 2435)
check("…the next kill (alt) is attempt 38 — counted locally until the program sends it", any("попытка 38" in c for c in out), out)
out = kill(L, g, "Сильвана Ветрокрылая", 15, 2435)
check("Heroic kill: the Mythic-only mount could not drop — no attempt line", not any("мог выпасть" in c for c in out), out)
out = kill(L, g, "Галеон", 0, 725, inst=("Долина Четырех Ветров", "none"))
check("world boss Galleon: attempt 1 · 1% (the 0.01 from the sheet)", any("Сын Галеона" in c and "попытка 1 " in c and "1%" in c for c in out), out)
check("no Lua errors", not any("Ошибка" in c or "error" in c.lower() for c in chat(g, 0)), chat(g, 0))

# ---- the boss tooltip
g.GameTooltip.lines = L.table()
g.UNIT_POSTCALL(g.GameTooltip)
tip = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("tooltip on Sylvanas: attempts 38 (32%) and one character left this week (Мирна; Эйра killed her)",
      any("Отмщение" in t and "попыток 38" in t and "(32%)" in t and "персонажей: 1" in t and "Media\\Icon" in t for t in tip), tip)
L.execute('TIP_UNIT = { "SECRET", "target" }')
g.GameTooltip.lines = L.table()
g.UNIT_POSTCALL(g.GameTooltip)
check("a hidden (secret) name: nothing added, no error", len(g.GameTooltip.lines) == 0)
L.execute('TIP_UNIT = { "Сильвана Ветрокрылая", "player" }')
g.GameTooltip.lines = L.table()
g.UNIT_POSTCALL(g.GameTooltip)
check("a player with a boss's name: nothing", len(g.GameTooltip.lines) == 0)
L.execute('TIP_UNIT = { "Кто-то другой", "target" }')
g.GameTooltip.lines = L.table()
g.UNIT_POSTCALL(g.GameTooltip)
check("any other NPC: nothing", len(g.GameTooltip.lines) == 0)

# ---- the Adventure Guide
g.Fire("ADDON_LOADED", "Blizzard_EncounterJournal")
g.EncounterJournal_DisplayEncounter(2435)
texts = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._type == "FontString" and r._parent is not None and r._parent._uid == EJ._uid]
check("Adventure Guide: Blizzard's function still runs, a line under the boss title", g.EJ_SHOWN == 2435 and texts
      and "Отмщение" in str(texts[0]._text) and texts[0]._shown and texts[0]._points[1].rel._uid == EJ.encounterTitle._uid, [str(t._text) for t in texts])
g.EncounterJournal_DisplayEncounter(1)
check("…another boss: the line is hidden", texts and not texts[0]._shown)

# ---- quiet: no chat at all
L2, g2, ns2, _, _ = new_game(quiet=True)
out = kill(L2, g2, "Сильвана Ветрокрылая", 16, 2435)
check("chat messages off: no attempt line either", not any("мог выпасть" in c for c in out), out)

# ---- English
L3, g3, ns3, _, _ = new_game()
L3.execute('MountSheetSyncDB.language = "enUS"; MSS_SET = true')
ns3.ApplyLanguage()
out = kill(L3, g3, "Сильвана Ветрокрылая", 16, 2435)
check("English: «… could drop: attempt 37 · chance over all attempts 31%»", any("could drop: attempt 37" in c and "31%" in c for c in out), out)

# ---- bug report in the settings (right click)
obj = L.eval('(LibStub("LibDataBroker-1.1"))').GetDataObjectByName(L.eval('(LibStub("LibDataBroker-1.1"))'), "MountSheetSync")
obj.OnClick(g.UIParent, "RightButton")
regs = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
btn = [r for r in regs if r._type == "Button" and str(r._text or "") == "Сведения для отчёта об ошибке…"]
check("settings window has «Сведения для отчёта об ошибке…»", g.MountSheetSyncSettingsFrame._shown and len(btn) == 1, [str(r._text) for r in regs if r._type == "Button"])
btn[0].MockClick(btn[0])
rep = g.MountSheetSyncReportFrame
boxes = [r for r in regs + [g.ALL_REGIONS[i] for i in range(len(regs) + 1, len(g.ALL_REGIONS) + 1)] if r._type == "EditBox" and r._multiline]
text = str(boxes[0]._text) if boxes else ""
check("report window: the text selected for Ctrl+C", rep is not None and rep._shown and boxes and boxes[0]._highlighted, text)
check("…versions, program state and counts", "Mount Sheet Sync bug report" in text and "Addon: 2.8.0" in text and "Game: 12.1.5 (build 65000" in text
      and "Program: online (version 2.8.0, sheet connected" in text and "attempt rows 2" in text and "Journal: 4 records" in text, text)
check("…no character names, realm or spreadsheet title", "Эйра" not in text and "Ревущий" not in text and "Мирна" not in text, text)
boxes[0]._scripts.OnTextChanged(boxes[0], True)
check("…read-only: typing restores the text", str(boxes[0]._text) == text)
check("no Lua errors after all that", not any("Ошибка" in c for c in chat(g, 0)), chat(g, 0)[-3:])

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
raise SystemExit(1 if failed else 0)
