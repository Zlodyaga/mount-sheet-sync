--[[
Mount Sheet Sync 2.3.0 — персонажи и коллекция маунтов
====================================================
По этим данным программа Mount Sheet Sync строит новую Google-таблицу
(«Создать таблицу»): листы «Рейды <Имя>» / «Подземелья <Имя>» на каждого
персонажа и «Собрано?» = «Да» для маунтов, которые уже есть.

Списка всех персонажей аккаунта игра не даёт, поэтому аддон запоминает
каждого, кто вошёл в игру с ним хотя бы раз. Коллекция маунтов общая
на аккаунт — её хватает прочитать одним персонажем.

В SavedVariables (MountSheetSyncDB):
  chars["Имя-Сервер"] = { name, realm, class, classFile, level, guid, seen }
  collection = { updated, journal, collected,
                 names  = { имена собранных маунтов },
                 ids    = { их mountID },
                 mounts = { { id, name, type, from, got, faction }, … } }   -- весь журнал маунтов
    type — sourceType из журнала (1 — добыча), from — источник без цветовых кодов,
    faction — только у маунтов одной фракции: 0 — Орда, 1 — Альянс.
    По mountID программа узнаёт маунты таблицы, даже если имя в таблице другое.

Команды: /mss chars — список, /mss forget <имя> — убрать персонажа.
]]

local ADDON_NAME, ns = ...

local issecretvalue = issecretvalue or function() return false end

local MAX_CHARS = 200          -- больше персонажей не храним (самые давние уходят)

local function Text(value)
  if value == nil or issecretvalue(value) then
    return nil
  end
  local s = tostring(value):gsub("^%s+", ""):gsub("%s+$", "")
  if s == "" then
    return nil
  end
  return s
end

local function Num(value)
  if value == nil or issecretvalue(value) then
    return nil
  end
  return tonumber(value)
end

local function DB()
  local db = ns.GetDB and ns.GetDB()
  if type(db) ~= "table" then
    return nil
  end
  return db
end

---------------------------------------------------------------------------
-- Персонажи
---------------------------------------------------------------------------
local function CharKey(name, realm)
  return name .. "-" .. realm
end

