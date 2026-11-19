--[[
Mount Sheet Sync — самопроверка в игре: /mss check (3.0.8)

Одна команда вместо ручного прохода по списку проверки: аддон сам смотрит, есть ли в этом клиенте всё, на что опираются
его части (журнал маунтов, сохранения, статистика, праздники, Rarity, чат, карта, подсказки), где игрок сейчас, какие
события игры уже приходили за сеанс (ns.Seen — счётчик и время, у боя — номер, сложность, успех) и что лежит в данных.
Итог — окно с выделенным текстом (то же, что «Сведения для отчёта об ошибке», плюс сам отчёт): Ctrl+C и прислать.

Строки — по-английски (их читает автор), метки: [OK] есть / работает, [--] нет данных или выключено (не ошибка),
[!!] нет того, без чего часть аддона не работает. «Секретные» значения не печатаются — вместо них «secret».
]]

local _, ns = ...

local seen = {}   -- [событие] = { n = сколько раз, t = когда последний, info = подробности боя }

local function Secret(v)
  return issecretvalue and issecretvalue(v)
end

local function V(v)
  if Secret(v) then
    return "secret"
  end
  return tostring(v)
end

--- Из обработчика событий: дёшево (счётчик и время), подробности — только у боёв.
function ns.Seen(event, a, b, c, d, e)
  local s = seen[event]
  if not s then
    s = { n = 0 }
    seen[event] = s
  end
  s.n = s.n + 1
  s.t = time()
  if event == "ENCOUNTER_END" then
    s.info = string.format("id %s, difficulty %s, size %s, success %s", V(a), V(c), V(d), V(e))
  elseif event == "BOSS_KILL" then
    s.info = string.format("id %s", V(a))
  end
end

local function Ago(t)
  if not t then
    return "never"
  end
  local s = time() - t
  if s < 120 then
    return s .. " s ago"
  elseif s < 7200 then
    return math.floor(s / 60) .. " min ago"
  end
  return math.floor(s / 3600) .. " h ago"
end

local EVENTS = { "ENCOUNTER_END", "BOSS_KILL", "LOOT_READY", "CURRENCY_DISPLAY_UPDATE", "NEW_MOUNT_ADDED",
                 "UPDATE_INSTANCE_INFO", "PLAYER_TARGET_CHANGED", "UPDATE_MOUSEOVER_UNIT", "NAME_PLATE_UNIT_ADDED" }

