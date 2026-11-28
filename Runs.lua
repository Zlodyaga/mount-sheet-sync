--[[
Mount Sheet Sync — сколько длится заход в подземелье (3.0.7)

Оценка — по заходам самого игрока (решение пользователя: общие оценки из гайдов не годятся). Заход: от входа в
подземелье, где есть несобранный маунт из списка, до убийства последнего такого босса. Убиты все такие боссы — заход
закрыт; вышел раньше — в заход идёт время до последнего убитого такого босса. Вернулся в то же подземелье за 10 минут
(/reload, вылет, вышел и зашёл) — тот же заход. Без убийств заход не хранится.

db.runOpen — идущий заход; db.runs[<номер подземелья в путеводителе>] — последние 20: { start, secs, char, diff,
kills, long, off, on }. long — дольше 2 ч: в среднее не идёт, пока игрок не вернёт (on); off — игрок сам убрал.
Строка при входе (3.0.8, ns.EnterNote): «Здесь маунт: Непобедимый (Король-лич, 1%) - попытка 87» — маунты этого подземелья
на текущей сложности, один раз на вход (db.enterSeen: та же карта за 10 мин — молчим); выкл. — «Не писать в чат» или
галочка «При входе в подземелье - его маунты» (db.enterNote = false).

Среднее — ns.RunAverage(inst, d): d — сложности строки (item.d, номера DifficultyID), заходы на других сложностях в него
не идут (25 героический и 10 обычный в ЦЛК — разное время); заход без известной сложности идёт в любое.
Сложность при входе игра называет не сразу (GetInstanceInfo даёт 0): тогда её спрашиваем ещё раз через DIFF_RETRY сек
(LaterDiff, до DIFF_TRIES раз) и берём из ENCOUNTER_END; строка «Здесь маунт» ждёт её. 0 в старых заходах (3.0.8) — «неизвестна». Правка — правый щелчок по строке «Попыток» > «Заходы» (UI.Rows.lua).
]]

local _, ns = ...
local L = ns.L

local RESUME = 600       -- секунд: вернулся в то же подземелье — тот же заход
local DIFF_RETRY, DIFF_TRIES = 2, 5   -- сложность при входе ещё 0: спросить снова через 2 сек, не больше 5 раз
local LONG = 2 * 3600    -- дольше — «долгий»
local KEEP = 20          -- заходов на подземелье

local function DB()
  return ns.GetDB and ns.GetDB()
end

local function Secret(v)
  return issecretvalue and issecretvalue(v)
end

-- карта подземелья (8-е значение GetInstanceInfo) и бой (ENCOUNTER_END) → путеводитель, по журналу аддона
local journalFor, instByMap, byEncounter

local function Journal()
  local db = DB()
  local journal = db and db.journal
  if type(journal) ~= "table" then
    return false
  end
  if journalFor ~= journal then
    journalFor, instByMap, byEncounter = journal, {}, {}
    for _, i in ipairs(journal.instances or {}) do
      if i.map then
        instByMap[i.map] = i.id
      end
    end
    for _, e in ipairs(journal.encounters or {}) do
      if e.id then
        byEncounter[e.id] = e
      end
    end
  end
  return true
end

-- бои с несобранным маунтом из списка в подземелье inst: { [бой в путеводителе] = true }, сколько их
local function MountBosses(inst)
  local out, n = {}, 0
  for _, m in ipairs(ns.GetAttemptsList and ns.GetAttemptsList() or {}) do
    local je = tonumber(m.je)
    if je and not out[je] and ns.ItemInstance(m) == inst then
      out[je] = true
      n = n + 1
    end
  end
  return out, n
end

-- заход на одной из сложностей строки (d пусто — любая; сложность захода неизвестна — тоже)
local function DiffFits(r, d)
  local id = tonumber(r.diff)
  if type(d) ~= "table" or #d == 0 or not id or id <= 0 then   -- 0: игра ещё не знала сложность (заходы 3.0.8)
    return true
  end
  for _, x in ipairs(d) do
    if tonumber(x) == id then
      return true
    end
  end
  return false