local function Prune(chars)
  local list = {}
  for key, c in pairs(chars) do
    list[#list + 1] = { key = key, seen = type(c) == "table" and tonumber(c.seen) or 0 }
  end
  if #list <= MAX_CHARS then
    return
  end
  table.sort(list, function(a, b) return a.seen < b.seen end)
  for i = 1, #list - MAX_CHARS do
    chars[list[i].key] = nil
  end
end

--- Запомнить текущего персонажа. Возвращает ключ «Имя-Сервер» или nil.
function ns.RememberCharacter()
  local db = DB()
  if not db then
    return nil
  end
  local name = Text(UnitName and UnitName("player"))
  local realm = Text(GetRealmName and GetRealmName())
  if not name or not realm or name == UNKNOWNOBJECT then
    return nil
  end
  if type(db.chars) ~= "table" then
    db.chars = {}
  end
  local key = CharKey(name, realm)
  local c = type(db.chars[key]) == "table" and db.chars[key] or {}
  c.name = name
  c.realm = realm
  if UnitClass then
    local className, classFile = UnitClass("player")
    c.class = Text(className) or c.class
    c.classFile = Text(classFile) or c.classFile
  end
  if UnitLevel then
    c.level = Num(UnitLevel("player")) or c.level
  end
  if UnitGUID then
    c.guid = Text(UnitGUID("player")) or c.guid
  end
  c.seen = time()
  db.chars[key] = c
  Prune(db.chars)
  return key
end

--- Персонажи по алфавиту: { { key, name, realm, class, level, seen }, … }
function ns.ListCharacters()
  local db = DB()
  local list = {}
  if not db or type(db.chars) ~= "table" then
    return list
  end
  for key, c in pairs(db.chars) do
    if type(c) == "table" and c.name then
      list[#list + 1] = { key = key, name = c.name, realm = c.realm, class = c.class, level = c.level, seen = c.seen }
    end
  end
  table.sort(list, function(a, b)
    if a.name ~= b.name then
      return a.name < b.name
    end
    return (a.realm or "") < (b.realm or "")
  end)
  return list
end

local function Lower(s)
  if ns.LowerText then
    return ns.LowerText(s)
  end
  return s:lower()
end

--- Убрать персонажа из списка: по имени или «Имя-Сервер». Возвращает убранные ключи.
function ns.ForgetCharacter(text)
  local db = DB()
  local want = Text(text)
  local removed = {}
  if not db or not want or type(db.chars) ~= "table" then
    return removed
  end
  want = Lower(want)
  for key, c in pairs(db.chars) do
    local name = type(c) == "table" and c.name or nil
    if Lower(key) == want or (name and Lower(name) == want) then
      removed[#removed + 1] = key
    end
  end
  table.sort(removed)
  for _, key in ipairs(removed) do
    db.chars[key] = nil
  end
  return removed
end

---------------------------------------------------------------------------
-- Коллекция маунтов
---------------------------------------------------------------------------
--- Прочитать журнал маунтов целиком. Возвращает число собранных или nil, если журнала нет.
function ns.ScanCollection()
  local db = DB()
  local journal = C_MountJournal
  if not db or not journal or not journal.GetMountIDs or not journal.GetMountInfoByID then
    return nil
  end
  local ok, ids = pcall(journal.GetMountIDs)
  if not ok or type(ids) ~= "table" then
    return nil
  end
  local names, got, mounts = {}, {}, {}
  local seen = {}
  local extra = journal.GetMountInfoExtraByID
  for _, id in ipairs(ids) do
    id = Num(id)
    if id and not seen[id] then
      seen[id] = true
      local name, _, _, _, _, sourceType, _, isFactionSpecific, faction, _, isCollected = journal.GetMountInfoByID(id)
      name = Text(name)
      if name then
        local collected = not issecretvalue(isCollected) and isCollected == true
        if collected then
          names[#names + 1] = name
          got[#got + 1] = id
        end
        local from
        if extra then
          local okExtra, _, _, sourceText = pcall(extra, id)
          if okExtra then
            from = ns.CleanSource and ns.CleanSource(sourceText) or nil
          end
        end
        local side
        if not issecretvalue(isFactionSpecific) and isFactionSpecific == true then
          side = Num(faction)   -- 0 — Орда, 1 — Альянс
        end
        mounts[#mounts + 1] = { id = id, name = name, type = Num(sourceType), from = from, got = collected or nil,
                                faction = side }
      end
    end
  end
  db.collection = {
    updated = time(),
    journal = #ids,
    collected = #got,
    names = names,
    ids = got,
    mounts = mounts,
  }
  return #got
end

local function MarkGot(list, mountID)
  if type(list) ~= "table" then
    return
  end
  for _, d in ipairs(list) do
    if type(d) == "table" and d.id == mountID then
      d.got = true
    end
  end
end

--- Новый маунт (NEW_MOUNT_ADDED): дописать в коллекцию без полного чтения журнала.
function ns.CollectionAdd(mountID, name)
  local db = DB()
  mountID = Num(mountID)
  name = Text(name)
  local c = db and db.collection
  if not mountID or not name or type(c) ~= "table" or type(c.ids) ~= "table" or type(c.names) ~= "table" then
    return false
  end
  for _, id in ipairs(c.ids) do
    if id == mountID then
      return false
    end
  end
  c.ids[#c.ids + 1] = mountID
  c.names[#c.names + 1] = name
  c.collected = #c.ids
  MarkGot(c.mounts, mountID)
  MarkGot(c.drops, mountID)   -- данные аддона 2.0.0
  c.updated = time()
  return true
end

---------------------------------------------------------------------------
-- Чат: /mss chars, /mss forget
---------------------------------------------------------------------------
function ns.PrintCharacters()
  local list = ns.ListCharacters()
  local db = DB()
  local col = db and db.collection
  local Print = ns.Print or print
  if #list == 0 then
    Print(ns.L.chars_none)
    return
  end
  Print(string.format(ns.L.chars_title, #list))
  for _, c in ipairs(list) do
    local extra = {}
    if c.class then
      extra[#extra + 1] = c.class
    end
    if c.level then
      extra[#extra + 1] = tostring(c.level)
    end
    DEFAULT_CHAT_FRAME:AddMessage(string.format("   %s |cff8c8c8c(%s%s)|r", c.name, c.realm or "?",
      #extra > 0 and (", " .. table.concat(extra, " ")) or ""))
  end
  if type(col) == "table" and col.collected then
    DEFAULT_CHAT_FRAME:AddMessage("   |cff8c8c8c" .. string.format(ns.L.chars_collected, col.collected, col.journal or col.collected) .. "|r")
  end
  DEFAULT_CHAT_FRAME:AddMessage("   |cff8c8c8c" .. ns.L.chars_forget_hint .. "|r")
end

function ns.ForgetCommand(text)
  local Print = ns.Print or print
  if not Text(text) then
    Print(string.format(ns.L.forget_need_name, (ns.GetPlayer and ns.GetPlayer().name) or "Name"))
    return
  end
  local removed = ns.ForgetCharacter(text)
  if #removed == 0 then
    Print(string.format(ns.L.forget_unknown, Text(text)))
  else
    Print(string.format(ns.L.forget_done, table.concat(removed, ", ")))
  end
end

---------------------------------------------------------------------------
-- Вызывает ядро
---------------------------------------------------------------------------
local function ScanLater()
  ns.ScanCollection()
end

function ns.OnLoginCollection()
  ns.RememberCharacter()
  -- журнал маунтов к PLAYER_LOGIN бывает ещё не готов — читаем чуть позже
  C_Timer.After(5, ScanLater)
end
