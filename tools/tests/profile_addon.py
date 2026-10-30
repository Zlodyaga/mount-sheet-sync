"""Rough timing and garbage of the addon's hot paths in the WoW mock, on a player's real data.

    python3 tools/tests/profile_addon.py <SavedVariables/MountSheetSync.lua> <AddOns/MountSheetSync/SheetData.lua>

Not part of run_tests.sh (numbers, not checks). Lua 5.1 via lupa — the same order of magnitude as the game.
"""
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
src = open(os.path.join(HERE, "test_attempts_addon.py"), encoding="utf-8").read()
head = src[:src.index("\nL, g, ns, files, EJ = new_game()")]
ns_py = {"__name__": "prof", "__file__": os.path.join(HERE, "test_attempts_addon.py")}
exec(compile(head, "harness", "exec"), ns_py)
ns_py["SHEET_SRC"] = open(sys.argv[2], encoding="utf-8").read()
SV = open(sys.argv[1], encoding="utf-8").read()
L, g, ns, files, EJ = ns_py["new_game"]()
# real saved variables (kills, chars, collection, journal) and a mount journal that answers for every ID
L.execute(SV)
g.ns_ = ns
L.execute(r'''
local ns = ns_
local db = ns.GetDB()
for k, v in pairs(MountSheetSyncDB) do db[k] = v end
MJ = {}
for _, m in ipairs(db.collection.mounts) do MJ[m.id] = m end
C_MountJournal = {
  GetMountIDs = function() local t = {} for _, m in ipairs(db.collection.mounts) do t[#t+1] = m.id end return t end,
  GetMountInfoByID = function(id) local m = MJ[id]; if not m then return nil end
    return m.name, 1, 132226, false, true, m.type or 1, false, m.faction ~= nil, m.faction, false, m.got == true, id, false end,
  GetMountInfoExtraByID = function(id) local m = MJ[id]; return 1, "desc", m and m.from and ("|cFFFFD200Добыча:|r " .. m.from) or "", false, 230, 4, 0, 0, false end,
}
function GetStatistic(id) return tostring(id % 37) end
function GetNumSavedInstances() return 20 end
function GetSavedInstanceInfo(i) return "Подземелье " .. i, 1000 + i, 3 * 86400, 16, true, false, 0, true, 20, "Эпохальный", 8, 3, false, 2450 + i end
''')
bench = L.eval(r'''function(name, n, fn, ...)
  collectgarbage("collect"); collectgarbage("stop")
  local m0, t0 = collectgarbage("count"), os.clock()
  for i = 1, n do fn(...) end
  local t1, m1 = os.clock(), collectgarbage("count")
  collectgarbage("restart")
  return string.format("%-44s %8.3f ms/call  %7.1f KB garbage/call", name, (t1 - t0) * 1000 / n, (m1 - m0) / n)
end''')
mem = L.eval('function() collectgarbage("collect"); return collectgarbage("count") end')
print("Lua memory, whole mock + addon + real data: %.0f KB" % mem())
print("from text share of collection.mounts:", L.eval('''(function() local a,b=0,0 for _,m in ipairs(ns_.GetDB().collection.mounts) do a=a+#(m.from or "") b=b+#(m.name or "")+20 end return a.." bytes of from vs "..(a+b).." total" end)()'''))
TIP = L.eval('''function(name) TIP_UNIT[1] = name; GameTooltip.lines = {}; UNIT_POSTCALL(GameTooltip) end''')
print(bench("tooltip: ordinary NPC (no mount)", 2000, TIP, "Стражник Штормграда"))
print(bench("tooltip: boss with 2 mounts (Безумие Смертокрыла)", 2000, TIP, "Безумие Смертокрыла"))
print(bench("tooltip: player name (early exit)", 2000, L.eval('function() TIP_UNIT[1]="Эйра"; TIP_UNIT[2]="player"; UNIT_POSTCALL(GameTooltip); TIP_UNIT[2]="target" end')))
print(bench("RareRow (every loot window)", 2000, ns.RareRow, "Кабан"))
rec = L.eval('''(function() local k = ns_.GetDB().kills; for i = #k, 1, -1 do if k[i].boss then return k[i] end end end)()''')
print(bench("AttemptsFor (each kill)", 300, ns.AttemptsFor, rec))
print(bench("BuildRemaining / GetRemaining", 300, ns.GetRemaining))
print(bench("ScanCollection (5 s after login, 1685 mounts)", 20, ns.ScanCollection))
print(bench("ScanStatistics (login)", 200, ns.ScanStatistics, ns.RememberCharacter()))
print(bench("UPDATE_INSTANCE_INFO, 20 lockouts", 200, g.Fire, "UPDATE_INSTANCE_INFO"))
print(bench("PLAYER_LOGIN handlers", 20, g.Fire, "PLAYER_LOGIN"))
print("Lua memory after scans: %.0f KB" % mem())
