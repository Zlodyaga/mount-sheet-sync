"""Mount Sheet Sync 1.4: «Осталось» tab (list from the sheet + live ticks) and new-mount records.

The list is the real reply of Code.gs action "status" for the fixture workbook, turned into
SheetData.lua the way the tray app does it (sheet_lua below mirrors SheetData.Build in C#).
Usage: test_addon_left.py [<SheetData.lua written by the exe>] [<layout out json>]
"""
import json
import os
import re
import subprocess
import sys
import time
from lupa import lua51
import paths

ADDON_DIR = paths.ADDON_DIR
HERE = os.path.dirname(os.path.abspath(__file__))
SHEET_FILE = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] != "-" else None
OUT_LAYOUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "..", "layout_left.json")
NOW = int(time.time())
UNTIL_RESET = 3 * 86400 + 1234

checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)))


# ---- the sheet's status reply -> SheetData.lua
def lua_str(s):
    out = []
    for ch in s:
        o = ord(ch)
        if ch == "\\": out.append("\\\\")
        elif ch == '"': out.append('\\"')
        elif ch == "\n": out.append("\\n")
        elif ch == "\r": out.append("\\r")
        elif o < 32 or o == 127: out.append("\\%03d" % o)
        else: out.append(ch)
    return '"' + "".join(out) + '"'


def sheet_lua(v):
    if v is None: return "nil"
    if isinstance(v, bool): return "true" if v else "false"
    if isinstance(v, (int, float)): return repr(v)
    if isinstance(v, str): return lua_str(v)
    if isinstance(v, list): return "{" + ", ".join(sheet_lua(x) for x in v) + "}"
    return "{" + ", ".join("[%s] = %s" % (lua_str(k), sheet_lua(x)) for k, x in v.items() if x is not None and k != "ok") + "}"


if SHEET_FILE:
    sheet_src = open(SHEET_FILE, encoding="utf-8").read()
else:
    subprocess.run([sys.executable, os.path.join(HERE, "make_fixture.py"), os.path.join(HERE, "wb_left.json"), "real"], check=True)
    subprocess.run(["node", os.path.join(HERE, "dump_status.js"), os.path.join(HERE, "wb_left.json"), os.path.join(HERE, "status_left.json")], check=True)
    status = json.load(open(os.path.join(HERE, "status_left.json"), encoding="utf-8"))
    sheet_src = "MountSheetSyncSheet = " + sheet_lua(status) + "\n"

L = lua51.LuaRuntime(unpack_returned_tuples=True)
L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
L.execute(r"""
MOCK = {
  now = %d, gt = 1000, untilReset = %d,
  name = "Эйра", realm = "Ревущий фьорд", guid = "Player-1602-0ABCDEF1",
  inst = { name = "Гробница Предвечных", type = "raid", diff = 16, max = 20, id = 2481 },
  saved = {}, world = {},
  mounts = {
    [363] = { "Синий протодракон", 59996, "|cFFFFD200Добыча:|r Скади Безжалостный|n|cFFFFD200Подземелье:|r Вершина Утгард|n|cFFFFD200Сложность:|r Героический" },
    [1471] = { "Смотритель Зерета", 368158, "|cFFFFD200Добыча:|r Тюремщик|n|cFFFFD200Рейд:|r Гробница Предвечных" },
  },
}
SECRET = setmetatable({}, { __tostring = function() error("addon touched a secret value!") end })
function issecretvalue(v) return rawequal(v, SECRET) end
C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return MOCK.untilReset end }
function time() return MOCK.now end
function GetTime() return MOCK.gt end
date = os.date
function UnitName() return MOCK.name end
function GetRealmName() return MOCK.realm end
function UnitGUID() return MOCK.guid end
local DIFF = { [1] = "Обычный", [2] = "Героический", [14] = "Обычный", [15] = "Героический", [16] = "Эпохальный",
               [24] = "Путешествие во времени", [167] = "Торгаст" }
function GetDifficultyInfo(id) return DIFF[id] end
function GetInstanceInfo() local i = MOCK.inst; return i.name, i.type, i.diff, DIFF[i.diff], i.max, 0, false, i.id end
function RequestRaidInfo() REQ = REQ + 1 end
function ReloadUI() RELOADED = true end
function GetNumSavedInstances() return 0 end
function GetNumSavedWorldBosses() return 0 end
C_MountJournal = {
  GetMountInfoByID = function(id)
    local m = MOCK.mounts[id]
    if not m then return nil end
    return m[1], m[2], 132226, false, true, 0, false, false, nil, false, true, id, false
  end,
  GetMountInfoExtraByID = function(id)
    local m = MOCK.mounts[id]
    return 12345, "Описание", m and m[3], false, 230, 4, 0, 0, false
  end,
}
function Advance(sec) MOCK.now = MOCK.now + sec; MOCK.gt = MOCK.gt + sec; MOCK.untilReset = MOCK.untilReset - sec end
-- characters that logged in on this account before (class for the colours on the left, GUID for their own kills)
MountSheetSyncDB = { version = 1, kills = {}, chars = {
  ["Эйра-Ревущий фьорд"] = { name = "Эйра", realm = "Ревущий фьорд", classFile = "HUNTER" },
  ["Элтара-Ревущий фьорд"] = { name = "Элтара", realm = "Ревущий фьорд", classFile = "MAGE", guid = "Player-1602-0E1715" },
  ["Мирна-Ревущий фьорд"] = { name = "Мирна", realm = "Ревущий фьорд", classFile = "PRIEST" },
  ["Каэлия-Ревущий фьорд"] = { name = "Каэлия", realm = "Ревущий фьорд", classFile = "WARLOCK" },
  ["Эллин-Ревущий фьорд"] = { name = "Эллин", realm = "Ревущий фьорд", classFile = "PALADIN" },
} }
RAID_CLASS_COLORS = { HUNTER = { colorStr = "ffaad372" }, MAGE = { colorStr = "ff3fc7eb" }, PRIEST = { colorStr = "ffffffff" },
                      WARLOCK = { colorStr = "ff8788ee" }, PALADIN = { colorStr = "fff48cba" } }
""" % (NOW, UNTIL_RESET))
g = L.globals()

toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
check("SheetData.lua is loaded before the core, Remaining.lua before the window",
      files.index("SheetData.lua") < files.index("MountSheetSync.lua") < files.index("Remaining.lua") < files.index("UI.lua"), files)
ns = L.table()
loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
for name in files:
    src = sheet_src if name == "SheetData.lua" else open(os.path.join(ADDON_DIR, name), encoding="utf-8").read()
    loader(src, name)("MountSheetSync", ns)
shipped = open(os.path.join(ADDON_DIR, "SheetData.lua"), encoding="utf-8").read()
check("shipped SheetData.lua is valid Lua and empty", "MountSheetSyncSheet = nil" in shipped and loader(shipped, "x") is not None)

g.Fire("ADDON_LOADED", "MountSheetSync")
g.Fire("PLAYER_LOGIN"); g.RunTimers()


def chat_all():
    return "\n".join(str(g.CHAT[i]) for i in range(1, len(g.CHAT) + 1))


def remaining():
    r = ns.GetRemaining()
    return r


r = remaining()
check("list for Эйра: 4 raid + 20 dungeon + 7 world left, 5 rows already «Да», nothing ticked yet",
      r.state == "ok" and r.name == "Эйра" and r.total == 36 and r.left == 31 and len(r["items"]) == 31, (r.state, r.total, r.left))


def kill(boss, inst_name, inst_type, diff, event="ENCOUNTER_END", eid=None):
    L.execute("Advance(200)")
    g.MOCK.inst = L.table_from({"name": inst_name, "type": inst_type, "diff": diff, "max": 20, "id": 1})
    if event == "ENCOUNTER_END":
        g.Fire("ENCOUNTER_END", eid or (abs(hash(boss)) % 100000), boss, diff, 20, 1)
    else:
        g.Fire("BOSS_KILL", eid or (abs(hash(boss)) % 100000), boss)
    g.RunTimers()


def done_bosses():
    out = []
    rr = remaining()
    for i in range(1, len(rr["items"]) + 1):
        e = rr["items"][i]
        if e.done:
            out.append(str(e.item.boss))
    return sorted(out)


kill("Тюремщик", "Гробница Предвечных", "raid", 16)
check("Mythic kill ticks the Mythic row", done_bosses() == ["Тюремщик"], done_bosses())
kill("Сильвана Ветрокрылая", "Святилище Господства", "raid", 15)
check("Heroic kill does not tick a Mythic-only row", "Сильвана Ветрокрылая" not in done_bosses())
kill("Девятка", "Святилище Господства", "raid", 14)
check("Normal kill ticks a Normal/Heroic/Mythic row", "Девятка" in done_bosses())
kill("Фиракк Пылающий", "Амирдрассил, Надежда Сна", "raid", 16)
check("«Фиракк» in the sheet = «Фиракк Пылающий» in the game", "Фиракк" in done_bosses())
kill("Король Имирон", "Вершина Утгард", "party", 24)
check("any boss in Timewalking ticks «Любой босс … путешествий во времени»", "Любой босс подземелий путешествий во времени" in done_bosses())
kill("Скади Безжалостный", "Вершина Утгард", "party", 24)
check("…but a Timewalking Skadi does not tick the Heroic Skadi row", "Скади Безжалостный" not in done_bosses())
kill("Мастер оков", "Торгаст, Башня Проклятых", "scenario", 167)
check("«Боссы Торгаста»: any boss inside Torghast (unknown difficulty, row allows any)", "Боссы Торгаста" in done_bosses())
kill("Галлеон", "Долина Четырех Ветров", "none", 0, event="BOSS_KILL")
check("world boss ticks the world row", "Галлеон" in done_bosses())
ns.AddManual("Ундаста")
check("manual mark ticks the world row by name", "Ундаста" in done_bosses())
kill("Ша Злости", "Вершина Кунь-Лай", "raid", 14)
check("a raid record never ticks a world row", "Ша Злости" not in done_bosses())
r = remaining()
check("7 ticked, 24 left", r.left == 24 and r.total == 36, (r.left, r.total))

# ---- the window: tabs
obj = L.eval('(LibStub("LibDataBroker-1.1"))').GetDataObjectByName(L.eval('(LibStub("LibDataBroker-1.1"))'), "MountSheetSync")
obj.OnClick(g.UIParent, "LeftButton")
win = g.MountSheetSyncFrame


def regions(kind=None):
    return [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1) if kind is None or g.ALL_REGIONS[i]._type == kind]


def under_window(x):
    p = x._parent
    while p is not None:
        if p._uid == win._uid:
            return True
        p = p._parent
    return False


tabs = [x for x in regions("Button") if x._template == "PanelTopTabButtonTemplate"]
check("four Blizzard top tabs above the list", [str(t._text) for t in tabs] == ["Осталось (24)", "Все персонажи", "Попытки", "Журнал"],
      [str(t._text) for t in tabs])
