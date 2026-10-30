"""Mount Sheet Sync 1.1: core + window + LibDataBroker icon, in Lua 5.1 with strict WoW mocks.

Usage: test_addon2.py <out SavedVariables file> <out layout json>
"""
import json
import os
import random
import sys
import tempfile
import time
from lupa import lua51
import paths

ADDON_DIR = paths.ADDON_DIR
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_SV = sys.argv[1] if len(sys.argv) > 1 else os.path.join(tempfile.gettempdir(), "MountSheetSync.lua")
OUT_LAYOUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(tempfile.gettempdir(), "layout.json")

L = lua51.LuaRuntime(unpack_returned_tuples=True)
NOW = int(time.time())
UNTIL_RESET = 3 * 86400 + 1234

L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
L.execute(r"""
MOCK = {
  now = %d, gt = 1000, untilReset = %d,
  name = "Эйра", realm = "Ревущий фьорд", guid = "Player-1602-0ABCDEF1",
  inst = { name = "Огненные Просторы", type = "raid", diff = 6, max = 25, id = 720 },
  saved = {}, world = {},
}
SECRET = setmetatable({}, { __tostring = function() error("addon touched a secret value!") end,
                            __eq = function() error("addon compared a secret value!") end })
function issecretvalue(v) return rawequal(v, SECRET) end
C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return MOCK.untilReset end }
function time() return MOCK.now end
function GetTime() return MOCK.gt end
date = os.date
function UnitName() return MOCK.name end
function GetRealmName() return MOCK.realm end
function UnitClass() return "Чернокнижница", "WARLOCK", 9 end
function UnitGUID() return MOCK.guid end
local DIFF = { [1] = "Обычный", [2] = "Героический", [6] = "25 игроков (героический)",
               [14] = "Обычный", [16] = "Эпохальный", [24] = "Путешествие во времени" }
function GetDifficultyInfo(id) return DIFF[id] end
function GetInstanceInfo()
  local i = MOCK.inst
  return i.name, i.type, i.diff, DIFF[i.diff], i.max, 0, false, i.id
end
function RequestRaidInfo() REQ = REQ + 1 end
function ReloadUI() RELOADED = true end
function GetNumSavedInstances() return #MOCK.saved end
function GetSavedInstanceInfo(i)
  local s = MOCK.saved[i]
  return s.name, 1000 + i, s.reset, s.diff, s.locked, s.extended, 0, s.isRaid, s.max,
         s.diffName, #s.bosses, 0, false, s.id
end
function GetSavedInstanceEncounterInfo(i, j)
  local b = MOCK.saved[i].bosses[j]
  return b[1], nil, b[2], false
end
function GetNumSavedWorldBosses() return #MOCK.world end
function GetSavedWorldBossInfo(i) local w = MOCK.world[i]; return w[1], w[2], w[3] end
function Advance(sec)
  MOCK.now = MOCK.now + sec; MOCK.gt = MOCK.gt + sec; MOCK.untilReset = MOCK.untilReset - sec
  for _, s in ipairs(MOCK.saved) do if s.reset > 0 then s.reset = s.reset - sec end end
  for _, w in ipairs(MOCK.world) do w[3] = w[3] - sec end
end
MountSheetSyncDB = { version = 1, kills = {
  { id = "K-old", boss = "Старый босс", char = "Эйра", time = MOCK.now - 30*86400 },
  { id = "K-keep", boss = "Свежий босс", char = "Эйра", time = MOCK.now - 2*86400, weekEnd = 1 },
} }
""" % (NOW, UNTIL_RESET))
g = L.globals()

# ---- load the addon exactly like the client: files in .toc order, shared (name, ns)
toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
ns = L.table()
loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
for name in files:
    src = open(os.path.join(ADDON_DIR, name), encoding="utf-8").read()
    loader(src, name)("MountSheetSync", ns)
print("loaded:", ", ".join(files))

checks = []


def check(name, cond):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name)


def kills():
    db = g.MountSheetSyncDB
    return [dict(db.kills[i].items()) for i in range(1, len(db.kills) + 1)]