--- Текст проверки (без окна) — для теста и для окна.
function ns.SelfCheck()
  local out = {}
  local function add(mark, name, fmt, ...)
    out[#out + 1] = string.format("[%s] %s: " .. fmt, mark, name, ...)
  end
  local function try(name, fn)
    local ok, err = pcall(fn)
    if not ok then
      add("!!", name, "error %s", tostring(err))
    end
  end
  out[1] = "Mount Sheet Sync self-check (/mss check)"
  local db = ns.GetDB and ns.GetDB() or {}

  try("secret values", function()
    add(issecretvalue and "OK" or "--", "secret values", issecretvalue and "issecretvalue present" or "no issecretvalue (older client)")
  end)

  try("mount journal", function()
    if not (C_MountJournal and C_MountJournal.GetMountIDs and C_MountJournal.GetMountInfoByID) then
      add("!!", "mount journal", "C_MountJournal missing: collection and attempts will not work")
      return
    end
    local ids, have = C_MountJournal.GetMountIDs() or {}, 0
    for _, id in ipairs(ids) do
      local isCollected = select(11, C_MountJournal.GetMountInfoByID(id))
      if not Secret(isCollected) and isCollected then
        have = have + 1
      end
    end
    add(#ids > 0 and "OK" or "!!", "mount journal", "%d mounts in the game, %d collected", #ids, have)
  end)

  try("place", function()
    local name, kind, diff, _, _, _, _, map = GetInstanceInfo()
    local inst = ns.RunsWhere and ns.RunsWhere() or nil
    add("OK", "place", "%s, type %s, difficulty %s, map %s, journal instance %s", V(name), V(kind), V(diff), V(map), tostring(inst or "-"))
  end)

  try("journal", function()
    local j = type(db.journal) == "table" and db.journal or nil
    local ni = j and type(j.instances) == "table" and #j.instances or 0
    local ne = j and type(j.encounters) == "table" and #j.encounters or 0
    add(ni > 0 and "OK" or "!!", "journal", "%d instances, %d encounters (needed for runs and boss tooltips)", ni, ne)
  end)

  try("runs", function()
    local total, places = 0, 0
    for _, list in pairs(type(db.runs) == "table" and db.runs or {}) do
      places = places + 1
      total = total + (type(list) == "table" and #list or 0)
    end
    local open = type(db.runOpen) == "table" and db.runOpen or nil
    local need = 0
    for _ in pairs(open and type(open.need) == "table" and open.need or {}) do
      need = need + 1
    end
    add(total > 0 and "OK" or "--", "runs", "%d stored in %d instances; open run: %s", total, places,
      open and string.format("instance %s, %d of %d mount bosses, started %s%s", tostring(open.inst), open.kills or 0, need, Ago(open.start),
        open.left and ", outside since " .. Ago(open.left) or "") or "none")
  end)

  try("lockouts", function()
    if not GetNumSavedInstances then
      add("!!", "lockouts", "GetNumSavedInstances missing")
      return
    end
    add("OK", "lockouts", "%s saved instances on this character", V(GetNumSavedInstances()))
  end)

  try("statistics", function()
    if not GetStatistic then
      add("!!", "statistics", "GetStatistic missing: attempts from kill statistics will not work")
      return
    end
    local id = ns.STATISTIC_IDS and ns.STATISTIC_IDS[1]
    add("OK", "statistics", "%d statistics known, first (%s) = %s", ns.STATISTIC_IDS and #ns.STATISTIC_IDS or 0, tostring(id), id and V(GetStatistic(id)) or "-")
  end)

  try("holidays", function()
    if not IsLFGDungeonJoinable then
      add("!!", "holidays", "IsLFGDungeonJoinable missing")
      return
    end
    local now, names = ns.HolidayNow and ns.HolidayNow() or {}, {}
    for _, h in ipairs(now) do
      names[#names + 1] = h.name .. (h.done and " (done today)" or "")
    end
    add("OK", "holidays", "%s", #names > 0 and table.concat(names, ", ") or "no holiday with a missing mount now")
  end)

  try("rarity", function()
    if not (ns.RarityAvailable and ns.RarityAvailable()) then
      add("--", "rarity", "Rarity not loaded")
      return
    end
    local plan, shared = ns.RarityPlan()
    add("OK", "rarity", "loaded; %d mounts with more attempts there, %s skipped (several rows)", plan and #plan or 0, tostring(shared or 0))
  end)

  try("chat", function()
    local send = C_ChatInfo and C_ChatInfo.SendChatMessage
    add(send and "OK" or "!!", "chat", "SendChatMessage %s; drop line to group %s, guild %s", send and "present" or "missing",
      db.tellGroup and "on" or "off", db.tellGuild and "on" or "off")
  end)

  try("quests", function()
    local q = C_QuestLog and C_QuestLog.IsQuestFlaggedCompleted
    add(q and "OK" or "!!", "quests", "IsQuestFlaggedCompleted %s (story-locked entrances)", q and "present" or "missing")
  end)

  try("tooltips", function()
    local t = TooltipDataProcessor and TooltipDataProcessor.AddTooltipPostCall
    add(t and "OK" or "!!", "tooltips", "TooltipDataProcessor %s", t and "present" or "missing")
  end)

  try("world map", function()
    if not WorldMapFrame then
      add("!!", "world map", "WorldMapFrame missing")
      return
    end
    local nLines, nDots = 0, 0
    if ns.MapRoutesDrawn then
      nLines, nDots = ns.MapRoutesDrawn()
    end
    add("OK", "world map", "routes drawn now: %s lines, %s spots", tostring(nLines or 0), tostring(nDots or 0))
  end)

  try("mapzeroth", function()
    local loaded = C_AddOns and C_AddOns.IsAddOnLoaded and C_AddOns.IsAddOnLoaded("Mapzeroth")
    add(loaded and "OK" or "--", "mapzeroth", "%s", loaded and "loaded" or "not loaded")
  end)

  try("hide", function()
    local text = ns.HiddenText and ns.HiddenText() or ""
    add("OK", "hide", "%s", text ~= "" and text or "nothing hidden")
  end)

  for _, event in ipairs(EVENTS) do
    local s = seen[event]
    add(s and "OK" or "--", "event " .. event, "%s", s and string.format("%d times, last %s%s", s.n, Ago(s.t), s.info and " (" .. s.info .. ")" or "")
      or "not seen this session")
  end

  if ns.BugReport then
    out[#out + 1] = ""
    local ok, text = pcall(ns.BugReport)
    out[#out + 1] = ok and text or ("bug report: " .. tostring(text))
  end
  return table.concat(out, "\n")
end