check("3.0: «Осталось» selected by default", win.selectedTab == 1 and not tabs[0].IsEnabled(tabs[0]) and tabs[3].IsEnabled(tabs[3]))


def boxes(extent):
    """ScrollBox lists of the window: 16 px rows — the main list, 20 px — the characters on the left."""
    return [x for x in regions("Frame") if x._template == "WowScrollBoxList" and under_window(x) and x._view._extent == extent]


def pool(extent):
    box = boxes(extent)[0]
    return [box._pool[i] for i in range(1, len(box._pool) + 1) if box._pool[i]._shown]


def shown_rows():
    return [str(f.text._text) for f in pool(16)]


def plain(text):
    return re.sub(r"\|c[0-9a-fA-F]{8}|\|r", "", str(text))


def char_rows():
    return [(plain(f.name._text), str(f.count._text), bool(f.sel._shown)) for f in pool(20)]


check("two ScrollBox lists with a scroll bar each", len(boxes(16)) == 1 and len(boxes(20)) == 1 and boxes(16)[0]._managedBar)
tabs[3].MockClick(tabs[3]); g.RunTimers()
check("journal tab shows the journal", any("Тюремщик" in t for t in shown_rows()) and not any("Осталось" in t for t in shown_rows()))
tabs[0].MockClick(tabs[0]); g.RunTimers()
rows = shown_rows()
check("«Осталось» tab: info line first", (rows[0].startswith("|cff8c8c8cОсталось 24 из 36 - список из таблицы сегодня в ") or rows[0].startswith("|cff8c8c8cОсталось 24 из 36 - список из таблицы вчера в ")), rows[0])
check("no reset this week known: orange reminder under it", rows[1] == "|cffff9a4dНа этой неделе «Сброс недели» не нажимали — в таблице могут быть «Да» прошлой недели.|r", rows[1])
check("group headers in order", [t for t in rows if t.startswith("|cffffd200")] == ["|cffffd200Рейды|r", "|cffffd200Подземелья|r", "|cffffd200Мировые боссы|r"])
check("row: boss (difficulty) — mount · place",
      "   Сильвана Ветрокрылая |cff8c8c8c(Mythic)|r — |cffc9a0ffМесть|r |cff8c8c8c- Sanctum of Domination|r" in rows, [t for t in rows if "Сильвана" in t])
raid = rows[rows.index("|cffffd200Рейды|r") + 1: rows.index("|cffffd200Подземелья|r")]
check("killed rows go to the bottom of their group with a check mark", raid[0].startswith("   Сильвана") and raid[-1].startswith("   |TInterface\\RaidFrame\\ReadyCheck-Ready")
      and "убит, ждёт таблицы" in raid[-1], raid)
check("selected tab is remembered", g.MountSheetSyncDB.tab == "left" and win.selectedTab == 1)
check("list scrolled to the top on tab switch", boxes(16)[0]._vscroll == 0)

# ---- 3.0: characters on the left, any character's «Осталось», «Все персонажи», «Попытки»
chars = char_rows()
allr = ns.GetAllRemaining()
check("3.0: characters from the sheet on the left, the current one first and selected",
      chars and chars[0][0] == "Эйра" and chars[0][2] and not any(c[2] for c in chars[1:]), chars)
check("…what is left next to each name", chars[0][1] == "24" and len(chars) == len(allr), chars)
check("…every character of the sheet", "Элтара" in [c[0] for c in chars] and any(c[0].startswith("Каэлия") for c in chars), chars)
lefts = [allr[i].r.left for i in range(2, len(allr) + 1)]
check("…then the ones with more left first", lefts == sorted(lefts, reverse=True), lefts)
box = boxes(16)[0]
inits = box._inits
win.Hide(win); win.Show(win); g.RunTimers()
check("reopening the window with nothing changed rebuilds no rows", box._inits == inits, (inits, box._inits))
eltara = [f for f in pool(20) if plain(f.name._text) == "Элтара"][0]
eltara.MockClick(eltara); g.RunTimers()
rows = shown_rows()
check("click on Элтара: her «Осталось» (12 of 16; Эйра's kills do not count)", rows[0].startswith("|cff8c8c8cОсталось 12 из 16"), rows[:1])
check("…she is selected on the left", [c[0] for c in char_rows() if c[2]] == ["Элтара"], char_rows())
check("…no «не фармлю» menu on another character's rows", not [f for f in pool(16) if f.elem.entry is not None])
check("…the tab title still counts the current character", tabs[0]._text == "Осталось (24)", tabs[0]._text)
check("…her name in her class colour: the sheet's key is just «элтара», found among this account's characters",
      str(eltara.name._text) == "|cff3fc7ebЭлтара|r", str(eltara.name._text))
L.execute("MSS_NS = ...", ns)
L.execute("""
local r
for _, c in ipairs(MSS_NS.GetAllRemaining()) do if c.name == "Элтара" then r = c.r end end
for _, e in ipairs(r.items) do
  if not e.done and e.item.kind == "world" then
    table.insert(MountSheetSyncDB.kills, { id = "M-eltara-test", kind = "manual", boss = e.item.boss, char = "Элтара",
      charKey = "Player-1602-0E1715", time = MOCK.now, weekEnd = MountSheetSyncDB.kills[1].weekEnd })
    break
  end
end
""")
L.execute("MSS_NS.OnChange()")   # what a new record does in the game: the open window refreshes
g.RunTimers()
check("…a kill Элтара made on this account (her GUID) ticks her own list", shown_rows()[0].startswith("|cff8c8c8cОсталось 11 из 16"),
      shown_rows()[:1])
