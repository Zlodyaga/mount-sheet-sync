"""Mount Sheet Sync 2.8.0: the addon says, softly, when the tray program is not running.

The program rewrites Program.lua (MountSheetSyncProgram.seen) every minute while it runs and sets seen = 0 on exit.
Without a fresh mark the window's grey bottom line says the addon works offline (details on hover), the Data Broker
tooltip has one grey line and the one-per-session hint after the first kill says the kill waits for the program.
"""
import os
import time
from lupa import lua51
import paths

ADDON_DIR = paths.ADDON_DIR
HERE = os.path.dirname(os.path.abspath(__file__))
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)))


def new_game(program_src=None, lang=None):
    L = lua51.LuaRuntime(unpack_returned_tuples=True)
    L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
    L.execute(r"""
    function issecretvalue() return false end
    C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return 3 * 86400 end }
    NOW = %d
    function time() return NOW end
    function GetTime() return 1000 end
    date = os.date
    function UnitName() return "Эйра" end
    function GetRealmName() return "Ревущий фьорд" end
    function UnitGUID() return "Player-1602-0ABCDEF1" end
    function GetDifficultyInfo() return "Обычный" end
    function GetInstanceInfo() return "Гробница Предвечных", "raid", 14, "Обычный", 20, 0, false, 2481 end
    function RequestRaidInfo() REQ = REQ + 1 end
    function ReloadUI() RELOADED = true end
    function GetNumSavedInstances() return 0 end
    function GetNumSavedWorldBosses() return 0 end
    """ % NOW)
    if lang:
        L.execute('MountSheetSyncDB = { version = 1, kills = {}, language = "%s" }' % lang)
    g = L.globals()
    toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
    files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
    ns = L.table()
    loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
    for name in files:
        src = open(os.path.join(ADDON_DIR, name), encoding="utf-8").read()
        if name == "Program.lua" and program_src is not None:
            src = program_src
        loader(src, name)("MountSheetSync", ns)
    g.Fire("ADDON_LOADED", "MountSheetSync")
    g.Fire("PLAYER_LOGIN")
    g.RunTimers()
    return L, g, ns, files


def mark(seen, sheet=True):
    # the same text ProgramMark.Text writes (C#)
    return ("-- Mount Sheet Sync: MountSheetSync.exe rewrites this file every minute while it runs,\r\n"
            "MountSheetSyncProgram = { seen = %d, sheet = %s, version = \"2.9.1\" }\r\n" % (seen, "true" if sheet else "false"))


def window_bits(L, g):
    obj = L.eval('(LibStub("LibDataBroker-1.1"))').GetDataObjectByName(L.eval('(LibStub("LibDataBroker-1.1"))'), "MountSheetSync")
    g.GameTooltip.lines = L.table()
    obj.OnTooltipShow(g.GameTooltip)
    broker = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
    obj.OnClick(g.UIParent, "LeftButton")
    win = g.MountSheetSyncFrame
    regs = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
    small = [r for r in regs if r._type == "FontString" and r._font == "GameFontDisableSmall" and r._parent is not None
             and r._parent._uid == win._uid]
    hint = small[0] if small else None
    hovers = [r for r in regs if r._type == "Frame" and r._parent is not None and r._parent._uid == win._uid
              and r._scripts is not None and r._scripts.OnEnter is not None and r._points is not None
              and len(r._points) > 0 and r._points[1].rel is not None and hint is not None and r._points[1].rel._uid == hint._uid]
    tip = None
    if hovers:
        hovers[0]._scripts.OnEnter(hovers[0])
        tip = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
    return broker, (str(hint._text) if hint is not None else None), tip


def first_kill_chat(L, g):
    before = len(g.CHAT)
    g.Fire("ENCOUNTER_END", 2423, "Тюремщик", 14, 20, 1)
    g.RunTimers()
    return [str(g.CHAT[i]) for i in range(before + 1, len(g.CHAT) + 1)]


shipped = open(os.path.join(ADDON_DIR, "Program.lua"), encoding="utf-8").read()
check("shipped Program.lua is empty (the program fills it)", "MountSheetSyncProgram = nil" in shipped)

# ---- no program (the shipped file)
L, g, ns, files = new_game()
check("Program.lua is loaded before the core", files.index("Program.lua") < files.index("MountSheetSync.lua"), files)
check("no mark → offline", ns.ProgramState() == "offline")
chat = first_kill_chat(L, g)
check("the kill is still recorded", len(g.MountSheetSyncDB.kills) == 1)
check("after the first kill: one grey line — remembered, goes to the sheet once the program starts",
      any("не запущена" in c and "запомнено" in c and "|cff8c8c8c" in c for c in chat)
      and not any("Отправить в таблицу" in c for c in chat), chat)
