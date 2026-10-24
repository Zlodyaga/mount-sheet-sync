--[[
Mount Sheet Sync 2.5.0 — ядро
=============================
Записывает убийства боссов на текущем персонаже и новые маунты, чтобы программа
Mount Sheet Sync (MountSheetSync.exe в трее Windows) отметила их в Google-таблице маунтов:
«Да» в «Пытался на этой неделе?» и «Да» в «Собрано?».

Аддоны WoW не могут выходить в интернет, поэтому записи хранятся в
SavedVariables. Игра сохраняет файл
    WTF\Account\<АККАУНТ>\SavedVariables\MountSheetSync.lua
при выходе из игры, выходе на выбор персонажа или /reload.

Откуда берутся записи:
  * ENCOUNTER_END (успешный бой) и BOSS_KILL (в т. ч. мировые боссы);
  * сохранения подземелий и мировых боссов этой недели — так попадают
    и убийства, сделанные до установки аддона;
  * ручная отметка и «Сброс недели» из окна аддона (UI.lua);
  * NEW_MOUNT_ADDED — в коллекции появился маунт.
Персонажи и коллекция маунтов для новой таблицы — в Collection.lua.

Команды: /mss (окно), /mss help, /mss sync, /mss left, /mss list,
         /mss add <босс>, /mss scan, /mss reset, /mss chars, /mss forget <имя>,
         /mss quiet, /mss minimap, /mss settings, /mss lang <язык>, /mss clear
Тексты — на языке из Locales.lua (ns.L).
]]

local ADDON_NAME, ns = ...

local VERSION = "2.5.0"
local MAX_RECORDS = 400               -- храним не больше стольких записей
local MAX_AGE = 21 * 24 * 60 * 60     -- и не старше трёх недель
local DUPLICATE_WINDOW = 120          -- ENCOUNTER_END и BOSS_KILL об одном бое, сек

local PREFIX = "|cff4fc3f7Mount Sheet Sync:|r "
local CMD = "|cffffd200"

-- В Midnight (12.x) часть данных может прийти «секретным» значением,
-- которое аддону нельзя читать. Такие значения просто пропускаем.
local issecretvalue = issecretvalue or function() return false end

local KIND_BY_INSTANCE_TYPE = {
  raid = "raid",
  party = "party",
  scenario = "scenario",
  none = "world",
}

local L = ns.L
local db                 -- = MountSheetSyncDB
local knownIds = {}
local lastKillAt = {}
local hintShown = false
local errorShown = false
local scanQueued = false
local manualScanPending = false

ns.VERSION = VERSION
-- Сайт проекта: там программа Mount Sheet Sync (список «Осталось» и таблицу ведёт она). Аддон показывает адрес
-- только там, где без программы чего-то нет (пустая вкладка «Осталось», ответ на /mss left), — не в чат сам по себе.
ns.PROGRAM_URL = "https://zlodyaga.github.io/"

local function Print(msg)
  DEFAULT_CHAT_FRAME:AddMessage(PREFIX .. msg)
end
ns.Print = Print

local function SafeCall(fn, ...)
  local ok, err = pcall(fn, ...)
  if not ok and not errorShown then
    errorShown = true
    Print("|cffff5555" .. L.error .. "|r " .. tostring(err))
  end
  return ok
end
ns.SafeCall = SafeCall

-- Сообщить окну, что журнал изменился.
local function Changed()
  if ns.OnChange then
    SafeCall(ns.OnChange)
  end
end

-- Строка без символов, которые помешали бы разбору файла сохранений:
-- управляющих, фигурных скобок, кавычек, обратной косой черты и «|».
-- Классы %s/%c намеренно не используются: они зависят от локали и
-- могут задеть байты кириллицы в UTF-8.
local function Clean(value)
  if value == nil or issecretvalue(value) then
    return nil
  end
  value = tostring(value)
  value = value:gsub("[%z\1-\31\127{}\"\\|]", " ")
  value = value:gsub("  +", " ")
  value = value:match("^ *(.-) *$")
  if value == "" then
    return nil
  end
  return value
