"""Mount Sheet Sync 3.0.7: how long a run takes, from the player's own runs (Runs.lua) — start on entering, end at the
last mount boss, /reload and a short exit keep the run, long runs and runs the player turned off are not averaged.

    python3 test_addon_runs.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

L.execute("""
local db = MountSheetSyncDB
db.journal = { instances = { { id = 758, name = "Цитадель Ледяной Короны", raid = true, map = 631 },
                             { id = 759, name = "Ульдуар", raid = true, map = 603 } },
               encounters = { { id = 1101, j = 1624, name = "Лорд Ребрад", i = 758 },
                              { id = 1106, j = 1636, name = "Король-лич", i = 758 },
                              { id = 1143, j = 1649, name = "Йогг-Сарон", i = 759 } } }
local s = MountSheetSyncSheet
s.mounts = {
  { kind = "raid", mount = "Непобедимый", boss = "Король-лич", names = { "король-лич" }, n = 10, p = 0.01, id = 9001, je = 1636 },
  { kind = "raid", mount = "Голова Мимирона", boss = "Йогг-Сарон", names = { "йогг-сарон" }, n = 4, p = 0.01, id = 9002, je = 1649 },
}
s.generated = time()
function Enter(map) MOCK.inst = { name = "x", type = "raid", diff = 6, max = 25, id = map }; Fire("PLAYER_ENTERING_WORLD") end
function Leave() MOCK.inst = { name = "Даларан", type = "none", diff = 0, max = 0, id = 1 }; Fire("ZONE_CHANGED_NEW_AREA") end
function Kill(id) Fire("ENCOUNTER_END", id, "Босс", 6, 25, 1) end
""")

def runs(i):
    r = ns.Runs(i)
    return len(r) if r else 0


g.Enter(631)
check("entering ICC (a mount boss there): a run starts", g.MountSheetSyncDB.runOpen is not None and g.MountSheetSyncDB.runOpen.inst == 758)
g.Advance(300)
g.Kill(1101)
check("a boss without a mount: the run goes on, nothing kept", g.MountSheetSyncDB.runOpen is not None and runs(758) == 0)
g.Advance(300)
g.Enter(631)   # /reload inside
check("/reload inside: the same run", g.MountSheetSyncDB.runOpen is not None and g.MountSheetSyncDB.runOpen.kills == 0 and g.MountSheetSyncDB.runOpen.left is None)
g.Advance(300)
g.Kill(1106)
check("the last mount boss killed: the run is kept — 15 min", g.MountSheetSyncDB.runOpen is None and runs(758) == 1 and ns.Runs(758)[1].secs == 900,
      runs(758) and ns.Runs(758) and ns.Runs(758)[1].secs)
avg, n = ns.RunAverage(758)
check("…average 15 min from 1 run, in the row tooltip", avg == 900 and n == 1 and "~15 мин на заход (по 1)" == str(ns.RunTip(758)), (avg, n, ns.RunTip(758)))

# entering and leaving without kills: nothing
g.Enter(631)
g.Advance(120)
g.Leave()
g.Advance(700)
g.Leave()
check("in and out without a mount boss: not a run", runs(758) == 1 and g.MountSheetSyncDB.runOpen is None)

# a short exit and back: the same run; Ulduar's end at Yogg
g.Enter(603)
t0 = g.MountSheetSyncDB.runOpen.start
g.Advance(600)
g.Leave()
g.Advance(300)
g.Enter(603)
check("out and back within 10 min: the same run", g.MountSheetSyncDB.runOpen.start == t0)
g.Advance(600)
g.Kill(1143)
check("Ulduar: 20 min to Yogg-Saron", runs(759) == 1 and ns.Runs(759)[1].secs == 1500, runs(759) and ns.Runs(759)[1].secs)

# a long run (3 h): kept, not averaged until turned back on
g.Enter(631)
g.Advance(3 * 3600)
g.Kill(1106)
check("a 3 h run is kept and marked long", runs(758) == 2 and ns.Runs(758)[2].long is True)
check("…not in the average", ns.RunAverage(758)[0] == 900)
ns.SetRunCounted(758, 2, True)
check("…turned back on by the player: averaged", ns.RunAverage(758)[0] == (900 + 3 * 3600) / 2)
ns.SetRunCounted(758, 1, False)
check("a run turned off: not averaged", ns.RunAverage(758)[0] == 3 * 3600)
ns.DeleteRun(758, 2)
check("delete: gone; the turned-off one alone gives no average", runs(758) == 1 and ns.RunAverage(758) is None and ns.RunTip(758) is None)
ns.SetRunCounted(758, 1, True)

# by difficulty: a row's own difficulties (item.d) average only their runs; a run of unknown difficulty fits any
L.execute('MOCK.inst = { name = "x", type = "raid", diff = 3, max = 10, id = 631 }; Fire("PLAYER_ENTERING_WORLD")')
g.Advance(600)
g.Kill(1106)
check("a 10-player run: kept with its difficulty", runs(758) == 2 and ns.Runs(758)[2].diff == 3, runs(758))
check("…all runs: 12.5 min", ns.RunAverage(758)[0] == 750)
check("…the row's 25 heroic: only its run", ns.RunAverage(758, L.table(6))[0] == 900 and str(ns.RunTip(758, L.table(6))) == "~15 мин на заход (по 1)")
check("…the row's 10 normal: only its run", ns.RunAverage(758, L.table(3, 5))[0] == 600)
check("…no runs of the row's difficulty: nothing", ns.RunAverage(758, L.table(16)) is None and ns.RunTip(758, L.table(16)) is None)
ns.Runs(758)[2].diff = None
check("…unknown difficulty: fits any row", ns.RunAverage(758, L.table(16))[0] == 600)
ns.DeleteRun(758, 2)

# a mount boss kill after leaving: not counted; a run left with a kill keeps the time up to that kill
g.Enter(631)
g.Advance(400)
g.Kill(1101)
g.Leave()
g.Advance(700)
g.Leave()
check("left after a boss without a mount: not a run", runs(758) == 1)

# the Attempts tab: the «Мин.» column and sorting by chance per minute
rows = list(ns.GetAttemptsList().values())
check("two rows in the counter", len(rows) == 2)
check("secret instance info: left alone", (L.execute("MOCK.inst = { name = SECRET, type = SECRET, diff = SECRET, max = 0, id = SECRET }; Fire('PLAYER_ENTERING_WORLD'); return true")) is True)
L.execute("""
MOCK.inst = { name = "x", type = "raid", diff = 6, max = 25, id = 631 }
for i = 1, 25 do Fire("PLAYER_ENTERING_WORLD"); Advance(60); Fire("ENCOUNTER_END", 1106, "Король-лич", 6, 25, 1); Advance(700); MOCK.inst = { name = "Д", type = "none", diff = 0, max = 0, id = 1 }; Fire("ZONE_CHANGED_NEW_AREA"); MOCK.inst = { name = "x", type = "raid", diff = 6, max = 25, id = 631 } end
""")
check("…25 more runs: 20 kept", runs(758) == 20, runs(758))
# the line on entering (3.0.8): the instance's mounts on this difficulty, once per entering
def notes():
    return env.chat().count("Здесь маунт")
L.execute("MountSheetSyncDB.enterSeen = nil")
g.Leave()
before = notes()
g.Enter(631)
last = env.chat().split(chr(10))[-1]
check("entering ICC: «Здесь маунт: …» with the boss, chance and the next attempt", notes() == before + 1
      and "Здесь маунт: Непобедимый (Король-лич, 1%) - попытка 11" in last, last)
check("…Ulduar's mount is not in it", "Голова Мимирона" not in last)
g.Leave()
g.Advance(120)
g.Enter(631)
check("back within 10 min (/reload, exit and back): not again", notes() == before + 1)
g.Leave()
g.Advance(700)
L.execute("MSS_NS.GetAttemptsList()[1].raw.d = { 16 }; MSS_NS.GetAttemptsList()[2].raw.d = { 16 }")
g.Enter(631)
check("a row of another difficulty (mythic only, entered 25 heroic): nothing", notes() == before + 1)
L.execute("MSS_NS.GetAttemptsList()[1].raw.d = nil; MSS_NS.GetAttemptsList()[2].raw.d = nil")
g.Leave()
g.Advance(700)
L.execute("MountSheetSyncDB.enterNote = false")
g.Enter(631)
check("the tick off: nothing", notes() == before + 1)
L.execute("MountSheetSyncDB.enterNote = nil; MountSheetSyncDB.quiet = true")
g.Leave()
g.Advance(700)
g.Enter(631)
check("«no chat messages»: nothing", notes() == before + 1)
L.execute("MountSheetSyncDB.quiet = nil")
g.Leave()
g.Advance(700)
g.Enter(631)
check("…on again: the line", notes() == before + 2)
g.Leave()
env.finish()
