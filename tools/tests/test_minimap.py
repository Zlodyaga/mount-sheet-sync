"""Mount Sheet Sync 1.4.1: the minimap button (HidingBar collects minimap buttons, not DataBroker icons by default)."""
import math
import os
import sys
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


def new_game(saved_db=None):
    L = lua51.LuaRuntime(unpack_returned_tuples=True)
    L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
    L.execute(r"""
    function issecretvalue() return false end
    C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return 3 * 86400 end }
    function time() return %d end
    function GetTime() return 1000 end
    date = os.date
    function UnitName() return "Эйра" end
    function GetRealmName() return "Ревущий фьорд" end
    function UnitGUID() return "Player-1602-0ABCDEF1" end
    function GetDifficultyInfo() return nil end
    function GetInstanceInfo() return "Штормград", "none", 0, "", 5, 0, false, 0 end
    function RequestRaidInfo() REQ = REQ + 1 end
    function ReloadUI() RELOADED = true end
    function GetNumSavedInstances() return 0 end
    function GetNumSavedWorldBosses() return 0 end
    """ % NOW)
    g = L.globals()
    if saved_db is not None:
        L.execute(saved_db)
    toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
    files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
    ns = L.table()
    loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
    for name in files:
        loader(open(os.path.join(ADDON_DIR, name), encoding="utf-8").read(), name)("MountSheetSync", ns)
    return L, g, ns


L, g, ns = new_game()
b = g.MountSheetSyncMinimapButton
check("button exists right after the files load (before ADDON_LOADED — HidingBar grabs at its start)", b is not None)
check("child of Minimap, named, 31x31", b._parent._uid == g.Minimap._uid and b._w == 31 and b._h == 31)
# HidingBar: grabMinimapAddonsButtons — square, max side > 16, smaller than half the minimap, has OnClick
pw = g.Minimap._w / 2
check("fits HidingBar's grab rule (square, 16 < size < half of the minimap, OnClick)",
      abs(b._w - b._h) < 5 and max(b._w, b._h) > 16 and b._w < pw and b._scripts.OnClick is not None)
regions = [x for x in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if x._parent is not None and x._parent._uid == b._uid]
textures = {x._texture for x in regions if x._type == "Texture"}
check("standard minimap look: border 136430, background 136467, horse icon",
      136430 in textures and 136467 in textures and "Interface\\Icons\\Ability_Mount_RidingHorse" in textures, textures)
check("icon reachable as .icon (HidingBar/Masque find it)", L.eval("rawget(MountSheetSyncMinimapButton, 'icon') ~= nil"))

g.Fire("ADDON_LOADED", "MountSheetSync")
g.Fire("PLAYER_LOGIN")
g.RunTimers()
p = b._points[1]
ang = math.radians(200)
check("default place on the minimap edge (200°)", p.point == "CENTER" and p.rel._uid == g.Minimap._uid
      and abs(p.x - math.cos(ang) * 75) < 0.01 and abs(p.y - math.sin(ang) * 75) < 0.01, (p.x, p.y))

b.MockClick(b)
check("click opens the window", g.MountSheetSyncFrame is not None and g.MountSheetSyncFrame._shown)
b.MockClick(b)
check("click again closes it", not g.MountSheetSyncFrame._shown)
b.MockClick(b, "RightButton")
check("right click opens the settings, not the window", g.MountSheetSyncSettingsFrame is not None
      and g.MountSheetSyncSettingsFrame._shown and not g.MountSheetSyncFrame._shown)
b.MockClick(b, "RightButton")
check("right click again closes the settings", not g.MountSheetSyncSettingsFrame._shown)

b._scripts.OnEnter(b)
lines = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("tooltip: title, week summary and the drag hint", lines[0] == "Mount Sheet Sync" and any("Перетащите" in t for t in lines)
      and g.GameTooltip.owner._uid == b._uid, lines)
b._scripts.OnLeave(b)

# drag: cursor straight above the minimap centre -> 90°
g.CURSOR[1], g.CURSOR[2] = 1800, 1100
b._scripts.OnDragStart(b)
b._scripts.OnUpdate(b, 0.016)
b._scripts.OnDragStop(b)
p = b._points[1]
check("drag moves it along the edge (cursor above -> 90°) and saves the angle",
      g.MountSheetSyncDB.minimap.angle == 90 and abs(p.x) < 0.01 and abs(p.y - 75) < 0.01, (g.MountSheetSyncDB.minimap.angle, p.x, p.y))
check("OnUpdate removed after the drag", b._scripts.OnUpdate is None)

g.SlashCmdList["MOUNTSHEETSYNC"]("minimap")
check("/mss minimap hides it", not b._shown and g.MountSheetSyncDB.minimap.hide is True and "спрятана" in str(g.CHAT[len(g.CHAT)]))
g.SlashCmdList["MOUNTSHEETSYNC"]("minimap")
check("/mss minimap again shows it", b._shown and g.MountSheetSyncDB.minimap.hide is None and "снова видна" in str(g.CHAT[len(g.CHAT)]))
g.SlashCmdList["MOUNTSHEETSYNC"]("help")
check("/mss help lists /mss minimap", any("/mss minimap" in str(g.CHAT[i]) for i in range(1, len(g.CHAT) + 1)))

# next session: saved angle and hidden state come back
L2, g2, ns2 = new_game("MountSheetSyncDB = { version = 1, kills = {}, minimap = { angle = 0, hide = true } }")
g2.Fire("ADDON_LOADED", "MountSheetSync")
g2.Fire("PLAYER_LOGIN")
b2 = g2.MountSheetSyncMinimapButton
p = b2._points[1]
check("after relog: saved angle used and the button stays hidden", abs(p.x - 75) < 0.01 and abs(p.y) < 0.01 and not b2._shown, (p.x, p.y, b2._shown))

# square minimap (SexyMap etc. define GetMinimapShape)
L3, g3, ns3 = new_game("MountSheetSyncDB = { version = 1, kills = {}, minimap = { angle = 45 } }")
L3.execute('function GetMinimapShape() return "SQUARE" end')
g3.Fire("ADDON_LOADED", "MountSheetSync")
g3.Fire("PLAYER_LOGIN")
p = g3.MountSheetSyncMinimapButton._points[1]
check("square minimap: 45° goes to the corner", abs(p.x - 75) < 0.01 and abs(p.y - 75) < 0.01, (p.x, p.y))

check("no Lua errors reported in chat", not any("ошибка" in str(g.CHAT[i]) for i in range(1, len(g.CHAT) + 1)))
failed = [n for n, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
