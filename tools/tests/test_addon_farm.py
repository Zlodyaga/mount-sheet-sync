"""Mount Sheet Sync 3.0.7: «Фармлю / Не фармлю» from the game for any character of the list — the rows the program
sends as «not farmed» (ch.off), bringing one back before the program has seen it, the right-click menu of the
«Все персонажи» grid, and the rows nobody farms. Stands alone: addon_env.start() and its own fixtures.

    python3 test_addon_farm.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check
L.execute("MSS_NS.UI.Toggle()")
g.RunTimers()

L.execute("""
FARM = {}
local ns = MSS_NS
local function sheetChar(name)
  for _, ch in ipairs(MountSheetSyncSheet.chars) do
    if ch.name == name then return ch end
  end
end
function FARM.has(list, item)
  for _, e in ipairs(list or {}) do
    if (e.item or e) == item then return true end
  end
  return false
end
function FARM.fresh()
  MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1
  return ns.GetAllRemaining()
end
-- the program's list says: the first character does not farm one of its raid rows
local all = ns.GetAllRemaining()
FARM.me, FARM.other = all[1], nil
for i = 2, #all do
  if all[i].who and all[i].who.key ~= "?" then FARM.other = all[i] break end
end
local ch = sheetChar(FARM.me.name)
for i, it in ipairs(ch.items) do
  if it.kind == "raid" and not it.sep and not it.y then
    FARM.item = table.remove(ch.items, i)
    break
  end
end
ch.off = { FARM.item }
FARM.ch = ch
all = FARM.fresh()
FARM.r1 = { listed = FARM.has(all[1].r.items, FARM.item), off = FARM.has(all[1].r.off, FARM.item) }
FARM.kills = #MountSheetSyncDB.kills
""")
F = g.FARM
check("the fixture has a raid row and a second character of this account", F.item is not None and F.other is not None)
check("3.0.7: a row the program sends as «not farmed» is not in the character's list, but is known as off",
      not F.r1.listed and F.r1.off, (F.r1.listed, F.r1.off))

L.execute("""
local ns = MSS_NS
local function rowOf(item)
  for _, row in ipairs(ns.GetSummary()) do
    if row.item == item or (row.item.boss == item.boss and row.item.mount == item.mount and row.item.diff == item.diff) then return row end
  end