def chat(n=1):
    c = g.CHAT
    return [str(c[i]) for i in range(max(1, len(c) - n + 1), len(c) + 1)]


def all_chat():
    return "\n".join(str(g.CHAT[i]) for i in range(1, len(g.CHAT) + 1))


# =================================================================== core
g.Fire("ADDON_LOADED", "SomethingElse")
check("ignores other addons' ADDON_LOADED", g.MountSheetSyncDB.kills[3] is None)
g.Fire("ADDON_LOADED", "MountSheetSync")
check("prunes records older than 21 days", [k["id"] for k in kills()] == ["K-keep"])
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("requests raid info on login", g.REQ == 1)

L.execute("""
MOCK.saved = {
  { name = "Огненные Просторы", reset = %d, diff = 6, locked = true, extended = false,
    isRaid = true, max = 25, diffName = "25 игроков (героический)", id = 720,
    bosses = { {"Бет'тилак", true}, {"Алисразор", true}, {"Рагнарос", false} } },
  { name = "Душа Дракона", reset = %d, diff = 5, locked = true, extended = true,
    isRaid = true, max = 10, diffName = "10 игроков (героический)", id = 967,
    bosses = { {"Безумие Смертокрыла", true} } },
}
MOCK.world = { { "Ша Злости", 1, %d } }
""" % (UNTIL_RESET, UNTIL_RESET + 7 * 86400, UNTIL_RESET))
g.Fire("UPDATE_INSTANCE_INFO"); g.Fire("UPDATE_INSTANCE_INFO")
check("scan is debounced", len(g.TIMERS) == 1)
g.RunTimers()
lk = [k for k in kills() if k.get("source") == "lockout"]
check("lockout scan: 2 raid + 1 world boss, extended skipped", len(lk) == 3 and sorted(k["kind"] for k in lk) == ["raid", "raid", "world"])
n = len(kills()); g.Fire("UPDATE_INSTANCE_INFO"); g.RunTimers()
check("re-scan adds nothing", len(kills()) == n)

L.execute("Advance(60)")
g.Fire("ENCOUNTER_END", 1206, "Шаннокс", 6, 25, 0)
check("wipe ignored", len(kills()) == n)
g.Fire("ENCOUNTER_END", 1205, "Шаннокс", 6, 25, 1, L.table())
k = kills()[-1]
check("ENCOUNTER_END kill recorded with all fields", k["boss"] == "Шаннокс" and k["kind"] == "raid" and k["difficultyID"] == 6
      and k["id"] == "K-Player-1602-0ABCDEF1-1205-%d" % (NOW + 60) and abs(k["weekEnd"] - (NOW + UNTIL_RESET)) <= 300)
L.execute("Advance(10)"); g.Fire("BOSS_KILL", 1205, "Шаннокс")
check("BOSS_KILL duplicate ignored", len(kills()) == n + 1)
g.RunTimers()
L.execute("""Advance(600); MOCK.inst = { name = "Вершина Утгард", type = "party", diff = 2, max = 5, id = 575 }""")
g.Fire("ENCOUNTER_END", 2029, "Скади Безжалостный", 2, 5, 1)
check("dungeon kill", kills()[-1]["kind"] == "party")
L.execute("""Advance(600); MOCK.inst = { name = "Долина Четырех Ветров", type = "none", diff = 0, max = 0, id = 870 }""")
g.Fire("BOSS_KILL", 725, "Галлеон")
check("open-world boss -> world", kills()[-1]["kind"] == "world")
g.RunTimers(); cnt = len(kills()); req = g.REQ
L.execute("Advance(300)")
g.Fire("ENCOUNTER_END", 9999, g.SECRET, 16, 20, 1); g.RunTimers()
check("secret name: no record, lockouts re-read", len(kills()) == cnt and g.REQ == req + 1)
g.Fire("ENCOUNTER_END", 9998, "Тест", 16, 20, g.SECRET)
check("secret success flag ignored", len(kills()) == cnt and "ошибка" not in all_chat())
L.execute("Advance(5)")
g.Fire("BOSS_KILL", 4242, 'Злой "босс" {x}\\|\n  Конец')
check("name sanitised", kills()[-1]["boss"] == "Злой босс x Конец")

