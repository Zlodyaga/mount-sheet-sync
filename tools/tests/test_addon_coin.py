"""Mount Sheet Sync 3.0.7: a bonus roll after a world boss is one more attempt — a second journal record of the same
boss with coin = true, once per kill, only soon after the kill and only for the bonus roll currencies.

    python3 test_addon_coin.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

L.execute("""
function Kills() return #MountSheetSyncDB.kills end
function Last() return MountSheetSyncDB.kills[#MountSheetSyncDB.kills] end
function AddWorld(id, boss)
  local t = MountSheetSyncDB.kills
  t[#t + 1] = { id = id, boss = boss, kind = "world", difficultyID = 0, char = MOCK.name, realm = MOCK.realm,
                charKey = MOCK.guid, time = time(), weekEnd = 123 }
end
""")
n0 = g.Kills()
g.Fire("CURRENCY_DISPLAY_UPDATE", 697, 9, -1)
check("a coin spent with no world boss killed: nothing", g.Kills() == n0)
g.AddWorld("W-1", "Ша Злости")
g.Fire("CURRENCY_DISPLAY_UPDATE", 1166, 5, -1)
check("another currency: nothing", g.Kills() == n0 + 1)
g.Fire("CURRENCY_DISPLAY_UPDATE", 697, 10, 1)
check("the currency grew (a coin was received): nothing", g.Kills() == n0 + 1)
g.Fire("CURRENCY_DISPLAY_UPDATE", 697, 9, -1)
last = g.Last()
check("a coin spent after the Sha of Anger: one more record of the same boss, marked as a bonus roll",
      g.Kills() == n0 + 2 and last.coin is True and str(last.boss) == "Ша Злости" and str(last.kind) == "world"
      and str(last.id).startswith("B-") and last.weekEnd == 123, (g.Kills() - n0, last.coin, last.boss, last.id))
check("…the journal and the chat call it a bonus roll", "бонусный бросок" in str(ns.Describe(last)) and "бонусный бросок" in env.chat(), ns.Describe(last))
g.Advance(5)
g.Fire("CURRENCY_DISPLAY_UPDATE", 697, 8, -1)
check("a second coin for the same kill: nothing", g.Kills() == n0 + 2)
g.AddWorld("D-Эйра-Ревущий фьорд-69769-1", "Зандаларский завоеватель")
g.Fire("CURRENCY_DISPLAY_UPDATE", 752, 3, -1)
check("the last kill is a rare (no bonus roll on rares) and the boss before it has had its coin: nothing", g.Kills() == n0 + 3)
g.Advance(700)
g.AddWorld("W-2", "Галеон")
g.Advance(700)
g.Fire("CURRENCY_DISPLAY_UPDATE", 697, 7, -1)
check("more than ten minutes after the kill: nothing", g.Kills() == n0 + 4)
L.execute('Fire("CURRENCY_DISPLAY_UPDATE", SECRET, SECRET, SECRET)')
check("secret values in the event are left alone", g.Kills() == n0 + 4)
env.finish()