end

local function ToNumber(value)
  if value == nil or issecretvalue(value) then
    return nil
  end
  return tonumber(value)
end

local function RoundTime(t)
  return math.floor(t / 600 + 0.5) * 600
end

local function SecondsUntilReset()
  local api = C_DateAndTime and C_DateAndTime.GetSecondsUntilWeeklyReset
  if not api then
    return nil
  end
  local ok, seconds = pcall(api)
  seconds = ok and ToNumber(seconds) or nil
  if not seconds or seconds <= 0 then
    return nil
  end
  return seconds
end
ns.SecondsUntilReset = SecondsUntilReset

-- Перечитать сохранения подземелий. C_Timer.After в 12.x принимает только
-- функции на Lua, поэтому RequestRaidInfo (функцию игры) зовём через эту обёртку.
local function RequestLockouts()
  if RequestRaidInfo then
    RequestRaidInfo()
  end
end

-- Момент ближайшего еженедельного сброса. Им помечаем записи, чтобы
-- программа не перенесла в таблицу убийство с прошлой недели.
local function WeekEnd()
  local seconds = SecondsUntilReset()
  if not seconds then
    return nil
  end
  return RoundTime(time() + seconds)
end
ns.WeekEnd = WeekEnd

local function DifficultyName(difficultyID)
  if not difficultyID or difficultyID == 0 or not GetDifficultyInfo then
    return nil
  end
  local ok, name = pcall(GetDifficultyInfo, difficultyID)
  return ok and Clean(name) or nil
end

local function GetPlayer()
  local name = Clean(UnitName("player"))
  local realm = Clean(GetRealmName())
  local guid = UnitGUID and Clean(UnitGUID("player"))
  return {
    name = name,
    realm = realm,
    key = guid or string.format("%s-%s", name or "?", realm or "?"),
  }
end
ns.GetPlayer = GetPlayer

local function RebuildIndex()
  knownIds = {}
  for _, rec in ipairs(db.kills) do
    knownIds[rec.id] = true
  end
end