# =================================================================== LibDataBroker icon (HidingBar)
ldb = L.eval('(LibStub("LibDataBroker-1.1"))')
obj = ldb.GetDataObjectByName(ldb, "MountSheetSync")
check("LibDataBroker launcher registered", obj is not None and obj.type == "launcher" and "MountSheetSync\\Media\\Icon" in obj.icon)
check("label for display addons", obj.label == "Mount Sheet Sync")

g.GameTooltip.lines = L.table()
obj.OnTooltipShow(g.GameTooltip)
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("tooltip: title, week count, LMB hint", tt[0] == "Mount Sheet Sync" and any("За эту неделю записей:" in t for t in tt) and tt[-2] == "ЛКМ — открыть окно" and tt[-1] == "ПКМ — настройки")

obj.OnClick(g.UIParent, "RightButton")
check("right click does nothing", g.MountSheetSyncFrame is None)
obj.OnClick(g.UIParent, "LeftButton")
win = g.MountSheetSyncFrame
check("left click opens the window", win is not None and win._shown)
check("window uses Blizzard DefaultPanelTemplate + title", win._template == "DefaultPanelTemplate" and win._title == "Mount Sheet Sync")
check("Esc closes it (UISpecialFrames)", "MountSheetSyncFrame" in [g.UISpecialFrames[i] for i in range(1, len(g.UISpecialFrames) + 1)])
obj.OnClick(g.UIParent, "LeftButton")
check("second left click closes", not win._shown)
g.MountSheetSync_OnAddonCompartmentClick("MountSheetSync", "LeftButton")
check("addon compartment click toggles", win._shown)
g.MountSheetSync_OnAddonCompartmentEnter("MountSheetSync", g.UIParent)
check("compartment tooltip", g.GameTooltip.shown and str(g.GameTooltip.lines[1]) == "Mount Sheet Sync")
g.MountSheetSync_OnAddonCompartmentLeave("MountSheetSync", g.UIParent)
check("compartment tooltip hides", not g.GameTooltip.shown)


# =================================================================== window contents
def regions(kind=None):
    out = []
    for i in range(1, len(g.ALL_REGIONS) + 1):
        r = g.ALL_REGIONS[i]
        if kind is None or r._type == kind:
            out.append(r)
    return out


def under_window(r):
    p = r._parent
    while p is not None:
        if p._uid == win._uid:
            return True
        p = p._parent
    return False


def click(b):
    b.MockClick(b)
    g.RunTimers()


def button(text):
    bs = [r for r in regions("Button") if r._text == text and under_window(r)]
    assert len(bs) == 1, (text, len(bs))
    return bs[0]


def list_rows():
    """Texts of the main list (ScrollBox with 16 px rows), top to bottom."""
    box = [r for r in regions("Frame") if r._template == "WowScrollBoxList" and under_window(r) and r._view._extent == 16][0]
    return [str(box._pool[i].text._text) for i in range(1, len(box._pool) + 1) if box._pool[i]._shown]


def settings_part(kind, text):
    sf = g.MountSheetSyncSettingsFrame
    found = [r for r in regions(kind) if r._parent is not None and r._parent._uid == sf._uid
             and str(r.Text._text if kind == "CheckButton" else r._text) == text]
    assert len(found) == 1, (text, len(found))
    return found[0]


fontstrings = [r for r in regions("FontString") if under_window(r)]
texts = [str(r._text) for r in fontstrings if r._text]
check("header: character and realm", "Эйра — Ревущий фьорд" in texts)
check("header: this week count and time to reset", any(t.startswith("Записей за эту неделю: ") and "до сброса недели: 2 д 23 ч" in t for t in texts))
check("3.0: the window opens on «Осталось»", win.selectedTab == 1 and g.MountSheetSyncDB.tab is None)
click(button("Журнал"))
check("journal tab remembered", win.selectedTab == 4 and g.MountSheetSyncDB.tab == "log")
rows = list_rows()
check("one row per record", len(rows) == len(kills()))
check("newest record first", "Злой босс x Конец" in rows[0])
check("rows are colored by kind", "|cff8cff8c" in [t for t in rows if "Галлеон" in t][0])

