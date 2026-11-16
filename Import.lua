--[[
Mount Sheet Sync — попытки из Rarity (3.0.7)

Rarity (и его продолжение Rarity: Collector's Edition — тот же глобальный объект Rarity) хранит попытки игрока по
названию предмета, а номера заклинания и предмета маунта — в своей встроенной базе, которую AceDB раскладывает в
Rarity.db.profile.groups. Берём оттуда только число попыток (данные самого игрока); код и база Rarity не копируются.
Маунт находим через C_MountJournal.GetMountFromSpell / GetMountFromItem и переносим число в нашу строку «Попыток»,
если у Rarity больше. Маунт, у которого строк несколько (падает с разных боссов), пропускаем: у Rarity одно число на все
источники, делить его не на чем — такое игрок правит сам на вкладке «Попытки».

/mss rarity — показать, что перенесётся; /mss rarity ok — перенести (как правка числа: ns.SetAttempts).
]]

local _, ns = ...
local L = ns.L

local SHOW = 15   -- строк в предпросмотре, остальное — «…и ещё N»

local function RarityMounts()
  local R = _G.Rarity
  local profile = type(R) == "table" and type(R.db) == "table" and R.db.profile
  local groups = type(profile) == "table" and profile.groups
  return type(groups) == "table" and type(groups.mounts) == "table" and groups.mounts or nil
end

local function MountOf(item)
  if not C_MountJournal then
    return nil
  end
  local id
  if tonumber(item.spellId) and C_MountJournal.GetMountFromSpell then
    id = C_MountJournal.GetMountFromSpell(tonumber(item.spellId))
  end
  if not tonumber(id) and tonumber(item.itemId) and C_MountJournal.GetMountFromItem then
    id = C_MountJournal.GetMountFromItem(tonumber(item.itemId))
  end
  return tonumber(id)
end

--- Что перенести из Rarity: { { entry = строка ns.GetAttemptsList(), from = наше число, to = у Rarity } } и сколько
--- маунтов пропущено (несколько строк). nil — Rarity не включён.
function ns.RarityPlan()
  local items = RarityMounts()
  if not items or not ns.GetAttemptsList then
    return nil
  end
  local byMount = {}
  for _, e in ipairs(ns.GetAttemptsList()) do
    local id = tonumber(e.id)
    if id then
      byMount[id] = byMount[id] or {}
      table.insert(byMount[id], e)
    end
  end
  -- у Rarity на один маунт бывает несколько записей (старая и новая под другим названием) — берём наибольшее число
  local best = {}
  for _, item in pairs(items) do
    local n = type(item) == "table" and tonumber(item.attempts)
    if n and n > 0 then
      local ok, id = pcall(MountOf, item)
      if ok and id and byMount[id] and math.floor(n) > (best[id] or 0) then
        best[id] = math.floor(n)
      end
    end
  end
  local plan, shared = {}, 0
  for id, n in pairs(best) do
    local rows = byMount[id]
    if #rows > 1 then
      shared = shared + 1
    elseif n > (tonumber(rows[1].n) or 0) then
      plan[#plan + 1] = { entry = rows[1], from = tonumber(rows[1].n) or 0, to = n }
    end
  end
  table.sort(plan, function(a, b)
    return tostring(a.entry.name) < tostring(b.entry.name)
  end)
  return plan, shared
end

--- /mss rarity [ok]
function ns.RarityImport(apply)
  local plan, shared = ns.RarityPlan()
  if not plan then
    ns.Print(L.rarity_none)
    return
  end
  if #plan == 0 then
    ns.Print(L.rarity_nothing)
  elseif not apply then
    ns.Print(string.format(L.rarity_plan, #plan))
    for i, p in ipairs(plan) do
      if i > SHOW then
        ns.Print(string.format(L.rarity_more, #plan - SHOW))
        break
      end
      ns.Print(string.format("  %s (%s): %d > %d", tostring(p.entry.name), tostring(p.entry.boss or "?"), p.from, p.to))
    end
  else
    local done = 0
    for _, p in ipairs(plan) do
      if ns.SetAttempts(p.entry.raw, p.to) then
        done = done + 1
      end
    end
    ns.Print(string.format(L.rarity_done, done))
  end
  if shared > 0 then
    ns.Print(string.format(L.rarity_shared, shared))
  end
  if #plan > 0 and not apply then
    ns.Print(L.rarity_confirm)
  end
end
