"""Mount Sheet Sync 3.0.7: what happens when a mount drops — the notice (Alert.lua), the «Выпавшие» tab, the row of
another faction's mount — and the usage counters. Stands alone: addon_env.start() and its own fixtures.

    python3 test_addon_drops.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check
L.execute("MSS_NS.UI.Toggle()")   # the window is open, as when the player looks at the list
g.RunTimers()

# 3.0.7: a mount of one faction («… [Орда]») is not listed or counted for a character of the other faction
L.execute("""
SIDE = { r = {} }
function SIDE.run(faction)
  for _, c in pairs(MountSheetSyncDB.chars) do c.faction = faction end
  MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1
  local r = MSS_NS.GetAllRemaining()[1].r
  local seen = false
  for _, it in ipairs(r.items) do if it.item.mount == "Проверочный мамонт [Орда]" then seen = true end end
  SIDE.r[faction or "none"] = { total = r.total, seen = seen }
end
local first = MSS_NS.GetAllRemaining()[1].name
for _, ch in ipairs(MountSheetSyncSheet.chars) do
  if ch.name == first then
    ch.items[#ch.items + 1] = { mount = "Проверочный мамонт [Орда]", boss = "Любой босс проверки", sheet = "Рейды", kind = "raid", p = 0.01 }
  end
end
SIDE.run(nil); SIDE.run("Horde"); SIDE.run("Alliance")""")
side = g.SIDE.r
check("3.0.7: a Horde-only mount is listed for a Horde character and when the faction is not known",
      side["Horde"].seen and side["none"].seen and side["Horde"].total == side["none"].total, (side["Horde"].total, side["none"].total))
check("…and neither listed nor counted for an Alliance character",
      not side["Alliance"].seen and side["Alliance"].total == side["Horde"].total - 1, side["Alliance"].total)

# 3.0.7: the mount drop notice — the row is struck out left to right, the count goes down by one, then it goes away
L.execute("""
ALERT = { sounds = {} }
PlaySoundFile = function(file, channel) ALERT.sounds[#ALERT.sounds + 1] = file end
MountSheetSyncDB.alert = false
ALERT.shownWhenOff = MSS_NS.ShowDrop({ name = "Тихий маунт", n = 3, p = 0.01, left = 10 })
ALERT.testShown = MSS_NS.TestDrop()   -- the test ignores the switch
MountSheetSyncDB.alert = nil
ALERT.shown = MSS_NS.ShowDrop({ name = "Непобедимый", n = 147, p = 0.01, left = 212 })
local f = MountSheetSyncAlertFrame
local step = f:GetScript("OnUpdate")
ALERT.before = f.left:GetText()
ALERT.tries = f.tries:GetText()
MOCK.gt = MOCK.gt + 1.4; step(f)
ALERT.midWidth, ALERT.midLeft, ALERT.midLine = f.line:GetWidth(), f.left:GetText(), f.line:IsShown()
MOCK.gt = MOCK.gt + 1.0; step(f)
ALERT.fullWidth, ALERT.after, ALERT.stillShown = f.line:GetWidth(), f.left:GetText(), f:IsShown()
MOCK.gt = MOCK.gt + 10; step(f)
ALERT.gone = not f:IsShown()
MountSheetSyncDB.alertSound = false
MSS_NS.TestDrop()
ALERT.muted = #ALERT.sounds
MountSheetSyncDB.alertSound = nil
MountSheetSyncAlertFrame:Hide()""")
al = g.ALERT
check("3.0.7: the drop notice is not shown when switched off; /mss drop shows it anyway", al.shownWhenOff is False and al.testShown is True and al.shown is True)
check("…it starts with the full count and the attempts", "212" in al.before and "147" in al.tries and "1%" in al.tries, (al.before, al.tries))
check("…the line grows left to right, not at once", al.midLine and 1 < al.midWidth < al.fullWidth and "212" in al.midLeft, (al.midWidth, al.fullWidth))
check("…when the line is done the count is one less", "211" in al.after and al.stillShown, al.after)
check("…and it goes away by itself", al.gone is True)
check("…a sound when it appears and one for the line; none when the sound is off", al.muted == 3 and "Drop.ogg" in al.sounds[1] and "Strike.ogg" in al.sounds[3], list(al.sounds.values()))

# 3.0.7: «Выпавшие» — what dropped, with the attempt and the day; no notice together with Rarity when the player asks so
L.execute("""
DROPS = { oldMounts = MountSheetSyncSheet.mounts, id = 363 }
MountSheetSyncSheet.mounts = { { id = 363, mount = "Непобедимый", boss = "Король-лич", p = 0.01, kind = "raid", sheet = "Рейды" },
  { id = 999001, mount = "Не из шанса", boss = "Торговец", p = 1 } }
MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1
DROPS.before = #MSS_NS.GetDrops()
MountSheetSyncDB.drops = { [tostring(DROPS.id)] = { n = 42, time = 1700000000, char = "Эйра" } }
local rows = MSS_NS.GetDrops()
DROPS.after, DROPS.n, DROPS.char, DROPS.rowId = #rows, rows[1].n, rows[1].char, rows[1].id
local view = MSS_NS.UI.DropsView(rows)
DROPS.cells = view.list[1].values.n .. "|" .. view.list[1].values.when .. "|" .. view.list[1].values.who
DROPS.emptyText = MSS_NS.UI.DropsView({}).empty
MSS_NS.UI.SelectTab(5); MSS_NS.UI.Refresh()
DROPS.tab = MountSheetSyncDB.tab
MSS_NS.UI.SelectTab(1); MSS_NS.UI.Refresh()
MountSheetSyncDB.drops = nil
MountSheetSyncSheet.mounts = DROPS.oldMounts
MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1
C_AddOns = C_AddOns or {}
DROPS.oldLoaded = C_AddOns.IsAddOnLoaded
C_AddOns.IsAddOnLoaded = function(name) return name == "Rarity" end
DROPS.withRarity = MSS_NS.ShowDrop({ name = "Маунт", n = 1, p = 0.01, left = 5 })
MountSheetSyncDB.alertRarity = true
DROPS.rarityOff = MSS_NS.ShowDrop({ name = "Маунт", n = 1, p = 0.01, left = 5 })
DROPS.testAnyway = MSS_NS.TestDrop()
MountSheetSyncDB.alertRarity = nil
C_AddOns.IsAddOnLoaded = DROPS.oldLoaded
MountSheetSyncAlertFrame:Hide()""")
dr = g.DROPS
check("3.0.7: «Выпавшие»: a mount noted when it dropped is listed with its attempt and character",
      dr.after >= 1 and dr.after >= dr.before and dr.n == 42 and dr.char == "Эйра" and dr.rowId == dr.id, (dr.before, dr.after, dr.n))
check("…the row shows the attempt, the day and the character; an empty list says so", dr.cells.startswith("42|") and ".2023|" in dr.cells and dr.cells.endswith("Эйра") and "не выпадали" in dr.emptyText, dr.cells)
check("…the tab opens and is remembered", dr.tab == "drops")
check("3.0.7: with Rarity on, the notice shows by default, not when the player ticked «without Rarity»; the test shows anyway",
      dr.withRarity is True and dr.rarityOff is False and dr.testAnyway is True)


# 3.0.7: usage counters — numbers only, by day, seven days kept
L.execute("""
MountSheetSyncDB.usage = nil
MSS_NS.UI.Toggle(); MSS_NS.UI.Toggle()      -- closed, opened again: one more «open»
MSS_NS.UI.SelectTab(3); MSS_NS.UI.SelectTab(1); MSS_NS.UI.SelectTab(1)
MSS_NS.AddManual("Ундаста")
MSS_NS.Count("route"); MSS_NS.Count(nil); MSS_NS.Count({})
USAGE = MountSheetSyncDB.usage
USAGE_TODAY = USAGE.days[os.date("%Y-%m-%d")]
for i = 1, 9 do USAGE.days["2020-01-0" .. i] = { open = 1 } end
USAGE.days[os.date("%Y-%m-%d")] = nil
MSS_NS.Count("open")
USAGE_KEPT = 0
for _ in pairs(USAGE.days) do USAGE_KEPT = USAGE_KEPT + 1 end""")
today = dict(g.USAGE_TODAY)
check("3.0.7: usage counters: opening the window, tab switches, a manual mark and a route are counted for today",
      today.get("open") == 1 and today.get("tab_left") == 2 and today.get("tab_attempts") == 1 and today.get("manual") == 1 and today.get("route") == 1, today)
check("…only numbers are stored", all(isinstance(v, (int, float)) for v in today.values()))
check("…seven days are kept, Mapzeroth is noted", g.USAGE_KEPT == 7 and g.USAGE.mapzeroth in (True, False), g.USAGE_KEPT)

env.finish()