local function Prune()
  local cutoff = time() - MAX_AGE
  local kept = {}
  for _, rec in ipairs(db.kills) do
    if type(rec) == "table" and rec.id and (tonumber(rec.time) or 0) >= cutoff then
      kept[#kept + 1] = rec
    end
  end
  local trimmed = {}
  for i = math.max(1, #kept - MAX_RECORDS + 1), #kept do
    trimmed[#trimmed + 1] = kept[i]
  end
  db.kills = trimmed
  RebuildIndex()
end

local function AddRecord(rec)
  if not rec.id or not rec.boss or not rec.char or knownIds[rec.id] then
    return false
  end
  knownIds[rec.id] = true
  db.kills[#db.kills + 1] = rec
  if #db.kills > MAX_RECORDS + 50 then
    Prune()
  end
  Changed()
  return true
end

-- Строка «не фармлю» без приставки: место или «босс — маунт».
local function PlanWhat(rec)
  if rec.scope == "place" then
    return rec.place or "?"
  end
  return (rec.boss or "?") .. (rec.mount and (" — " .. rec.mount) or "")
end

local function Describe(rec)
  if rec.kind == "mount" then
    return string.format(L.desc_mount, rec.mount or rec.boss or "?")
  end
  if rec.kind == "plan" then
    return string.format(L.desc_plan, PlanWhat(rec))
  end
  if rec.kind == "reset" then
    return L.reset_week
  end
  if rec.difficulty then
    return string.format("%s (%s)", rec.boss, rec.difficulty)
  end
  return rec.boss
end
ns.Describe = Describe

local function Announce(rec)
  if db.quiet then
    return
  end
  Print(string.format(L.announce, Describe(rec), rec.char))
  if not hintShown then
    hintShown = true
    Print(string.format(L.announce_hint, CMD .. "/mss|r"))
  end
end

local function BaseRecord(player, stamp)
  return {
    char = player.name,
    realm = player.realm,
    charKey = player.key,
    time = stamp,
  }
end

---------------------------------------------------------------------------
-- Убийство в бою (ENCOUNTER_END / BOSS_KILL)
---------------------------------------------------------------------------
local function OnBossKilled(source, encounterID, encounterName, difficultyID, groupSize)
  encounterID = ToNumber(encounterID)
  local boss = Clean(encounterName)

  -- ENCOUNTER_END и BOSS_KILL приходят об одном и том же бое.
  local dedupeKey = (encounterID and encounterID > 0) and ("id" .. encounterID)
    or ("name" .. (boss or "?"))
  local now = GetTime()
  if lastKillAt[dedupeKey] and now - lastKillAt[dedupeKey] < DUPLICATE_WINDOW then
    return
  end
  for key, at in pairs(lastKillAt) do   -- храним только последние 2 минуты
    if now - at >= DUPLICATE_WINDOW then
      lastKillAt[key] = nil
    end
  end
  lastKillAt[dedupeKey] = now

  -- Через несколько секунд перечитаем сохранения подземелий: так босс
  -- попадёт в журнал, даже если имя в событии оказалось скрытым.
  if C_Timer then
    C_Timer.After(5, RequestLockouts)
  end

  local player = GetPlayer()
  if not boss or not player.name then
    return
  end

  local instanceName, instanceType, instanceDifficulty = GetInstanceInfo()
  if issecretvalue(instanceType) then
    instanceType = nil
  end
  difficultyID = ToNumber(difficultyID)
  if not difficultyID or difficultyID == 0 then
    difficultyID = ToNumber(instanceDifficulty)
  end

  local stamp = time()
  local rec = BaseRecord(player, stamp)
  rec.id = string.format("K-%s-%d-%d", player.key, encounterID or 0, stamp)
  rec.source = source
  rec.boss = boss
  rec.encounterID = encounterID
  rec.instance = Clean(instanceName)
  rec.kind = KIND_BY_INSTANCE_TYPE[instanceType or "none"] or "world"
  rec.difficultyID = difficultyID
  rec.difficulty = DifficultyName(difficultyID)
  rec.weekEnd = WeekEnd()

  if AddRecord(rec) then
    Announce(rec)
  end
end

---------------------------------------------------------------------------
-- Сохранения подземелий и мировых боссов этой недели
---------------------------------------------------------------------------
local function WeekKey(kind, difficultyID, boss)
  return string.format("%s|%d|%s", kind or "", difficultyID or 0, boss or "")
end

-- Что уже записано этому персонажу за неделю: один проход по журналу на скан,
-- дальше проверка за O(1) (раньше — поиск по всему журналу на каждого босса).
local function WeekIndex(player, weekEnd)
  local index = {}
  for _, rec in ipairs(db.kills) do
    if rec.charKey == player.key and rec.weekEnd == weekEnd then
      index[WeekKey(rec.kind, rec.difficultyID, rec.boss)] = true
    end
  end
  return index
end

local function ScanLockouts()
  local player = GetPlayer()
  if not player.name then
    return 0
  end
  local weekEnd = WeekEnd()
  local stamp = time()
  local added = 0
  local seen = WeekIndex(player, weekEnd)

  local count = ToNumber(GetNumSavedInstances and GetNumSavedInstances()) or 0
  for i = 1, count do
    local instanceName, _, reset, difficultyID, locked, extended, _, isRaid, _,
      difficultyName, numEncounters, _, _, instanceID = GetSavedInstanceInfo(i)
    reset = ToNumber(reset)
    -- продлённые сохранения пропускаем: убийство могло быть на прошлой неделе
    if locked and not extended and reset and reset > 0 then
      local lockEnd = RoundTime(stamp + reset)
      local kind = isRaid and "raid" or "party"
      difficultyID = ToNumber(difficultyID)
      local place = ToNumber(instanceID) or Clean(instanceName) or i
      for j = 1, ToNumber(numEncounters) or 0 do
        local bossName, _, isKilled = GetSavedInstanceEncounterInfo(i, j)
        local boss = Clean(bossName)
        local key = boss and WeekKey(kind, difficultyID, boss)
        if boss and isKilled and not issecretvalue(isKilled) and not seen[key] then
          seen[key] = true
          local rec = BaseRecord(player, stamp)
          rec.id = string.format("L-%s-%s-%d-%d-%d", player.key, tostring(place),
            difficultyID or 0, j, lockEnd)
          rec.source = "lockout"
          rec.boss = boss
          rec.instance = Clean(instanceName)
          rec.kind = kind
          rec.difficultyID = difficultyID
          rec.difficulty = Clean(difficultyName) or DifficultyName(difficultyID)
          rec.weekEnd = weekEnd or lockEnd
          if AddRecord(rec) then
            added = added + 1
          end
        end
      end
    end
  end

  count = ToNumber(GetNumSavedWorldBosses and GetNumSavedWorldBosses()) or 0
  for i = 1, count do
    local name, worldBossID, reset = GetSavedWorldBossInfo(i)
    local boss = Clean(name)
    reset = ToNumber(reset)
    local key = boss and WeekKey("world", 0, boss)
    if boss and reset and reset > 0 and not seen[key] then
      seen[key] = true
      local lockEnd = RoundTime(stamp + reset)
      local rec = BaseRecord(player, stamp)
      rec.id = string.format("W-%s-%s-%d", player.key, tostring(ToNumber(worldBossID) or boss), lockEnd)
      rec.source = "lockout"
      rec.boss = boss
      rec.kind = "world"
      rec.difficultyID = 0
      rec.weekEnd = weekEnd or lockEnd
      if AddRecord(rec) then
        added = added + 1
      end
    end
  end

  return added
end

local function RunScan()
  local added = ScanLockouts()
  if manualScanPending then
    manualScanPending = false
    if added > 0 then
      Print(string.format(L.scan_added, added))
    else
      Print(L.scan_none)
    end
  elseif added > 0 and not db.quiet then
    Print(string.format(L.scan_found, added))
  end
end

-- UPDATE_INSTANCE_INFO приходит пачками — сканируем раз в секунду.
local function QueueScan()
  if scanQueued then
    return
  end
  scanQueued = true
  C_Timer.After(1, function()
    scanQueued = false
    SafeCall(RunScan)
  end)
end

---------------------------------------------------------------------------
-- Новый маунт (NEW_MOUNT_ADDED) → «Собрано?» в таблице
---------------------------------------------------------------------------
-- Источник из журнала маунтов («|cFFFFD200Добыча:|r Скади…|n…») без кодов цвета и разметки.
local function CleanSource(text)
  if text == nil or issecretvalue(text) then
    return nil
  end
  text = tostring(text)
  text = text:gsub("|c%x%x%x%x%x%x%x%x", "")
  text = text:gsub("|r", "")
  text = text:gsub("|n", " / ")
  text = text:gsub("|T.-|t", "")
  text = text:gsub("|A.-|a", "")
  text = text:gsub("|H.-|h(.-)|h", "%1")
  return Clean(text)
end
ns.CleanSource = CleanSource

local function OnMountAdded(mountID)
  mountID = ToNumber(mountID)
  local journal = C_MountJournal
  if not mountID or not journal or not journal.GetMountInfoByID then
    return
  end
  local name, spellID = journal.GetMountInfoByID(mountID)
  name = Clean(name)
  local player = GetPlayer()
  if not name or not player.name then
    return
  end
  if ns.CollectionAdd then
    SafeCall(ns.CollectionAdd, mountID, name)
  end
  local from
  if journal.GetMountInfoExtraByID then
    local _, _, sourceText = journal.GetMountInfoExtraByID(mountID)
    from = CleanSource(sourceText)
  end
  local rec = BaseRecord(player, time())
  rec.id = string.format("C-%s-%d", player.key, mountID)
  rec.source = "mount"
  rec.kind = "mount"
  rec.boss = name          -- у записи всегда есть «boss»; для маунта это его имя
  rec.mount = name
  rec.mountID = mountID
  rec.spellID = ToNumber(spellID)
  rec.from = from          -- откуда маунт, по журналу маунтов: по нему таблица узнаёт босса
  if AddRecord(rec) and not db.quiet then
    Print(string.format(L.mount_added, name))
  end
end

---------------------------------------------------------------------------
-- Действия (их вызывают окно и чат-команды)
---------------------------------------------------------------------------
local function AddManual(text)
  local boss = Clean(text)
  if not boss then
    Print(string.format(L.manual_need_boss, L.manual_example))
    return false
  end
  local player = GetPlayer()
  if not player.name then
    return false
  end
  local stamp = time()
  local rec = BaseRecord(player, stamp)
  rec.id = string.format("M-%s-%d-%d", player.key, stamp, #db.kills + 1)
  rec.source = "manual"
  rec.kind = "manual"
  rec.boss = boss
  rec.weekEnd = WeekEnd()
  if AddRecord(rec) then
    Print(string.format(L.manual_done, boss, player.name))
    return true
  end
  return false
end

function ns.AddManual(text)
  local ok, added = pcall(AddManual, text)
  return ok and added or false
end

-- «Не фармлю»: строка (scope = "row") или весь рейд / подземелье (scope = "place") из списка «Осталось».
-- Программа снимет галочку «Фармлю» на листе персонажа; до этого строки прячутся здесь, в аддоне.
local function AddPlan(item, scope)
  if type(item) ~= "table" or (item.kind ~= "raid" and item.kind ~= "dungeon") then
    return false
  end
  local player = GetPlayer()
  if not player.name then
    return false
  end
  scope = scope == "place" and "place" or "row"
  local place = Clean(item.place)
  if scope == "place" and not place then
    return false
  end
  local stamp = time()
  local rec = BaseRecord(player, stamp)
  rec.id = string.format("P-%s-%d-%d", player.key, stamp, #db.kills + 1)
  rec.source = "plan"
  rec.kind = "plan"
  rec.list = item.kind
  rec.scope = scope
  rec.boss = Clean(item.boss) or place or "?"
  rec.mount = Clean(item.mount)
  rec.diff = Clean(item.diff)
  rec.place = place
  rec.farm = false
  if not AddRecord(rec) then
    return false
  end
  db.notFarmed = type(db.notFarmed) == "table" and db.notFarmed or {}
  local mine = type(db.notFarmed[player.key]) == "table" and db.notFarmed[player.key] or {}
  db.notFarmed[player.key] = mine
  mine[#mine + 1] = { scope = scope, list = rec.list, boss = rec.boss, mount = rec.mount, diff = rec.diff,
                      place = place, time = stamp }
  if scope == "place" then
    Print(string.format(L.plan_place, place, player.name))
  else
    Print(string.format(L.plan_row, PlanWhat(rec), player.name))
  end
  return true
end

function ns.AddPlan(item, scope)
  local ok, added = pcall(AddPlan, item, scope)
  return ok and added or false
end

-- Запись «сброс недели»: программа синхронизации поставит «Нет» всем персонажам
-- и заново отметит убийства этой недели из журнала.
function ns.ResetWeek()
  local player = GetPlayer()
  if not player.name then
    return nil
  end
  local stamp = time()
  local rec = BaseRecord(player, stamp)
  rec.id = string.format("R-%s-%d", player.key, stamp)
  rec.source = "reset"
  rec.kind = "reset"
  rec.boss = L.reset_week   -- у записи всегда есть «boss»; таблице оно не важно
  rec.weekEnd = WeekEnd()
  if AddRecord(rec) then
    return rec
  end
  return nil
end

function ns.RequestScan()
  manualScanPending = true
  Print(L.scan_start)
  RequestLockouts()
end

function ns.SyncNow()
  Print(L.sync_reload)
  ReloadUI()
end

function ns.ClearLog()
  db.kills = {}
  RebuildIndex()
  Print(L.log_cleared)
  Changed()
end

function ns.SetQuiet(quiet)
  db.quiet = quiet and true or nil
  Changed()
end

function ns.GetDB()
  return db
end

function ns.CountThisWeek()
  local weekEnd = WeekEnd()
  local count = 0
  if weekEnd and db then
    for _, rec in ipairs(db.kills) do
      if rec.weekEnd == weekEnd and rec.kind ~= "reset" and rec.kind ~= "mount" then
        count = count + 1
      end
    end
  end
  return count
end

---------------------------------------------------------------------------
-- Чат-команды
---------------------------------------------------------------------------
local function ListRecent(limit)
  local total = #db.kills
  if total == 0 then
    Print(L.log_empty)
    return
  end
  limit = math.max(1, math.min(limit or 10, 50))
  Print(string.format(L.log_recent, math.min(limit, total), total))
  for i = total, math.max(1, total - limit + 1), -1 do
    local rec = db.kills[i]
    DEFAULT_CHAT_FRAME:AddMessage(string.format("   %s  %s — %s",
      date("%d.%m %H:%M", rec.time or 0), rec.char or "?", Describe(rec)))
  end
end

local function ShowHelp()
  local player = GetPlayer()
  Print(string.format(L.help_version, VERSION, player.name or "?", player.realm or "?", #db.kills, ns.CountThisWeek()))
  local lines = {
    CMD .. "/mss|r — " .. L.help_open,
    CMD .. "/mss sync|r — " .. L.help_sync,
    CMD .. "/mss left|r — " .. L.help_left,
    CMD .. "/mss list|r — " .. L.help_list,
    CMD .. "/mss add " .. L.help_add_arg .. "|r — " .. L.help_add,
    CMD .. "/mss scan|r — " .. L.help_scan,
    CMD .. "/mss reset|r — " .. L.help_reset,
    CMD .. "/mss chars|r — " .. L.help_chars,
    CMD .. "/mss forget " .. L.help_forget_arg .. "|r — " .. L.help_forget,
    CMD .. "/mss quiet|r — " .. (db.quiet and L.help_quiet_off or L.help_quiet_on),
    CMD .. "/mss minimap|r — " .. L.help_minimap,
    CMD .. "/mss settings|r — " .. L.help_settings,
    CMD .. "/mss clear|r — " .. L.help_clear,
  }
  for _, line in ipairs(lines) do
    DEFAULT_CHAT_FRAME:AddMessage("   " .. line)
  end
end

SLASH_MOUNTSHEETSYNC1 = "/mss"
SLASH_MOUNTSHEETSYNC2 = "/mountsheet"
SlashCmdList["MOUNTSHEETSYNC"] = function(msg)
  if not db then
    return
  end
  local cmd, rest = (msg or ""):match("^ *([^ ]*) *(.-) *$")
  cmd = (cmd or ""):lower()
  if cmd == "" then
    if ns.ToggleWindow then
      ns.ToggleWindow()
    else
      ShowHelp()
    end
  elseif cmd == "help" or cmd == "?" then
    ShowHelp()
  elseif cmd == "sync" or cmd == "reload" then
    ns.SyncNow()
  elseif cmd == "left" or cmd == "осталось" then
    if ns.PrintRemaining then
      ns.PrintRemaining()
    end
  elseif cmd == "list" then
    ListRecent(tonumber(rest) or 10)
  elseif cmd == "add" then
    ns.AddManual(rest)
  elseif cmd == "scan" then
    ns.RequestScan()
  elseif cmd == "reset" then
    if ns.ConfirmResetWeek then
      ns.ConfirmResetWeek()
    end
  elseif cmd == "chars" or cmd == "персонажи" then
    if ns.PrintCharacters then
      ns.PrintCharacters()
    end
  elseif cmd == "forget" then
    if ns.ForgetCommand then
      ns.ForgetCommand(rest)
    end
  elseif cmd == "quiet" then
    ns.SetQuiet(not db.quiet)
    Print(db.quiet and L.quiet_on or L.quiet_off)
  elseif cmd == "clear" then
    ns.ClearLog()
  elseif cmd == "minimap" then
    local shown = ns.ToggleMinimapButton and ns.ToggleMinimapButton()
    if shown ~= nil then
      Print(shown and L.minimap_shown or L.minimap_hidden)
    end
  elseif cmd == "settings" or cmd == "options" or cmd == "config" or cmd == "настройки" then
    if ns.ToggleSettings then
      ns.ToggleSettings()
    end
  elseif cmd == "lang" or cmd == "language" or cmd == "язык" then
    local code = ns.ParseLanguage(rest)
    if not code or not ns.SetLanguage(code) then
      local names = {}
      for _, c in ipairs(ns.LANGUAGES) do
        names[#names + 1] = c:sub(1, 2):lower() .. " (" .. ns.LANGUAGE_NAMES[c] .. ")"
      end
      Print(string.format(L.settings_unknown_language, rest or "", "auto, " .. table.concat(names, ", ")))
    else
      Print(string.format(L.settings_language_set, ns.LANGUAGE_NAMES[ns.CurrentLanguage()]))
    end
  else
    ShowHelp()
  end
end

---------------------------------------------------------------------------
-- События
---------------------------------------------------------------------------
local function Init()
  if type(MountSheetSyncDB) ~= "table" then
    MountSheetSyncDB = {}
  end
  db = MountSheetSyncDB
  db.version = 1
  if type(db.kills) ~= "table" then
    db.kills = {}
  end
  Prune()
  ns.ApplyLanguage()
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:SetScript("OnEvent", function(self, event, ...)
  if event == "ADDON_LOADED" then
    if ... ~= ADDON_NAME then
      return
    end
    self:UnregisterEvent("ADDON_LOADED")
    if SafeCall(Init) then
      self:RegisterEvent("PLAYER_LOGIN")
      self:RegisterEvent("ENCOUNTER_END")
      self:RegisterEvent("BOSS_KILL")
      self:RegisterEvent("UPDATE_INSTANCE_INFO")
      self:RegisterEvent("NEW_MOUNT_ADDED")
    end
  elseif event == "PLAYER_LOGIN" then
    if ns.SetupMinimapButton then
      SafeCall(ns.SetupMinimapButton)
    end
    if ns.OnLoginCollection then
      SafeCall(ns.OnLoginCollection)
    end
    C_Timer.After(3, RequestLockouts)
  elseif event == "ENCOUNTER_END" then
    local encounterID, encounterName, difficultyID, groupSize, success = ...
    if not issecretvalue(success) and (success == 1 or success == true) then
      SafeCall(OnBossKilled, "encounter", encounterID, encounterName, difficultyID, groupSize)
    end
  elseif event == "BOSS_KILL" then
    local encounterID, encounterName = ...
    SafeCall(OnBossKilled, "bosskill", encounterID, encounterName)
  elseif event == "UPDATE_INSTANCE_INFO" then
    QueueScan()
  elseif event == "NEW_MOUNT_ADDED" then
    SafeCall(OnMountAdded, ...)
  end
end)
