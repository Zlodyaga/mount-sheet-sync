"""Mount Sheet Sync 3.0.7: routes and spawn points of rares on the world map (Map.lua, Routes.lua) — the icon is there
only on a map with an uncollected mount that has data, nothing stays drawn on another map, what was switched on comes
back with the map, several mounts of one map are switched one by one.

    python3 test_addon_map.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check
check("without the world map the layer waits (nothing breaks at login)", ns.InitMapRoutes() is False)
L.execute("""
WorldMapFrame = MockNewRegion("Frame", "WorldMapFrame", UIParent)
WorldMapFrame.ScrollContainer = MockNewRegion("Frame", nil, WorldMapFrame)
CANVAS = MockNewRegion("Frame", nil, WorldMapFrame.ScrollContainer)
CANVAS:SetSize(1000, 600)
MAPID = 120
function WorldMapFrame:GetCanvas() return CANVAS end
function WorldMapFrame:GetMapID() return MAPID end
function WorldMapFrame:AddDataProvider(p) PROVIDER = p end
function WorldMapFrame:GetPinFrameLevelsManager()
  return { GetValidFrameLevel = function(self, kind) LEVEL_ASKED = kind; return 2500 end }
end
MapCanvasDataProviderMixin = {}
function CreateFromMixins(m) local t = {} for k, v in pairs(m) do t[k] = v end return t end
function GoTo(id) MAPID = id; PROVIDER:OnMapChanged() end
function Drawn() local a, b, c, d = MSS_NS.MapRoutesDrawn(); return { lines = a, dots = b, map = c, button = d } end
function Click() local b = MountSheetSyncMapButton; b:GetScript("OnClick")(b) end
""")
check("with the world map the layer is made", ns.InitMapRoutes() is True and g.PROVIDER is not None)
d = g.Drawn()
check("Storm Peaks, the proto-drake is not collected: the icon is there, nothing is drawn until it is clicked",
      d.button and d.lines == 0 and d.dots == 0, (d.button, d.lines, d.dots))
overlay = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._type == "Frame" and r._parent is not None and r._parent._uid == g.CANVAS._uid][0]
check("the layer sits at a map pin level, above the textures of explored land (not just above the canvas)",
      overlay._level == 2500 and str(g.LEVEL_ASKED) == "PIN_FRAME_LEVEL_AREA_POI", (overlay._level, g.LEVEL_ASKED))
g.Click()
d = g.Drawn()
lines = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._type == "Line" and r._shown]
check("one mount on the map: a click draws its four routes as lines and its four spawn points",
      d.lines > 40 and d.dots == 4 and d.map == 120 and g.MountSheetSyncDB.routes[120][265] is True, (d.lines, d.dots, d.map))
check("…a line starts at its spawn point, in the canvas's own units (52, 34 of a 1000 x 600 canvas)",
      abs(lines[0]._start[1] - 520) < 0.01 and abs(lines[0]._start[2] + 204) < 0.01, list(lines[0]._start.values()))
check("…four routes — four colours", len({tuple(round(c, 2) for c in list(l._color.values())[:3]) for l in lines}) == 4)
g.CANVAS._scale = 0.5
g.PROVIDER.OnCanvasScaleChanged(g.PROVIDER)
check("zoomed out to a half: the lines are twice as thick in canvas units, so they look the same", lines[0]._thickness == 6, lines[0]._thickness)
g.GoTo(947)
d = g.Drawn()
check("another map without such a mount: no icon and nothing left drawn", not d.button and d.lines == 0 and d.dots == 0, (d.button, d.lines, d.dots))
g.GoTo(120)
d2 = g.Drawn()
check("back on the map: the same routes are on again, without a click", d2.button and d2.lines > 40 and d2.dots == 4, (d2.lines, d2.dots))

# several mounts on one map (the three Zandalari Warbringers in the Jade Forest): a list, each one on its own
g.GoTo(371)
d = g.Drawn()
check("a map with three mounts: the icon, nothing drawn yet", d.button and d.dots == 0, (d.button, d.dots))
before = len(g.MENUS)
g.Click()
menu = g.MENUS[len(g.MENUS)]
boxes = [menu["items"][i] for i in range(1, len(menu["items"]) + 1) if menu["items"][i]["kind"] == "checkbox"]
check("a click opens a list of the three mounts instead of drawing", len(g.MENUS) == before + 1 and len(boxes) == 3 and g.Drawn().dots == 0, len(boxes))
boxes[0]["cb"]()
check("the first one on: its spawn point", g.Drawn().dots == 1 and boxes[0]["on"]() and not boxes[1]["on"]())
boxes[1]["cb"]()
check("the second one on: the first stays", g.Drawn().dots == 2 and boxes[0]["on"]() and boxes[1]["on"]())
boxes[0]["cb"]()
check("the first one off: only the second is left", g.Drawn().dots == 1 and not boxes[0]["on"]() and boxes[1]["on"]())
g.GoTo(120)
check("Storm Peaks again: its own state, not the Jade Forest's", g.Drawn().dots == 4)
g.GoTo(371)
check("the Jade Forest again: the second mount is still on", g.Drawn().dots == 1)

# the continent map shows what its zones have: the Warbringers' spots, moved to where the zone lies on Pandaria
L.execute("""
C_Map = C_Map or {}
function C_Map.GetMapRectOnMap(child, parent)
  if parent == 424 and child == 371 then return 0.5, 0.9, 0.2, 0.6 end   -- the Jade Forest: the east of the continent
  if parent == 424 and child == 422 then return 0.1, 0.4, 0.6, 0.9 end
  return nil