L.execute("table.remove(MountSheetSyncDB.kills)")
g.RunTimers()
tabs[1].MockClick(tabs[1]); g.RunTimers()
rows = shown_rows()
summ = ns.GetSummary()
check("«Все персонажи»: head line with rows and characters",
      rows[0] == "|cff8c8c8cЕщё убить на этой неделе: строк %d - персонажей в таблице: %d|r" % (len(summ), len(allr)), rows[0])
check("…grouped by kind", [t for t in rows if t.startswith("|cffffd200")] == ["|cffffd200Рейды|r", "|cffffd200Подземелья|r", "|cffffd200Мировые боссы|r"],
      [t for t in rows if t.startswith("|cffffd200")])
first = summ[1]
check("…a row of the sheet, then who still has to kill it", rows[2] == "   " + ns.FormatRemaining(L.table_from({"item": first.item}))
      and rows[3].startswith("      |cff8c8c8c") and str(first.names[1]) in rows[3], rows[2:4])
check("…more than %d names: «и ещё N»" % 3, all((" и ещё " in rows[3 + 2 * (i - 1)]) == (len(summ[i].names) > 3)
                                                 for i in range(1, len(summ) + 1) if summ[i].kind == first.kind), rows[2:8])
names_row = pool(16)[3]
names_row._scripts.OnEnter(names_row)
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("…hover: every name in the tooltip", tt[1] == ", ".join(str(first.names[i]) for i in range(1, len(first.names) + 1)), tt)
eira = [f for f in pool(20) if plain(f.name._text) == "Эйра"][0]
eira.MockClick(eira); g.RunTimers()
check("click on a character from another tab opens his «Осталось»", win.selectedTab == 1 and shown_rows()[0].startswith("|cff8c8c8cОсталось 24 из 36"))
tabs[2].MockClick(tabs[2]); g.RunTimers()
att = ns.GetAttemptsList()
empty = [x for x in regions("FontString") if x._font == "GameFontDisable" and under_window(x)][0]
check("«Попытки»: the counter or a word that there is none yet",
      (len(att) == 0 and empty._shown and "Счётчика попыток пока нет" in str(empty._text))
      or (len(att) > 0 and shown_rows()[0].startswith("|cff8c8c8cНесобранные маунты со случайным шансом: %d" % len(att))), len(att))
tabs[0].MockClick(tabs[0]); g.RunTimers()

LUA_RAWGET = L.eval("rawget")