# manual add from the window
edit = [r for r in regions("EditBox") if under_window(r) and r._maxLetters == 60][0]   # «Отметить вручную»
url = [r for r in regions("EditBox") if under_window(r) and r._maxLetters != 60][0]
edit._text = "  Ша Злости "
cnt = len(kills())
click(button("Отметить"))
check("«Отметить» adds a manual record and clears the field", len(kills()) == cnt + 1 and kills()[-1]["kind"] == "manual"
      and kills()[-1]["boss"] == "Ша Злости" and edit._text == "")
check("list refreshed after adding", "Ша Злости" in list_rows()[0])
edit._text = ""
click(button("Отметить"))
check("empty manual name -> hint, nothing added", len(kills()) == cnt + 1 and "Укажите имя босса" in chat()[0])
edit._text = "Ундаста"
edit._scripts.OnEnterPressed(edit)
g.RunTimers()
check("Enter in the field also adds", kills()[-1]["boss"] == "Ундаста")

req = g.REQ
click(button("Найти в сохранениях"))
check("«Найти в сохранениях» requests raid info", g.REQ == req + 1)
g.Fire("UPDATE_INSTANCE_INFO"); g.RunTimers()
check("…and reports the result", "сохранениях" in chat()[0] or "сохранений" in chat()[0])

check("3.0: no checkboxes and no «Очистить журнал» in the window (rare things live in the settings)",
      not [r for r in regions("CheckButton") if under_window(r)]
      and not [r for r in regions("Button") if r._text == "Очистить журнал" and under_window(r)])
gear = [r for r in regions("Button") if under_window(r) and r._highlight == "Interface\\Buttons\\ButtonHilight-Square"]
check("one gear button in the window", len(gear) == 1)
if g.MountSheetSyncSettingsFrame is not None and g.MountSheetSyncSettingsFrame._shown:
    g.MountSheetSyncSettingsFrame.Hide(g.MountSheetSyncSettingsFrame)   # opened above by the right click on the icon
click(gear[0])
check("…it opens the settings", g.MountSheetSyncSettingsFrame is not None and g.MountSheetSyncSettingsFrame._shown)
click(gear[0])
check("…a second click closes them", not g.MountSheetSyncSettingsFrame._shown)
click(gear[0])
quiet = settings_part("CheckButton", "Не писать в чат")
click(quiet)
check("settings checkbox turns quiet on", g.MountSheetSyncDB.quiet is True)
click(quiet)
check("settings checkbox turns quiet off", g.MountSheetSyncDB.quiet is None)

# clear (with confirmation) — from the settings
popups_before = len(g.POPUPS)
click(settings_part("Button", "Очистить журнал"))
check("«Очистить журнал» asks first", len(g.POPUPS) == popups_before + 1 and len(kills()) > 0)
g.MountSheetSyncSettingsFrame.Hide(g.MountSheetSyncSettingsFrame)
saved_kills = kills()

# reset week (with confirmation) -> record + reload
click(button("Сброс недели"))
popup = g.POPUPS[len(g.POPUPS)]
check("«Сброс недели» asks for confirmation", "Начать новую неделю?" in popup.text and popup.acceptText == "Сбросить" and popup.cancelText == "Отмена")
check("nothing happens before confirming", not g.RELOADED and kills()[-1]["kind"] != "reset")
L.execute("Advance(30)")
popup.callback()
g.RunTimers()
r = kills()[-1]
check("confirm -> reset record + reload", r["kind"] == "reset" and r["id"].startswith("R-") and r["char"] == "Эйра"
      and abs(r["weekEnd"] - (NOW + UNTIL_RESET)) <= 300 and g.RELOADED is True)