chat2 = first_kill_chat(L, g)
check("…and only once per session", not any("не запущена" in c for c in chat2), chat2)
broker, hint, tip = window_bits(L, g)
check("Data Broker tooltip: one grey offline line, the click hints stay last",
      sum("Офлайн" in t for t in broker) == 1 and broker[-2].startswith("ЛКМ") and broker[-1].startswith("ПКМ"), broker)
check("window bottom line: offline, kills remembered", hint is not None and hint.startswith("Офлайн") and "запоминаются" in hint, hint)
check("hover: why (addons can't go online) and when it is checked (login, /reload)",
      tip is not None and tip[0] == "Работа без программы" and any("/reload" in t and "сохранениях игры" in t for t in tip), tip)

# ---- the program runs (mark written 40 s ago)
L, g, ns, _ = new_game(mark(NOW - 40))
check("fresh mark → online", ns.ProgramState() == "online")
chat = first_kill_chat(L, g)
check("first kill: the usual «goes to the sheet when you log out…» hint", any("В таблицу попадёт" in c for c in chat)
      and not any("не запущена" in c for c in chat), chat)
broker, hint, tip = window_bits(L, g)
check("no offline line in the tooltip", not any("Офлайн" in t for t in broker), broker)
check("window bottom line: the usual hint, no hover", hint is not None and hint.startswith("Отметки попадают") and tip is None, (hint, tip))

# ---- the program runs, but has no sheet yet
L, g, ns, _ = new_game(mark(NOW - 10, sheet=False))
check("running without a sheet → nosheet", ns.ProgramState() == "nosheet")
broker, hint, tip = window_bits(L, g)
check("window: not connected to a sheet yet; hover says how to set it up",
      hint is not None and "не подключена" in hint and tip is not None and any("Настройки…" in t for t in tip), (hint, tip))

# ---- the program was closed (seen = 0) / stopped long ago / the computer clock jumped
for name, src in (("closed (seen = 0)", mark(0)), ("last mark 10 min ago", mark(NOW - 600))):
    L, g, ns, _ = new_game(src)
    check(name + " → offline", ns.ProgramState() == "offline")
L, g, ns, _ = new_game(mark(NOW - 140))
check("a mark 140 s old still counts (the program writes once a minute)", ns.ProgramState() == "online")

# ---- the answer is the one at load time
L, g, ns, _ = new_game(mark(NOW - 30))
L.execute("NOW = NOW + 3600")
check("an hour later in the same session it is still online (checked at login and /reload only)", ns.ProgramState() == "online")

# ---- English
L, g, ns, _ = new_game(lang="enUS")
broker, hint, tip = window_bits(L, g)
check("English: «Offline: the Mount Sheet Sync program isn't running…»", hint is not None and hint.startswith("Offline:")
      and any(t.startswith("Offline:") for t in broker), (hint, broker))

# ---- separate releases: the program wants a newer addon, or the addon a newer program
def prog(seen, version, addon=None):
    return 'MountSheetSyncProgram = { seen = %d, sheet = true, version = "%s"%s }\n' % (seen, version, ', addon = "%s"' % addon if addon else "")


L, g, ns, _ = new_game(prog(NOW - 20, "2.9.4", addon="3.99.0"))
check("program 2.9.4 needs addon ≥ 3.99.0 → oldaddon", ns.ProgramState() == "oldaddon" and not ns.ProgramOffline())
chat = first_kill_chat(L, g)
check("…kills still go to the sheet: the usual hint after the first kill", any("В таблицу попадёт" in c for c in chat), chat)
broker, hint, tip = window_bits(L, g)
check("…window and tooltip: «нужен аддон новее — обновите его в CurseForge», hover explains", hint is not None and "аддон новее" in hint
      and any("аддон новее" in t for t in broker) and tip is not None and tip[0] == "Нужно обновление" and any("/reload" in t for t in tip), (hint, broker, tip))
L, g, ns, _ = new_game(prog(NOW - 20, "2.7.0"))
check("program 2.7.0 (older than the addon needs) → oldprogram", ns.ProgramState() == "oldprogram")
broker, hint, tip = window_bits(L, g)
check("…«Программа … устарела — обновите её»", hint is not None and "устарела" in hint, hint)
L, g, ns, _ = new_game(prog(NOW - 20, "2.9.1", addon="2.8.0"))
check("same versions → online", ns.ProgramState() == "online")
vl = ns.VersionLess
check("version order like the program: 2.9 < 2.10, 2.9.0-alpha1 < alpha2 < beta1 < 2.9.0, 2.9.0 = 2.9",
      vl("2.9.0", "2.10.0") and vl("2.9.0-alpha1", "2.9.0-alpha2") and vl("2.9.0-alpha2", "2.9.0-beta1") and vl("2.9.0-beta1", "2.9.0")
      and not vl("2.9.0", "2.9") and not vl("2.9", "2.9.0") and not vl("2.10.0", "2.9.9"))

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
raise SystemExit(1 if failed else 0)
