"""Mount Sheet Sync 3.0.7: rares that share a name (the three Zandalari Warbringers) are told apart by the creature number —
the unit hint and the attempt for a kill belong to the mount of that very creature; a collected one says so.

    python3 test_addon_npc.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

L.execute("""
local names = { "зандаларский завоеватель", "zandalari warbringer" }
local s = MountSheetSyncSheet
s.mounts = s.mounts or {}
s.mounts[#s.mounts + 1] = { kind = "rare", mount = "Нефритовый дикорог", boss = "Зандаларский завоеватель №3", names = names, n = 1, p = 0.05, id = 536, npcs = { 69842 } }
s.mounts[#s.mounts + 1] = { kind = "rare", mount = "Янтарный дикорог", boss = "Зандаларский завоеватель №2", names = names, n = 0, p = 0.05, id = 534, npcs = { 69841 } }
s.had = { { kind = "rare", mount = "Сланцевый дикорог", boss = "Зандаларский завоеватель №1", n = 4, p = 0.05, id = 535, npcs = { 69769 } } }
s.generated = s.generated + 1
MOCK.mounts[534] = { "Янтарный дикорог", 1, "", true }
NPC = {}
local function names_of(list) local o = {} for i, h in ipairs(list) do o[i] = h.name .. (h.have and "+" or "") end return table.concat(o, ";") end
NPC.byName = names_of(MSS_NS.BossHint("Зандаларский завоеватель"))
NPC.jade = names_of(MSS_NS.BossHint("Зандаларский завоеватель", 69842))
NPC.slate = names_of(MSS_NS.BossHint("Зандаларский завоеватель", 69769))
NPC.amber = names_of(MSS_NS.BossHint("Зандаларский завоеватель", 69841))
NPC.line = MSS_NS.BossHintLines("Зандаларский завоеватель", nil, "world", 69769)[1]
NPC.rowAny = MSS_NS.RareRow("Зандаларский завоеватель")
NPC.rowJade = MSS_NS.RareRow("Зандаларский завоеватель", 69842)
NPC.rowSlate = MSS_NS.RareRow("Зандаларский завоеватель", 69769)
NPC.forSlate = MSS_NS.RareRowForNpc(69769)
NPC.forJade = MSS_NS.RareRowForNpc(69842)
""")
n = g.NPC
print(n.byName, "|", n.jade, "|", n.slate, "|", n.amber)
check("without a creature number the hint goes by the name, as before", "Нефритовый" in n.byName and "+" not in n.byName, n.byName)
check("the Warbringer with the jade direhorn: only its own mount", n.jade.startswith("Нефритовый") and ";" not in n.jade and "+" not in n.jade, n.jade)
check("the Warbringer whose mount the sheet has as collected (had): «already collected», not the jade one",
      n.slate.startswith("Сланцевый") and n.slate.endswith("+") and "Нефритовый" not in n.slate, n.slate)
check("…the same when only the game knows it is collected", n.amber.startswith("Янтарный") and n.amber.endswith("+") and "Нефритовый" not in n.amber, n.amber)
check("the hint line says «уже в коллекции»", "уже в коллекции" in str(n.line) or "already collected" in str(n.line), n.line)
check("a kill is an attempt only for the creature of an uncollected mount",
      n.rowAny and n.rowJade and not n.rowSlate and n.forSlate is None and n.forJade == "Зандаларский завоеватель №3",
      (n.rowAny, n.rowJade, n.rowSlate, n.forSlate, n.forJade))
env.finish()
