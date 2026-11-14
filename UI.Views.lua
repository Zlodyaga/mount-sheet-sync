--[[
Mount Sheet Sync 3.0.7 — окно: содержимое вкладок
Столбцы таблиц и виды: «Осталось», сетка, «Достижения», «Попытки», «Журнал», «Выпавшие».
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local COLUMN_GAP, CHANCE_BAR_WIDTH, GRID_SCROLL, GRID_ALL, MIN_TEXT_WIDTH, L = P.COLUMN_GAP, P.CHANCE_BAR_WIDTH, P.GRID_SCROLL, P.GRID_ALL, P.MIN_TEXT_WIDTH, P.L
local grid, FormatRow, Safe, ShortDiff, ShortBoss, BossAndDiff = P.grid, P.FormatRow, P.Safe, P.ShortDiff, P.ShortBoss, P.BossAndDiff
local PartsText, DONE_TEXTURE, ClassColored, Line, GroupLine = P.PartsText, P.DONE_TEXTURE, P.ClassColored, P.Line, P.GroupLine

---------------------------------------------------------------------------
-- Столбцы таблиц
---------------------------------------------------------------------------
-- spec: { key, w или flex, label — ключ надписи, right — по правому краю, sort, tip, span — заголовок шире столбца }.
local function Columns(spec, width)
  local free, flex = width - 8 - COLUMN_GAP * (#spec - 1), 0
  for _, c in ipairs(spec) do
    if c.flex then
      flex = flex + c.flex
    else
      free = free - c.w
    end
  end
  free = math.max(0, free)
  local out, x = {}, 4
  for i, c in ipairs(spec) do
    local col = {}
    for k, v in pairs(c) do
      col[k] = v
    end
    col.w = c.flex and math.floor(free * c.flex / flex) or c.w
    col.x = x
    x = x + col.w + COLUMN_GAP
    out[i] = col
  end
  return out
end

local attemptsCols, leftCols, leftDoneCols   -- столбцы вкладок: ширина окна задаётся один раз

local function AttemptsColumns()
  if not attemptsCols then
    attemptsCols = Columns({
      { key = "icon", w = 18 },
      { key = "mount", flex = 1, label = "col_mount", sort = "name" },
      { key = "boss", flex = 1.2, label = "col_boss", sort = "boss" },
      { key = "diff", w = 46, label = "col_diff" },
      { key = "n", w = 52, right = true, bold = true, label = "col_attempts", sort = "n" },
      { key = "chance", w = 38, right = true, bold = true, label = "col_chance", sort = "p", tip = "tip_col_chance", span = CHANCE_BAR_WIDTH + COLUMN_GAP },
      { key = "bar", w = CHANCE_BAR_WIDTH },
      { key = "left", w = 56, right = true, bold = true, label = "col_left", sort = "left", tip = "tip_col_left" },
    }, S.listWidth)
  end
  return attemptsCols
end

local function LeftColumns()
  if not leftCols then
    leftCols = Columns({
      { key = "icon", w = 18 },
      { key = "boss", flex = 1.15, label = "col_boss" },
      { key = "diff", w = 46, label = "col_diff" },
      { key = "mount", flex = 1.25, label = "col_mount" },
      { key = "place", flex = 0.9, label = "col_where" },
      { key = "n", w = 52, right = true, bold = true, label = "col_attempts" },
      { key = "chance", w = 40, right = true, bold = true, label = "col_chance", tip = "tip_col_chance" },
    }, S.listWidth)
    -- убитая строка: те же босс и маунт, дальше одна ячейка «убит, ждёт таблицы» (место — в подсказке)
    local place, last = leftCols[5], leftCols[7]
    leftDoneCols = { leftCols[1], leftCols[2], leftCols[3], leftCols[4],
      { key = "status", x = place.x, w = last.x + last.w - place.x, right = true } }
  end
  return leftCols, leftDoneCols
end

-- Сетка: сколько столбцов показать и где они начинаются; лишние персонажи — за полосой прокрутки.
local function GridColumns(count)
  local view = ns.GetDB().gridView == "all" and GRID_ALL or GRID_SCROLL
  local fixed = 8 + 18 + 40 + 40 + COLUMN_GAP * 4
  local shown = math.min(count, view.visible)
  while shown > 1 and S.listWidth - fixed - shown * view.cell < MIN_TEXT_WIDTH do
    shown = shown - 1
  end
  local cols = Columns({
    { key = "icon", w = 18 },
    { key = "boss", flex = 1, label = "col_boss" },
    { key = "diff", w = 40, label = "col_diff" },
    { key = "mount", flex = 1.3, label = "col_mount" },
    { key = "grid", w = shown * view.cell },
    { key = "total", w = 40, right = true, bold = true, label = "col_total", tip = "tip_col_total" },
  }, S.listWidth)
  grid.shown, grid.cell, grid.letters = shown, view.cell, view.letters
  grid.x = cols[5].x
  grid.overflow = count > shown
  return cols
end

-- Цвет шанса: от 63 % (ожидаемое выпадение уже прошло) — оранжевый, от 30 % — жёлтый.
local function ChanceColor(n, p)
  local v = 1 - (1 - p) ^ n
  if v >= 0.63 then
    return "ff9a3c", { 1, 0.6, 0.24 }, v
  elseif v >= 0.3 then
    return "ffd24a", { 1, 0.82, 0.29 }, v
  end
  return "c8c2b4", { 0.78, 0.76, 0.71 }, v
end

---------------------------------------------------------------------------
-- Содержимое вкладок: { list, empty, url, summary, cols, attempts, grid }
---------------------------------------------------------------------------
-- Вкладка «Журнал»: записи аддона, новые сверху.
local function JournalView(db)
  local list = {}
  for i = #db.kills, 1, -1 do
    list[#list + 1] = Line(FormatRow(db.kills[i]))
  end
  return { list = list, empty = L.journal_empty, log = true }
end

local NO_DATA = {
  list = {},
  url = true,
}

local function NoDataView()
  NO_DATA.empty = L.left_nodata .. "\n\n" .. L.left_getprogram
  return NO_DATA
end

-- ID маунта, которого знает игра (у него есть значок): только такой можно открыть в журнале.
local function KnownMount(id)
  return ns.MountIcon(id) and tonumber(id) or nil
end

-- Куда ведут щелчки по строке: путеводитель и путь к входу в рейд или подземелье, а к мировому боссу — метка, где его показывает игра.
local function WithPlace(elem, item)
  elem.instance = ns.ItemInstance(item)
  elem.encounter = tonumber(item.je)
  elem.world = item.kind == "world" or nil
  elem.spot = item.kind == "rare" and ns.RARE_SPOTS and ns.RARE_SPOTS[tonumber(item.id)] or nil
  elem.route = (elem.instance and (item.kind == "raid" or item.kind == "dungeon")) or (elem.world and elem.encounter ~= nil)
    or elem.spot ~= nil or nil
  elem.placeName = ns.Shown(item, "place")
  elem.bossName = ns.Shown(item, "boss")
  return elem
end

-- Строка таблицы у персонажа: осталось — со счётчиком попыток, убита — серая, с галочкой.
local function RemainingRow(entry, menu, stripe)
  local cols, doneCols = LeftColumns()
  local item = entry.item
  local tip = { Safe(ns.Shown(item, "mount")), BossAndDiff(ns.Shown(item, "boss"), item.diff) }
  if Safe(ns.Shown(item, "place")) ~= "" then
    tip[#tip + 1] = Safe(ns.Shown(item, "place"))
  end
  local n, p, id = ns.ItemAttempts(item)
  if entry.done then
    local status = (entry.again and string.format(L.left_again, ns.FormatDuration(entry.again)) or L.left_done):gsub("^%s*—%s*", "")
    return WithPlace({ cols = doneCols, done = true, icon = DONE_TEXTURE, mountID = KnownMount(item.id or id), stripe = stripe, tip = tip, values = {
      boss = "|cff8c8c8c" .. ShortBoss(ns.Shown(item, "boss")) .. "|r",
      diff = "|cff8c8c8c" .. ShortDiff(item.diff) .. "|r",
      mount = "|cff8c8c8c" .. Safe(ns.Shown(item, "mount")) .. "|r",
      status = "|cff8c8c8c" .. status .. "|r",
    } }, item)
  end
  local diff = "|cffb7ae9a" .. ShortDiff(item.diff) .. "|r"
  if entry.killed then
    diff = PartsText(entry.killed, "ffd9d3c4")
  end
  local chance = "|cff8c8c8c-|r"
  if n and n > 0 then
    chance = "|cff" .. ChanceColor(n, p) .. ns.FormatChance(n, p) .. "|r"
    tip[#tip + 1] = string.format(L.boss_hint, n, ns.FormatChance(n, p))
  end
  return WithPlace({ cols = cols, icon = ns.MountIcon(item.id or id), mountID = KnownMount(item.id or id), stripe = stripe, tip = tip, entry = menu and entry or nil, values = {
    boss = ShortBoss(ns.Shown(item, "boss")),
    diff = diff,
    mount = "|cffc9a0ff" .. Safe(ns.Shown(item, "mount")) .. "|r",
    place = "|cff8c8c8c" .. Safe(ns.Shown(item, "place")) .. "|r",
    n = n and n > 0 and tostring(n) or "|cff8c8c8c-|r",
    chance = chance,
  } }, item)
end

-- Вкладка «Осталось» у персонажа: строки «Нет» из таблицы, убитые после обновления списка — с галочкой внизу группы.
-- own — список текущего персонажа: только у него правый щелчок «Не фармлю».
local function RemainingView(r, own)
  if r.state == "nodata" then
    return NoDataView()
  end
  if r.state == "nochar" then
    return { list = {}, empty = string.format(L.left_nochar, r.name or "?") }
  end
  local list = {}
  local note = ns.RemainingNote(r)
  if note then
    list[#list + 1] = Line((r.afterReset and "|cff8cff8c" or "|cffff9a4d") .. note .. "|r")
  end
  for _, kind in ipairs(ns.KIND_ORDER) do
    local group = {}
    for _, entry in ipairs(r.items) do
      if kind ~= "reward" and ns.GroupOf(entry.item) == kind then   -- достижения — на своей вкладке
        group[#group + 1] = entry
      end
    end
    if #group > 0 then
      list[#list + 1] = GroupLine(kind)
      local stripe = false
      for pass = 1, 2 do   -- сначала то, что осталось, потом убитые
        for _, entry in ipairs(group) do
          if entry.done == (pass == 2) then
            local menu = own and kind ~= "world" and kind ~= "rare" and kind ~= "guaranteed" and kind ~= "mobs" and not entry.done
            list[#list + 1] = RemainingRow(entry, menu, stripe)
            stripe = not stripe
          end
        end
      end
    end
  end
  if r.total == 0 then
    list[#list + 1] = Line("|cff8c8c8c" .. L.left_norows .. "|r")
  elseif r.left == 0 then
    list[#list + 1] = Line("|cff8cff8c" .. L.left_alldone .. "|r")
  end
  return { list = list, cols = LeftColumns(),
    summary = "|cff8c8c8c" .. string.format(L.left_head, r.left, r.total, ns.FormatWhen(r.generated)) .. "|r" }
end

-- Вкладка «Достижения»: достижение (щелчок — открыть его), условие, маунт. Полученное — серое и внизу. Попыток у них нет.
local achCols
local function AchievementColumns()
  if not achCols then
    achCols = Columns({
      { key = "icon", w = 18 },
      { key = "boss", flex = 1.3, label = "col_ach" },
      { key = "cond", flex = 1.6, label = "col_cond" },
      { key = "mount", flex = 1.4, label = "col_mount" },
    }, S.listWidth)
  end
  return achCols
end

-- Достижение из игры: получено ли (на аккаунте) и его условие на языке игры. nil — игра его не знает.
local function AchievementInfo(id)
  if not id or not GetAchievementInfo then
    return nil
  end
  local ok, _, name, _, completed, _, _, _, description = pcall(GetAchievementInfo, id)
  if not ok or not name then
    return nil
  end
  return { completed = completed and true or false, description = type(description) == "string" and description or "" }
end

local ACH_GROUPS = { "raid", "dungeon", "mplus", "other" }

local function AchievementGroupTitle(group)
  if group == "mplus" then
    return L.ach_group_mplus
  elseif group == "other" then
    return L.ach_group_other
  end
  return ns.KindTitle(group)
end

-- Достижение получено: оно есть на аккаунте, маунт уже собран («Собрано?») или есть в коллекции.
local function AchievementDone(item, info)
  -- у маунта за седло достижение — нынешнее, на седло: получено ли оно, о маунте не говорит
  if (info and info.completed and item.via ~= "saddle") or item.got then
    return true
  end
  local id = tonumber(item.id)
  if id and C_MountJournal and C_MountJournal.GetMountInfoByID then
    local ok, _, _, _, _, _, _, _, _, _, _, collected = pcall(C_MountJournal.GetMountInfoByID, id)
    return ok and collected == true
  end
  return false
end

local function AchievementsView(rows)
  if not rows then
    return NoDataView()
  end
  if #rows == 0 then
    return { list = {}, empty = L.ach_empty }
  end
  local cols = AchievementColumns()
  local groups = {}
  for _, item in ipairs(rows) do
    local info = AchievementInfo(tonumber(item.ach))
    local group = item.group or (item.kind == "raid" and "raid" or "dungeon")
    groups[group] = groups[group] or {}
    local saddle = item.via == "saddle"
    -- маунт прошлого сезона: «Затерянное во времени седло», а чьей наградой он был — в подсказке
    local was = saddle and Safe(ns.Shown(item, "boss")):match("^(.-)%s+/%s+") or nil
    table.insert(groups[group], { item = item, info = info, saddle = saddle, was = was,
      name = saddle and L.ach_saddle or Safe(ns.Shown(item, "boss")), done = AchievementDone(item, info) })
  end
  local list, earned, total = {}, 0, 0
  for _, group in ipairs(ACH_GROUPS) do
    local items = groups[group]
    if items then
      table.sort(items, function(a, b)
        if a.saddle ~= b.saddle then
          return not a.saddle   -- сначала достижения этого сезона, за ними маунты за седло
        end
        if a.done ~= b.done then
          return not a.done   -- ещё не полученные — сверху
        end
        if a.name ~= b.name then
          return a.name < b.name
        end
        return Safe(ns.Shown(a.item, "mount")) < Safe(ns.Shown(b.item, "mount"))
      end)
      list[#list + 1] = { text = "|cffffd200" .. AchievementGroupTitle(group) .. "|r", group = true }
      for i, a in ipairs(items) do
        local item = a.item
        local cond = ns.ConditionText(tonumber(item.ach), a.info and a.info.description ~= "" and a.info.description or nil) or Safe(item.diff)
        local mount = Safe(ns.Shown(item, "mount"))
        total = total + 1
        if a.done then
          earned = earned + 1
        end
        local tip = { mount, a.name, Safe(cond) }
        if a.done then
          tip[#tip + 1] = "|cff8cff8c" .. L.ach_done .. "|r"
        end
        if a.was then
          tip[#tip + 1] = "|cff8c8c8c" .. string.format(L.ach_saddle_was, a.was) .. "|r"
        end
        list[#list + 1] = { cols = cols, icon = ns.MountIcon(item.id), mountID = KnownMount(item.id), achievement = tonumber(item.ach),
          stripe = i % 2 == 0, tip = tip, values = {
            boss = a.done and ("|cff8c8c8c" .. a.name .. "|r") or a.name,
            cond = "|cff" .. (a.done and "8c8c8c" or "b7ae9a") .. Safe(cond) .. "|r",
            mount = "|cff" .. (a.done and "8c8c8c" or "c9a0ff") .. mount .. "|r",
          } }
      end
    end
  end
  return { list = list, cols = cols, summary = "|cff8c8c8c" .. string.format(L.ach_head, total, earned) .. "|r" }
end

-- Вкладка «Выпавшие» (ns.GetDrops): маунт, босс, с какой попытки, когда и кому.
local dropCols

function UI.DropsView(rows)
  if not rows then
    return NoDataView()
  end
  if #rows == 0 then
    return { list = {}, empty = L.drops_empty }
  end
  dropCols = dropCols or Columns({
    { key = "icon", w = 18 },
    { key = "mount", flex = 1.3, label = "col_mount" },
    { key = "boss", flex = 1.3, label = "col_boss" },
    { key = "n", w = 64, right = true, bold = true, label = "col_attempts" },
    { key = "chance", w = 46, right = true, label = "col_drop_chance" },
    { key = "when", w = 84, right = true, label = "col_when" },
    { key = "who", flex = 0.7, label = "col_who" },
  }, S.listWidth)
  local cols, list = dropCols, {}
  for i, d in ipairs(rows) do
    list[#list + 1] = { cols = cols, icon = ns.MountIcon(d.id), mountID = KnownMount(d.id), stripe = i % 2 == 0,
      tip = { Safe(d.name), Safe(d.boss) }, values = {
        mount = "|cffc9a0ff" .. Safe(d.name) .. "|r",
        boss = Safe(d.boss),
        n = d.n and d.n > 0 and tostring(d.n) or "-",
        chance = ns.FormatDropChance(d.p) or "",
        when = d.time and d.time > 0 and (date or os.date)("%d.%m.%Y", d.time) or "-",
        who = Safe(d.char),
      } }
  end
  return { list = list, cols = cols, summary = "|cff8c8c8c" .. string.format(L.drops_head, #rows) .. "|r" }
end

-- Персонажи для столбцов сетки: имя, цвет класса (без класса — серый: персонаж другого аккаунта).
local function GridChars(all)
  local chars = {}
  for i, c in ipairs(all) do
    local color = c.classFile and RAID_CLASS_COLORS and RAID_CLASS_COLORS[c.classFile]
    chars[i] = { name = c.name, classFile = c.classFile, colored = ClassColored(c.name, c.classFile),
      hex = color and color.colorStr or "ffd9d9d9",
      r = color and color.r or 0.85, g = color and color.g or 0.85, b = color and color.b or 0.85 }
  end
  return chars
end

-- «Все персонажи»: строка таблицы и столбец на каждого персонажа.
local function GridView(rows, all)
  grid.chars = GridChars(all)
  local cols = GridColumns(#all)
  grid.offset = math.min(grid.offset, math.max(0, #all - grid.shown))
  local list, kind, stripe = {}, nil, false
  local toKill, attempts = 0, 0
  for _, row in ipairs(rows) do
    if row.kind ~= "reward" then
      toKill = toKill + 1
      attempts = attempts + row.total
    end
    if row.kind ~= kind then
      kind = row.kind
      stripe = false
      list[#list + 1] = GroupLine(kind)
    end
    local item = row.item
    local names, shownNames = {}, {}
    for i, name in ipairs(row.names) do
      names[name] = true
      shownNames[i] = Safe(name)
      local killed = row.killed and row.killed[name]
      if killed then
        local left = {}
        for _, p in ipairs(ns.PARTS) do
          if not killed[p] then
            left[#left + 1] = p
          end
        end
        shownNames[i] = shownNames[i] .. " (" .. table.concat(left, ", ") .. ")"
      end
    end
    local _, _, id = ns.ItemAttempts(item)
    local tip = { Safe(ns.Shown(item, "mount")), BossAndDiff(ns.Shown(item, "boss"), item.diff), table.concat(shownNames, ", ") }
    local killedThisWeek, notFarming = {}, {}
    for _, c in ipairs(all) do
      if row.done and row.done[c.name] then
        killedThisWeek[#killedThisWeek + 1] = Safe(c.name)
      elseif not names[c.name] then
        notFarming[#notFarming + 1] = Safe(c.name)
      end
    end
    if #killedThisWeek > 0 then
      tip[#tip + 1] = "|cff8c8c8c" .. string.format(L.grid_tip_done, table.concat(killedThisWeek, ", ")) .. "|r"
    end
    if #notFarming > 0 then
      tip[#tip + 1] = "|cff8c8c8c" .. string.format(L.grid_tip_none, table.concat(notFarming, ", ")) .. "|r"
    end
    list[#list + 1] = WithPlace({ cols = cols, names = names, killed = row.killed or false, done = row.done or false, icon = ns.MountIcon(item.id or id), mountID = KnownMount(item.id or id), stripe = stripe,
      tip = tip, values = {
        boss = ShortBoss(ns.Shown(item, "boss")),
        diff = "|cffb7ae9a" .. ShortDiff(item.diff) .. "|r",
        mount = "|cffc9a0ff" .. Safe(ns.Shown(item, "mount")) .. "|r",
        total = "|cffd8c98f" .. row.total .. "|r",
      } }, item)
    stripe = not stripe
  end
  if #rows == 0 then
    list[#list + 1] = Line("|cff8cff8c" .. L.all_empty .. "|r")
  end
  return { list = list, cols = #rows > 0 and cols or nil, grid = #rows > 0,
    summary = "|cff8c8c8c" .. string.format(L.all_head, toKill, attempts, #all) .. "|r" }
end

-- Сортировка и фильтры вкладки «Попытки» (запоминаются): db.attemptsView.
local function AttemptsState()
  local db = ns.GetDB()
  if type(db.attemptsView) ~= "table" then
    db.attemptsView = {}
  end
  local st = db.attemptsView
  st.sort = st.sort or "p"
  st.kind = st.kind or "all"
  if st.desc == nil then
    st.desc = true
  end
  return st
end

local function AttemptsSorted(rows, st)
  local out = {}
  for _, m in ipairs(rows) do
    if (st.kind == "all" or m.kind == st.kind) and not (st.hideZero and m.n == 0) then
      out[#out + 1] = m
    end
  end
  local function value(m)
    if st.sort == "name" or st.sort == "boss" then
      return tostring(m[st.sort] or "")
    elseif st.sort == "p" then
      return m.n * m.p
    end
    return m[st.sort] or 0
  end
  table.sort(out, function(a, b)
    local x, y = value(a), value(b)
    if x == y then
      return a.name < b.name
    end
    if st.desc then
      return x > y
    end
    return x < y
  end)
  return out
end

-- Вкладка «Попытки»: маунт, босс, сложность, попыток, шанс с полоской, сколько персонажей ещё могут.
local function AttemptsView(rows, chars)
  if #rows == 0 then
    return { list = {}, empty = L.attempts_empty, attempts = true }
  end
  local cols = AttemptsColumns()
  local sorted = AttemptsSorted(rows, AttemptsState())
  local list = {}
  for i, m in ipairs(sorted) do
    local count = m.n > 0 and string.format(L.boss_hint, m.n, ns.FormatChance(m.n, m.p)) or L.boss_hint_none
    local tip = { Safe(m.name), BossAndDiff(m.boss, m.diff), count }
    if m.left > 0 then
      tip[#tip + 1] = string.format(L.boss_hint_left, m.left)
    end
    local hasTries = m.n > 0
    local color, bar, v = "8c8c8c", nil, nil
    if hasTries then
      color, bar, v = ChanceColor(m.n, m.p)
    end
    list[i] = WithPlace({ cols = cols, icon = ns.MountIcon(m.id), mountID = KnownMount(m.id), stripe = i % 2 == 0, tip = tip, bar = v, barColor = bar, values = {
      mount = "|cffc9a0ff" .. Safe(m.name) .. "|r",
      boss = ShortBoss(m.boss),
      diff = "|cffb7ae9a" .. ShortDiff(m.diff) .. "|r",
      n = hasTries and tostring(m.n) or "|cff8c8c8c-|r",
      chance = "|cff" .. color .. (hasTries and ns.FormatChance(m.n, m.p) or "-") .. "|r",
      left = (m.left > 0 and "|cff7fdc7f" or "|cff8c8c8c") .. m.left .. "/" .. chars .. "|r",
    } }, m)
  end
  return { list = list, cols = cols, attempts = true, count = #sorted }
end

-- нужны следующим файлам окна
P.JournalView, P.RemainingView, P.AchievementsView, P.GridView, P.AttemptsState, P.AttemptsView = JournalView, RemainingView, AchievementsView, GridView, AttemptsState, AttemptsView