end

local NOTE_MAX = 3   -- маунтов в строке, остальные — «и ещё N»

--- Маунты подземелья inst на сложности diff: строка для чата или nil (нет таких).
function ns.EnterNoteText(inst, diff)
  local parts, more = {}, 0
  for _, m in ipairs(ns.GetAttemptsList and ns.GetAttemptsList() or {}) do
    if ns.ItemInstance(m) == inst and DiffFits({ diff = diff }, (m.raw or m).d) then
      if #parts < NOTE_MAX then
        parts[#parts + 1] = string.format(L.enter_item, tostring(m.name), tostring(m.boss or "?"),
          ns.FormatDropChance(m.p) or "?", (tonumber(m.n) or 0) + 1)
      else
        more = more + 1
      end
    end
  end
  if #parts == 0 then
    return nil
  end
  local text = string.format(L.enter_note, table.concat(parts, "; "))
  if more > 0 then
    text = text .. " " .. string.format(L.enter_more, more)
  end
  return text
end

local function EnterNote(db, inst, map, diff, now)
  if db.quiet or db.enterNote == false then
    return
  end
  local last = type(db.enterSeen) == "table" and db.enterSeen or nil
  if last and last.map == map and now - (tonumber(last.t) or 0) <= RESUME then
    last.t = now   -- /reload, вылет, вышел и зашёл — уже сказано
    return
  end
  db.enterSeen = { map = map, t = now }
  local text = ns.EnterNoteText(inst, diff)
  if text and ns.Print then
    ns.Print(text)
  end
end

local function Close(db)
  local run = db.runOpen
  db.runOpen = nil
  if type(run) ~= "table" or not run.last or not run.inst then
    return
  end
  local secs = run.last - run.start
  if secs <= 0 then
    return
  end
  db.runs = type(db.runs) == "table" and db.runs or {}
  local key = tostring(run.inst)
  local list = type(db.runs[key]) == "table" and db.runs[key] or {}
  list[#list + 1] = { start = run.start, secs = secs, char = run.char, diff = run.diff, kills = run.kills,
                      long = secs > LONG or nil }
  while #list > KEEP do
    table.remove(list, 1)
  end
  db.runs[key] = list
end

local function Where()
  local _, kind, diff, _, _, _, _, map = GetInstanceInfo()
  if Secret(kind) or Secret(map) or (kind ~= "party" and kind ~= "raid") then
    return nil
  end
  map = tonumber(map)
  if not map or not Journal() then
    return nil
  end
  local inst = instByMap[map]
  diff = not Secret(diff) and tonumber(diff) or nil
  return inst, map, diff and diff > 0 and diff or nil   -- 0 — сразу после входа игра сложность ещё не знает
end
ns.RunsWhere = Where   -- /mss check: в каком подземелье путеводителя игрок сейчас

-- Сложность не известна при входе: спросить игру ещё раз чуть позже; узнали (или так и нет) — в заход и строка «Здесь маунт».
local function LaterDiff(db, inst, map, tries)
  C_Timer.After(DIFF_RETRY, function()
    local inst2, map2, diff = Where()
    if inst2 ~= inst or map2 ~= map then
      return   -- уже вышел
    end
    if not diff and tries < DIFF_TRIES then
      LaterDiff(db, inst, map, tries + 1)
      return
    end
    local run = db.runOpen
    if diff and type(run) == "table" and run.map == map and not run.diff then
      run.diff = diff
    end
    EnterNote(db, inst, map, diff, time())
  end)
end

