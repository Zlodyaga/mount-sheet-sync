"""Speed of the addon's lists: 400 journal records of this week, then 20+ characters — the list rebuilt, the caches, the
«Все персонажи» grid and the left panel. Moved out of test_addon_left.py (16.11.2026).

    python3 test_addon_speed.py
"""
import time

from addon_env import start
from window_helpers import Window

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check
w = Window(env)

L.execute("""
local kills = MountSheetSyncDB.kills
local weekEnd = MSS_NS.WeekEnd()
for i = #kills + 1, 400 do
  kills[i] = { id = "X" .. i, boss = "Босс номер " .. i, char = "Эйра", charKey = MOCK.guid, kind = (i % 2 == 0) and "raid" or "party",
               difficultyID = 16, weekEnd = weekEnd, time = MOCK.now - i }
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
w.win.Show(w.win)
w.click_all()
check("«Все персонажи» with 20+ characters: the bar under the columns", w.win.gridSlider._shown and w.win.gridSlider._max == len(g.MountSheetSyncSheet.chars) - 7,
      (w.win.gridSlider._shown, w.win.gridSlider._max))
check("left panel lists them all (the ScrollBox scrolls)", len(w.char_rows()) == len(g.MountSheetSyncSheet.chars) + 1, len(w.char_rows()))
env.finish()
