"""Mount Sheet Sync 3.0.7, after the market review: boss skip through a saved lockout (row tooltip), a drop told to the
group / guild (two ticks, off by default), holiday mounts (login line, head of «Осталось»).

    python3 test_addon_extras.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

# ---------------------------------------------------------------- boss skip through a saved lockout
check("ICC: its own steps", "25 героическом" in str(ns.AnchorTip(758)))
check("Ulduar, Nighthold, Dazar'alor, Tazavesh: the general line", all("Пропуск через сохранение" in str(ns.AnchorTip(i)) for i in (759, 786, 1176, 1194)))
check("Dragon Soul (does not work there) and unknown: nothing", ns.AnchorTip(187) is None and ns.AnchorTip(None) is None)
g.MountSheetSyncDB.tips = False
check("tips turned off in the settings: nothing", ns.AnchorTip(758) is None)
g.MountSheetSyncDB.tips = None

# ---------------------------------------------------------------- the drop told to the group / guild
L.execute("""
SENT = {}
C_ChatInfo = C_ChatInfo or {}
C_ChatInfo.SendChatMessage = function(text, channel) SENT[#SENT + 1] = channel .. ": " .. text end
IN_GROUP, IN_RAID, IN_GUILD, IN_INSTANCE = true, false, true, false
LE_PARTY_CATEGORY_HOME, LE_PARTY_CATEGORY_INSTANCE = 1, 2
function IsInGroup(cat) if cat == 2 then return IN_INSTANCE end if cat == 1 then return IN_GROUP and not IN_INSTANCE end return IN_GROUP end
function IsInRaid() return IN_RAID end
function IsInGuild() return IN_GUILD end
function Sent() return table.concat(SENT, "\\n") end
""")
check("off by default: nothing is sent", ns.TellDrop("Пепел Ал'ара", 120) == 0 and g.Sent() == "")
g.MountSheetSyncDB.tellGroup = True
check("group tick: the party gets one line with the attempt", ns.TellDrop("Пепел Ал'ара", 120) == 1 and g.Sent() == "PARTY: [Mount Sheet Sync] Выпал маунт: Пепел Ал'ара - с 120-й попытки!", g.Sent())
L.execute("SENT = {}; IN_RAID = true")
ns.TellDrop("Пепел Ал'ара", 0)
check("…in a raid — raid chat; no attempt count — a shorter line", g.Sent() == "RAID: [Mount Sheet Sync] Выпал маунт: Пепел Ал'ара!", g.Sent())
L.execute("SENT = {}; IN_RAID = false; IN_INSTANCE = true")
ns.TellDrop("X", 3)
check("…a finder group — instance chat", g.Sent().startswith("INSTANCE_CHAT: "), g.Sent())
L.execute("SENT = {}; IN_INSTANCE = false")
g.MountSheetSyncDB.tellGuild = True
check("both ticks: group and guild", ns.TellDrop("X", 3) == 2 and "GUILD: " in g.Sent(), g.Sent())
L.execute("SENT = {}; IN_GROUP = false; IN_GUILD = false")
check("not in a group or guild: nothing", ns.TellDrop("X", 3) == 0 and g.Sent() == "")
g.MountSheetSyncDB.tellGroup = None
g.MountSheetSyncDB.tellGuild = None
L.execute("SENT = {}")
ns.TestDrop()
check("/mss drop (the test) never sends anything", g.Sent() == "")

# ---------------------------------------------------------------- holiday mounts
L.execute("""
OPEN = {}
DONE = {}
function IsLFGDungeonJoinable(id) return OPEN[id] == true end
function GetLFGDungeonInfo(id) return ({ [285] = "Всадник без головы", [287] = "Корен Худовар", [288] = "Химическая компания" })[id] end
function GetLFGDungeonRewards(id) return DONE[id] == true end
local get0 = C_MountJournal.GetMountInfoByID
local HOLI = { [219] = "Скакун Всадника без головы", [2623] = "Упырский скакун Всадника" }
C_MountJournal.GetMountInfoByID = function(id) if HOLI[id] then return HOLI[id], 1, 1, false, true, 0, false, false, nil, false, false end return get0(id) end
""")
check("no holiday: nothing", len(ns.HolidayNow()) == 0 and ns.HolidayHead() == "")
L.execute("OPEN[285] = true")
h = ns.HolidayNow()
check("Hallow's End open: the Horseman with his mounts", len(h) == 1 and str(h[1].name) == "Всадник без головы" and len(h[1].mounts) == 2 and not h[1].done,
      [(str(x.name), len(x.mounts), x.done) for x in h.values()])
check("…the head of «Осталось» names it", "праздник: Всадник без головы" in str(ns.HolidayHead()), ns.HolidayHead())
n0 = len(env.chat())
ns.HolidayNote()
check("…the login line: open today, the mounts", "открыт «Всадник без головы»" in env.chat()[n0:] and "Упырский скакун" in env.chat()[n0:], env.chat()[n0:])
L.execute("DONE[285] = true")
check("done today: not in the head", ns.HolidayHead() == "")
n0 = len(env.chat())
ns.HolidayNote()
check("…the login line says it is done", "уже пройден" in env.chat()[n0:])
g.MountSheetSyncDB.loginNote = False
n0 = len(env.chat())
ns.HolidayNote()
check("login line turned off: silent", env.chat()[n0:].strip() == "")
g.MountSheetSyncDB.loginNote = None
L.execute("""
local get = C_MountJournal.GetMountInfoByID
C_MountJournal.GetMountInfoByID = function(id) if id == 219 or id == 2623 then return "M", 1, 1, false, true, 0, false, false, nil, false, true end return get(id) end
""")
check("all its mounts collected: the holiday is not shown", len(ns.HolidayNow()) == 0)

# ---------------------------------------------------------------- «Скрыть» on «Осталось»: only what is shown
def kinds(r):
    return [str(e.item.kind) for e in r["items"].values()]
r0 = ns.GetRemaining()
check("the fixture character has dungeons and raids", "dungeon" in kinds(r0) and "raid" in kinds(r0), sorted(set(kinds(r0))))
off0 = len(r0.off) if r0.off else 0
ns.ToggleHidden("dungeon")
r1 = ns.GetRemaining()
check("dungeons hidden: none in the list, the count is smaller", "dungeon" not in kinds(r1) and r1.total < r0.total and r1.left <= r0.left, (r0.total, r1.total))
check("…the head says what is hidden", str(ns.HiddenText()) == "Подземелья")
check("…«Фармлю / Не фармлю» is untouched", (len(r1.off) if r1.off else 0) == off0 and g.MountSheetSyncDB.hideKinds.dungeon is True)
ns.ToggleHidden("dungeon")
r2 = ns.GetRemaining()
check("pressed again: everything is back, nothing saved", g.MountSheetSyncDB.hideKinds is None and r2.total == r0.total and ns.HiddenText() is None)

env.finish()