# ---- layouts for the screenshots (tools/screens): the window on both tabs and the settings window
def dump_layout(root, path):
    def under(x):
        p = x._parent
        while p is not None:
            if p._uid == root._uid:
                return True
            p = p._parent
        return False
    export = [x for x in regions() if under(x)]

    def ref(x):
        if x is None:
            return None
        if x._uid == root._uid:
            return 0
        for i, y in enumerate(export):
            if x._uid == y._uid:
                return i + 1
        return "UIParent"

    layout = []
    for i, x in enumerate([root] + export):
        pts = []
        for j in range(1, len(x._points) + 1):
            p = x._points[j]
            pts.append({"point": p.point, "rel": ref(p.rel), "relPoint": p.relPoint, "x": p.x, "y": p.y})
        layout.append({"i": i, "type": x._type, "template": x._template, "font": x._font if x._type == "FontString" else None,
                       "text": str(x._text) if x._text is not None else None, "w": x._w, "h": x._h, "shown": x._shown,
                       "checked": bool(x._checked) if x._type == "CheckButton" else None,
                       "title": str(x._title) if x._title is not None else None,
                       "radio": x._type == "CheckButton" and LUA_RAWGET(x, "code") is not None,
                       "layer": x._layer if x._type == "Texture" else None,
                       "texture": str(x._texture) if x._type == "Texture" and x._texture is not None else None,
                       "color": [x._color[k] for k in range(1, 5)] if x._type == "Texture" and x._color is not None else None,
                       "parent": ref(x._parent) if i else "UIParent", "points": pts, "scrollChildOf": None})
    for i, x in enumerate(export):
        if x._type == "ScrollFrame" and x._scrollChild is not None:
            layout[ref(x._scrollChild)]["scrollChildOf"] = i + 1
    json.dump(layout, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


tabs[0].MockClick(tabs[0]); g.RunTimers()
dump_layout(win, OUT_LAYOUT)
LAYOUT_DIR = os.path.dirname(os.path.abspath(OUT_LAYOUT))
tabs[1].MockClick(tabs[1]); g.RunTimers()
dump_layout(win, os.path.join(LAYOUT_DIR, "layout_all.json"))
tabs[3].MockClick(tabs[3]); g.RunTimers()
dump_layout(win, os.path.join(LAYOUT_DIR, "layout_log.json"))
# «Попытки» for the picture: the fixture's sheet is older than the counter — rows like a real player's (2.9 program)
L.execute(r"""SCREEN_MOUNTS = {
  {p = 0.01, mount = "Пламенное Крыло Алисразор", d = {1, 2, 3, 4, 5, 6, 9, 14, 15, 150}, sheet = "Рейды", id = 425, boss = "Алисразор", diff = "Normal/Heroic", place = "Firelands", names = {"алисразор", "alysrazor"}, kind = "raid", row = 30, n = 105},
  {p = 0.01, mount = "Эксперимент 12-Б", d = {1, 2, 3, 4, 5, 6, 9, 14, 15, 150}, sheet = "Рейды", id = 445, boss = "Ультраксион", diff = "Normal/Heroic", place = "Dragon Soul", names = {"ультраксион", "ultraxion"}, kind = "raid", row = 31, n = 96},
  {p = 0.01, mount = "Пылающий дракон", d = {1, 2, 3, 4, 5, 6, 9, 14, 15, 150}, sheet = "Рейды", id = 442, boss = "Безумие Смертокрыла", diff = "Normal/Heroic", place = "Dragon Soul", names = {"безумие смертокрыла", "madness of deathwing"}, kind = "raid", row = 32, n = 95},
  {p = 0.01, mount = "Служанка Хранительницы жизни", d = {2, 5, 6, 15}, sheet = "Рейды", id = 444, boss = "Безумие Смертокрыла", diff = "Heroic", place = "Dragon Soul", names = {"безумие смертокрыла", "madness of deathwing"}, kind = "raid", row = 33, n = 92},
  {p = 0.01, mount = "Грозовой кобальтовый облачный змей", d = {}, sheet = "Мировые боссы", boss = "Налак", diff = "", place = "Isle of Thunder", names = {"налак", "nalak"}, kind = "world", row = 5, n = 59},
  {p = 0.01, mount = "Небесный ониксовый облачный змей", d = {}, sheet = "Мировые боссы", boss = "Ша Злости", diff = "", place = "Kun-Lai Summit", names = {"ша злости", "sha of anger"}, kind = "world", row = 2, n = 55},
  {p = 0.01, mount = "Конь смерти Ривендера", d = {1, 3, 4, 9, 14, 150}, sheet = "Подземелья", id = 69, boss = "Лорд Аурий Ривендер", diff = "Normal", place = "Stratholme", names = {"лорд аурий ривендер", "lord aurius rivendare"}, kind = "dungeon", row = 3, n = 40},
  {p = 0.01, mount = "Сын Галеона", d = {}, sheet = "Мировые боссы", boss = "Галлеон", diff = "", place = "Valley of the Four Winds", names = {"галлеон", "galleon"}, kind = "world", row = 3, n = 32},
  {p = 0.01, mount = "Призрачный крылобег", d = {8, 16, 23}, sheet = "Подземелья", id = 2805, boss = "Неспокойное Сердце", diff = "Mythic / Mythic+", place = "Windrunner Spire", names = {"неспокойное сердце", "restless heart"}, kind = "dungeon", row = 57, n = 18},
}
SCREEN_OLD = MountSheetSyncSheet
local copy = {}
for k, v in pairs(MountSheetSyncSheet) do copy[k] = v end
copy.mounts = SCREEN_MOUNTS
MountSheetSyncSheet = copy""")
tabs[2].MockClick(tabs[2]); g.RunTimers()
check("«Попытки» with rows (for the picture): most expected drops first", ns.GetAttemptsList()[1].name == "Пламенное Крыло Алисразор", ns.GetAttemptsList()[1].name)
dump_layout(win, os.path.join(LAYOUT_DIR, "layout_attempts.json"))
L.execute("MountSheetSyncSheet = SCREEN_OLD")
tabs[0].MockClick(tabs[0]); g.RunTimers()
g.SlashCmdList["MOUNTSHEETSYNC"]("settings"); g.RunTimers()
sf = g.MountSheetSyncSettingsFrame
check("settings window opens for the screenshots", sf is not None and sf._shown)
dump_layout(sf, os.path.join(LAYOUT_DIR, "layout_settings.json"))
sf.Hide(sf)
tabs[0].MockClick(tabs[0]); g.RunTimers()

kill("Кто-то совсем другой", "Святилище Господства", "raid", 16)
check("odd boss name does not tick anything new", remaining().left == 24, remaining().left)
kill("Сильвана Ветрокрылая", "Святилище Господства", "raid", 16)
check("new kill refreshes the open tab and its title", tabs[0]._text == "Осталось (23)" and any("Сильвана" in t and "убит" in t for t in shown_rows()), tabs[0]._text)

# ---- broker tooltip, slash command
g.GameTooltip.lines = L.table()
obj.OnTooltipShow(g.GameTooltip)
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("tooltip: what is left this week", "Осталось на этой неделе: 23 из 36" in tt, tt)
g.SlashCmdList["MOUNTSHEETSYNC"]("left")
lines = chat_all().split("\n")
start = max(i for i, t in enumerate(lines) if "Осталось на этой неделе (Эйра): 23 из 36" in t)
printed = [t for t in lines[start + 1:] if t.startswith("   ")]
check("/mss left prints the summary and the 23 rows left (ticked ones skipped)",
      len(printed) == 23 and not any("Тюремщик" in t or "Сильвана" in t for t in printed), (len(printed), printed[:3]))
g.SlashCmdList["MOUNTSHEETSYNC"]("help")
check("/mss help lists /mss left", "/mss left" in chat_all())

# ---- another character: only his rows and his kills
g.MOCK.name = "Элтара"; g.MOCK.guid = "Player-1602-0E1715"
r = remaining()
check("Элтара: 5 raid + 7 world left (+4 «Да»), Эйра' kills do not count", r.state == "ok" and r.name == "Элтара" and r.total == 16 and r.left == 12, (r.state, r.total, r.left))
g.MOCK.name = "Каэлия"; g.MOCK.guid = "Player-1602-0C0FFEE"
r = remaining()
check("«Каэлия» finds «Каэлия (Лок)»", r.state == "ok" and r.name == "Каэлия")
g.MOCK.name = "Тралл"; g.MOCK.guid = "Player-1602-0ABC"
r = remaining()
check("character without sheets: nochar", r.state == "nochar" and r.name == "Тралл")
win.Hide(win); win.Show(win)
empty = [x for x in regions("FontString") if x._font == "GameFontDisable" and under_window(x)][0]
check("window explains a missing character", empty._shown and "Персонажа «Тралл» нет в таблице" in str(empty._text) and tabs[0]._text == "Осталось", str(empty._text))

# ---- «Сброс недели»: known from the sheet (resetAt) or from the journal
g.MOCK.name = "Эйра"; g.MOCK.guid = "Player-1602-0ABCDEF1"
week_start = NOW + UNTIL_RESET - 7 * 86400
saved_generated = g.MountSheetSyncSheet.generated
g.MountSheetSyncSheet.resetAt = week_start + 600
win.Hide(win); win.Show(win)
r = remaining()
check("sheet says it was reset this week: no reminder", not r.notReset and not r.afterReset
      and not any("Сброс недели" in t for t in shown_rows()), [t for t in shown_rows() if "Сброс" in t])
g.MountSheetSyncSheet.resetAt = week_start - 2 * 86400
check("sheet was last reset before this week: reminder", remaining().notReset is True)
g.MountSheetSyncSheet.generated = NOW - 8 * 86400
check("list from last week, no reset: reminder (not «after reset»)", remaining().notReset is True and not remaining().afterReset)
g.MountSheetSyncSheet.generated = saved_generated
g.MountSheetSyncSheet.resetAt = None

before = remaining()
kill("Ультраксион", "Душа Дракона", "raid", 14)
check("kill of a row that is already «Да» in the sheet: not listed, nothing changes",
      remaining().left == before.left and "Ультраксион" not in done_bosses(), done_bosses())
L.execute("Advance(60)")
ns.ResetWeek()
r = remaining()
check("after «Сброс недели»: «Да» rows are back in the list at once", r.afterReset is True and not r.notReset and r.total == 36
      and len(r["items"]) == 36 and r.left == before.left + 4, (r.afterReset, r.total, len(r["items"]), r.left, before.left))
check("…a kill of this week before the reset stays ticked (the program re-marks it)", "Ультраксион" in done_bosses()
      and "Тюремщик" in done_bosses(), done_bosses())
win.Hide(win); win.Show(win)
rows = shown_rows()
check("…the window says the week was reset", rows[1] == "|cff8cff8cНеделя сброшена — в списке снова все строки, убитые отмечены галочкой.|r"
      and rows[0].startswith("|cff8c8c8cОсталось %d из 36" % (before.left + 4)), rows[:2])
check("…and the tab title counts them", tabs[0]._text == "Осталось (%d)" % (before.left + 4), tabs[0]._text)
other = L.table_from({"id": "R-other", "kind": "reset", "char": "Элтара", "charKey": "Player-1602-0E1715", "boss": "Сброс недели",
                      "time": NOW - 30 * 86400, "weekEnd": NOW - 23 * 86400})
L.globals().MountSheetSyncDB.kills[len(L.globals().MountSheetSyncDB.kills) + 1] = other
check("a reset from an older week in the journal changes nothing", remaining().afterReset is True)
_resets = [g.MountSheetSyncDB.kills[i].time for i in range(1, len(g.MountSheetSyncDB.kills) + 1) if g.MountSheetSyncDB.kills[i].kind == "reset"]
g.MountSheetSyncSheet.generated = max(_resets) + 30   # the program brought the list made after the reset
r = remaining()
check("new list from the sheet after the reset: «Да» rows count as done again", not r.afterReset and not r.notReset and len(r["items"]) == 31, (r.afterReset, len(r["items"])))
g.MountSheetSyncSheet.generated = saved_generated
for i in range(len(g.MountSheetSyncDB.kills), 0, -1):
    if g.MountSheetSyncDB.kills[i].kind == "reset":
        L.eval("table.remove")(g.MountSheetSyncDB.kills, i)
check("journal without resets: back to the reminder", remaining().notReset is True)

# ---- no list at all
sheet_table = g.MountSheetSyncSheet
g.MountSheetSyncSheet = None
win.Hide(win); win.Show(win)
check("no list yet: explained", remaining().state == "nodata" and "Списка пока нет" in str(empty._text) and empty._shown)
g.SlashCmdList["MOUNTSHEETSYNC"]("left")
check("/mss left without a list: explained, with the address of the program's site",
      "Списка «Осталось» пока нет" in chat_all().split("\n")[-2]
      and "Программы ещё нет? Она бесплатная: https://zlodyaga.github.io/" in chat_all().split("\n")[-1], chat_all()[-300:])
urls = [r for r in regions("EditBox") if r._text == "https://zlodyaga.github.io/"]
check("no list yet: the site address in a box to copy, under the text", len(urls) == 1 and urls[0]._shown
      and "Программы ещё нет" in str(empty._text), [(r._text, r._shown) for r in urls])
urls[0]._text = "что-то другое"
urls[0]._scripts.OnTextChanged(urls[0], True)
check("…the address cannot be changed", urls[0]._text == "https://zlodyaga.github.io/")
g.MountSheetSyncSheet = sheet_table
win.Hide(win); win.Show(win)
check("with a list: no address box", not [r for r in regions("EditBox") if r._text == "https://zlodyaga.github.io/" and r._shown])

# ---- text from the sheet is shown literally
item = None
r = remaining()
for i in range(1, len(r["items"]) + 1):
    if str(r["items"][i].item.boss) == "Девятка":
        item = r["items"][i].item
item.mount = "Конь|cffff0000красный"
check("«|» in a cell cannot inject colour codes", "Конь||cffff0000красный" in ns.FormatRemaining(L.table_from({"item": item, "done": False})))

# ---- new mount -> record for «Собрано?»
n = len(g.MountSheetSyncDB.kills)
week_count = ns.CountThisWeek()
left_before_mount = remaining().left
g.Fire("NEW_MOUNT_ADDED", 363)
k = dict(g.MountSheetSyncDB.kills[n + 1].items())
check("NEW_MOUNT_ADDED -> mount record", k["kind"] == "mount" and k["boss"] == "Синий протодракон" and k["mount"] == "Синий протодракон"
      and k["mountID"] == 363 and k["spellID"] == 59996 and k["id"] == "C-Player-1602-0ABCDEF1-363" and k["char"] == "Эйра", k)
check("mount record: source text without colour codes", k["from"] == "Добыча: Скади Безжалостный / Подземелье: Вершина Утгард / Сложность: Героический", k.get("from"))
check("mount record has no weekEnd and does not count as a kill", "weekEnd" not in k and ns.CountThisWeek() == week_count)
check("chat says what will happen", "Новый маунт: Синий протодракон — «Собрано?»" in chat_all().split("\n")[-1])
g.Fire("NEW_MOUNT_ADDED", 363)
check("same mount twice -> one record", len(g.MountSheetSyncDB.kills) == n + 1)
g.Fire("NEW_MOUNT_ADDED", 99999)
g.Fire("NEW_MOUNT_ADDED", g.SECRET)
check("unknown or secret mount id: nothing, no error", len(g.MountSheetSyncDB.kills) == n + 1 and "ошибка" not in chat_all())
tabs[3].MockClick(tabs[3]); g.RunTimers()
check("journal shows the mount in purple", any(t.startswith("|cff8c8c8c") and "|cffc77dffНовый маунт: Синий протодракон|r" in t for t in shown_rows()))
check("mount record does not tick list rows", remaining().left == left_before_mount, (remaining().left, left_before_mount))
g.MountSheetSyncDB.quiet = True
g.Fire("NEW_MOUNT_ADDED", 1471)
check("quiet: recorded without a chat line", g.MountSheetSyncDB.kills[n + 2].kind == "mount" and "Смотритель Зерета" not in chat_all().split("\n")[-1])
g.MountSheetSyncDB.quiet = None
saved_journal = g.C_MountJournal
g.C_MountJournal = None
g.Fire("NEW_MOUNT_ADDED", 363)
check("no mount journal API: ignored quietly", "ошибка" not in chat_all())
g.C_MountJournal = saved_journal

# ---- «не фармлю» from the «Осталось» tab (right click on a row)
raw = L.eval("rawget")
tabs[0].MockClick(tabs[0]); g.RunTimers()


def row_buttons():
    """Rows of the main list that have the right-click menu (in list order)."""
    return [f for f in pool(16) if f.elem.entry is not None]


def row_text(b):
    return str(b.text._text)


rb = row_buttons()
texts = [row_text(b) for b in rb]
check("right-click buttons only on raid/dungeon rows that are left (not headers, world bosses or ticked rows)",
      len(rb) > 10 and all(t.startswith("   ") and "убит" not in t for t in texts)
      and not any("Галлеон" in t or "Налак" in t for t in texts), texts[:5])
left0 = remaining().left
first = None
for i in range(1, len(remaining()["items"]) + 1):
    it = remaining()["items"][i]
    if not it.done and str(it.item.kind) in ("raid", "dungeon"):
        first = it.item
        break
boss0, place0 = str(first.boss), str(first.place)
btn = [b for b in rb if row_text(b).startswith("   " + boss0)][0]
n_kills = len(g.MountSheetSyncDB.kills)
btn.MockClick(btn, "RightButton")
menu = g.MENUS[len(g.MENUS)]
items = [(str(menu["items"][i].kind), str(menu["items"][i].text)) for i in range(1, len(menu["items"]) + 1)]
check("menu: title + «не фармлю эту строку» + «весь …»", items == [("title", boss0),
      ("button", "Не фармлю эту строку на этом персонаже"), ("button", "Не фармлю весь «%s» на этом персонаже" % place0)], items)
L.eval("function(b) b.cb() end")(menu["items"][2])
rec = dict(g.MountSheetSyncDB.kills[n_kills + 1].items())
check("record «plan»: row scope, the row's boss, mount, difficulty, place, list", rec["kind"] == "plan" and rec["scope"] == "row"
      and rec["boss"] == boss0 and rec["list"] == str(first.kind) and rec.get("mount") == (str(first.mount) or None)
      and rec["place"] == place0 and rec["farm"] is False and rec["char"] == "Эйра"
      and rec["id"].startswith("P-Player-1602-0ABCDEF1-") and "weekEnd" not in rec, rec)
r = remaining()
check("the row is gone from «Осталось» right away", r.left == left0 - 1
      and not any(str(r["items"][i].item.boss) == boss0 and str(r["items"][i].item.mount) == str(first.mount)
                  for i in range(1, len(r["items"]) + 1)), r.left)
check("chat explains and says how to undo", "не фармлю на Эйра" in chat_all() and "«Что фармить…»" in chat_all(), chat_all().split("\n")[-1])
g.RunTimers()
check("window refreshed, tab title updated", not any(t.startswith("   " + boss0) for t in shown_rows())
      and tabs[0]._text == "Осталось (%d)" % (left0 - 1), tabs[0]._text)
# whole place
rb = row_buttons()
target_place = None
for i in range(1, len(remaining()["items"]) + 1):
    it = remaining()["items"][i]
    if not it.done and str(it.item.kind) == "dungeon" and str(it.item.place) != place0:
        target_place = str(it.item.place)
        break
dbtn = [b for b in rb if target_place and ("- " + target_place) in row_text(b)][0]
dbtn.MockClick(dbtn, "RightButton")
menu = g.MENUS[len(g.MENUS)]
L.eval("function(b) b.cb() end")(menu["items"][3])
r = remaining()
check("«весь …» hides every row of that dungeon", not any(str(r["items"][i].item.place) == target_place for i in range(1, len(r["items"]) + 1))
      and dict(g.MountSheetSyncDB.kills[len(g.MountSheetSyncDB.kills)].items())["scope"] == "place", target_place)
hidden = g.MountSheetSyncDB.notFarmed["Player-1602-0ABCDEF1"]
check("pending «не фармлю» kept per character until the sheet's list is newer", len(hidden) == 2)
g.MOCK.name = "Элтара"; g.MOCK.guid = "Player-1602-0E1715"
check("another character is not affected", remaining().total == 16)
g.MOCK.name = "Эйра"; g.MOCK.guid = "Player-1602-0ABCDEF1"
saved_generated = g.MountSheetSyncSheet.generated
g.MountSheetSyncSheet.generated = NOW + 3600   # the program sent it and brought a newer list
r = remaining()
check("newer list from the sheet: local hiding dropped (the sheet decides now)", g.MountSheetSyncDB.notFarmed["Player-1602-0ABCDEF1"] is None
      and any(str(r["items"][i].item.boss) == boss0 for i in range(1, len(r["items"]) + 1)))
g.MountSheetSyncSheet.generated = saved_generated
tabs[3].MockClick(tabs[3]); g.RunTimers()
check("journal shows «Не фармлю» in grey", any("|cff9d9d9dНе фармлю: " + boss0 in t for t in shown_rows()), [t for t in shown_rows() if "фармлю" in t])
check("world rows have no «не фармлю» menu", ns.AddPlan(L.table_from({"kind": "world", "boss": "Галлеон"}), "row") is False)

# ---- speed: full journal + list
L.execute("""
local kills = MountSheetSyncDB.kills
for i = #kills + 1, 400 do
  kills[i] = { id = "X" .. i, boss = "Босс номер " .. i, char = "Эйра", charKey = MOCK.guid, kind = (i % 2 == 0) and "raid" or "party",
               difficultyID = 16, weekEnd = kills[1].weekEnd, time = MOCK.now - i }
end
""")
build = L.eval("""function(ns) return function()
  return ns.BuildRemaining(MountSheetSyncSheet, ns.GetPlayer(), ns.WeekEnd(), MountSheetSyncDB.kills) end end""")(ns)
build()   # names prepared once, like after the first refresh in the game
t0 = time.perf_counter()
for _ in range(20):
    build()
ms = (time.perf_counter() - t0) / 20 * 1000
check("list rebuilt with 400 records of this week: %.1f ms" % ms, ms < 10)
t0 = time.perf_counter()
for _ in range(200):
    ns.GetRemaining()
ms = (time.perf_counter() - t0) / 200 * 1000
check("unchanged journal: cached list, %.3f ms" % ms, ms < 0.5)

# 3.0: many characters (the sheet's characters copied up to 20+), 400 records — the left panel and «Все персонажи»
L.execute("""
local chars = MountSheetSyncSheet.chars
local n = #chars
for k = 1, 5 do
  for i = 1, n do
    local c = chars[i]
    chars[#chars + 1] = { name = c.name .. " " .. k, keys = { "copy " .. k .. " " .. i }, items = c.items }
  end
end
""")
check("speed test sheet has 20+ characters", len(g.MountSheetSyncSheet.chars) >= 20, len(g.MountSheetSyncSheet.chars))
rebuild_all = L.eval("""function(ns) return function()
  MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1   -- new list: caches are dropped
  ns.GetSummary()
  ns.GetAttemptsList()
  return ns.GetAllRemaining() end end""")(ns)
rebuild_all()
t0 = time.perf_counter()
for _ in range(10):
    rebuild_all()
ms = (time.perf_counter() - t0) / 10 * 1000
check("all %d characters + summary + attempts rebuilt: %.1f ms" % (len(g.MountSheetSyncSheet.chars), ms), ms < 40)
t0 = time.perf_counter()
for _ in range(200):
    ns.GetSummary()
    ns.GetAllRemaining()
ms = (time.perf_counter() - t0) / 200 * 1000
check("unchanged: cached characters and summary, %.3f ms" % ms, ms < 0.5)
win.Show(win)
tabs[1].MockClick(tabs[1]); g.RunTimers()
check("«Все персонажи» with 20+ characters: names cut to 3 + «и ещё N»", any(" и ещё " in t for t in shown_rows()))
check("left panel lists them all (the ScrollBox scrolls)", len(char_rows()) == len(g.MountSheetSyncSheet.chars), len(char_rows()))

check("no Lua errors reported in chat", "ошибка" not in chat_all())
failed = [nm for nm, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
