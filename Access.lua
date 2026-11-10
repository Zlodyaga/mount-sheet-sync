--[[
Mount Sheet Sync 3.0.5 — входы, закрытые сюжетом или фазой зоны.

ns.ACCESS[journalInstanceID]: что нужно, чтобы дойти до входа.
  quests  — задания, любое из которых открывает доступ (C_QuestLog.IsQuestFlaggedCompleted);
  ach     — достижения, любое из которых получено этим персонажем (GetAchievementInfo → wasEarnedByMe);
  account — доступ общий на весь аккаунт: раз увидели его у одного персонажа — помним в MountSheetSyncDB.access
            (и спрашиваем игру C_QuestLog.IsQuestFlaggedCompletedOnAccount, где она это умеет);
  hint    — ключ подсказки в Locales (access_<hint>); без quests/ach — только подсказка, проверки нет.
Номера — с Wowhead (ноябрь 2026): проверить в игре на персонаже без цепочки (docs/TODO.md).
]]
local _, ns = ...

ns.ACCESS = {
  [946] = { quests = { 47221, 47835, 48506, 48507 }, hint = "argus" },   -- Анторус: «Рука судьбы» (Аргус), каждому персонажу
  [1178] = { ach = { 13553, 13700 }, hint = "mechagon" },               -- Операция «Мехагон»: «Мехагонская угроза»
  [1180] = { hint = "nyalotha" },                                       -- Ни'алота: вход раз в неделю в Ульдуме или в долине, Зидорми
  [1207] = { hint = "dream" },                                          -- Амирдрассил: доступ в Изумрудный Сон
}

local function QuestDone(id, account)
  local q = C_QuestLog
  if not q then
    return false
  end
  local ok, done = pcall(q.IsQuestFlaggedCompleted, id)
  if ok and done == true then
    return true
  end
  if account and q.IsQuestFlaggedCompletedOnAccount then
    ok, done = pcall(q.IsQuestFlaggedCompletedOnAccount, id)
    return ok and done == true
  end
  return false
end

local function AchievementEarnedHere(id)
  if not GetAchievementInfo then
    return false
  end
  local ok, _, _, _, _, _, _, _, _, _, _, _, _, wasEarnedByMe = pcall(GetAchievementInfo, id)
  return ok and wasEarnedByMe == true
end

-- Название достижения на языке игры (для подсказки): первое из списка, которое игра знает.
local function AchievementName(list)
  for _, id in ipairs(list or {}) do
    local ok, _, name = pcall(GetAchievementInfo or error, id)
    if ok and type(name) == "string" and name ~= "" and not (issecretvalue and issecretvalue(name)) then
      return name
    end
  end
  return nil
end

-- Что известно о доступе к входу: nil — ограничений не знаем;
-- { open = true/false/nil, hint = текст } — open = false: проверка говорит «не пройдено», nil — проверить нечем.
function ns.AccessCheck(instance)
  local a = ns.ACCESS[tonumber(instance) or 0]
  if not a then
    return nil
  end
  local db = ns.GetDB and ns.GetDB()
  if db then
    db.access = type(db.access) == "table" and db.access or {}
  end
  local open
  if a.quests or a.ach then
    open = false
    if a.account and db and db.access[instance] then
      open = true
    end
    for _, id in ipairs(a.quests or {}) do
      open = open or QuestDone(id, a.account)
    end
    for _, id in ipairs(a.ach or {}) do
      open = open or AchievementEarnedHere(id)
    end
    if open and a.account and db then
      db.access[instance] = true   -- общий на аккаунт: другим персонажам проверять больше не нужно
    end
  end
  local L = ns.L or {}
  local text = L["access_" .. a.hint] or ""
  if text:find("%%s") then
    text = string.format(text, AchievementName(a.ach) or "?")
  end
  return { open = open, hint = text }
end

-- При входе в игру: открытый доступ «на весь аккаунт» запоминается сразу, а не при первом Ctrl + щелчке.
function ns.RememberAccess()
  for instance, a in pairs(ns.ACCESS) do
    if a.account then
      pcall(ns.AccessCheck, instance)
    end
  end
end
