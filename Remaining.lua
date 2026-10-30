--[[
Mount Sheet Sync 3.0.1 — «Осталось на этой неделе»
==================================================
Список приходит из Google-таблицы: программа MountSheetSync.exe записывает его
в SheetData.lua (глобальная MountSheetSyncSheet), а игра читает этот файл при
входе и /reload. Боссов, убитых после этого, аддон отмечает сам по журналу
этой недели — по тем же правилам, что и скрипт таблицы: имя босса, сложность
и строки «Любой босс …». В списке есть и строки, где в таблице уже «Да»
(y = true): после «Сброса недели» они снова считаются оставшимися — сразу,
не дожидаясь, пока программа принесёт новый список.
]]

local ADDON_NAME, ns = ...

local WEEK = 7 * 24 * 60 * 60
local L = ns.L

---------------------------------------------------------------------------
-- Имена — так же, как norm_() в Code.gs
---------------------------------------------------------------------------

-- Кириллица в UTF-8: А–П = D0 90–9F, Р–Я = D0 A0–AF, Ё = D0 81;
-- строчные а–п = D0 B0–BF, р–я = D1 80–8F, ё = D1 91.
-- Латиница с диакритикой: À–Þ = C3 80–9E (кроме × = C3 97) → à–þ = C3 A0–BE.
local function Lower(s)
  s = s:lower()
  s = s:gsub("\208([\144-\159])", function(c) return "\208" .. string.char(c:byte() + 32) end)
  s = s:gsub("\208([\160-\175])", function(c) return "\209" .. string.char(c:byte() - 32) end)
  s = s:gsub("\195([\128-\150\152-\158])", function(c) return "\195" .. string.char(c:byte() + 32) end)
  s = s:gsub("\208\129", "\208\181")   -- Ё → е
  s = s:gsub("\209\145", "\208\181")   -- ё → е
  return s
end
ns.LowerText = Lower   -- и для /mss forget (Collection.lua)

local UTF8_CHAR = "[%z\1-\127\192-\255][\128-\191]*"

local APOSTROPHES = {
  ["'"] = true, ["`"] = true, ["\226\128\153"] = true, ["\226\128\152"] = true,   -- ’ ‘
  ["\202\188"] = true, ["\194\180"] = true,                                     -- ʼ ´
}
-- Не буквы и не цифры — в Code.gs они тоже разделяют слова: знаки U+0080–U+00BF
-- (« » - неразрывный пробел…), × и ÷, всё из U+2000–U+2FFF (— – “ ” … → № …)
-- и знаки препинания U+3000–U+303F.
local function IsSeparator(ch)
  local b1, b2 = ch:byte(1, 2)
  return b1 == 194 or b1 == 226 or (b1 == 195 and (b2 == 151 or b2 == 183)) or (b1 == 227 and b2 == 128)
end

