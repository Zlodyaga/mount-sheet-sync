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
    {"name": "Мировые боссы", "rows": [WH, ["Нет", "MoP", "Valley of the Four Winds", "Сын Галеона", "Галеон", "", "", "", 0.01, "", False, False],
                                       ["Нет", "BfA", "Uldum", "Дракон Четырех Ветров", "Исхак Повелитель Четырех Ветров", "Ежедневно", "", "", 0.03, "", False, False]]},
]
up = host({"catalog": None, "sheets": SHEETS, "mode": "upgrade", "properties": {"layout": "2"}})
sheets = [{"name": s["name"], "rows": s["rows"], "formulas": s["formulas"]} for s in up["sheets"]]
[s for s in sheets if s["name"] == "Рейды"][0]["rows"][1][15] = 36   # 36 attempts so far
status = host({"catalog": None, "sheets": sheets, "mode": "handle", "properties": {"layout": "3"}, "request": {"action": "status"}})["reply"]
status["generated"] = NOW - 3600
check("the program's status carries the attempt rows", [m["mount"] for m in status.get("mounts", [])] == ["Отмщение", "Сын Галеона", "Дракон Четырех Ветров"], status.get("mounts"))


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
    MOCK.wb = {}
    function GetNumSavedWorldBosses() return #MOCK.wb end
    function GetSavedWorldBossInfo(i) local w = MOCK.wb[i]; return w[1], w[2], 3 * 86400 end
    -- loot: MOCK.loot = { guid, … }; the corpse is the target
    MOCK.loot = {}
    TARGET = { guid = nil, name = nil }
    function GetNumLootItems() return #MOCK.loot end
    function GetLootSourceInfo(slot) return MOCK.loot[slot], 1 end
    function UnitGUID(u) if u == "target" then return TARGET.guid end if u == "player" then return "Player-1602-0ABCDEF1" end end
    _UnitName = UnitName
    function UnitName(u) if u == "target" then return TARGET.name end return "Эйра" end
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
    function EncounterJournal_DisplayInstance(id) EJ_INST = id end
    EJ_BOSSES = { [1193] = { "Кто-то", "Сильвана Ветрокрылая" }, [999] = { "Кто-то", "Никто" } }
    function EJ_GetEncounterInfoByIndex(i, inst) local b = EJ_BOSSES[inst]; return b and b[i] end
    MountSheetSyncDB = { version = 1, kills = {}, quiet = %s }
    """ % (NOW, "true" if quiet else "false"))
    EJ = L.eval("CreateFrame('Frame', nil, UIParent)")
    L.globals().EncounterJournal = L.table_from({"encounter": L.table_from({"info": EJ})})
    EJ.encounterTitle = L.eval("UIParent:CreateFontString()")
    EJ.difficulty = L.eval("CreateFrame('Button', nil, UIParent)")
    EJ.lootTab = L.eval("CreateFrame('Button', nil, UIParent)")
    EJ.modelTab = L.eval("CreateFrame('Button', nil, UIParent)")
    EJ.model = L.eval("CreateFrame('Frame', nil, UIParent)")
    EJ.model.Hide(EJ.model)
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

# ---- the unit tooltip: only rares (3.0). Inside instances the game hides NPC names even before the pull, so raid and
# dungeon bosses get nothing there (they have the Adventure Guide icon); world bosses neither.
ISHAK_NAME = "Исхак Повелитель Четырех Ветров"
L.execute('UNIT_CLASS = "rareelite"; function UnitClassification() return UNIT_CLASS end; TIP_UNIT = { "%s", "target" }' % ISHAK_NAME)
def tipl():
    g.GameTooltip.lines = L.table()
    g.UNIT_POSTCALL(g.GameTooltip)
    return [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
tip = tipl()
check("tooltip on a rare (Ishak, rare elite): its mount, no attempts yet",
      any("Дракон Четырех Ветров" in t and "попыток пока нет" in t and "Media\\Icon" in t for t in tip), tip)
L.execute('UNIT_CLASS = "rare"')
check("…a plain rare too", any("Дракон Четырех Ветров" in t for t in tipl()))
L.execute('UNIT_CLASS = "worldboss"; TIP_UNIT = { "Сильвана Ветрокрылая", "target" }')
check("a raid boss: nothing (only the Adventure Guide shows bosses)", tipl() == [])
L.execute('TIP_UNIT = { "Галеон", "target" }')
check("a world boss: nothing", tipl() == [])
L.execute('UNIT_CLASS = "SECRET"; TIP_UNIT = { "%s", "target" }' % ISHAK_NAME)
check("classification hidden by the game: nothing", tipl() == [])
L.execute('UNIT_CLASS = "rareelite"; TIP_UNIT = { "SECRET", "target" }')
check("a hidden (secret) name: nothing added, no error", tipl() == [])
g.SlashCmdList["MOUNTSHEETSYNC"]("tip")
n0 = len(g.CHAT)
tipl()
check("/mss tip: the chat says why there is no hint (the game hides the name)", any("hides this name" in c for c in chat(g, n0)), chat(g, n0))
L.execute('TIP_UNIT = { "Сильвана Ветрокрылая", "target" }; UNIT_CLASS = "worldboss"')
tipl()
check("…and that a boss is not a rare", any("is not a rare (worldboss)" in c for c in chat(g, n0)), chat(g, n0))
L.execute('TIP_UNIT = { nil, "target" }; TARGET.name = "SECRET"; UNIT_CLASS = "rareelite"')
g.GameTooltip.lines = L.table()
g.UNIT_POSTCALL(g.GameTooltip, L.eval('{ lines = { { leftText = "%s" } } }' % ISHAK_NAME))
check("no name from GetUnit / UnitName: the name from the tooltip data is used",
      any("Дракон Четырех Ветров" in str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)))
check("…and /mss tip reports the lines", any(ISHAK_NAME + "» — " in c and "lines" in c for c in chat(g, n0)), chat(g, n0))
L.execute('TIP_UNIT = { "%s", "target" }; TARGET.name = nil; _UIP = UnitIsPlayer; UnitIsPlayer = function() return "SECRET" end' % ISHAK_NAME)
check("«is it a player» hidden by the game: the rare hint still shows", len(tipl()) > 0)
L.execute('UnitIsPlayer = _UIP')
g.SlashCmdList["MOUNTSHEETSYNC"]("tip")
n1 = len(g.CHAT)
L.execute('TIP_UNIT = { "SECRET", "target" }')
tipl()
check("/mss tip again: off, silent", all("tooltip:" not in c for c in chat(g, n1)), chat(g, n1))
L.execute('TIP_UNIT = { "%s", "player" }' % ISHAK_NAME)
check("a player with a rare's name: nothing", tipl() == [])
L.execute('TIP_UNIT = { "Кто-то другой", "target" }')
check("any other rare: nothing", tipl() == [])
L.execute('TIP_UNIT = { "Сильвана Ветрокрылая", "target" }; UnitClassification = nil')

# ---- the Adventure Guide
g.Fire("ADDON_LOADED", "Blizzard_EncounterJournal")
g.EncounterJournal_DisplayEncounter(2435)
regions = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
texts = [r for r in regions if r._type == "FontString" and r._parent is not None and r._parent._uid == EJ._uid]
buttons = [r for r in regions if r._type == "Button" and r._parent is not None and r._parent._uid == EJ._uid]
check("Adventure Guide: Blizzard's function still runs; no text on the page, an icon under the tabs on the right (not by the filters)",
      g.EJ_SHOWN == 2435 and not texts and len(buttons) == 1 and buttons[0]._shown
      and buttons[0]._points[1].rel._uid == EJ.modelTab._uid, (len(texts), len(buttons)))
EJ.model.Show(EJ.model)
check("…«Model» tab open: the icon hides (it would hang over the boss)", not buttons[0]._shown)
EJ.model.Hide(EJ.model)
check("…back to another tab: shown again", buttons[0]._shown)
buttons[0]._scripts.OnEnter(buttons[0])
gt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("…its tooltip: the boss line and the mount line", g.GameTooltip.shown and any("Отмщение" in t and "попыток: 38" in t for t in gt)
      and any("ещё не убит у персонажей: 1" in t for t in gt), gt)
buttons[0]._scripts.OnLeave(buttons[0])
check("…and it hides again", not g.GameTooltip.shown)
g.EncounterJournal_DisplayEncounter(1)
check("…another boss: the icon is hidden", not buttons[0]._shown)
g.EncounterJournal_DisplayInstance(1193)
buttons[0]._scripts.OnEnter(buttons[0])
gt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("raid page (before any boss): the icon shows the mounts of all its bosses, by boss", g.EJ_INST == 1193 and buttons[0]._shown
      and any("Сильвана Ветрокрылая" in t for t in gt) and any("Отмщение" in t for t in gt) and not any("Кто-то" in t for t in gt), gt)
g.EncounterJournal_DisplayInstance(999)
check("…another raid without such mounts: the icon goes away (no old hint left over)", not buttons[0]._shown)
g.EncounterJournal_DisplayEncounter(2435)
g.EncounterJournal_DisplayInstance(999)
check("…also after a boss page with mounts", not buttons[0]._shown)

# ---- hint cache (2.9.1): the answer is remembered per name, but a kill or a new mount shows at once
Lc, gc, nsc, _, _ = new_game()
def tipc():
    return list(nsc.BossHintLines("Сильвана Ветрокрылая", 6).values())
t1 = tipc()
kill(Lc, gc, "Сильвана Ветрокрылая", 16, 2435)
t2 = tipc()
check("hint cache: after a kill the hint changes at once (36 → 37 attempts)",
      any("попыток: 36" in t for t in t1) and any("попыток: 37" in t for t in t2), (t1, t2))
Lc.execute('local f = C_MountJournal.GetMountInfoByID; C_MountJournal.GetMountInfoByID = function(id) '
           'if id == 1471 then return "Отмщение", 351195, 1, false, true, 0, false, false, nil, false, true, id, false end return f(id) end')
gc.Fire("NEW_MOUNT_ADDED", 1471)
t3 = tipc()
check("…and a mount just learned leaves the hint", not any("Отмщение" in t for t in t3), t3)

# ---- quiet: no chat at all
L2, g2, ns2, _, _ = new_game(quiet=True)
out = kill(L2, g2, "Сильвана Ветрокрылая", 16, 2435)
check("chat messages off: no attempt line either", not any("мог выпасть" in c for c in out), out)

# ---- a rare (no encounter, no lockout): the attempt counts when its corpse is looted
L4, g4, ns4, _, _ = new_game()


def loot(L, g, guid, name):
    L.execute("MOCK.now = MOCK.now + 120")
    L.execute('TARGET = { guid = "%s", name = %s }; MOCK.loot = { "%s" }' % (guid, '"%s"' % name if name else "nil", guid))
    n = len(g.CHAT)
    g.Fire("LOOT_READY", False)
    g.RunTimers()
    return chat(g, n)


ISHAK = "Creature-0-3767-1-41-157134-00004A2B3C"
out = loot(L4, g4, ISHAK, "Исхак Повелитель Четырех Ветров")
recs = g4.MountSheetSyncDB.kills
last = recs[len(recs)]
check("looting Ishak: recorded as a world kill (source loot, NPC ID from the GUID)", last.kind == "world" and last.source == "loot"
      and last.boss == "Исхак Повелитель Четырех Ветров" and last.npcID == 157134 and str(last.id).startswith("D-Player-1602-0ABCDEF1-157134-"),
      (last.kind, last.source, last.boss, last.npcID, last.id))
check("…chat: recorded + «Дракон Четырех Ветров мог выпасть: попытка 1 · шанс за все попытки 3%»",
      any("записано" in c for c in out) and any("Дракон Четырех Ветров" in c and "попытка 1 " in c and "3%" in c for c in out), out)
n = len(recs)
out = loot(L4, g4, ISHAK, "Исхак Повелитель Четырех Ветров")
check("the same corpse looted again: nothing new", len(g4.MountSheetSyncDB.kills) == n and not any("мог выпасть" in c for c in out), out)
out = loot(L4, g4, "Creature-0-3767-1-41-12345-0000AAAA", "Песчаный скорпид")
check("an ordinary mob: nothing", len(g4.MountSheetSyncDB.kills) == n, out)
out = loot(L4, g4, "Creature-0-3767-1-41-157134-0000BBBB", None)
check("a corpse that is not the target (name unknown): nothing, no error", len(g4.MountSheetSyncDB.kills) == n and not any("Ошибка" in c for c in out), out)
out = kill(L4, g4, "Галеон", 0, 725, inst=("Долина Четырех Ветров", "none"))
out = loot(L4, g4, "Creature-0-3767-1-41-62346-0000CCCC", "Галеон")
check("a world boss looted right after its kill: not counted twice", not any("мог выпасть" in c for c in out)
      and sum(1 for i in range(1, len(g4.MountSheetSyncDB.kills) + 1) if g4.MountSheetSyncDB.kills[i].boss == "Галеон") == 1, out)

# ---- a kill whose name the game hides: the lockout scan right after it finds it and still says the attempt
L5, g5, ns5, _, _ = new_game()
L5.execute("MOCK.now = MOCK.now + 300")
L5.execute('MOCK.inst = { "Долина Четырех Ветров", "none", 0, 0 }')
n0 = len(g5.CHAT)
g5.Fire("BOSS_KILL", 725, "SECRET")
L5.execute('MOCK.wb = { { "Галеон", 725 } }')
g5.Fire("UPDATE_INSTANCE_INFO")
g5.RunTimers()
out = chat(g5, n0)
check("hidden name: found in the world boss lockouts a moment later — «Сын Галеона мог выпасть: попытка 1»",
      any("Сын Галеона" in c and "попытка 1 " in c for c in out), out)
L5.execute('MOCK.wb = { { "Галеон", 725 }, { "Ундаста", 826 } }')
L5.execute("MOCK.now = MOCK.now + 3600")
n0 = len(g5.CHAT)
g5.Fire("UPDATE_INSTANCE_INFO")
g5.RunTimers()
check("…a later scan (login) without a kill: no attempt lines", not any("мог выпасть" in c for c in chat(g5, n0)), chat(g5, n0))

# ---- English
L3, g3, ns3, _, _ = new_game()
L3.execute('MountSheetSyncDB.language = "enUS"; MSS_SET = true')
ns3.ApplyLanguage()
out = kill(L3, g3, "Сильвана Ветрокрылая", 16, 2435)
check("English: «… could drop: attempt 37 · chance over all attempts 31%»", any("could drop: attempt 37" in c and "31%" in c for c in out), out)

# ---- 3.0: «Попытки» tab in the window
obj = L.eval('(LibStub("LibDataBroker-1.1"))').GetDataObjectByName(L.eval('(LibStub("LibDataBroker-1.1"))'), "MountSheetSync")
obj.OnClick(g.UIParent, "LeftButton")
regs = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
tab = [r for r in regs if r._type == "Button" and r._template == "PanelTopTabButtonTemplate" and str(r._text) == "Попытки"][0]
tab.MockClick(tab)
g.RunTimers()
regs = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
box = [r for r in regs if r._type == "Frame" and r._template == "WowScrollBoxList" and r._view._extent == 16][0]
rows = [box._pool[i] for i in range(1, len(box._pool) + 1) if box._pool[i]._shown]
texts = [str(f.text._text) for f in rows]
att = ns.GetAttemptsList()
check("«Попытки»: head line, then uncollected mounts, most expected drops first",
      texts[0] == "|cff8c8c8cНесобранные маунты со случайным шансом: %d · выше — больше ожидаемых выпадений|r" % len(att)
      and texts[1].startswith("   |cffc9a0ffОтмщение|r") and texts[1].endswith("попыток: 38, шанс 32%") and len(texts) == len(att) + 1, texts)
rows[1]._scripts.OnEnter(rows[1])
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("…hover: how many characters can still try this week", tt[0] == "Отмщение" and any("ещё не убит у персонажей: 1" in t for t in tt), tt)
check("…the same numbers as the boss tooltip (one count for both)", att[1].n == 38 and att[1].left == 1)
g.MountSheetSyncFrame.Hide(g.MountSheetSyncFrame)

# ---- bug report in the settings (right click)
obj.OnClick(g.UIParent, "RightButton")
regs = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
btn = [r for r in regs if r._type == "Button" and str(r._text or "") == "Сведения для отчёта об ошибке…"]
check("settings window has «Сведения для отчёта об ошибке…»", g.MountSheetSyncSettingsFrame._shown and len(btn) == 1, [str(r._text) for r in regs if r._type == "Button"])
btn[0].MockClick(btn[0])
rep = g.MountSheetSyncReportFrame
boxes = [r for r in regs + [g.ALL_REGIONS[i] for i in range(len(regs) + 1, len(g.ALL_REGIONS) + 1)] if r._type == "EditBox" and r._multiline]
text = str(boxes[0]._text) if boxes else ""
check("report window: the text selected for Ctrl+C", rep is not None and rep._shown and boxes and boxes[0]._highlighted, text)
check("…versions, program state and counts", "Mount Sheet Sync bug report" in text and "Addon: 3.0.0" in text and "Game: 12.1.5 (build 65000" in text
      and "Program: online (version 2.8.0, sheet connected" in text and "attempt rows 3" in text and "Journal: 4 records" in text, text)
check("…no character names, realm or spreadsheet title", "Эйра" not in text and "Ревущий" not in text and "Мирна" not in text, text)
boxes[0]._scripts.OnTextChanged(boxes[0], True)
check("…read-only: typing restores the text", str(boxes[0]._text) == text)
check("no Lua errors after all that", not any("Ошибка" in c for c in chat(g, 0)), chat(g, 0)[-3:])

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
raise SystemExit(1 if failed else 0)
