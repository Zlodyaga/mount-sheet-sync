"""The probe addon's route recording (/mssprobe route) and routes_from_probe.py: the probe addon runs in the WoW
imitation, the player follows a targeted rare, the run lands in its SavedVariables, the converter turns it into a route.

    python3 test_routes_from_probe.py
"""
import importlib.util, io, os, sys

from lupa import lua51

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:800]))


L = lua51.LuaRuntime(unpack_returned_tuples=True)
L.execute(io.open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
L.execute("""
function time() return 1800000000 end
POS = { map = 120, x = 0.52, y = 0.34 }
TARGET = "Creature-0-1-2-3-32491-0000"
TICKERS = {}
C_Timer.NewTicker = function(sec, fn) local t = { fn = fn, Cancel = function(self) self.off = true end }; TICKERS[#TICKERS + 1] = t; return t end
function Tick() for _, t in ipairs(TICKERS) do if not t.off then t.fn() end end end
function UnitGUID(u) if u == "target" then return TARGET end end
function UnitExists(u) return u == "target" and TARGET ~= nil end
function UnitName(u) if u == "target" then return "Затерянный во времени протодракон" end return "Эйра" end
function GetRealmName() return "Ревущий фьорд" end
C_Map = C_Map or {}
C_Map.GetBestMapForUnit = function() return POS.map end
C_Map.GetPlayerMapPosition = function() return { GetXY = function() return POS.x, POS.y end } end
""")
L.execute(io.open(os.path.join(ROOT, "tools", "probe", "MountSheetSyncProbe", "Probe.lua"), encoding="utf-8").read())
g = L.globals()
slash = g.SlashCmdList["MOUNTSHEETSYNCPROBE"]
L.execute('TARGET = nil')
slash("route")
check("no target: nothing is recorded", g.MountSheetSyncProbeDB is None or g.MountSheetSyncProbeDB.routes is None)
L.execute('TARGET = "Creature-0-1-2-3-32491-0000"')
slash("route")
for x, y in [(0.52, 0.34), (0.5201, 0.3401), (0.50, 0.31), (0.42, 0.31), (0.345, 0.322)]:
    L.execute("POS.x, POS.y = %r, %r; Tick()" % (x, y))
L.execute('TARGET = "Creature-0-1-2-3-99999-0000"; POS.x, POS.y = 0.30, 0.30; Tick()')
L.execute('TARGET = "Creature-0-1-2-3-32491-0000"; POS.map = 125; POS.x, POS.y = 0.10, 0.10; Tick(); POS.map = 120')
slash("route")
runs = g.MountSheetSyncProbeDB.routes["32491"]
pts = list(runs[1].points.values())
check("a run of the targeted rare: its map, the player's points, a step too small and another target or map skipped",
      len(runs) == 1 and runs[1].map == 120 and len(pts) == 8 and pts[:2] == [0.52, 0.34] and pts[-2:] == [0.345, 0.322], (len(pts), pts))
check("…the recording stopped: no more points", (L.execute("Tick()") or True) and len(list(g.MountSheetSyncProbeDB.routes["32491"][1].points.values())) == 8)

spec = importlib.util.spec_from_file_location("rfp", os.path.join(ROOT, "tools", "catalog", "routes_from_probe.py"))
rfp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rfp)
db = rfp.py(g.MountSheetSyncProbeDB)
data = [{"npc": 32491, "map": 120, "name": "Time-Lost Proto-Drake", "source": "x", "routes": []}]
added = rfp.merge(data, db)
route = data[0]["routes"][0] if data[0]["routes"] else {}
check("the converter: one route on the proto-drake's map, in 0-100, from its first point",
      added == [(32491, 120, 4)] and route.get("spawn") == [52.0, 34.0] and route.get("line")[-1] == [34.5, 32.2], (added, route))
check("…the same run again: not added twice", rfp.merge(data, db) == [] and len(data[0]["routes"]) == 1)
data2 = []
rfp.merge(data2, db)
check("…a creature with no entry yet gets one", len(data2) == 1 and data2[0]["npc"] == 32491 and "probe" in data2[0]["source"], data2)

failed = [n for n, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