end""")
g.GoTo(424)
d = g.Drawn()
entries = list(ns.RouteEntries(424).values())
check("Pandaria: the icon is there, and the list has the three mounts of its zones", d.button and len(entries) == 3, len(entries))
ns.SetRouteShown(424, 535, True)
moved = [e for e in entries if e.mount == 535][0]
check("…the slate direhorn: its spots of the two known zones, in the continent's coordinates (52.6, 18.8 of the Jade Forest > 71.04, 27.52)",
      g.Drawn().dots == 2 and abs(moved.spawns[1] - 71.04) < 0.01 and abs(moved.spawns[2] - 27.52) < 0.01, (g.Drawn().dots, moved.spawns[1], moved.spawns[2]))
ns.SetRouteShown(424, 535, False)
g.GoTo(371)
check("…and the Jade Forest keeps its own state", g.Drawn().dots == 1)

# a spawn point says whose it is
dot = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._type == "Frame" and r._shown and r._mouseClick is False][0]
dot._scripts.OnEnter(dot)
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("hover over a spawn point: the mount and the coordinates; clicks go through to the map",
      len(tt) == 2 and "52.6, 18.8" in tt[1], tt)
g.GoTo(1961)
ns.SetRouteShown(1961, 1514, True)
dot = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._type == "Frame" and r._shown and r._mouseClick is False and r.x == 53.0][0]
dot._scripts.OnEnter(dot)
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("…a spot next to a named place: its name from the game's table, in the addon's language (Konthrogz: «Святилище наставлений»)",
      len(tt) == 3 and tt[1] == "Святилище наставлений" and "53.0, 22.3" in tt[2], tt)
ns.SetRouteShown(1961, 1514, False)
g.GoTo(371)

# a rare in this character's list: the spawn point says if it can be killed now or when again
L.execute("""
local s = MountSheetSyncSheet
for _, c in ipairs(s.chars) do
  if c.name == "Эйра" then
    c.items[#c.items + 1] = { kind = "rare", mount = "Огромный сероклык", boss = "Горок", names = { "горок" }, id = 627, every = 86400, sheet = "Редкие монстры", row = 501 }
  end
end
s.mounts = s.mounts or {}
s.mounts[#s.mounts + 1] = { kind = "rare", mount = "Огромный сероклык", boss = "Горок", names = { "горок" }, n = 0, p = 1, id = 627 }
s.generated = s.generated + 1
""")
g.GoTo(525)
ns.SetRouteShown(525, 627, True)
def gorok_tip():
    d = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._type == "Frame" and r._shown and r._mouseClick is False and r.mount == 627][0]
    d._scripts.OnEnter(d)
    return [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
tt = gorok_tip()
check("a rare of this character's list, not killed: «can kill it now» at its spawn point", "Этот персонаж может убить его сейчас" in tt, tt)
L.execute("""
local t = MountSheetSyncDB.kills
t[#t + 1] = { id = "D-x-50992-1", boss = "Горок", kind = "world", difficultyID = 0, char = MOCK.name, realm = MOCK.realm, charKey = MOCK.guid,
              time = MOCK.now - 3600, weekEnd = MSS_NS.WeekEnd() }
MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1
""")
tt = gorok_tip()
check("…killed an hour ago with a day's cooldown: «killed, again in 23 h …»", any(t.startswith("Убит этим персонажем - снова через 23 ч") for t in tt), tt)
ns.SetRouteShown(525, 627, False)

# Darkshore: a rare of one faction is shown to that faction only (the Alliance and the Horde each have their own)
L.execute('function UnitFactionGroup() return MOCK_FACTION end')
g.MOCK_FACTION = "Horde"
moxo = [e for e in ns.RouteEntries(62).values() if e.mount == 1203]
g.MOCK_FACTION = "Alliance"
moxo_a = [e for e in ns.RouteEntries(62).values() if e.mount == 1203]
g.MOCK_FACTION = None
moxo_n = [e for e in ns.RouteEntries(62).values() if e.mount == 1203]
check("Darkshore: the Horde sees Athil Dewfire's spot, the Alliance Moxo's; an unknown faction gets the mount once in the list",
      len(moxo) == 1 and abs(moxo[0].spawns[2] - 75.0) < 0.01 and len(moxo_a) == 1 and abs(moxo_a[0].spawns[2] - 19.6) < 0.01 and len(moxo_n) == 1,
      (len(moxo), len(moxo_a), len(moxo_n)))

# a collected mount has no route and no icon
L.execute('MOCK.mounts[265] = { "Затерянный во времени протодракон", 60002, "" }')
g.GoTo(120)
d = g.Drawn()
check("the proto-drake is collected: no icon on its map and nothing drawn, though the route was on",
      not d.button and d.lines == 0 and d.dots == 0, (d.button, d.lines, d.dots))
env.finish()