end
FARM.rowOf = rowOf
local row = rowOf(FARM.item)
FARM.row1 = row and { off = row.off and row.off[FARM.me.name] or false, offOnly = row.offOnly or false, who = row.who and row.who[FARM.me.name] ~= nil } or nil
""")
check("…the «Все персонажи» row knows who does not farm it and whom the menu can change",
      F.row1 is not None and F.row1.off is True and F.row1.who is True, F.row1 and (F.row1.off, F.row1.who))

# bring it back from the game: a record for the program, and the row is shown at once
L.execute("""
local ns = MSS_NS
FARM.ok = ns.AddPlan(FARM.item, "row", true)
local rec = MountSheetSyncDB.kills[#MountSheetSyncDB.kills]
FARM.rec = { kind = rec.kind, farm = rec.farm, scope = rec.scope, char = rec.char, boss = rec.boss, mount = rec.mount, list = rec.list }
local r = ns.GetAllRemaining()[1].r
FARM.back = { listed = FARM.has(r.items, FARM.item), off = FARM.has(r.off, FARM.item) }
FARM.noPlace = ns.AddPlan(FARM.item, "place", true)
""")
rec = F.rec
check("«farming again»: a record for the program (kind plan, farm = true, this character, the row)",
      F.ok is True and len(g.MountSheetSyncDB.kills) == F.kills + 1 and rec.kind == "plan" and rec.farm is True and rec.scope == "row"
      and rec.char == F.me.who.name and rec.boss == F.item.boss and rec.mount == F.item.mount and rec.list == "raid",
      (F.ok, rec.kind, rec.farm, rec.char))
check("…the row is back in the list at once, before the program has seen the record", F.back.listed and not F.back.off, (F.back.listed, F.back.off))
check("…a whole place cannot be turned on in one go (only a row)", F.noPlace is False)
chat = "\n".join(str(m) for m in env.chat()) if hasattr(env, "chat") else ""

# …then off again before the program came: the two marks do not pile up
L.execute("""
local ns = MSS_NS
FARM.offAgain = ns.AddPlan(FARM.item, "row", false)
local r = ns.GetAllRemaining()[1].r
FARM.again = { listed = FARM.has(r.items, FARM.item), off = FARM.has(r.off, FARM.item),
  pending = MountSheetSyncDB.farmAgain and MountSheetSyncDB.farmAgain[FARM.me.who.key] ~= nil }
ns.AddPlan(FARM.item, "row", true)
-- the program has seen it: the row is in its list again
FARM.ch.off = nil
FARM.ch.items[#FARM.ch.items + 1] = FARM.item
local was = MountSheetSyncSheet.generated
MountSheetSyncSheet.generated = time() + 100   -- made after the marks
local r2 = ns.GetAllRemaining()[1].r
MountSheetSyncSheet.generated = was
FARM.fresh()
FARM.seen = { listed = FARM.has(r2.items, FARM.item), pending = MountSheetSyncDB.farmAgain and MountSheetSyncDB.farmAgain[FARM.me.who.key] ~= nil }
""")
check("off again before the program came: hidden, and no «farming again» mark is left", F.offAgain is True and not F.again.listed and not F.again.pending,
      (F.again.listed, F.again.off, F.again.pending))
check("a newer list from the program: the row comes from it, the local mark is dropped", F.seen.listed and not F.seen.pending, (F.seen.listed, F.seen.pending))

# another character of the account, from this one
L.execute("""
local ns = MSS_NS
local other
for _, ch in ipairs(MountSheetSyncSheet.chars) do
  if ch.name == FARM.other.name then other = ch end
end
FARM.otherItem = nil
for _, it in ipairs(other.items) do
  if it.kind == "raid" and not it.sep and not it.y then FARM.otherItem = it break end
end
local n = #MountSheetSyncDB.kills
FARM.otherOk = FARM.otherItem and ns.AddPlan(FARM.otherItem, "row", false, FARM.other.who) or false
local rec = MountSheetSyncDB.kills[#MountSheetSyncDB.kills]
FARM.otherRec = { n = #MountSheetSyncDB.kills - n, char = rec.char, key = rec.charKey, farm = rec.farm }
local r
for _, c in ipairs(ns.GetAllRemaining()) do
  if c.name == FARM.other.name then r = c.r end
end
FARM.otherNow = { listed = FARM.has(r.items, FARM.otherItem), off = FARM.has(r.off, FARM.otherItem) }
FARM.stranger = ns.AddPlan(FARM.otherItem, "row", false, { name = "Чужой", key = "?" })
""")
check("another character of the account: the record carries that character, not the one logged in",
      F.otherOk is True and F.otherRec.n == 1 and F.otherRec.char == F.other.who.name and F.otherRec.key == F.other.who.key
      and F.otherRec.char != F.me.who.name and F.otherRec.farm is False, (F.otherOk, F.otherRec.char, F.otherRec.key))
check("…its row is hidden for that character at once and known as off", not F.otherNow.listed and F.otherNow.off, (F.otherNow.listed, F.otherNow.off))
check("…a character of another account (no records here) cannot be changed", F.stranger is False)

# the menu of the grid row: a tick per character that has the row
L.execute("""
local ns = MSS_NS
local P = ns.UI.private
local function elemOf(item)
  local view = P.GridView(ns.GetSummary(), ns.GetAllRemaining())
  for _, e in ipairs(view.list) do
    if e.gridItem and e.gridItem.boss == item.boss and e.gridItem.mount == item.mount and e.gridItem.diff == item.diff then return e end
  end
end
FARM.elemOf = elemOf
MountSheetSyncDB.showOff = true   -- the row may be one nobody farms now
local elem = elemOf(FARM.otherItem)
local before = #MENUS
P.ShowGridMenu({ elem = elem })
local menu = MENUS[#MENUS]
FARM.menu = { opened = #MENUS == before + 1, boxes = 0 }
for _, it in ipairs(menu and menu.items or {}) do
  if it.kind == "checkbox" then
    FARM.menu.boxes = FARM.menu.boxes + 1
    if it.text:find(FARM.other.name, 1, true) then FARM.box = it end
  end
end
FARM.menu.was = FARM.box and FARM.box.on()
local n = #MountSheetSyncDB.kills
if FARM.box then FARM.box.cb() end
local rec = MountSheetSyncDB.kills[#MountSheetSyncDB.kills]
FARM.menu.now = FARM.box and FARM.box.on()
FARM.menu.rec = { n = #MountSheetSyncDB.kills - n, farm = rec.farm, char = rec.char }
-- a world boss row has no such menu
local world
for _, e in ipairs(P.GridView(ns.GetSummary(), ns.GetAllRemaining()).list) do
  if e.gridItem and e.gridItem.kind ~= "raid" and e.gridItem.kind ~= "dungeon" then world = e break end
end
before = #MENUS
if world then P.ShowGridMenu({ elem = world }) end
FARM.menu.world = world ~= nil and #MENUS == before
MountSheetSyncDB.showOff = nil
""")
m = F.menu
check("right-click on a grid row: a menu with a tick for every character that has the row", m.opened and m.boxes >= 1 and g.FARM.box is not None, (m.opened, m.boxes))
check("…the character that does not farm it is unticked; a click ticks it and writes «farming again» for that character",
      m.was is False and m.now is True and m.rec.n == 1 and m.rec.farm is True and m.rec.char == F.other.who.name, (m.was, m.now, m.rec.n, m.rec.farm))
check("…rows that are not raids or dungeons have no menu", m.world is True, m.world)

# a row nobody farms: only with the setting, in grey
L.execute("""
local ns = MSS_NS
local item = FARM.item
for _, ch in ipairs(MountSheetSyncSheet.chars) do
  for i = #(ch.items or {}), 1, -1 do
    local it = ch.items[i]
    if it.kind == item.kind and it.boss == item.boss and it.mount == item.mount and it.diff == item.diff then
      ch.off = ch.off or {}
      ch.off[#ch.off + 1] = table.remove(ch.items, i)
    end
  end
end
FARM.fresh()
local row = FARM.rowOf(item)
FARM.nobody = { row = row ~= nil, offOnly = row and row.offOnly or false }
MountSheetSyncDB.showOff = nil
FARM.nobody.hidden = FARM.elemOf(item) == nil
MountSheetSyncDB.showOff = true
local e = FARM.elemOf(item)
FARM.nobody.shown = e ~= nil
FARM.nobody.grey = e and e.values.mount:find("|cff8c8c8c", 1, true) ~= nil or false
MountSheetSyncDB.showOff = nil
""")
n = F.nobody
check("a row nobody farms is kept apart (offOnly) and is not in the grid by default", n.row and n.offOnly and n.hidden, (n.row, n.offOnly, n.hidden))
check("…with «show rows nobody farms» it is in the grid, in grey", n.shown and n.grey, (n.shown, n.grey))

# 3.0.7: the number of attempts can be corrected in the game (the «Попытки» tab, right-click)
L.execute("""
local ns = MSS_NS
EDIT = { oldMounts = MountSheetSyncSheet.mounts }
MountSheetSyncSheet.mounts = { { id = 990363, mount = "Непобедимый", boss = "Король-лич", p = 0.01, n = 10, kind = "raid", sheet = "Рейды" } }
MountSheetSyncSheet.generated = MountSheetSyncSheet.generated + 1
local e = ns.GetAttemptsList()[1]
EDIT.before = e and e.n
EDIT.bad = { ns.SetAttempts(e.raw, -1), ns.SetAttempts(e.raw, "много"), ns.SetAttempts(nil, 5) }
local n = #MountSheetSyncDB.kills
EDIT.ok = ns.SetAttempts(e.raw, 55)
local rec = MountSheetSyncDB.kills[#MountSheetSyncDB.kills]
EDIT.rec = { added = #MountSheetSyncDB.kills - n, kind = rec.kind, n = rec.n, id = rec.mountID, boss = rec.boss, mount = rec.mount, list = rec.list }
EDIT.after = ns.GetAttemptsList()[1].n
-- the little window: the number now, a new one, Enter
ns.UI.EditAttempts(ns.GetAttemptsList()[1])
local box
for i = 1, #ALL_REGIONS do
  local r = ALL_REGIONS[i]
  if r._type == "EditBox" and r._parent == MountSheetSyncAttemptsFrame then box = r end
end
EDIT.window = { shown = MountSheetSyncAttemptsFrame:IsShown(), text = box and box:GetText() }
if box then box:SetText("77") end
ns.UI.private.SaveAttempts()
EDIT.window.n = ns.GetAttemptsList()[1].n
EDIT.window.closed = not MountSheetSyncAttemptsFrame:IsShown()
-- a newer list from the program carries the number: the local mark goes
MountSheetSyncSheet.mounts[1].n = 77
local was = MountSheetSyncSheet.generated
MountSheetSyncSheet.generated = time() + 100
EDIT.seen = ns.GetAttemptsList()[1].n
EDIT.left = next(MountSheetSyncDB.attemptEdits or {}) ~= nil
MountSheetSyncSheet.generated = was
MountSheetSyncSheet.mounts = EDIT.oldMounts
""")
E = g.EDIT
check("3.0.7: attempts corrected in the game: a record for the program (kind attempts, the mount, the boss, the number)",
      E.before == 10 and E.ok is True and E.rec.added == 1 and E.rec.kind == "attempts" and E.rec.n == 55 and E.rec.id == 990363
      and E.rec.boss == "Король-лич" and E.rec.mount == "Непобедимый" and E.rec.list == "raid", (E.before, E.ok, E.rec.kind, E.rec.n))
check("…the tab shows the new number at once; the record itself is not counted as one more attempt", E.after == 55, E.after)
check("…a negative number, a word or no row are refused", [E.bad[1], E.bad[2], E.bad[3]] == [False, False, False])
w = E.window
check("…right-click opens a small window with the number; a new number and Enter save it and close the window",
      w.shown and w.text == "55" and w.n == 77 and w.closed, (w.shown, w.text, w.n, w.closed))
check("…a newer list from the program carries the number, the local mark is dropped", E.seen == 77 and not E.left, (E.seen, E.left))

# 3.0.7: farming tips of the catalog in the row tooltip (Tips.lua), in the addon's language
L.execute("""
local ns = MSS_NS
TIPS = { ru = ns.FarmTip(363), none = ns.FarmTip(99999999), twin = ns.FarmTip(286) == ns.FarmTip(287) and ns.FarmTip(287) ~= nil }
local count = 0
for _ in pairs(ns.FARM_TIPS.ru) do count = count + 1 end
TIPS.count = count
ns.SetLanguage("deDE")
local rare
for id in pairs(ns.FARM_TIPS.de) do rare = id break end
TIPS.de = ns.FarmTip(rare) == ns.FARM_TIPS.de[rare]
TIPS.deFallsBack = ns.FarmTip(363) == ns.FARM_TIPS.en[363]
ns.SetLanguage("ruRU")
MountSheetSyncDB.tips = false
TIPS.off = ns.FarmTip(363)
MountSheetSyncDB.tips = nil
TIPS.bad = false
for _, t in pairs(ns.FARM_TIPS) do
  for _, text in pairs(t) do
    if text:find(string.char(226, 134)) or text:find(string.char(194, 183)) then TIPS.bad = true end
  end
end
""")
T = g.TIPS
check("3.0.7: a farming tip for a mount of the catalog, in Russian for a Russian addon; none for an unknown mount",
      isinstance(T.ru, str) and len(T.ru) > 10 and T.none is None and T.count > 200, (T.ru, T.count))
check("…in the addon's language where there is one (rares in German), in English otherwise", T.de is True and T.deFallsBack is True, (T.de, T.deFallsBack))
check("…the mammoth's twin has the same tip; the setting turns tips off", T.twin is True and T.off is None, (T.twin, T.off))
check("…no arrows or middle dots the game's fonts cannot draw", T.bad is False)

env.finish()
