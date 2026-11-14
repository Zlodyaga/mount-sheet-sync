"""The addon inside the WoW imitation (wow_mock.lua), ready for a test: one call instead of a copy of the setup.

    from addon_env import start
    env = start()                    # the list of the fixture workbook (status_left.json), character «Эйра», logged in
    L, g, ns, check = env.L, env.g, env.ns, env.check
    ... check("what must hold", condition, what_to_show_if_not) ...
    env.finish()                     # «N/N checks passed», exit code 1 if something failed

A test of one topic gets its own file and prepares what it needs itself; it must not lean on what another test did
(test_addon_left.py is the old long scenario — new checks do not go there).
"""
import json
import os
import sys
import time
from lupa import lua51
import paths

HERE = os.path.dirname(os.path.abspath(__file__))


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
    """A Python value as Lua source, the way the tray program writes SheetData.lua (SheetData.Build in C#)."""
    if v is None: return "nil"
    if isinstance(v, bool): return "true" if v else "false"
    if isinstance(v, (int, float)): return repr(v)
    if isinstance(v, str): return lua_str(v)
    if isinstance(v, list): return "{" + ", ".join(sheet_lua(x) for x in v) + "}"
    return "{" + ", ".join("[%s] = %s" % (lua_str(k), sheet_lua(x)) for k, x in v.items() if x is not None and k != "ok") + "}"


class Env(object):
    def __init__(self):
        self.checks = []

    def check(self, name, cond, extra=None):
        self.checks.append((name, bool(cond)))
        print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:800]))

    def chat(self):
        return "\n".join(str(self.g.CHAT[i]) for i in range(1, len(self.g.CHAT) + 1))

    def finish(self):
        self.check("no Lua errors reported in chat", "ошибка" not in self.chat())
        failed = [nm for nm, ok in self.checks if not ok]
        print("\n%d/%d checks passed" % (len(self.checks) - len(failed), len(self.checks)))
        sys.exit(1 if failed else 0)


def start(status=None, with_sheet=True):
    """status: the list as the program sends it (default: tools/tests/status_left.json, the fixture workbook's reply)."""
    env = Env()
    if status is None:
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
    [363] = { "Непобедимый", 72286, "|cFFFFD200Добыча:|r Король-лич|n|cFFFFD200Рейд:|r Цитадель Ледяной Короны" },
    [1587] = { "Смотритель Зерета", 368158, "|cFFFFD200Добыча:|r Тюремщик|n|cFFFFD200Рейд:|r Гробница Предвечных" },
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
local DIFF = { [1] = "Обычный", [2] = "Героический", [14] = "Обычный", [15] = "Героический", [16] = "Эпохальный" }
function GetDifficultyInfo(id) return DIFF[id] end
function GetInstanceInfo() local i = MOCK.inst; return i.name, i.type, i.diff, DIFF[i.diff], i.max, 0, false, i.id end
function RequestRaidInfo() end
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
MountSheetSyncDB = { version = 1, kills = {}, chars = {
  ["Эйра-Ревущий фьорд"] = { name = "Эйра", realm = "Ревущий фьорд", classFile = "HUNTER" },
  ["Элтара-Ревущий фьорд"] = { name = "Элтара", realm = "Ревущий фьорд", classFile = "MAGE", guid = "Player-1602-0E1715" },
} }
RAID_CLASS_COLORS = { HUNTER = { colorStr = "ffaad372" }, MAGE = { colorStr = "ff3fc7eb" } }
""" % (int(time.time()), 3 * 86400 + 1234))
    toc = open(os.path.join(paths.ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
    files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
    ns = L.table()
    loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
    for name in files:
        if name == "SheetData.lua" and with_sheet:
            src = sheet_src
        else:
            src = open(os.path.join(paths.ADDON_DIR, name), encoding="utf-8").read()
        loader(src, name)("MountSheetSync", ns)
    L.eval("function(n) n.UsesListWords = function() return false end end")(ns)   # the texts of a player with a Google sheet
    L.execute("MSS_NS = ...", ns)
    g = L.globals()
    g.Fire("ADDON_LOADED", "MountSheetSync")
    g.Fire("PLAYER_LOGIN")
    g.RunTimers()
    env.L, env.g, env.ns, env.files = L, g, ns, files
    return env
