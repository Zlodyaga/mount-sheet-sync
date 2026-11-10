--[[
Mount Sheet Sync 3.0.5 — входы, закрытые сюжетом или фазой зоны.

ns.ACCESS[journalInstanceID]: что нужно, чтобы дойти до входа.
  quests  — задания, любое из которых открывает доступ (C_QuestLog.IsQuestFlaggedCompleted);
  inlog   — доступ открыт уже тогда, когда такое задание взято (вход в Ни'алоту — с «Surfacing Threats» в журнале);
  ach     — достижения, любое из которых получено этим персонажем (GetAchievementInfo → wasEarnedByMe);
  account — доступ общий на весь аккаунт: раз увидели его у одного персонажа — помним в MountSheetSyncDB.access
            (и спрашиваем игру C_QuestLog.IsQuestFlaggedCompletedOnAccount, где она это умеет);
  steps   — шаги цепочки по порядку: { quest, faction, name (английское, если игра не знает названия), map, x, y,
            where (ключ L.access_where_<…>) }; первый невыполненный — следующий шаг: задание в журнале — цель на него,
            иначе метка там, где его выдают (map, x, y), иначе слова «где»;
  hint    — ключ подсказки в Locales (access_<hint>); без quests/ach — только подсказка, проверки нет;
  after   — пометка, когда доступ уже открыт (access_<after>: вход в Ни'алоту в другой фазе зоны — Зидорми).
Номера и места — с Wowhead (ноябрь 2026): проверить в игре на персонаже без цепочки (docs/TODO.md).
]]
local _, ns = ...

ns.ACCESS = {
  -- Анторус: «Рука судьбы» (Аргус), каждому персонажу
  [946] = { quests = { 47221, 47835, 48506, 48507 }, hint = "argus" },
  -- Операция «Мехагон»: «Мехагонская угроза»
  [1178] = { ach = { 13553, 13700 }, hint = "mechagon" },
  -- Ни'алота: цепочка «Видения Н'Зота» до «Surfacing Threats» (взять его достаточно); потом вход раз в неделю
  -- в Ульдуме или в долине, другую фазу показывает Зидорми
  [1180] = {
    quests = { 56376 }, inlog = true, hint = "nyalotha", after = "nyalotha_phase",
    steps = {
      { quest = 57010, name = "Harnessing the Power", where = "heart" },
      { quest = 58582, faction = "Horde", name = "Return of the Black Prince", where = "wrathion" },
      { quest = 58496, faction = "Alliance", name = "An Unwelcome Advisor", where = "wrathion" },
      { quest = 56376, name = "Surfacing Threats", map = 1527, x = 0.548, y = 0.328 },
    },
  },
  -- Ульдум и долина в фазе нападения Н'Зота: старый вход может быть не виден — Зидорми покажет зону, как была
  [68] = { hint = "uldum_phase" },    -- Вершина Смерча
  [74] = { hint = "uldum_phase" },    -- Трон Четырех Ветров
  [369] = { hint = "vale_phase" },    -- Осада Оргриммара
  -- Тазавеш: цепочка от трактирщицы Та'релы в Орибосе до «The Veiled Market»; точка полёта потом открыта всем персонажам
  [1194] = {
    quests = { 63985 }, account = true, hint = "tazavesh",
    steps = { { quest = 63985, name = "The Veiled Market", where = "tarela" } },
  },
  -- Гробница Предвечных: Древний транслокатор после 4-й главы кампании 9.2 (номера нет — только подсказка)
  [1195] = { hint = "sepulcher" },
  -- Манагорн Омега: без плаща «Reshii Wraps» не пустят; плащ — кампания К'ареша, глава 3, «The Tabiqa»
  [1302] = {
    quests = { 84910 }, hint = "karesh",
    steps = { { quest = 84910, name = "The Tabiqa", map = 2371, x = 0.758, y = 0.342 } },
  },
  -- Амирдрассил: доступ в Изумрудный Сон; «Emerald Welcome» открывает портал в Вальдраккене всем персонажам
  [1207] = {
    quests = { 76318 }, account = true, hint = "dream",
    steps = {
      { quest = 75918, name = "Fire Season", map = 2112, x = 0.502, y = 0.53 },
      { quest = 76317, name = "Call of the Dream", map = 2023, x = 0.186, y = 0.518 },
      { quest = 76318, name = "Emerald Welcome", map = 2023, x = 0.186, y = 0.518 },
    },
  },
}

local function Call(f, ...)
  if not f then
    return nil
  end
  local ok, v = pcall(f, ...)
  return ok and v or nil
end

local function QuestDone(id, account)
  local q = C_QuestLog
  if not q then
    return false
  end
  if Call(q.IsQuestFlaggedCompleted, id) == true then
    return true
  end
  return account and Call(q.IsQuestFlaggedCompletedOnAccount, id) == true or false
end

local function QuestInLog(id)
  local q = C_QuestLog
  return q and (Call(q.IsOnQuest, id) == true or (tonumber(Call(q.GetLogIndexForQuestID, id)) or 0) > 0) or false
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

-- Название задания: у игры (на языке клиента), иначе английское из таблицы.
function ns.QuestTitle(step)
  local title = C_QuestLog and Call(C_QuestLog.GetTitleForQuestID, step.quest)
  if type(title) == "string" and title ~= "" and not (issecretvalue and issecretvalue(title)) then
    return title
  end
  return step.name or tostring(step.quest)
end

-- Первый невыполненный шаг цепочки для фракции этого персонажа; inLog — задание уже взято.
local function NextStep(a)
  local faction = UnitFactionGroup and Call(UnitFactionGroup, "player")
  for _, s in ipairs(a.steps or {}) do
    if not s.faction or s.faction == faction then
      if not QuestDone(s.quest, a.account) then
        return s, QuestInLog(s.quest)
      end
    end
  end
  return nil
end

-- Что известно о доступе к входу: nil — ограничений не знаем; иначе { open, hint, step, stepInLog }:
-- open = false — проверка говорит «не пройдено», nil — проверить нечем; step — следующий шаг цепочки.
function ns.AccessCheck(instance)
  instance = tonumber(instance) or 0
  local a = ns.ACCESS[instance]
  if not a then
    return nil
  end
  local db = ns.GetDB and ns.GetDB()
  if db then
    db.access = type(db.access) == "table" and db.access or {}
  end
  local open
  if a.quests or a.ach then
    open = a.account and db and db.access[instance] == true or false
    for _, id in ipairs(a.quests or {}) do
      open = open or QuestDone(id, a.account) or (a.inlog and QuestInLog(id)) or false
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
  local result = { open = open, hint = text, after = a.after and L["access_" .. a.after] or nil }
  if open == false then
    result.step, result.stepInLog = NextStep(a)
  end
  return result
end

-- При входе в игру: открытый доступ «на весь аккаунт» запоминается сразу, а не при первом Ctrl + щелчке.
function ns.RememberAccess()
  for instance, a in pairs(ns.ACCESS) do
    if a.account then
      pcall(ns.AccessCheck, instance)
    end
  end
end