local function Norm(s)
  if s == nil then
    return ""
  end
  local out = {}
  for ch in Lower(tostring(s)):gmatch(UTF8_CHAR) do
    if APOSTROPHES[ch] then
      -- «Н'Зот» → «нзот», как в таблице
    elseif #ch == 1 then
      out[#out + 1] = ch:find("^[0-9a-z]$") and ch or " "
    else
      out[#out + 1] = IsSeparator(ch) and " " or ch
    end
  end
  return (table.concat(out):gsub(" +", " "):match("^ ?(.-) ?$"))
end
ns.Norm = Norm

local function CharLen(s)
  return select(2, s:gsub("[^\128-\191]", ""))
end

local function Split(s)
  local words = {}
  for w in s:gmatch("[^ ]+") do
    words[#words + 1] = w
  end
  return words
end

local function WordSet(words)
  local set = {}
  for _, w in ipairs(words) do
    set[w] = true
  end
  return set
end

-- Имя, разобранное на слова один раз (список строится заново при каждом обновлении окна).
local prepared = {}

local function Prepare(name)
  local p = prepared[name]
  if not p then
    local words = Split(name)
    p = { name = name, words = words, set = WordSet(words), len = CharLen(name) }
    prepared[name] = p
  end
  return p
end

local function Subset(words, set)
  for i = 1, #words do
    if not set[words[i]] then
      return false
    end
  end
  return true
end

-- nameMatches_(): «Фиракк» в таблице совпадает с «Фиракк Пылающий» из игры;
-- loose (ручная отметка) — хватает одного слова. a — из игры, b — из таблицы.
local function MatchPrepared(a, b, loose)
  if a.name == "" or b.name == "" then
    return false
  end
  if a.name == b.name then
    return true
  end
  if b.len >= 4 and Subset(b.words, a.set) then
    return true
  end
  local minWords = (loose and a.len >= 4) and 1 or 2
  return #a.words >= minWords and Subset(a.words, b.set)
end

local function NameMatches(a, b, loose)
  return MatchPrepared(Prepare(a), Prepare(b), loose)
end
ns.NameMatches = NameMatches

local function Chars(s)
  local list = {}
  for ch in s:gmatch(UTF8_CHAR) do
    list[#list + 1] = ch
  end
  return list
end

-- sameWord_(): одно слово в разных падежах («Склеп Аркавона» ↔ «Склепа Аркавона»).
local function SameWord(a, b)
  if a == b then
    return true
  end
  local ca, cb = Chars(a), Chars(b)
  local i = 0
  while i < #ca and i < #cb and ca[i + 1] == cb[i + 1] do
    i = i + 1
  end
  return i >= math.max(4, math.min(#ca, #cb) - 2)
end

local PLACE_STOP_WORDS = {
  ["любой"] = true, ["любые"] = true, ["все"] = true, ["всех"] = true, ["босс"] = true,
  ["боссы"] = true, ["боссов"] = true, ["any"] = true, ["boss"] = true, ["bosses"] = true, ["the"] = true,
}

local function PlaceWords(text)
  local words = {}
  for _, w in ipairs(Split(Norm(text))) do
    if CharLen(w) >= 3 and not PLACE_STOP_WORDS[w] then
      words[#words + 1] = w
    end
  end
  return words
end

local function Covers(xs, ys)
  for _, x in ipairs(xs) do
    local found = false
    for _, y in ipairs(ys) do
      if SameWord(x, y) then
        found = true
        break
      end
    end
    if not found then
      return false
    end
  end
  return true
end

local function PlaceMentioned(a, b)
  if type(a) ~= "table" or type(b) ~= "table" or #a == 0 or #b == 0 then
    return false
  end
  return Covers(a, b) or Covers(b, a)
end

---------------------------------------------------------------------------
-- Запись журнала ↔ строка списка (rowMatch_ и markInWorld_ в Code.gs)
---------------------------------------------------------------------------
local KIND_FITS = {
  raid = { raid = true, manual = true },
  dungeon = { party = true, scenario = true, manual = true },
  world = { world = true, manual = true },
}

-- d — DifficultyID, при которых строка засчитывается (пусто — любая сложность).
-- Неизвестную игре сложность обычная строка принимает, «Любой босс …» — нет.
local function DifficultyFits(item, rec, strict, known)
  local d = item.d
  if type(d) ~= "table" or #d == 0 then
    return true
  end
  local id = tonumber(rec.difficultyID)
  if not id or not known[id] then
    return not strict
  end
  for _, x in ipairs(d) do
    if x == id then
      return true
    end
  end
  return false
end

local function Fits(item, rec, recName, known)
  local fits = KIND_FITS[item.kind]
  if not fits or not fits[rec.kind] then
    return false
  end
  local manual = rec.kind == "manual"
  local named = false
  local names = type(item.names) == "table" and item.names or {}
  for i = 1, #names do
    if MatchPrepared(recName, Prepare(names[i]), manual) then
      named = true
      break
    end
  end
  if item.kind == "world" or manual then
    return named   -- мировые боссы и ручная отметка: сложность не проверяется
  end
  if named and DifficultyFits(item, rec, false, known) then
    return true
  end
  if item.any and DifficultyFits(item, rec, true, known) then
    if item.tw then
      return true
    end
    local where = PlaceWords(rec.instance)
    local aw = type(item.aw) == "table" and item.aw or {}
    return PlaceMentioned(where, aw[1]) or PlaceMentioned(where, aw[2])
  end
  return false
end

---------------------------------------------------------------------------
-- Список для текущего персонажа
---------------------------------------------------------------------------
local normCache = setmetatable({}, { __mode = "k" })

local function RecordName(rec)
  local n = normCache[rec]
  if not n then
    n = Prepare(Norm(rec.boss))
    normCache[rec] = n
  end
  return n
end

local function KnownIds(data)
  if not data._known then
    local known = {}
    for _, id in ipairs(type(data.known) == "table" and data.known or {}) do
      if tonumber(id) then
        known[tonumber(id)] = true
      end
    end
    data._known = known
  end
  return data._known
end

local function FindCharacter(data, player)
  if not player.name then
    return nil
  end
  local keys = { [Norm(player.name)] = true }
  if player.realm then
    keys[Norm(player.name .. "-" .. player.realm)] = true
    keys[Norm(player.name .. "-" .. player.realm:gsub(" ", ""))] = true
  end
  for _, ch in ipairs(data.chars) do
    if type(ch) == "table" then
      for _, key in ipairs(type(ch.keys) == "table" and ch.keys or {}) do
        if keys[key] then
          return ch
        end
      end
    end
  end
  return nil
end

--[[
Что осталось текущему персонажу:
  state = "nodata" — программа ещё не прислала список;
  state = "nochar" — персонажа нет в таблице;
  state = "ok"     — items = { { item = строка, done = убит после обновления списка, by = запись }, … }
                     (строки «Да» сюда не входят), left — сколько осталось, total — сколько всего строк.
generated — когда список взят из таблицы;
afterReset — «Сброс недели» нажат уже после этого: строки «Да» снова в списке;
notReset — на этой неделе «Сброса недели» не было: в таблице могут остаться «Да» прошлой недели.
]]
local cacheKey, cacheResult

function ns.GetRemaining()
  local data = MountSheetSyncSheet
  if type(data) ~= "table" or type(data.chars) ~= "table" then
    return { state = "nodata" }
  end
  local player = ns.GetPlayer()
  local weekEnd = ns.WeekEnd()
  local db = ns.GetDB()
  local kills = db and db.kills or {}
  local last = kills[#kills]
  local key = table.concat({ tostring(data), tostring(data.generated), tostring(data.resetAt), player.key or "?",
    tostring(weekEnd), #kills, last and tostring(last.id) or "" }, "|")
  if key == cacheKey then
    return cacheResult
  end
  local result = ns.BuildRemaining(data, player, weekEnd, kills)
  cacheKey, cacheResult = key, result
  return result
end

-- Последний «Сброс недели» этой недели в журнале (его мог нажать любой персонаж аккаунта).
local function ResetTime(kills, weekEnd)
  local last
  if weekEnd then
    for _, rec in ipairs(kills) do
      local t = tonumber(rec.time)
      if rec.kind == "reset" and rec.weekEnd == weekEnd and t and (not last or t > last) then
        last = t
      end
    end
  end
  return last
end

-- ch — строка персонажа из таблицы, если она уже известна (список всех персонажей), иначе ищется по player.
function ns.BuildRemaining(data, player, weekEnd, kills, ch)
  local result = { generated = tonumber(data.generated), spreadsheet = data.spreadsheet }
  local resetT = ResetTime(kills, weekEnd)
  result.afterReset = resetT ~= nil and (not result.generated or result.generated < resetT)
  if weekEnd and not resetT then
    -- сброс мог прийти и с другого компьютера или аккаунта — тогда о нём знает таблица
    local resetAt = tonumber(data.resetAt)
    result.notReset = not (resetAt and resetAt >= weekEnd - WEEK - 3600)
  end
  ch = ch or FindCharacter(data, player)
  if not ch then
    result.state = "nochar"
    result.name = player.name
    return result
  end

  -- записи этого персонажа за эту неделю
  local recs = {}
  if weekEnd then
    for _, rec in ipairs(kills) do
      if rec.weekEnd == weekEnd and rec.kind ~= "reset" and rec.kind ~= "mount"
        and (rec.charKey == player.key or (not rec.charKey and rec.char == player.name)) then
        recs[#recs + 1] = rec
      end
    end
  end

  local known = KnownIds(data)
  local hidden = ns.NotFarmed and ns.NotFarmed(player.key, tonumber(data.generated)) or {}
  local items, left, total = {}, 0, 0
  for _, item in ipairs(type(ch.items) == "table" and ch.items or {}) do
    if type(item) == "table" and not ns.IsNotFarmed(hidden, item) then
      total = total + 1
      if not item.y or result.afterReset then
        local by
        for _, rec in ipairs(recs) do
          if Fits(item, rec, RecordName(rec), known) then
            by = rec
            break
          end
        end
        items[#items + 1] = { item = item, done = by ~= nil, by = by }
        if not by then
          left = left + 1
        end
      end
    end
  end
  result.state = "ok"
  result.name = ch.name
  result.items = items
  result.left = left
  result.total = total
  return result
end

-- Все персонажи из таблицы: { { name, classFile, current, r }, … }, r — как у ns.GetRemaining().
-- Текущий персонаж первым, дальше — у кого больше осталось, потом по имени. Пересчёт — только когда
-- поменялись список из таблицы, журнал, неделя или персонаж.
local allKey, allResult

function ns.GetAllRemaining()
  local data = MountSheetSyncSheet
  if type(data) ~= "table" or type(data.chars) ~= "table" then
    return {}
  end
  local player = ns.GetPlayer()
  local weekEnd = ns.WeekEnd()
  local db = ns.GetDB()
  local kills = db and db.kills or {}
  local last = kills[#kills]
  local key = table.concat({ tostring(data), tostring(data.generated), tostring(data.resetAt), player.key or "?",
    tostring(weekEnd), #kills, last and tostring(last.id) or "" }, "|")
  if key == allKey then
    return allResult
  end
  -- персонажи этого аккаунта (класс и GUID) по ключу, как в таблице
  local byKey = {}
  for _, c in pairs(db and type(db.chars) == "table" and db.chars or {}) do
    if type(c) == "table" and c.name then
      local realm = tostring(c.realm or "")
      byKey[Norm(c.name .. "-" .. realm)] = c
      byKey[Norm(c.name .. "-" .. (realm:gsub(" ", "")))] = c
      local short = Norm(c.name)   -- в таблице ключ обычно просто имя листа
      byKey[short] = byKey[short] or c
    end
  end
  local current = FindCharacter(data, player)
  local list = {}
  for _, ch in ipairs(data.chars) do
    if type(ch) == "table" then
      local known
      for _, k in ipairs(type(ch.keys) == "table" and ch.keys or {}) do
        if byKey[k] then
          known = byKey[k]
          break
        end
      end
      local who
      if ch == current then
        who = player
      elseif known then
        who = { name = known.name, realm = known.realm, key = known.guid or (known.name .. "-" .. tostring(known.realm or "?")) }
      else
        who = { name = ch.name, key = "?" }   -- персонаж с другого аккаунта: его записей здесь нет
      end
      list[#list + 1] = {
        name = tostring(ch.name or who.name or "?"),
        classFile = known and known.classFile or nil,
        current = ch == current,
        r = ns.BuildRemaining(data, who, weekEnd, kills, ch),
      }
    end
  end
  table.sort(list, function(a, b)
    if a.current ~= b.current then
      return a.current
    end
    if (a.r.left or 0) ~= (b.r.left or 0) then
      return (a.r.left or 0) > (b.r.left or 0)
    end
    return a.name < b.name
  end)
  allKey, allResult = key, list
  return list
end

-- Сводка «кому что осталось»: { { kind, item, names = { имя, … } }, … } — строки, которые хоть у кого-то ещё
-- не убиты; одна на босса + сложность + маунт. Порядок: ns.KIND_ORDER, потом у кого больше персонажей, потом босс.
local summaryFor, summaryResult
local summaryKey = setmetatable({}, { __mode = "k" })   -- строка таблицы → её ключ (строки живут до /reload)

function ns.GetSummary()
  local all = ns.GetAllRemaining()
  if all == summaryFor then
    return summaryResult
  end
  local rows, byKey = {}, {}
  for _, c in ipairs(all) do
    if c.r.state == "ok" then
      for _, entry in ipairs(c.r.items) do
        if not entry.done then
          local it = entry.item
          local k = summaryKey[it]
          if not k then
            k = table.concat({ tostring(it.kind), Norm(it.boss), Norm(it.diff), Norm(it.mount) }, "|")
            summaryKey[it] = k
          end
          local row = byKey[k]
          if not row then
            row = { kind = it.kind, item = it, names = {} }
            byKey[k] = row
            rows[#rows + 1] = row
          end
          row.names[#row.names + 1] = c.name
        end
      end
    end
  end
  local order = {}
  for i, kind in ipairs(ns.KIND_ORDER) do
    order[kind] = i
  end
  table.sort(rows, function(a, b)
    local ka, kb = order[a.kind] or 99, order[b.kind] or 99
    if ka ~= kb then
      return ka < kb
    end
    if #a.names ~= #b.names then
      return #a.names > #b.names
    end
    return tostring(a.item.boss) < tostring(b.item.boss)
  end)
  summaryFor, summaryResult = all, rows
  return rows
end

---------------------------------------------------------------------------
-- Счётчик попыток (таблица программы 2.8+: строки «mounts» в списке)
---------------------------------------------------------------------------
-- В строке: те же поля, что у строк «Осталось» (имена босса, d — сложности), n — попыток по таблице на момент
-- списка, p — шанс за попытку. Убийства после списка аддон досчитывает сам — так в чате и в подсказке
-- уже есть попытка, которую программа ещё не отправила.

-- Попытка ли эта запись для строки: ручная отметка (без сложности) — только для мировых боссов, как в программе.
local function IsAttempt(m, rec, recName, known)
  if rec.kind == "manual" and m.kind ~= "world" then
    return false
  end
  return Fits(m, rec, recName, known)
end

-- Маунт уже собран (игра знает его ID) — счётчик ему больше не нужен.
local function Collected(m)
  local id = tonumber(m.id)
  if not id or not C_MountJournal or not C_MountJournal.GetMountInfoByID then
    return false
  end
  local ok, _, _, _, _, _, _, _, _, _, _, isCollected = pcall(C_MountJournal.GetMountInfoByID, id)
  return ok and isCollected == true
end

-- Имя маунта на языке игры (по ID), иначе — как в таблице.
local function MountName(m)
  local id = tonumber(m.id)
  if id and C_MountJournal and C_MountJournal.GetMountInfoByID then
    local ok, name = pcall(C_MountJournal.GetMountInfoByID, id)
    if ok and type(name) == "string" and name ~= "" and not (issecretvalue and issecretvalue(name)) then
      return name
    end
  end
  return tostring(m.mount or "?")
end

local function AttemptRows()
  local data = MountSheetSyncSheet
  if type(data) ~= "table" or type(data.mounts) ~= "table" then
    return nil
  end
  return data
end

-- Сколько попыток у строки сейчас: из таблицы плюс подходящие убийства после списка.
local function CountNow(m, data, kills, known)
  local generated = tonumber(data.generated) or 0
  local n = tonumber(m.n) or 0
  for _, rec in ipairs(kills) do
    if (tonumber(rec.time) or 0) > generated and IsAttempt(m, rec, RecordName(rec), known) then
      n = n + 1
    end
  end
  return n
end

-- 1 − (1 − p)^n: «31%», «4,9%» (меньше 10 % — с десятой; десятичный знак — L.decimal языка аддона).
function ns.FormatChance(n, p)
  local v = (1 - (1 - p) ^ n) * 100
  local text = v < 10 and string.format("%.1f", v) or string.format("%d", math.floor(v + 0.5))
  text = text:gsub("%.0$", "")
  if L.decimal == "," then
    text = text:gsub("%.", ",")
  end
  return text .. "%"
end

-- Убийство rec (уже в журнале): строки, для которых оно попытка, — { name, n, p } (n — вместе с этой попыткой).
function ns.AttemptsFor(rec)
  local data = AttemptRows()
  if not data or type(rec) ~= "table" then
    return {}
  end
  local known = KnownIds(data)
  local db = ns.GetDB()
  local kills = db and db.kills or {}
  local recName = RecordName(rec)
  local out = {}
  for _, m in ipairs(data.mounts) do
    local p = type(m) == "table" and tonumber(m.p)
    if p and p > 0 and p < 1 and IsAttempt(m, rec, recName, known) and not Collected(m) then
      local n = CountNow(m, data, kills, known)
      if (tonumber(rec.time) or 0) <= (tonumber(data.generated) or 0) then
        n = n + 1   -- запись старше списка (часы?) — эту попытку таблица ещё не видела
      end
      out[#out + 1] = { name = MountName(m), n = n, p = p }
    end
  end
  return out
end

-- Труп с таким именем роняет несобранный маунт со строки «Мировые боссы» (рар без боя и сохранений).
function ns.RareRow(name)
  local data = AttemptRows()
  if not data or type(name) ~= "string" or name == "" then
    return false
  end
  local want = Prepare(Norm(name))
  for _, m in ipairs(data.mounts) do
    if type(m) == "table" and m.kind == "world" and not Collected(m) then
      for _, n in ipairs(type(m.names) == "table" and m.names or {}) do
        if MatchPrepared(want, Prepare(n), false) then
          return true
        end
      end
    end
  end
  return false
end

-- Персонаж из списка — тот, кто сделал запись (имена записи нормализуются один раз на запись).
local whoCache = setmetatable({}, { __mode = "k" })

local function RecordBy(ch, rec)
  local keys = type(ch.keys) == "table" and ch.keys or {}
  local who = whoCache[rec]
  if not who then
    who = { [Norm(rec.char)] = true }
    if rec.realm then
      who[Norm(tostring(rec.char) .. "-" .. tostring(rec.realm))] = true
    end
    whoCache[rec] = who
  end
  for _, k in ipairs(keys) do
    if who[k] then
      return true
    end
  end
  return false
end

-- Строки персонажей по маунту, один раз на список из таблицы: [kind|маунт] = { { ch, item }, … }
-- (у персонажа — первая такая строка). Без него вкладка «Попытки» нормализовала бы имена сотни тысяч раз.
local function MountIndex(data)
  if not data._byMount then
    local index = {}
    for _, ch in ipairs(type(data.chars) == "table" and data.chars or {}) do
      local seen = {}
      for _, item in ipairs(type(ch) == "table" and type(ch.items) == "table" and ch.items or {}) do
        if type(item) == "table" then
          local k = tostring(item.kind) .. "|" .. Norm(item.mount)
          if not seen[k] then
            seen[k] = true
            index[k] = index[k] or {}
            index[k][#index[k] + 1] = { ch = ch, item = item }
          end
        end
      end
    end
    data._byMount = index
  end
  return data._byMount
end

-- Сколько персонажей из таблицы ещё не убивали этого босса на этой неделе (строка с этим маунтом у них есть).
local function CharactersLeft(m, data, kills, known, weekEnd)
  local resetT = ResetTime(kills, weekEnd)
  local afterReset = resetT ~= nil and (not tonumber(data.generated) or tonumber(data.generated) < resetT)
  local left = 0
  for _, e in ipairs(MountIndex(data)[tostring(m.kind) .. "|" .. Norm(m.mount)] or {}) do
    local done = e.item.y and not afterReset
    if not done and weekEnd then
      for _, rec in ipairs(kills) do
        if rec.weekEnd == weekEnd and RecordBy(e.ch, rec) and Fits(e.item, rec, RecordName(rec), known) then
          done = true
          break
        end
      end
    end
    if not done then
      left = left + 1
    end
  end
  return left
end

-- Подсказка юнита обновляется много раз, пока на НИП наведена мышь: ответ по имени запоминаем, пока не
-- поменялись список из таблицы, журнал убийств, коллекция или неделя (иначе на каждый показ — проход по журналу).
local hintCache, hintStamp = {}, {}
local hintCount = 0

local function SameStamp(data, kills, weekEnd, collected)
  local last = kills[#kills]
  return hintStamp.data == data and hintStamp.generated == data.generated and hintStamp.kills == #kills
    and hintStamp.last == last and hintStamp.weekEnd == weekEnd and hintStamp.collected == collected
end

-- Подсказка о боссе по имени из игры (подсказка юнита, Атлас приключений):
-- { { name, n, p, left } … } — несобранные маунты со счётчиком, которые с него падают.
function ns.BossHint(bossName)
  local data = AttemptRows()
  if not data or type(bossName) ~= "string" or bossName == "" then
    return {}
  end
  local db0 = ns.GetDB()
  local kills0 = db0 and db0.kills or {}
  local weekEnd0 = ns.WeekEnd and ns.WeekEnd()
  local collected = db0 and type(db0.collection) == "table" and db0.collection.collected or nil
  if not SameStamp(data, kills0, weekEnd0, collected) then
    hintCache, hintCount = {}, 0
    hintStamp = { data = data, generated = data.generated, kills = #kills0, last = kills0[#kills0], weekEnd = weekEnd0,
                  collected = collected }
  end
  local cached = hintCache[bossName]
  if cached then
    return cached
  end
  local out = ns.BossHintNow(bossName, data)
  if hintCount < 200 then   -- имён за неделю немного; на всякий случай не копим без конца
    hintCache[bossName] = out
    hintCount = hintCount + 1
  end
  return out
end

function ns.BossHintNow(bossName, data)
  local want = Prepare(Norm(bossName))
  local db = ns.GetDB()
  local kills = db and db.kills or {}
  local known = KnownIds(data)
  local weekEnd = ns.WeekEnd and ns.WeekEnd()
  local out = {}
  for _, m in ipairs(data.mounts) do
    local p = type(m) == "table" and tonumber(m.p)
    if p and p > 0 and p < 1 and not Collected(m) then
      local named = false
      for _, n in ipairs(type(m.names) == "table" and m.names or {}) do
        if MatchPrepared(want, Prepare(n), false) then
          named = true
          break
        end
      end
      if named then
        out[#out + 1] = { name = MountName(m), kind = m.kind, n = CountNow(m, data, kills, known), p = p,
          left = CharactersLeft(m, data, kills, known, weekEnd), diff = m.diff }
      end
    end
  end
  return out
end

-- Вкладка «Попытки»: несобранные маунты со счётчиком { name, n, p, left, diff, kind, boss }.
-- Пересчёт — только когда поменялись список, журнал, неделя или коллекция (тот же признак, что у подсказки).
local attemptsStamp, attemptsResult

function ns.GetAttemptsList()
  local data = AttemptRows()
  if not data then
    return {}
  end
  local db = ns.GetDB()
  local kills = db and db.kills or {}
  local known = KnownIds(data)
  local weekEnd = ns.WeekEnd and ns.WeekEnd()
  local last = kills[#kills]
  local stamp = table.concat({ tostring(data), tostring(data.generated), #kills, last and tostring(last.id) or "",
    tostring(weekEnd), tostring(db and type(db.collection) == "table" and db.collection.collected) }, "|")
  if stamp == attemptsStamp then
    return attemptsResult
  end
  local out = {}
  for _, m in ipairs(data.mounts) do
    local p = type(m) == "table" and tonumber(m.p)
    if p and p > 0 and p < 1 and not Collected(m) then
      local n = CountNow(m, data, kills, known)
      out[#out + 1] = { name = MountName(m), n = n, p = p, left = CharactersLeft(m, data, kills, known, weekEnd), diff = m.diff, kind = m.kind, boss = m.boss }
    end
  end
  table.sort(out, function(a, b)
    if a.n * a.p > b.n * b.p then
      return true
    elseif a.n * a.p < b.n * b.p then
      return false
    else
      return a.name < b.name
    end
  end)
  attemptsStamp, attemptsResult = stamp, out
  return out
end

---------------------------------------------------------------------------
-- «Не фармлю», которое таблица ещё не видела
---------------------------------------------------------------------------
-- Отметки этого персонажа новее списка из таблицы (старые таблица уже учла — их убираем).
function ns.NotFarmed(charKey, generated)
  local db = ns.GetDB()
  local all = db and type(db.notFarmed) == "table" and db.notFarmed or nil
  local mine = all and charKey and all[charKey]
  if type(mine) ~= "table" then
    return {}
  end
  local keep = {}
  for _, e in ipairs(mine) do
    if type(e) == "table" and (not generated or (tonumber(e.time) or 0) >= generated) then
      keep[#keep + 1] = e
    end
  end
  if #keep ~= #mine then
    all[charKey] = #keep > 0 and keep or nil
  end
  return keep
end

function ns.IsNotFarmed(hidden, item)
  for _, e in ipairs(hidden) do
    if e.list == item.kind then
      if e.scope == "place" then
        if e.place and Norm(e.place) == Norm(item.place) then
          return true
        end
      elseif Norm(e.boss) == Norm(item.boss) and Norm(e.mount) == Norm(item.mount)
        and (not e.diff or Norm(e.diff) == Norm(item.diff)) then
        return true
      end
    end
  end
  return false
end

---------------------------------------------------------------------------
-- Текст для окна и чата
---------------------------------------------------------------------------
function ns.KindTitle(kind)
  return L["kind_" .. kind]
end
ns.KIND_ORDER = { "raid", "dungeon", "world" }

-- «|» в тексте ячейки игра приняла бы за код цвета — удваиваем его.
local function Safe(value)
  if value == nil then
    return ""
  end
  return (tostring(value):gsub("|", "||"))
end

local DONE_ICON = "|TInterface\\RaidFrame\\ReadyCheck-Ready:12:12|t "

function ns.FormatRemaining(entry)
  local item = entry.item
  local boss = Safe(item.boss) ~= "" and Safe(item.boss) or "?"
  local diff = Safe(item.diff)
  if entry.done then
    return DONE_ICON .. "|cff8c8c8c" .. boss .. (diff ~= "" and (" (" .. diff .. ")") or "")
      .. L.left_done .. "|r"
  end
  local text = boss
  if diff ~= "" then
    text = text .. " |cff8c8c8c(" .. diff .. ")|r"
  end
  if Safe(item.mount) ~= "" then
    text = text .. " — |cffc9a0ff" .. Safe(item.mount) .. "|r"
  end
  if Safe(item.place) ~= "" then
    text = text .. " |cff8c8c8c- " .. Safe(item.place) .. "|r"
  end
  return text
end

-- «сегодня в 21:14», «вчера в 21:14», «14.10 в 21:14»
function ns.FormatWhen(stamp)
  if not stamp then
    return "?"
  end
  local day, today = date("%Y%m%d", stamp), date("%Y%m%d", time())
  local clock = date("%H:%M", stamp)
  if day == today then
    return string.format(L.when_today, clock)
  elseif day == date("%Y%m%d", time() - 86400) then
    return string.format(L.when_yesterday, clock)
  end
  return string.format(L.when_date, date("%d.%m", stamp), clock)
end

-- Пояснение под заголовком списка: неделя сброшена после списка или ещё не сброшена.
function ns.RemainingNote(r)
  if r.afterReset then
    return L.note_after_reset
  elseif r.notReset then
    return L.note_not_reset
  end
  return nil
end

function ns.PrintRemaining()
  local r = ns.GetRemaining()
  if r.state == "nodata" then
    ns.Print(L.print_nodata)
    ns.Print(string.format(L.print_getprogram, ns.PROGRAM_URL))
    return
  end
  if r.state == "nochar" then
    ns.Print(string.format(L.print_nochar, Safe(r.name or "?")))
    return
  end
  ns.Print(string.format(L.print_left, Safe(r.name), r.left, r.total, ns.FormatWhen(r.generated)))
  local note = ns.RemainingNote(r)
  if note then
    ns.Print(note)
  end
  local shown = 0
  for _, entry in ipairs(r.items) do
    if not entry.done then
      shown = shown + 1
      if shown > 25 then
        DEFAULT_CHAT_FRAME:AddMessage("   " .. string.format(L.print_more, r.left - 25))
        break
      end
      DEFAULT_CHAT_FRAME:AddMessage("   " .. ns.FormatRemaining(entry))
    end
  end
end
