"""Mount Sheet Sync 3.0.7: /mss rarity — attempts copied from Rarity where it has more (Import.lua).

    python3 test_addon_rarity.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

L.execute("""
local s = MountSheetSyncSheet
s.mounts = {
  { kind = "world", mount = "Небесный ониксовый облачный змей", boss = "Ша Злости", names = { "ша злости" }, n = 3, p = 0.01, id = 473 },
  { kind = "world", mount = "Седло сына Галеона", boss = "Галеон", names = { "галеон" }, n = 40, p = 0.01, id = 515 },
  { kind = "raid", mount = "Пепел Ал'ара", boss = "Кael", names = { "кель" }, n = 2, p = 0.01, id = 183 },
  { kind = "raid", mount = "Пепел Ал'ара", boss = "Другой босс", names = { "другой" }, n = 1, p = 0.01, id = 183 },
}
s.generated = time()
SPELL_TO_MOUNT = { [127158] = 473, [130965] = 515, [40192] = 183 }
ITEM_TO_MOUNT = { [99999] = 473 }
C_MountJournal.GetMountFromSpell = function(id) return SPELL_TO_MOUNT[id] end
C_MountJournal.GetMountFromItem = function(id) return ITEM_TO_MOUNT[id] end
function Edits() local n = 0 for _ in pairs(MountSheetSyncDB.attemptEdits or {}) do n = n + 1 end return n end
function Kills() return #MountSheetSyncDB.kills end
""")

check("no Rarity: nil plan and a message", ns.RarityPlan() is None)
ns.RarityImport(False)
check("…the chat says Rarity is not enabled", "Rarity не включён" in env.chat(), env.chat()[-200:])

L.execute("""
Rarity = { db = { profile = { groups = { mounts = {
  ["Reins of the Heavenly Onyx Cloud Serpent"] = { spellId = 127158, itemId = 87771, attempts = 25 },
  ["Son of Galleon's Saddle"] = { spellId = 130965, itemId = 89783, attempts = 12 },
  ["Ashes of Al'ar"] = { spellId = 40192, attempts = 80 },
  ["Unknown"] = { spellId = 1, attempts = 5 },
  ["Not tried"] = { spellId = 127158 },
  ["Old entry"] = { itemId = 99999, attempts = 7 },
  ["Broken"] = "x",
} } } } }
""")
plan, shared = ns.RarityPlan()
check("one mount where Rarity has more; fewer (Galleon 12 < 40) is left", plan is not None and len(plan) == 1, plan and len(plan))
p = plan[1]
check("…the Onyx Serpent 3 > 25", p.entry.id == 473 and p["from"] == 3 and p.to == 25, (p.entry.id, p["from"], p.to))
check("a mount with two rows (two bosses) is skipped and counted", shared == 1, shared)

n0, e0 = g.Kills(), g.Edits()
ns.RarityImport(False)
out = env.chat()
check("preview lists the mount and the numbers, nothing is written", "3 > 25" in out and "/mss rarity ok" in out and g.Kills() == n0 and g.Edits() == e0, out[-300:])
check("…and says about the skipped one", "пропущено" in out)

check("Rarity is there: the Attempts tab shows its button", ns.RarityAvailable() is True)
ns.RarityHint()
ns.RarityHint()
check("login hint once per account", env.chat().count("Перенести: кнопка «Из Rarity»") == 1 and g.MountSheetSyncDB.rarityHinted is True, env.chat()[-300:])

ns.RarityImport(True)
last = g.MountSheetSyncDB.kills[g.Kills()]
check("ok: one attempts record for the program, like a manual edit", g.Kills() == n0 + 1 and str(last.kind) == "attempts" and last.n == 25 and last.mountID == 473,
      (g.Kills() - n0, last.kind, last.n, last.mountID))
check("…the attempts tab shows the new number at once", any(e.id == 473 and e.n == 25 for e in ns.GetAttemptsList().values()))
check("…the chat reports it", "Перенесено из Rarity: 1" in env.chat())

plan, shared = ns.RarityPlan()
check("a second run: nothing left to copy", len(plan) == 0)
ns.RarityImport(True)
check("…says so and writes nothing", "переносить нечего" in env.chat() and g.Kills() == n0 + 1)

# «Выпавшие»: collected before the addon kept track — where the sheet has 0 attempts, the number at the drop from Rarity
L.execute("""
SPELL_TO_MOUNT[555] = 900; SPELL_TO_MOUNT[556] = 901
Rarity.db.profile.groups.mounts["Found one"] = { spellId = 555, attempts = 60, found = true, finds = { { num = 1, totalAttempts = 48, attempts = 48 } } }
Rarity.db.profile.groups.mounts["Found old"] = { spellId = 556, attempts = 9, found = true, lastAttempts = 9 }
MountSheetSyncSheet.had = {
  { mount = "Маунт с нулём", boss = "Босс А", p = 0.01, n = 0, id = 900 },
  { mount = "Маунт с числом", boss = "Босс Б", p = 0.01, n = 5, id = 901 },
}
""")
rows = {r.id: r for r in ns.GetDrops().values()}
check("earlier drop with 0 in the sheet: attempts from Rarity's find, marked", 900 in rows and rows[900].n == 48 and rows[900].rarity is True,
      900 in rows and (rows[900].n, rows[900].rarity))
check("…where the sheet has a number, it stays", 901 in rows and rows[901].n == 5 and not rows[901].rarity, 901 in rows and rows[901].n)
check("RarityFinds: lastAttempts when there is no finds list", ns.RarityFinds()[901] == 9)
L.execute("MountSheetSyncSheet.had = nil")
L.execute("Rarity = { db = {} }")
check("Rarity without its profile (not loaded yet): nil, no button", ns.RarityPlan() is None and ns.RarityAvailable() is False)
g.SlashCmdList.MOUNTSHEETSYNC("help")
check("…/mss rarity in the help", "/mss rarity" in env.chat())

env.finish()