--- Вход в зону / подземелье (PLAYER_ENTERING_WORLD, ZONE_CHANGED_NEW_AREA).
function ns.RunsOnZone()
  local db = DB()
  if not db then
    return
  end
  local now = time()
  local run = type(db.runOpen) == "table" and db.runOpen or nil
  local inst, map, diff = Where()
  if inst then
    if diff or not C_Timer then
      EnterNote(db, inst, map, diff, now)
    else
      LaterDiff(db, inst, map, 1)
    end
    if run and run.map == map and (not run.left or now - run.left <= RESUME) then
      run.left = nil   -- тот же заход
      return
    end
    if run then
      Close(db)
    end
    local need, n = MountBosses(inst)
    if n > 0 then
      db.runOpen = { inst = inst, map = map, diff = diff, start = now, char = UnitName and UnitName("player") or nil,
                     kills = 0, need = need, got = {} }
    end
  elseif run then
    if not run.left then
      run.left = now
    elseif now - run.left > RESUME then
      Close(db)
    end
  end
end

--- Убит босс (ENCOUNTER_END, успех): босс с маунтом в идущем заходе — время до него идёт в заход.
function ns.RunsOnKill(encounterID, difficultyID)
  local db = DB()
  local run = db and type(db.runOpen) == "table" and db.runOpen or nil
  if not run or run.left or Secret(encounterID) or not Journal() then
    return
  end
  local diff = not Secret(difficultyID) and tonumber(difficultyID) or nil
  if not run.diff and diff and diff > 0 then
    run.diff = diff   -- при входе игра сложность не назвала — бой называет
  end
  local e = byEncounter[tonumber(encounterID) or 0]
  local je = e and e.i == run.inst and e.j
  if not je or type(run.need) ~= "table" or not run.need[je] or (type(run.got) == "table" and run.got[je]) then
    return
  end
  run.got = type(run.got) == "table" and run.got or {}
  run.got[je] = true
  run.last = time()
  run.kills = (run.kills or 0) + 1
  for j in pairs(run.need) do
    if not run.got[j] then
      return
    end
  end
  Close(db)   -- все боссы с маунтом убиты
end

local function Counted(r)
  return not r.off and (not r.long or r.on) and true or false
end
ns.RunCounted = Counted

--- Заходы подземелья (старые — первыми), nil — нет.
function ns.Runs(inst)
  local db = DB()
  local list = inst and db and type(db.runs) == "table" and db.runs[tostring(inst)]
  return type(list) == "table" and #list > 0 and list or nil
end


--- Среднее по учитываемым заходам на сложностях d (item.d; nil — все): секунды, сколько заходов; nil — нет ни одного.
function ns.RunAverage(inst, d)
  local sum, n = 0, 0
  for _, r in ipairs(ns.Runs(inst) or {}) do
    if Counted(r) and tonumber(r.secs) and DiffFits(r, d) then
      sum, n = sum + r.secs, n + 1
    end
  end
  if n == 0 then
    return nil
  end
  return sum / n, n
end

function ns.RunMinutes(secs)
  return math.max(1, math.floor(secs / 60 + 0.5))
end

--- «~12 мин на заход (по 5)» для подсказки строки.
function ns.RunTip(inst, d)
  local avg, n = ns.RunAverage(inst, d)
  return avg and string.format(L.runs_tip, ns.RunMinutes(avg), n) or nil
end

function ns.SetRunCounted(inst, i, yes)
  local r = (ns.Runs(inst) or {})[i]
  if type(r) ~= "table" then
    return
  end
  r.off = not yes or nil
  if r.long then
    r.on = yes or nil
  end
end

function ns.DeleteRun(inst, i)
  local list = ns.Runs(inst)
  if list and list[i] then
    table.remove(list, i)
  end
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("ZONE_CHANGED_NEW_AREA")
frame:RegisterEvent("ENCOUNTER_END")
frame:SetScript("OnEvent", function(_, event, ...)
  local call = ns.SafeCall or pcall
  if event == "ENCOUNTER_END" then
    local encounterID, _, difficultyID, _, success = ...
    if not Secret(success) and (success == 1 or success == true) then
      call(ns.RunsOnKill, encounterID, difficultyID)
    end
  else
    call(ns.RunsOnZone)
  end
end)