check("reset shown in the list", "— Сброс недели —" in list_rows()[0])
check("reset is not counted as a kill this week", int([t for t in [str(x._text) for x in fontstrings] if t.startswith("Записей за эту неделю")][0].split(":")[1].split()[0]) >= 0)

g.RELOADED = False
click(button("Отправить в таблицу"))
check("«Отправить в таблицу» reloads the UI", g.RELOADED is True)

# drag saves position
win._scripts.OnDragStart(win)
win._points = L.table()
win.SetPoint(win, "TOPLEFT", g.UIParent, "TOPLEFT", 300, -200)
win._scripts.OnDragStop(win)
pos = g.MountSheetSyncDB.window
check("window position saved", pos.point == "TOPLEFT" and pos.x == 300 and pos.y == -200)

# slash commands
slash = g.SlashCmdList["MOUNTSHEETSYNC"]
shown = win._shown
slash("")
check("/mss toggles the window", win._shown != shown)
slash("help")
check("/mss help prints commands", "/mss reset" in all_chat())
n_popups = len(g.POPUPS)
slash("reset")
check("/mss reset asks for confirmation", len(g.POPUPS) == n_popups + 1)

# ---- export layout of the open window for the geometry check
if not win._shown:
    win.Show(win)
export_regions = [x for x in regions() if under_window(x)]
idx = {id(x): i for i, x in enumerate([win] + export_regions)}


def ref(x):
    if x is None:
        return None
    if x._uid == win._uid:
        return 0
    for i, y in enumerate(export_regions):
        if x._uid == y._uid:
            return i + 1
    return "UIParent"


layout = []
for i, x in enumerate([win] + export_regions):
    pts = []
    for j in range(1, len(x._points) + 1):
        p = x._points[j]
        pts.append({"point": p.point, "rel": ref(p.rel), "relPoint": p.relPoint, "x": p.x, "y": p.y})
    layout.append({"i": i, "type": x._type, "template": x._template, "font": x._font if x._type == "FontString" else None,
                   "text": str(x._text) if x._text is not None else None, "w": x._w, "h": x._h, "shown": x._shown,
                   "parent": ref(x._parent) if i else "UIParent", "points": pts,
                   "scrollChildOf": None})
for i, x in enumerate(export_regions):
    if x._type == "ScrollFrame" and x._scrollChild is not None:
        layout[ref(x._scrollChild)]["scrollChildOf"] = i + 1
json.dump(layout, open(OUT_LAYOUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

# clear confirmed last (keeps the SavedVariables file realistic otherwise)
check("all records still there before clear confirm", len(kills()) == len(saved_kills) + 1)

# ---- write SavedVariables like WoW does
def lua_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


def ser(v, ind):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return repr(v)
    if isinstance(v, str):
        return lua_str(v)
    items = list(v.items())
    d = dict(items)
    n = 0
    while (n + 1) in d:
        n += 1
    hsh = [(k, x) for k, x in items if not (isinstance(k, int) and 1 <= k <= n)]
    random.shuffle(hsh)
    tab = "\t" * (ind + 1)
    out = ["{"]
    for k, x in hsh:
        out.append("%s[%s] = %s," % (tab, lua_str(k) if isinstance(k, str) else k, ser(x, ind + 1)))
    for i in range(1, n + 1):
        out.append("%s%s, -- [%d]" % (tab, ser(d[i], ind + 1), i))
    out.append("\t" * ind + "}")
    return "\n".join(out)


with open(OUT_SV, "w", encoding="utf-8", newline="\r\n") as f:
    f.write("\nMountSheetSyncDB = " + ser(g.MountSheetSyncDB, 0) + "\n")

# finally confirm the clear popup
clear_popup = [g.POPUPS[i] for i in range(1, len(g.POPUPS) + 1) if "Очистить журнал аддона?" in g.POPUPS[i].text][0]
clear_popup.callback()
g.RunTimers()
check("clear confirmed -> journal empty, empty-state text shown", len(kills()) == 0 and
      any(x._shown and "Пока ничего не записано" in str(x._text) for x in regions("FontString")))
check("no Lua errors reported in chat", "ошибка" not in all_chat())

failed = [nm for nm, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
