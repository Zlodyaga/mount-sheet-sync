--[[
Mount Sheet Sync 3.0.5 — окно, иконка и настройки
==================================================
  * окно (ЛКМ по иконке или /mss): слева «Все персонажи» и персонажи из таблицы (сколько каждому осталось), справа
    вкладки «Осталось» (таблица выбранного персонажа или сетка всех), «Попытки» (счётчик маунтов, сортировка и
    фильтры) и «Журнал» (записи аддона, ручная отметка, поиск в сохранениях); внизу — отправка в таблицу и настройки;
  * списки — ScrollBox игры: кадры только для видимых строк, перестраиваются, только когда поменялись данные;
  * кнопка у миникарты — её забирают к себе HidingBar и похожие панели;
  * иконка LibDataBroker — для Titan Panel, ChocolateBar и т. п.;
  * пункт в меню аддонов у миникарты (Addon Compartment);
  * настройки (ПКМ по иконке, кнопка в окне или /mss settings): язык, вид сетки всех персонажей, сообщения в чате,
    кнопка у миникарты, очистка журнала, сведения для отчёта об ошибке.
Все надписи — из ns.L; при смене языка окно и настройки перерисовываются сразу.
]]

local ADDON_NAME, ns = ...

local ICON = "Interface\\AddOns\\MountSheetSync\\Media\\Icon"   -- the horseshoe, same as the tray program
local WINDOW_NAME = "MountSheetSyncFrame"
local WIDTH, HEIGHT = 1060, 600
local SIDE_WIDTH = 160
local ROW_HEIGHT = 22
local CHAR_HEIGHT = 24
local TOOLBAR_HEIGHT = 34
local HEAD_HEIGHT = 24
local SLIDER_ROOM = 16    -- место под полосой прокрутки столбцов
local COLUMN_GAP = 8
local CHANCE_BAR_WIDTH = 58
-- Сетка всех персонажей: ширина столбца, сколько столбцов видно, сколько букв имени в заголовке.
local GRID_SCROLL = { cell = 40, visible = 7, letters = 3 }
local GRID_ALL = { cell = 16, visible = 20, letters = 1 }
local MIN_TEXT_WIDTH = 240   -- меньше этого на босса и маунт не отдаём: лучше меньше столбцов

local KIND_COLOR = {
  raid = "ffffffff",
  party = "ff8cc8ff",
  scenario = "ff8cc8ff",
  world = "ff8cff8c",
  manual = "ffffd200",
  reset = "ffff9a4d",
  mount = "ffc77dff",
  plan = "ff9d9d9d",
}

-- Вкладки по порядку; в db.tab хранится ключ.
local TAB_LEFT, TAB_ACH, TAB_ATTEMPTS, TAB_LOG = 1, 2, 3, 4
local TAB_KEYS = { "left", "ach", "attempts", "log" }
local TAB_LABELS = { "tab_left", "tab_ach", "tab_attempts", "tab_log" }
local ALL_CHARS = {}      -- selectedChar: первая строка слева, «Все персонажи»
local FILTER_KINDS = { "all", "raid", "dungeon", "world", "rare" }

local UI = {}
local L = ns.L
local Skin = ns.Skin
local relabel = {}      -- функции, которые заново ставят надписи (смена языка)
local frame, listBox, charBox, emptyText, header1, header2, editBox, urlBox
local toolbar, summaryText, focusText, attemptsBar, attemptsCount, logBar, headRow, gridSlider, columnGlow, hideZeroCheck
local listWidth = WIDTH - SIDE_WIDTH - 62
local tabs = {}
local filterButtons = {}
local headCells = {}
local refreshQueued = false
local selectedChar      -- имя персонажа из таблицы, выбранного слева; nil — текущий; ALL_CHARS — все
local shownList, shownChars   -- что сейчас в списках: перестраиваем только при изменении
local grid = { chars = {}, offset = 0, shown = 0, cell = GRID_SCROLL.cell, x = 0 }   -- сетка всех персонажей
local shownHead         -- столбцы и подписи заголовка таблицы, что сейчас на экране

---------------------------------------------------------------------------
-- Мелочи
---------------------------------------------------------------------------
local function FormatDuration(seconds)
  seconds = math.max(0, math.floor(seconds or 0))
  local days = math.floor(seconds / 86400)
  local hours = math.floor((seconds % 86400) / 3600)
  local minutes = math.floor((seconds % 3600) / 60)
  if days > 0 then
    return string.format(L.duration_dh, days, hours)
  elseif hours > 0 then
    return string.format(L.duration_hm, hours, minutes)
  end
  return string.format(L.duration_m, minutes)
end
ns.FormatDuration = FormatDuration

-- Подсказка: ключи текстов, сам текст берётся при наведении (на текущем языке).
local function AttachTooltip(widget, titleKey, textKey)
  widget:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
    GameTooltip:SetText(L[titleKey], 1, 0.82, 0)
    if textKey then
      GameTooltip:AddLine(L[textKey], 1, 1, 1, true)
    end
    GameTooltip:Show()
  end)
  widget:SetScript("OnLeave", function()
    GameTooltip:Hide()
  end)
end

-- Надпись, которая меняется вместе с языком.
local function Label(widget, key)
  local function apply()
    widget:SetText(L[key])
  end
  apply()
  relabel[#relabel + 1] = apply
end

-- Кнопка шире, если длинная надпись на другом языке в ней не помещается.
local function MakeButton(parent, key, width, tipKey, onClick)
  local button = Skin.Button(parent, width, 26)
  button:SetScript("OnClick", onClick)
  local function apply()
    button:SetText(L[key])
    local fs = button:GetFontString()
    local need = fs and fs.GetStringWidth and (fs:GetStringWidth() + 24) or width
    button:SetWidth(math.max(width, need))
  end
  apply()
  relabel[#relabel + 1] = apply
  if tipKey then
    AttachTooltip(button, key, tipKey)
  end
  return button
end

-- Окно подтверждения в стиле игры.
local function Confirm(text, acceptText, onAccept)
  if StaticPopup_ShowCustomGenericConfirmation then
    StaticPopup_ShowCustomGenericConfirmation({
      text = text,
      callback = onAccept,
      acceptText = acceptText,
      cancelText = CANCEL,
      showAlert = true,
    })
  else
    StaticPopupDialogs["MOUNTSHEETSYNC_CONFIRM"] = {
      text = text,
      button1 = acceptText,
      button2 = CANCEL,
      OnAccept = function() onAccept() end,
      timeout = 0,
      whileDead = true,
      hideOnEscape = true,
      preferredIndex = 3,
    }
    StaticPopup_Show("MOUNTSHEETSYNC_CONFIRM")
  end
end

local function FormatRow(rec)
  local when = date("%d.%m %H:%M", rec.time or 0)
  local color = KIND_COLOR[rec.kind] or KIND_COLOR.raid
  if rec.kind == "reset" then
    return string.format("|cff8c8c8c%s|r  |c%s%s|r  |cff8c8c8c(%s)|r", when, color, L.reset_row, rec.char or "?")
  end
  return string.format("|cff8c8c8c%s|r  %s  |c%s%s|r", when, rec.char or "?", color, ns.Describe(rec))
end

local function FillBrokerTooltip(tooltip)
  tooltip:AddLine("Mount Sheet Sync")
  local db = ns.GetDB()
  if db then
    local left = ns.GetRemaining and ns.GetRemaining()
    if left and left.state == "ok" then
      tooltip:AddLine(string.format(L.tip_left, left.left, left.total), 1, 1, 1)
    end
    tooltip:AddLine(string.format(L.tip_week, ns.CountThisWeek()), 1, 1, 1)
    local last = db.kills[#db.kills]
    if last then
      tooltip:AddLine(string.format(L.tip_last, ns.Describe(last), last.char or "?"), 0.8, 0.8, 0.8)
    end
  end
  local state = ns.ProgramState()
  if state == "oldaddon" or state == "oldprogram" then
    tooltip:AddLine(L["window_" .. state], 0.6, 0.6, 0.6, true)
  elseif state ~= "online" then
    tooltip:AddLine(L.tip_offline_short, 0.6, 0.6, 0.6, true)
  end
  tooltip:AddLine(L.tip_left_click, 0.6, 0.6, 0.6)
  tooltip:AddLine(L.tip_right_click, 0.6, 0.6, 0.6)
end

---------------------------------------------------------------------------
-- Действия
---------------------------------------------------------------------------
function UI.ConfirmResetWeek()
  Confirm(L.confirm_reset, L.confirm_reset_button,
    function()
      ns.ResetWeek()
      ns.SyncNow()
    end)
end

local function ConfirmClear()
  Confirm(L.confirm_clear, L.confirm_clear_button,
    function()
      ns.ClearLog()
    end)
end

local function SubmitManual()
  local text = editBox:GetText()
  if ns.AddManual(text) then
    editBox:SetText("")
  end
  editBox:ClearFocus()
end

---------------------------------------------------------------------------
-- Окно
---------------------------------------------------------------------------
-- «|» в тексте ячейки игра приняла бы за код цвета — удваиваем его.
local function Safe(value)
  if value == nil then
    return ""
  end
  return (tostring(value):gsub("|", "||"))
end

-- Первые n букв имени (UTF-8): заголовок столбца сетки.
local function FirstLetters(name, n)
  local out, count = {}, 0
  for ch in tostring(name):gmatch("[%z\1-\127\194-\244][\128-\191]*") do
    count = count + 1
    if count > n then
      break
    end
    out[count] = ch
  end
  return table.concat(out)
end

-- Значения каталога, которые не сокращаются правилами ниже: слово на языке игры или короткая запись.
local DIFF_WORDS = {
  ["Achievement"] = "diff_ach", ["Guild Achievement"] = "diff_ach", ["Mythic dungeon achievements"] = "diff_ach",
  ["Heroic achievements"] = "diff_ach", ["Heroic/Mythic dungeon achievements"] = "diff_ach",
  ["Любая"] = "diff_any", ["Any difficulty"] = "diff_any", ["Any / raid-specific"] = "diff_any",
  ["Удалённая награда"] = "diff_removed", ["Удалённая награда Retail"] = "diff_removed", ["Удалённая TBC-версия"] = "diff_removed",
  ["Torghast"] = "diff_torghast",
}
local DIFF_SHORT = {
  ["Обычный рейд"] = "N", ["10 игроков"] = "10", ["25-player loot table / no Keepers"] = "25",
  ["Normal 10, 3 drakes up"] = "N10", ["Normal 25, 3 drakes up"] = "N25", ["Heroic 10"] = "H10", ["Heroic 25"] = "H25",
  ["M+ rating / vendor"] = "M+", ["3400+ M+ rating in current season"] = "3400+", ["2000+ M+ rating"] = "2000+", ["3000+ M+ rating"] = "3000+",
  ["MoP Challenge Mode"] = "CM", ["WoD Challenge Mode"] = "CM", ["Heroic random dungeon only"] = "H", ["Mythic, hard mode"] = "M",
}

-- Сложность короче для колонки; полная — в подсказке строки.
local function ShortDiff(diff)
  local text = Safe(diff)
  if DIFF_WORDS[text] then
    return L[DIFF_WORDS[text]]
  end
  if DIFF_SHORT[text] then
    return DIFF_SHORT[text]
  end
  text = text:gsub("Normal", "N"):gsub("Heroic", "H"):gsub("Mythic%+", "M+"):gsub("Mythic", "M")
  text = text:gsub("Timewalking", "TW"):gsub(" timed run", ""):gsub("%s*/%s*", "/")
  text = text:gsub("Layers%s*", ""):gsub("Challenge cache", "CM"):gsub("Challenge", "CM")
  return text
end

-- Босс короче для колонки: что идёт после него («/ Сундук претендента», «> Затерянное во времени седло») — в подсказке строки.
local function ShortBoss(boss)
  return (Safe(boss):gsub("%s+[/>]%s+.*$", ""))
end

local function BossAndDiff(boss, diff)
  local text = Safe(boss)
  if Safe(diff) ~= "" then
    text = text .. " (" .. Safe(diff) .. ")"
  end
  return text
end

-- Окна игры лежат ниже нашего и были бы закрыты им: прячем своё, а когда то окно закроют, возвращаем.
local stepAsideFor, stepAsideHooked = nil, {}

local function StepAside(panel)
  if not (panel and panel:IsShown() and frame:IsShown()) then
    return
  end
  if not stepAsideHooked[panel] then
    stepAsideHooked[panel] = true
    panel:HookScript("OnHide", function(self)
      if stepAsideFor == self then
        stepAsideFor = nil
        frame:Show()
      end
    end)
  end
  stepAsideFor = panel
  frame:Hide()
end

-- Журнал маунтов игры на этом маунте.
-- Окно достижений игры на этом достижении.
local function ShowAchievement(id)
  -- OpenAchievementFrameToAchievement есть только в классической игре; в Retail — открыть окно и выбрать достижение
  local ok = pcall(function()
    if not AchievementFrame then
      C_AddOns.LoadAddOn("Blizzard_AchievementUI")
    end
    if not AchievementFrame:IsShown() then
      AchievementFrame_ToggleAchievementFrame()
    end
    AchievementFrame_SelectAchievement(id, true)
  end)
  if not ok then
    ns.Print(L.ach_open_failed)
    return
  end
  StepAside(AchievementFrame)
end

local function ShowMountInJournal(id)
  local ok = pcall(function()
    if not MountJournal then
      C_AddOns.LoadAddOn("Blizzard_Collections")
    end
    local tab = COLLECTIONS_JOURNAL_TAB_INDEX_MOUNTS or 1
    if SetCollectionsJournalShown then
      SetCollectionsJournalShown(true, tab)
    else
      if not (CollectionsJournal and CollectionsJournal:IsShown()) then
        ToggleCollectionsJournal(tab)
      end
      CollectionsJournal_SetTab(CollectionsJournal, tab)
    end
    MountJournal_SelectByMountID(id)
  end)
  if not ok then
    ns.Print(L.journal_open_failed)
    return
  end
  StepAside(CollectionsJournal)
end

-- Путеводитель приключений на этом рейде или подземелье, на боссе, если он известен.
local function ShowInGuide(instance, encounter)
  local ok = pcall(function()
    if not EncounterJournal_OpenJournal then
      C_AddOns.LoadAddOn("Blizzard_EncounterJournal")
    end
    EncounterJournal_OpenJournal(nil, instance, encounter)
  end)
  if not ok then
    ns.Print(L.guide_open_failed)
    return
  end
  StepAside(EncounterJournal)
end

-- Вход в рейд или подземелье: игра отдаёт входы по картам. 946 — карта всего мира: обходим все карты в ней, не только
-- зоны — вход в «Душу Дракона» есть лишь на карте Пещер Времени. Ошибка на одной карте не обрывает поиск по остальным.
local WORLD_MAP = 946
local MAP_ZONE = 3   -- Enum.UIMapType.Zone
local entrances, entranceByName, entranceList = {}, {}, {}

-- Входы, которых игра может не отдать (значка на карте нет: персонаж там не бывал, или вообще), — запасная точка у
-- каждого рейда и подземелья каталога: ns.ENTRANCES из Entrances.lua (его пишет tools/catalog/entrances.py по таблицам
-- игры). Сначала всегда спрашиваем игру.
-- Мировой босс, у которого в журнале игры нет карты (UiMapID 0). Координаты — Warcraft Wiki.
local KNOWN_BOSS_SPOTS = {
  [2210] = { map = 864, x = 0.44, y = 0.558 },      -- Краулок, Вол'дун
}

-- Строчные буквы и для кириллицы: string.lower в игре меняет только латиницу.
local function Lower(s)
  s = s:lower():gsub("\208([\144-\175])", function(c)
    local b = c:byte()
    return b < 160 and ("\208" .. string.char(b + 32)) or ("\209" .. string.char(b - 32))
  end)
  return (s:gsub("\208\129", "\209\145"))   -- Ё
end

-- Знаки, которых нет среди ASCII: кавычки-ёлочки и лапки, тире, многоточие, апостроф, неразрывный пробел.
local WIDE_MARKS = { "\194\171", "\194\187", "\226\128\156", "\226\128\157", "\226\128\158", "\226\128\147", "\226\128\148",
  "\226\128\166", "\226\128\153", "\226\128\152", "\194\160" }

-- Имя без пробелов, знаков и регистра: «Операция: Мехагон» и «Операция "Мехагон"», «Стратхольм – черный ход» и
-- «Стратхольм - черный ход» — одно и то же.
local function NameKey(name)
  local s = Lower(tostring(name or ""))
  for _, mark in ipairs(WIDE_MARKS) do
    s = s:gsub(mark, "")
  end
  return (s:gsub("[%p%s]", ""))
end

local function ScanEntrances()
  entranceList = {}
  local ok, maps = pcall(C_Map.GetMapChildrenInfo, WORLD_MAP, nil, true)
  for _, map in ipairs(ok and type(maps) == "table" and maps or {}) do
    pcall(function()
      for _, e in ipairs(C_EncounterJournal.GetDungeonEntrancesForMap(map.mapID) or {}) do
        entranceList[#entranceList + 1] = { name = e.name, id = e.journalInstanceID, map = map.mapID, mapName = map.name }
        local spot = { map = map.mapID, zone = map.mapType == MAP_ZONE, poi = e.areaPoiID, x = e.position.x, y = e.position.y }
        local old = entrances[e.journalInstanceID]
        if not old or (spot.zone and not old.zone) then   -- метка на карте зоны удобнее
          entrances[e.journalInstanceID] = spot
        end
        if e.name and e.name ~= "" then
          entranceByName[NameKey(e.name)] = entrances[e.journalInstanceID]
        end
      end
    end)
  end
end

local function FindEntrance(instance, name)
  -- запасной путь — по названию: у части подземелий в журнале и на карте разные номера
  local function Known()
    return entrances[instance] or (name and entranceByName[NameKey(name)]) or nil
  end
  if not Known() then
    ScanEntrances()   -- нет в найденном — обходим карты заново: игра могла ещё не отдать их в прошлый раз
  end
  return Known() or (ns.ENTRANCES and ns.ENTRANCES[instance])
end

-- /mss route <часть названия>: что игра отдаёт о входах — для разбора «На карте не нашлось» (по-английски, как /mss tip).
function ns.RouteDebug(text)
  local key = NameKey(text)
  ScanEntrances()
  local maps = {}
  for _, e in ipairs(entranceList) do
    maps[e.map] = true
  end
  local nMaps = 0
  for _ in pairs(maps) do
    nMaps = nMaps + 1
  end
  ns.Print(string.format("route: %d entrances on %d maps; looking for \"%s\"", #entranceList, nMaps, Safe(text)))
  if key == "" then
    return
  end
  local shown = 0
  for _, e in ipairs(entranceList) do
    if shown < 8 and NameKey(e.name):find(key, 1, true) then
      shown = shown + 1
      ns.Print(string.format("  entrance \"%s\": journal %s, map %s (%s)", Safe(e.name), tostring(e.id), tostring(e.map), Safe(e.mapName)))
    end
  end
  if shown == 0 then
    ns.Print("  no entrance by that name on any map")
  end
  local data = MountSheetSyncSheet
  local seen, rows = {}, 0
  for _, ch in ipairs(type(data) == "table" and type(data.chars) == "table" and data.chars or {}) do
    for _, item in ipairs(type(ch) == "table" and type(ch.items) == "table" and ch.items or {}) do
      local place = type(item) == "table" and item.place
      if rows < 4 and type(place) == "string" and NameKey(place):find(key, 1, true) and not seen[place] then
        seen[place] = true
        rows = rows + 1
        local inst = ns.ItemInstance(item)
        ns.Print(string.format("  row \"%s\": journal %s (%s), map %s", Safe(place), tostring(inst), Safe(inst and ns.InstanceName(inst) or "?"), tostring(item.im)))
      end
    end
  end
end

-- Игрок на этой карте или на карте внутри неё? У Пещер Времени две карты с одним названием и одной родительской (74 и 75),
-- вход записан на одной: стоя на другой, игрок тоже внутри.
local function PlayerInside(map)
  local ok, inside = pcall(function()
    local here = C_Map.GetBestMapForUnit and C_Map.GetBestMapForUnit("player")
    local target = C_Map.GetMapInfo(map)
    local steps = 0
    while here and steps < 20 do
      if here == map then
        return true
      end
      local info = C_Map.GetMapInfo(here)
      if info and target and info.name == target.name and info.parentMapID == target.parentMapID then
        return true
      end
      here = info and info.parentMapID ~= here and info.parentMapID or nil
      steps = steps + 1
    end
    return false
  end)
  return ok and inside or false
end

-- Вход в пещеру на карте зоны: ссылки на вложенную карту у Пещер Времени в данных игры нет, а сам вход в рейд лежит глубоко
-- под землёй. Точка — из навигации игры (WaypointSafeLocs 7655 «Caverns of Time»).
local CAVE_MOUTHS = {
  [74] = { x = 0.6525, y = 0.4979 },
  [75] = { x = 0.6525, y = 0.4979 },
}

-- Вход лежит на карте внутри другой зоны (Пещеры Времени в Танарисе): куда идти снаружи — та же точка на карте зоны.
-- nil — карта входа сама зона или игрок уже внутри неё.
local function OuterStep(e)
  if PlayerInside(e.map) then
    return nil   -- уже внутри — сразу ко входу
  end
  local ok, step = pcall(function()
    local inner = C_Map.GetMapInfo(e.map)
    local outer = inner and inner.parentMapID and C_Map.GetMapInfo(inner.parentMapID)
    if not (outer and outer.mapType == MAP_ZONE) then
      return nil
    end
    local outerMap = outer.mapID or inner.parentMapID
    -- лучше всего — значок входа в пещеру на карте зоны (ссылка на вложенную карту): там, где в неё входят
    if C_Map.GetMapLinksForMap then
      for _, link in ipairs(C_Map.GetMapLinksForMap(outerMap) or {}) do
        local pos = link.linkedUiMapID == e.map and link.position
        local x, y
        if pos then
          x, y = pos.x, pos.y
          if not x and pos.GetXY then
            x, y = pos:GetXY()
          end
        end
        if x and y then
          return { map = outerMap, x = x, y = y, name = inner.name, zone = outer.name }
        end
      end
    end
    local mouth = CAVE_MOUTHS[e.map]
    if mouth then
      return { map = outerMap, x = mouth.x, y = mouth.y, name = inner.name, zone = outer.name }
    end
    -- иначе — где на карте зоны лежит сам вход (точка внутри пещеры, может быть в стороне от её входа)
    local minX, maxX, minY, maxY = C_Map.GetMapRectOnMap(e.map, outerMap)
    if not (minX and maxX and minY and maxY) then
      return nil
    end
    return { map = outerMap, x = minX + e.x * (maxX - minX), y = minY + e.y * (maxY - minY),
             name = inner.name, zone = outer.name }
  end)
  return ok and step or nil
end

-- Босс на карте зоны (мировые боссы: входа у них нет).
local bossSpots = {}

local function FindBossSpot(encounter)
  if bossSpots[encounter] then
    return bossSpots[encounter]
  end
  local ok = pcall(function()
    for _, map in ipairs(C_Map.GetMapChildrenInfo(WORLD_MAP, Enum.UIMapType.Zone, true) or {}) do
      for _, e in ipairs(C_EncounterJournal.GetEncountersOnMap(map.mapID) or {}) do
        if not bossSpots[e.encounterID] then
          bossSpots[e.encounterID] = { map = map.mapID, x = e.mapX, y = e.mapY }
        end
      end
    end
  end)
  return ok and bossSpots[encounter] or KNOWN_BOSS_SPOTS[encounter]
end

-- Своя метка на карте, выбранная целью.
local function PinAndTrack(map, x, y)
  return pcall(function()
    assert(C_Map.CanSetUserWaypointOnMap(map))
    C_Map.SetUserWaypoint(UiMapPoint.CreateFromCoordinates(map, x, y))
    C_SuperTrack.SetSuperTrackedUserWaypoint(true)
  end)
end

-- Показать путь: игра ведёт к отметке входа, а если не вышло (не изучена территория и т. п.) — своя метка на карте у входа.
-- У мировых боссов входа нет: метка там, где их показывает игра.
-- Путь, ждущий второго шага: { entrance, name, inner }. routeOwn — цель меняет сам аддон, это не выбор игрока.
local pendingRoute, routeOwn, routeWatcher = nil, false, nil

-- Путь прямо ко входу: метка входа, а если игра её не ведёт — своя метка у входа.
local function TrackEntrance(e, name)
  local tracked = pcall(function()
    assert(e.poi)   -- запасная точка (ns.ENTRANCES): значка игры нет, вести не к чему
    C_SuperTrack.SetSuperTrackedMapPin(Enum.SuperTrackingMapPinType.AreaPOI, e.poi)
    local _, id = C_SuperTrack.GetSuperTrackedMapPin()
    assert(id == e.poi)
  end)
  if tracked then
    ns.Print(string.format(L.route_set, name))
    return
  end
  ns.Print(string.format(PinAndTrack(e.map, e.x, e.y) and L.route_pin or L.route_none, name))
end

-- Второй шаг: игрок внутри зоны со входом — путь ко входу.
local function StepInside()
  local route = pendingRoute
  if route and not routeOwn and PlayerInside(route.entrance.map) then
    pendingRoute = nil
    routeOwn = true
    ns.Print(string.format(L.route_inside, route.inner, route.name))
    TrackEntrance(route.entrance, route.name)
    routeOwn = false
  end
end

-- Событие смены зоны приходит не всегда (попал внутрь порталом): пока путь не завершён, раз в 2 секунды проверяем сами.
local routeTicking = false
local function TickRoute()
  StepInside()
  if pendingRoute then
    C_Timer.After(2, TickRoute)
  else
    routeTicking = false
  end
end

-- Игрок вошёл в зону со входом — второй шаг. Игрок сам выбрал другую цель — ждать больше нечего.
local function WatchRoute()
  if not routeTicking then
    routeTicking = true
    C_Timer.After(2, TickRoute)
  end
  if routeWatcher then
    return
  end
  routeWatcher = CreateFrame("Frame")
  for _, event in ipairs({ "ZONE_CHANGED", "ZONE_CHANGED_INDOORS", "ZONE_CHANGED_NEW_AREA", "SUPER_TRACKING_CHANGED" }) do
    pcall(routeWatcher.RegisterEvent, routeWatcher, event)
  end
  routeWatcher:SetScript("OnEvent", function(_, event)
    local route = pendingRoute
    if not route or routeOwn then
      return
    end
    if event == "SUPER_TRACKING_CHANGED" then
      -- метку у входа игра снимает сама, когда до неё дошли: это не повод забыть путь; другая цель — повод
      local ok, other = pcall(function()
        return C_SuperTrack.IsSuperTrackingAnything() and not C_SuperTrack.IsSuperTrackingUserWaypoint()
      end)
      if ok and other then
        pendingRoute = nil
      end
      return
    end
    StepInside()
  end)
end

local function ShowRoute(elem)
  pendingRoute = nil   -- новый путь заменяет недоделанный
  if elem.spot then
    -- рар: точка из каталога (ns.RARE_SPOTS); точек появления несколько — карта его зоны
    local name, s = Safe(elem.bossName), elem.spot
    if s.x and PinAndTrack(s.map, s.x, s.y) then
      ns.Print(string.format(L.route_boss, name))
    elseif not s.x and pcall(OpenWorldMap, s.map) then
      ns.Print(string.format(L.route_zone, name))
      StepAside(WorldMapFrame)
    else
      ns.Print(string.format(L.route_none, name))
    end
    return
  end
  if elem.world then
    local name = Safe(elem.bossName)
    local spot = elem.encounter and FindBossSpot(elem.encounter)
    if spot and PinAndTrack(spot.map, spot.x, spot.y) then
      ns.Print(string.format(L.route_boss, name))
    else
      ns.Print(string.format(L.route_none, name))
    end
    return
  end
  local instance = elem.instance
  local name = Safe(elem.placeName) ~= "" and Safe(elem.placeName) or ns.InstanceName(instance) or "?"
  local e = FindEntrance(instance, ns.InstanceName(instance) or elem.placeName)
  if not e then
    ns.Print(string.format(L.route_none, name))
    return
  end
  local outer = OuterStep(e)
  if not outer then
    TrackEntrance(e, name)
    return
  end
  -- в два шага, но одним щелчком: сначала метка у зоны со входом (Пещеры Времени), а когда игрок в неё войдёт,
  -- аддон сам переключит путь на вход (RouteWatch)
  routeOwn = true
  local pinned = PinAndTrack(outer.map, outer.x, outer.y)
  routeOwn = false
  if not pinned then
    ns.Print(string.format(L.route_none, Safe(outer.name)))
    return
  end
  pendingRoute = { entrance = e, name = name, inner = Safe(outer.name) }
  WatchRoute()
  ns.Print(string.format(L.route_first, Safe(outer.name), Safe(outer.zone), name))
end

-- Курсор над столбцом строки (key — ключ столбца)?
local function OverColumn(button, key)
  local left = button:GetLeft()
  if not left or not button.elem.cols then
    return false
  end
  local x = GetCursorPosition() / button:GetEffectiveScale() - left
  for _, col in ipairs(button.elem.cols) do
    if col.key == key then
      return x >= col.x and x <= col.x + col.w
    end
  end
  return false
end

-- Меню по правому щелчку на строке «Осталось» (только у текущего персонажа): не фармить строку или весь рейд / подземелье.
local function ShowRowMenu(button)
  local entry = button.elem and button.elem.entry
  if not entry or entry.done or not MenuUtil or not MenuUtil.CreateContextMenu then
    return
  end
  local item = entry.item
  MenuUtil.CreateContextMenu(button, function(owner, root)
    root:CreateTitle(ns.Shown(item, "boss") or "?")
    root:CreateButton(L.menu_not_row, function()
      ns.AddPlan(item, "row")
    end)
    if ns.Shown(item, "place") and ns.Shown(item, "place") ~= "" then
      root:CreateButton(string.format(L.menu_not_place, ns.Shown(item, "place")), function()
        ns.AddPlan(item, "place")
      end)
    end
  end)
end

local function HideTooltip()
  GameTooltip:Hide()
end

-- Сетка: какой столбец под курсором — подсветить его и назвать персонажа (при узких столбцах имени не видно).
local function FocusColumn(index)
  local c = index and grid.chars[index]
  local slot = c and index - grid.offset
  if not c or slot < 1 or slot > grid.shown then
    columnGlow:Hide()
    focusText:SetText("")
    return
  end
  columnGlow:ClearAllPoints()
  columnGlow:SetPoint("TOPLEFT", 6 + grid.x + (slot - 1) * grid.cell, -TOOLBAR_HEIGHT)
  columnGlow:SetPoint("BOTTOMLEFT", 6 + grid.x + (slot - 1) * grid.cell, grid.overflow and SLIDER_ROOM + 6 or 6)
  columnGlow:SetWidth(grid.cell)
  columnGlow:Show()
  focusText:SetText(string.format(L.all_focus, c.colored))
end

local function GridHover(button)
  local left = button:GetLeft()
  if not left then
    return
  end
  local x = GetCursorPosition() / button:GetEffectiveScale() - left - grid.x
  local slot = math.floor(x / grid.cell) + 1
  FocusColumn(slot >= 1 and slot <= grid.shown and slot + grid.offset or nil)
end

local function ClickRow(button, mouse)
  if mouse == "RightButton" then
    ShowRowMenu(button)
    return
  end
  local elem = button.elem
  if not elem then
    return
  end
  if IsControlKeyDown() then
    if elem.route then
      ShowRoute(elem)
    end
  elseif elem.achievement and OverColumn(button, "boss") then
    ShowAchievement(elem.achievement)
  elseif elem.instance and OverColumn(button, "boss") then
    ShowInGuide(elem.instance, elem.encounter)
  elseif elem.mountID then
    ShowMountInJournal(elem.mountID)
  end
end

-- Подсказка строки: elem.tip = { заголовок, строка, … }; у строк с меню — ещё «ПКМ — не фармлю».
local function ShowRowTooltip(button)
  local elem = button.elem
  if not elem or not (elem.tip or elem.entry) then
    return
  end
  -- слева от таблицы: так подсказка не закрывает строки
  GameTooltip:SetOwner(button, "ANCHOR_NONE")
  GameTooltip:SetPoint("RIGHT", button, "LEFT", -8, 0)
  if elem.tip then
    GameTooltip:SetText(elem.tip[1], 1, 0.82, 0)
    for i = 2, #elem.tip do
      GameTooltip:AddLine(elem.tip[i], 1, 1, 1, true)
    end
    if elem.instance then
      GameTooltip:AddLine(L.tip_row_guide, 0.6, 0.6, 0.6, true)
    end
    if elem.achievement then
      GameTooltip:AddLine(L.tip_row_ach, 0.6, 0.6, 0.6, true)
    end
    if elem.mountID then
      GameTooltip:AddLine(L.tip_row_open, 0.6, 0.6, 0.6, true)
    end
    if elem.route then
      GameTooltip:AddLine(L.tip_row_route, 0.6, 0.6, 0.6, true)
    end
    if elem.entry then
      GameTooltip:AddLine(L.tip_row_menu, 0.6, 0.6, 0.6, true)
    end
  else
    GameTooltip:SetText(L.tip_row_menu, 1, 1, 1)
  end
  GameTooltip:Show()
  if elem.names then
    button:SetScript("OnUpdate", GridHover)
  end
end

local function LeaveRow(button)
  button:SetScript("OnUpdate", nil)
  HideTooltip()
  if button.elem and button.elem.names then
    FocusColumn(nil)
  end
end

-- Кадры строк, которые уже собраны (ScrollBox отдаёт и новые, и переиспользованные).
local built = setmetatable({}, { __mode = "k" })

local DOT_TEXTURE = Skin.DOT   -- белый круг: красим в цвет класса
local NONE_TEXTURE = "Interface\\AddOns\\MountSheetSync\\Media\\Close"   -- тот же крестик, что у кнопки закрытия, только тусклый

-- «N H M»: что ещё можно — цветом color, убитое — тёмным.
local function PartsText(killed, color)
  local out = {}
  for i, p in ipairs(ns.PARTS) do
    out[i] = "|c" .. (killed[p] and "ff5f5a4e" or color) .. p .. "|r"
  end
  return table.concat(out, " ")
end

-- Ячейки сетки у строки: кружок цвета класса — надо убить, чёрточка — убит на этой неделе, крестик — строки у
-- персонажа нет (не фармит или нет такой группы); у строки с раздельными сохранениями — буквы сложностей
-- (в узких столбцах — сколько осталось).
local function DrawGrid(button, elem)
  for slot = 1, grid.shown do
    local c = grid.chars[grid.offset + slot]
    local dot = button.dots[slot]
    if not dot then
      dot = button:CreateTexture(nil, "ARTWORK")
      button.dots[slot] = dot
    end
    dot:ClearAllPoints()
    dot:SetPoint("CENTER", button, "LEFT", grid.x + (slot - 0.5) * grid.cell, 0)
    local mark = button.marks[slot]
    if mark then
      mark:SetText("")
    end
    local killed = c and elem.killed and elem.killed[c.name]
    if killed then
      if not mark then
        mark = button:CreateFontString(nil, "OVERLAY", "MSSFontHead")
        button.marks[slot] = mark
      end
      mark:ClearAllPoints()
      mark:SetPoint("CENTER", button, "LEFT", grid.x + (slot - 0.5) * grid.cell, 0)
      if grid.cell >= GRID_SCROLL.cell then
        mark:SetText(PartsText(killed, c.hex))
      else
        local left = 0
        for _, p in ipairs(ns.PARTS) do
          left = left + (killed[p] and 0 or 1)
        end
        mark:SetText("|c" .. c.hex .. left .. "|r")
      end
      dot:SetSize(1, 1)
      dot:SetColorTexture(0, 0, 0, 0)
      dot:SetVertexColor(1, 1, 1)
    elseif c and elem.names[c.name] then
      dot:SetSize(10, 10)
      dot:SetTexture(DOT_TEXTURE)
      dot:SetVertexColor(c.r, c.g, c.b)
    elseif c and not (elem.done and elem.done[c.name]) then
      dot:SetSize(8, 8)
      dot:SetTexture(NONE_TEXTURE)
      dot:SetVertexColor(0.36, 0.33, 0.28)
    else
      dot:SetSize(8, 2)
      dot:SetColorTexture(0.42, 0.38, 0.32)
      dot:SetVertexColor(1, 1, 1)
    end
    dot:Show()
  end
  for slot = grid.shown + 1, #button.dots do
    button.dots[slot]:Hide()
    if button.marks[slot] then
      button.marks[slot]:SetText("")
    end
  end
end

local function HideDots(button)
  for _, dot in ipairs(button.dots) do
    dot:Hide()
  end
  for _, mark in pairs(button.marks) do
    mark:SetText("")
  end
end

-- Строка таблицы: значок, ячейки по столбцам elem.cols, полоска шанса. Столбцы ставятся, только если сменились.
local function DrawCells(button, elem)
  local cols = elem.cols
  local used = 0
  for _, col in ipairs(cols) do
    if col.key == "bar" then
      button.barBack:ClearAllPoints()
      button.barBack:SetPoint("LEFT", col.x, 0)
      button.barBack:SetWidth(col.w)
      button.barFill:ClearAllPoints()
      button.barFill:SetPoint("LEFT", col.x, 0)
      button.barFill:SetWidth(math.max(1, col.w * (elem.bar or 0)))
    elseif col.key ~= "grid" and col.key ~= "icon" then
      used = used + 1
      local cell = button.cells[used]
      if not cell then
        cell = button:CreateFontString(nil, "OVERLAY", "MSSFontBody")
        cell:SetWordWrap(false)
        button.cells[used] = cell
      end
      if button.laidOut ~= cols then
        cell:ClearAllPoints()
        cell:SetPoint("LEFT", col.x, 0)
        cell:SetWidth(col.w)
        cell:SetJustifyH(col.right and "RIGHT" or "LEFT")
        cell:SetFontObject(col.bold and MSSFontBold or MSSFontBody)
      end
      cell:SetText(elem.values[col.key] or "")
      cell:Show()
    end
  end
  for i = used + 1, #button.cells do
    button.cells[i]:Hide()
  end
  button.laidOut = cols
  button.iconBack:Show()
  button.icon:SetShown(elem.icon ~= nil)   -- ID маунта неизвестен — пустая рамка
  if elem.icon then
    button.icon:SetTexture(elem.icon)
    if elem.done then
      button.icon:SetTexCoord(0, 1, 0, 1)
    else
      button.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
    end
  end
  local hasBar = elem.bar ~= nil
  button.barBack:SetShown(hasBar)
  button.barFill:SetShown(hasBar)
  if hasBar then
    button.barFill:SetColorTexture(elem.barColor[1], elem.barColor[2], elem.barColor[3])
  end
end

-- Строка списка. ScrollBox переиспользует кадры: всё, что зависит от строки, ставится при каждом показе.
local function InitRow(button, elem)
  if not built[button] then
    built[button] = true
    button:SetHeight(ROW_HEIGHT)
    button.cells, button.dots, button.marks, button.laidOut = {}, {}, {}, false
    button.stripe = button:CreateTexture(nil, "BACKGROUND")
    button.stripe:SetAllPoints()
    button.stripe:SetColorTexture(1, 1, 1, 0.03)
    button.text = button:CreateFontString(nil, "OVERLAY", "MSSFontBody")
    button.text:SetPoint("LEFT", 4, 0)
    button.text:SetPoint("RIGHT", -4, 0)
    button.text:SetJustifyH("LEFT")
    button.text:SetWordWrap(false)
    button.iconBack = button:CreateTexture(nil, "BORDER")
    button.iconBack:SetSize(20, 20)
    button.iconBack:SetPoint("LEFT", 3, 0)
    button.iconBack:SetColorTexture(Skin.COLOR.line[1], Skin.COLOR.line[2], Skin.COLOR.line[3])
    button.icon = button:CreateTexture(nil, "ARTWORK")
    button.icon:SetSize(18, 18)
    button.icon:SetPoint("CENTER", button.iconBack)
    button.barBack = button:CreateTexture(nil, "ARTWORK")
    button.barBack:SetHeight(6)
    button.barBack:SetColorTexture(1, 1, 1, 0.08)
    button.barFill = button:CreateTexture(nil, "OVERLAY")
    button.barFill:SetHeight(6)
    button.hl = button:CreateTexture(nil, "HIGHLIGHT")
    button.hl:SetAllPoints()
    button.hl:SetColorTexture(1, 1, 1, 0.08)
    button:RegisterForClicks("LeftButtonUp", "RightButtonUp")
    button:SetScript("OnClick", ClickRow)
    button:SetScript("OnEnter", ShowRowTooltip)
    button:SetScript("OnLeave", LeaveRow)
  end
  button.elem = elem
  button.stripe:SetShown(elem.stripe and true or false)
  button.hl:SetShown(elem.entry ~= nil or elem.tip ~= nil)
  if elem.cols then
    button.text:SetText("")
    button.text:Hide()
    DrawCells(button, elem)
    if elem.names then
      DrawGrid(button, elem)
    else
      HideDots(button)
    end
  else
    button.text:SetFontObject(elem.group and MSSFontTitle or MSSFontBody)
    button.text:SetText(elem.text)
    button.text:Show()
    button.iconBack:Hide()
    button.icon:Hide()
    button.barBack:Hide()
    button.barFill:Hide()
    for _, cell in ipairs(button.cells) do
      cell:Hide()
    end
    HideDots(button)
  end
end

local DONE_MARK = "|TInterface\\RaidFrame\\ReadyCheck-Ready:12:12|t"
local DONE_TEXTURE = "Interface\\RaidFrame\\ReadyCheck-Ready"

local function ClassColored(name, classFile)
  local color = classFile and RAID_CLASS_COLORS and RAID_CLASS_COLORS[classFile]
  if color and color.colorStr then
    return "|c" .. color.colorStr .. Safe(name) .. "|r"
  end
  return Safe(name)
end

-- Строка слева: «Все персонажи» или персонаж в цвете класса; справа — сколько осталось (или галочка).
local function InitCharRow(button, elem)
  if not built[button] then
    built[button] = true
    button:SetHeight(CHAR_HEIGHT)
    button.sel = button:CreateTexture(nil, "BACKGROUND")
    button.sel:SetAllPoints()
    button.sel:SetColorTexture(0.227, 0.184, 0.071)
    button.hl = button:CreateTexture(nil, "HIGHLIGHT")
    button.hl:SetAllPoints()
    button.hl:SetColorTexture(1, 1, 1, 0.08)
    button.name = button:CreateFontString(nil, "OVERLAY", "MSSFontName")
    button.name:SetPoint("LEFT", 6, 0)
    button.name:SetPoint("RIGHT", -30, 0)
    button.name:SetJustifyH("LEFT")
    button.name:SetWordWrap(false)
    button.count = button:CreateFontString(nil, "OVERLAY", "MSSFontGold")
    button.count:SetPoint("RIGHT", -6, 0)
    button:SetScript("OnClick", function(self)
      if self.elem.all then
        UI.SelectChar(ALL_CHARS)
      else
        UI.SelectChar(not self.elem.current and self.elem.name or nil)   -- свой персонаж — снова «текущий»
      end
    end)
    button:SetScript("OnEnter", function(self)
      local e = self.elem
      GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
      if e.all then
        GameTooltip:SetText(L.side_all, 1, 0.82, 0)
        GameTooltip:AddLine(string.format(L.tip_side_all, e.total), 1, 1, 1, true)
      else
        GameTooltip:SetText(Safe(e.name), 1, 1, 1)
        if e.r.state == "ok" then
          GameTooltip:AddLine(string.format(L.tip_char, e.r.left, e.r.total), 1, 0.82, 0)
        end
        if selectedChar == ALL_CHARS then
          FocusColumn(e.index)
        end
      end
      GameTooltip:Show()
    end)
    button:SetScript("OnLeave", function()
      HideTooltip()
      if selectedChar == ALL_CHARS then
        FocusColumn(nil)
      end
    end)
  end
  button.elem = elem
  button.sel:SetShown(elem.selected)
  if elem.all then
    button.name:SetText("|cffffd200" .. L.side_all .. "|r")
    button.count:SetText(elem.total > 0 and tostring(elem.total) or DONE_MARK)
    return
  end
  button.name:SetText(ClassColored(elem.name, elem.classFile))
  local r = elem.r
  if r.state ~= "ok" then
    button.count:SetText("")
  elseif r.left > 0 then
    button.count:SetText(tostring(r.left))
  else
    button.count:SetText(DONE_MARK)
  end
end

function UI.RefreshHeader()
  local player = ns.GetPlayer()
  header1:SetText(string.format("%s — %s", player.name or "?", player.realm or "?"))
  local untilReset = ns.SecondsUntilReset()
  header2:SetText(string.format(L.header_week, ns.CountThisWeek(), untilReset and FormatDuration(untilReset) or "?"))
end

local function Line(text, entry, tip)
  return { text = text, entry = entry, tip = tip }
end

local function GroupLine(kind)
  return { text = "|cffffd200" .. ns.KindTitle(kind) .. "|r", group = true }
end

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
    }, listWidth)
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
    }, listWidth)
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
  while shown > 1 and listWidth - fixed - shown * view.cell < MIN_TEXT_WIDTH do
    shown = shown - 1
  end
  local cols = Columns({
    { key = "icon", w = 18 },
    { key = "boss", flex = 1, label = "col_boss" },
    { key = "diff", w = 40, label = "col_diff" },
    { key = "mount", flex = 1.3, label = "col_mount" },
    { key = "grid", w = shown * view.cell },
    { key = "total", w = 40, right = true, bold = true, label = "col_total", tip = "tip_col_total" },
  }, listWidth)
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
            local menu = own and kind ~= "world" and kind ~= "rare" and not entry.done
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
    }, listWidth)
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

---------------------------------------------------------------------------
-- Заголовок таблицы
---------------------------------------------------------------------------
local function HeadCell(i)
  local cell = headCells[i]
  if not cell then
    cell = CreateFrame("Button", nil, headRow)
    cell:SetHeight(HEAD_HEIGHT)
    cell.text = cell:CreateFontString(nil, "OVERLAY", "MSSFontHead")
    cell.text:SetAllPoints()
    cell.text:SetWordWrap(false)
    cell.arrow = cell:CreateTexture(nil, "OVERLAY")
    cell.arrow:SetTexture(Skin.ARROW)
    cell.arrow:SetSize(10, 10)
    cell.arrow:SetVertexColor(Skin.COLOR.gold[1], Skin.COLOR.gold[2], Skin.COLOR.gold[3])
    cell:SetScript("OnClick", function(self)
      if self.sort then
        UI.SortAttempts(self.sort)
      end
    end)
    cell:SetScript("OnEnter", function(self)
      if self.charIndex then
        FocusColumn(self.charIndex)
      elseif self.tipKey then
        GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
        GameTooltip:SetText(L[self.labelKey], 1, 0.82, 0)
        GameTooltip:AddLine(L[self.tipKey], 1, 1, 1, true)
        GameTooltip:Show()
      end
    end)
    cell:SetScript("OnLeave", function(self)
      HideTooltip()
      if self.charIndex then
        FocusColumn(nil)
      end
    end)
    headCells[i] = cell
  end
  cell:Show()
  return cell
end

local function PlaceHead(cell, x, w, right)
  cell:ClearAllPoints()
  cell:SetPoint("LEFT", x, 0)
  cell:SetWidth(w)
  cell.text:SetJustifyH(right and "RIGHT" or "LEFT")
end

local function DrawHead(cols, sortState, isGrid)
  local used = 0
  for _, col in ipairs(cols) do
    if col.label then
      used = used + 1
      local cell = HeadCell(used)
      PlaceHead(cell, col.x, col.w + (col.span or 0), col.right and not col.span)
      cell.sort, cell.tipKey, cell.labelKey, cell.charIndex = col.sort or false, col.tip or false, col.label, false
      cell:EnableMouse(col.sort ~= nil or col.tip ~= nil)
      cell.text:SetText(L[col.label])
      local active = sortState and col.sort == sortState.sort
      if active then
        cell.text:SetTextColor(Skin.COLOR.gold[1], Skin.COLOR.gold[2], Skin.COLOR.gold[3])
      else
        cell.text:SetTextColor(0.659, 0.624, 0.545)
      end
      cell.arrow:SetShown(active and true or false)
      if active then
        cell.arrow:ClearAllPoints()
        if col.right and not col.span then
          cell.arrow:SetPoint("RIGHT", cell, "LEFT", 0, 0)
        else
          cell.arrow:SetPoint("LEFT", cell.text, "LEFT", cell.text:GetStringWidth() + 3, 0)
        end
        if sortState.desc then
          cell.arrow:SetTexCoord(0, 1, 0, 1)
        else
          cell.arrow:SetTexCoord(0, 1, 1, 0)
        end
      end
    end
  end
  if isGrid then
    for slot = 1, grid.shown do
      local index = grid.offset + slot
      local c = grid.chars[index]
      used = used + 1
      local cell = HeadCell(used)
      PlaceHead(cell, grid.x + (slot - 1) * grid.cell, grid.cell, false)
      cell.text:SetJustifyH("CENTER")
      cell.sort, cell.tipKey, cell.labelKey, cell.charIndex = false, false, false, index
      cell:EnableMouse(true)
      cell.arrow:Hide()
      cell.text:SetText(c and ClassColored(FirstLetters(c.name, grid.letters), c.classFile) or "")
    end
  end
  for i = used + 1, #headCells do
    headCells[i]:Hide()
  end
end

-- Сдвинули полосу под столбцами: перерисовать заголовок и видимые строки, данные не трогаем.
local function RedrawGrid()
  if shownHead then
    DrawHead(shownHead, nil, true)
  end
  listBox:ForEachFrame(function(button)
    if button.elem and button.elem.names then
      DrawGrid(button, button.elem)
    end
  end)
end

-- Части окна вокруг списка: что нужно этому виду.
local function ApplyChrome(view)
  local top = TOOLBAR_HEIGHT + (view.cols and HEAD_HEIGHT or 0)
  local useSlider = view.grid and grid.overflow and true or false
  listBox:ClearAllPoints()
  listBox:SetPoint("TOPLEFT", 6, -top)
  listBox:SetPoint("BOTTOMRIGHT", -22, useSlider and SLIDER_ROOM + 6 or 6)
  summaryText:SetText(view.summary or "")
  summaryText:SetShown(not view.attempts and not view.log)
  focusText:SetText("")
  focusText:SetShown(view.grid and true or false)
  columnGlow:Hide()
  attemptsBar:SetShown(view.attempts and true or false)
  logBar:SetShown(view.log and true or false)
  headRow:SetShown(view.cols ~= nil)
  shownHead = view.cols
  if view.cols then
    DrawHead(view.cols, view.attempts and AttemptsState() or nil, view.grid)
  end
  if view.attempts then
    attemptsCount:SetText(view.count and string.format(L.attempts_count, view.count) or "")
    local st = AttemptsState()
    for _, b in ipairs(filterButtons) do
      Skin.ChipState(b, b.kind == st.kind)
    end
    hideZeroCheck:SetChecked(st.hideZero and true or false)
  end
  gridSlider:SetShown(useSlider)
  if useSlider then
    gridSlider:ClearAllPoints()
    gridSlider:SetPoint("BOTTOMLEFT", 6 + grid.x, 6)
    gridSlider:SetWidth(grid.shown * grid.cell)
    gridSlider:SetMinMaxValues(0, #grid.chars - grid.shown)
    gridSlider:SetValue(grid.offset)
  end
end

local function UpdateTabTitle(r, total)
  local tab = tabs[TAB_LEFT]
  local title = L.tab_left
  if total then
    title = string.format(L.tab_left_n, total)
  elseif r.state == "ok" then
    title = string.format(L.tab_left_n, r.left)
  end
  if tab:GetText() ~= title then
    tab:SetText(title)
    Skin.TabWidth(tab, frame.minTabWidth)
  end
end

-- Выбранный слева персонаж — его строка из ns.GetAllRemaining(); nil — текущий (или «Все персонажи»).
local function SelectedEntry(all)
  if not selectedChar or selectedChar == ALL_CHARS then
    return nil
  end
  for _, c in ipairs(all) do
    if c.name == selectedChar then
      return not c.current and c or nil
    end
  end
  return nil
end

local function TotalLeft(all)
  local total = 0
  for _, c in ipairs(all) do
    if c.r.state == "ok" then
      total = total + c.r.left
    end
  end
  return total
end

local function RefreshChars(all, sel)
  local showAll = selectedChar == ALL_CHARS and #all > 1
  local key = tostring(all) .. "|" .. tostring(sel) .. "|" .. tostring(showAll)
  if key == shownChars then
    return
  end
  shownChars = key
  local elems = {}
  if #all > 1 then   -- один персонаж: сравнивать не с кем, сразу его «Осталось»
    elems[1] = { all = true, total = TotalLeft(all), selected = showAll }
  end
  for i, c in ipairs(all) do
    elems[#elems + 1] = { name = c.name, classFile = c.classFile, r = c.r, current = c.current, index = i,
      selected = (not showAll and ((sel and c == sel) or (not sel and c.current))) or false }
  end
  charBox:SetDataProvider(CreateDataProvider(elems), ScrollBoxConstants.RetainScrollPosition)
end

-- Списки перестраиваются, только когда поменялись их данные (кэши Remaining.lua отдают ту же таблицу):
-- шапка раз в 30 с, новая запись или открытие окна без изменений не трогают кадры.
function UI.Refresh()
  if not frame or not frame:IsShown() then
    return
  end
  local db = ns.GetDB()
  UI.RefreshHeader()
  local all = ns.GetAllRemaining()
  local sel = SelectedEntry(all)
  RefreshChars(all, sel)
  local own = ns.GetRemaining()
  local showAll = selectedChar == ALL_CHARS and #all > 1
  UpdateTabTitle(sel and sel.r or own, showAll and TotalLeft(all) or nil)

  local tab = frame.selectedTab
  local key, build
  if tab == TAB_LEFT and showAll then
    local rows = ns.GetSummary()
    key = "all|" .. tostring(rows) .. "|" .. tostring(db.gridView)
    build = function() return GridView(rows, all) end
  elseif tab == TAB_LEFT then
    local r = sel and sel.r or own
    key = "left|" .. tostring(r)
    build = function() return RemainingView(r, not sel) end
  elseif tab == TAB_ACH then
    local rows = ns.GetAchievementRows()
    key = "ach|" .. tostring(rows)
    build = function() return AchievementsView(rows) end
  elseif tab == TAB_ATTEMPTS then
    local rows = ns.GetAttemptsList()
    local st = AttemptsState()
    key = table.concat({ "attempts", tostring(rows), st.sort, tostring(st.desc), st.kind, tostring(st.hideZero) }, "|")
    build = function() return AttemptsView(rows, #all) end
  else
    local last = db.kills[#db.kills]
    key = "log|" .. #db.kills .. "|" .. tostring(last and last.id)
    build = function() return JournalView(db) end
  end
  if key == shownList then
    return
  end
  shownList = key
  local view = build()
  ApplyChrome(view)
  listBox:SetDataProvider(CreateDataProvider(view.list), ScrollBoxConstants.RetainScrollPosition)
  emptyText:SetText(view.empty or "")
  emptyText:SetShown(#view.list == 0)
  urlBox:SetShown(#view.list == 0 and view.url and true or false)
end

local function ShowTabState(id)
  frame.selectedTab = id
  for i, tab in ipairs(tabs) do
    Skin.TabState(tab, i == id)
  end
end

function UI.SelectTab(id)
  ns.GetDB().tab = TAB_KEYS[id]
  ShowTabState(id)
  shownList = nil
  listBox:ScrollToBegin()
  UI.Refresh()
end

-- Щелчок слева: «Осталось» персонажа (name = nil — текущий) или всех (ALL_CHARS).
function UI.SelectChar(name)
  selectedChar = name
  if frame.selectedTab ~= TAB_LEFT then
    UI.SelectTab(TAB_LEFT)
    return
  end
  shownList = nil
  listBox:ScrollToBegin()
  UI.Refresh()
end

-- Щелчок по заголовку «Попыток»: та же колонка — обратный порядок; новая — большие сверху (имена — по алфавиту).
function UI.SortAttempts(sort)
  local st = AttemptsState()
  if st.sort == sort then
    st.desc = not st.desc
  else
    st.sort = sort
    st.desc = sort ~= "name" and sort ~= "boss"
  end
  listBox:ScrollToBegin()
  UI.Refresh()
end

function UI.FilterAttempts(kind)
  AttemptsState().kind = kind
  listBox:ScrollToBegin()
  UI.Refresh()
end

-- Настройки: вид сетки всех персонажей («scroll» — по умолчанию, «all» — все столбцы сразу).
function UI.SetGridView(view)
  ns.GetDB().gridView = view == "all" and "all" or nil
  grid.offset = 0
  shownList = nil
  UI.Refresh()
end

-- Прокручиваемый список ScrollBox: кадры строк создаются только для видимой части и переиспользуются.
local function CreateList(parent, extent, initializer)
  local box = CreateFrame("Frame", nil, parent, "WowScrollBoxList")
  local bar = CreateFrame("EventFrame", nil, parent, "MinimalScrollBar")
  bar:SetPoint("TOPLEFT", box, "TOPRIGHT", 4, 0)
  bar:SetPoint("BOTTOMLEFT", box, "BOTTOMRIGHT", 4, 0)
  local view = CreateScrollBoxListLinearView()
  view:SetElementExtent(extent)
  view:SetElementInitializer("Button", initializer)
  ScrollUtil.InitScrollBoxListWithScrollBar(box, bar, view)
  ScrollUtil.AddManagedScrollBarVisibilityBehavior(box, bar)
  Skin.ScrollBar(bar)
  return box
end

-- Полоса под столбцами сетки: шаг — один персонаж, колесо тоже двигает.
local function CreateGridSlider(parent)
  local slider = CreateFrame("Slider", nil, parent)
  slider:SetOrientation("HORIZONTAL")
  slider:SetHeight(12)
  slider:SetValueStep(1)
  slider:SetObeyStepOnDrag(true)
  local back = slider:CreateTexture(nil, "BACKGROUND")
  back:SetAllPoints()
  back:SetColorTexture(1, 1, 1, 0.08)
  local thumb = slider:CreateTexture(nil, "OVERLAY")
  thumb:SetSize(36, 12)
  thumb:SetColorTexture(0.75, 0.68, 0.5, 0.9)
  slider:SetThumbTexture(thumb)
  slider:SetScript("OnValueChanged", function(_, value)
    local offset = math.floor(value + 0.5)
    if offset ~= grid.offset then
      grid.offset = offset
      RedrawGrid()
    end
  end)
  slider:EnableMouseWheel(true)
  slider:SetScript("OnMouseWheel", function(self, delta)
    self:SetValue(self:GetValue() - delta)
  end)
  slider:Hide()
  return slider
end

-- Над списком: у «Попыток» — фильтры, у «Журнала» — ручная отметка и поиск, у остальных — строка итога.
local function CreateToolbar(inset)
  toolbar = CreateFrame("Frame", nil, inset)
  toolbar:SetPoint("TOPLEFT", 6, -2)
  toolbar:SetPoint("RIGHT", -6, 0)
  toolbar:SetHeight(TOOLBAR_HEIGHT - 4)

  summaryText = toolbar:CreateFontString(nil, "OVERLAY", "MSSFontBody")
  summaryText:SetPoint("LEFT", 4, 0)
  summaryText:SetJustifyH("LEFT")
  frame.summaryText = summaryText
  focusText = toolbar:CreateFontString(nil, "OVERLAY", "MSSFontBody")
  focusText:SetPoint("RIGHT", -4, 0)
  focusText:SetJustifyH("RIGHT")

  attemptsBar = CreateFrame("Frame", nil, toolbar)
  attemptsBar:SetAllPoints()
  local previous
  for _, kind in ipairs(FILTER_KINDS) do
    local key = kind == "all" and "attempts_filter_all" or ("kind_" .. kind)
    local b = Skin.Chip(attemptsBar, 22)
    local function apply()
      b:SetText(L[key])
      b:SetWidth(math.max(44, b.label:GetStringWidth() + 22))
    end
    apply()
    relabel[#relabel + 1] = apply
    b.kind = kind
    b:SetScript("OnClick", function(self)
      UI.FilterAttempts(self.kind)
    end)
    if previous then
      b:SetPoint("LEFT", previous, "RIGHT", 6, 0)
    else
      b:SetPoint("LEFT", 2, 0)
    end
    filterButtons[#filterButtons + 1] = b
    previous = b
  end
  hideZeroCheck = Skin.Check(attemptsBar, false)
  hideZeroCheck:SetPoint("LEFT", previous, "RIGHT", 14, 0)
  Label(hideZeroCheck.Text, "attempts_hide_zero")
  hideZeroCheck:SetScript("OnClick", function(self)
    AttemptsState().hideZero = self:GetChecked() and true or false
    listBox:ScrollToBegin()
    UI.Refresh()
  end)
  attemptsCount = attemptsBar:CreateFontString(nil, "OVERLAY", "MSSFontSmall")
  attemptsCount:SetPoint("RIGHT", -4, 0)

  logBar = CreateFrame("Frame", nil, toolbar)
  logBar:SetAllPoints()
  local manualLabel = logBar:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  manualLabel:SetPoint("LEFT", 4, 0)
  Label(manualLabel, "manual_label")

  editBox = Skin.EditBox(logBar, 220, 24)
  editBox:SetPoint("LEFT", manualLabel, "RIGHT", 12, 0)
  editBox:SetMaxLetters(60)
  editBox:SetScript("OnEnterPressed", SubmitManual)
  editBox:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)

  local addButton = MakeButton(logBar, "button_mark", 100, "tip_mark", SubmitManual)
  addButton:SetPoint("LEFT", editBox, "RIGHT", 8, 0)

  local scanButton = MakeButton(logBar, "button_scan", 156, "tip_scan", function() ns.RequestScan() end)
  scanButton:SetPoint("LEFT", addButton, "RIGHT", 6, 0)

  local line = toolbar:CreateTexture(nil, "ARTWORK")
  line:SetPoint("BOTTOMLEFT", -5, -2)
  line:SetPoint("BOTTOMRIGHT", 5, -2)
  line:SetHeight(1)
  line:SetColorTexture(1, 1, 1, 0.06)
end

local function CreateWindow()
  -- маленький экран или крупный масштаб: окно уже, столбцы названий отдают ширину сами
  local screen = UIParent:GetWidth() or 0
  local width = screen > 0 and math.min(WIDTH, screen - 20) or WIDTH
  listWidth = width - SIDE_WIDTH - 62
  frame = Skin.Window(WINDOW_NAME, width, HEIGHT, "HIGH")
  frame.titleText:SetText("Mount Sheet Sync")
  frame:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    self.SnapToPixels(self)
    local point, _, relativePoint, x, y = self:GetPoint()
    ns.GetDB().window = { point = point, relativePoint = relativePoint, x = x, y = y }
  end)

  -- Окно — на целых пикселях экрана: по центру его край часто попадает между пикселями, и текст в нём мутный.
  local function SnapToPixels(self)
    local left, top, scale = self:GetLeft(), self:GetTop(), self:GetEffectiveScale()
    if not (left and top and scale and scale > 0 and GetPhysicalScreenSize) then
      return
    end
    local _, height = GetPhysicalScreenSize()
    if not height or height <= 0 then
      return
    end
    local pixel = 768 / height / scale   -- один пиксель экрана в единицах окна
    self:ClearAllPoints()
    self:SetPoint("TOPLEFT", UIParent, "BOTTOMLEFT", math.floor(left / pixel + 0.5) * pixel, math.floor(top / pixel + 0.5) * pixel)
  end
  frame.SnapToPixels = SnapToPixels

  local pos = ns.GetDB().window
  if type(pos) == "table" and pos.point then
    frame:SetPoint(pos.point, UIParent, pos.relativePoint or pos.point, pos.x or 0, pos.y or 0)
  else
    frame:SetPoint("CENTER")
  end

  -- Шапка: одной строкой, чтобы не отнимать высоту у списка
  header1 = frame:CreateFontString(nil, "OVERLAY", "MSSFontStrong")
  header1:SetPoint("LEFT", frame, "TOPLEFT", 16, -48)
  header2 = frame:CreateFontString(nil, "OVERLAY", "MSSFontGold")
  header2:SetPoint("RIGHT", frame, "TOPRIGHT", -16, -48)

  -- Слева — все персонажи из таблицы
  local charsTitle = frame:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  charsTitle:SetPoint("LEFT", frame, "TOPLEFT", 18, -80)
  Label(charsTitle, "side_title")
  local side = Skin.Panel(frame)
  side:SetPoint("TOPLEFT", 12, -94)
  side:SetPoint("BOTTOMLEFT", 12, 44)
  side:SetWidth(SIDE_WIDTH)
  charBox = CreateList(side, CHAR_HEIGHT, InitCharRow)
  charBox:SetPoint("TOPLEFT", 4, -4)
  charBox:SetPoint("BOTTOMRIGHT", -14, 4)

  -- Справа — список, вкладки над ним (низ вкладок уходит под панель: у неё кадр выше)
  local inset = Skin.Panel(frame)
  inset:SetFrameLevel(frame:GetFrameLevel() + 5)
  inset:SetPoint("TOPLEFT", side, "TOPRIGHT", 10, 0)
  inset:SetPoint("BOTTOMRIGHT", -12, 44)

  frame.minTabWidth = 90
  for i, key in ipairs(TAB_LABELS) do
    local tab = Skin.Tab(frame)
    tab:SetID(i)
    tab:SetText(L[key])
    Skin.TabWidth(tab, frame.minTabWidth)
    if i ~= TAB_LEFT then   -- у «Осталось» заголовок с числом ставит UpdateTabTitle
      relabel[#relabel + 1] = function()
        tab:SetText(L[key])
        Skin.TabWidth(tab, frame.minTabWidth)
      end
    end
    tab:SetScript("OnClick", function(self)
      UI.SelectTab(self:GetID())
    end)
    if i == 1 then
      tab:SetPoint("BOTTOMLEFT", inset, "TOPLEFT", 8, 0)
    else
      tab:SetPoint("BOTTOMLEFT", tabs[i - 1], "BOTTOMRIGHT", 4, 0)
    end
    tabs[i] = tab
  end
  local selected = TAB_LEFT
  for i, key in ipairs(TAB_KEYS) do
    if ns.GetDB().tab == key then
      selected = i
    end
  end
  if ns.GetDB().tab == "all" then   -- до 3.0.3 «Все персонажи» были вкладкой
    selectedChar = ALL_CHARS
    ns.GetDB().tab = "left"
  end
  ShowTabState(selected)

  CreateToolbar(inset)

  headRow = CreateFrame("Frame", nil, inset)
  headRow:SetPoint("TOPLEFT", 6, -TOOLBAR_HEIGHT)
  headRow:SetPoint("RIGHT", -22, 0)
  headRow:SetHeight(HEAD_HEIGHT)
  local headLine = headRow:CreateTexture(nil, "ARTWORK")
  headLine:SetPoint("BOTTOMLEFT")
  headLine:SetPoint("BOTTOMRIGHT")
  headLine:SetHeight(1)
  headLine:SetColorTexture(Skin.COLOR.line[1], Skin.COLOR.line[2], Skin.COLOR.line[3])

  listBox = CreateList(inset, ROW_HEIGHT, InitRow)
  listBox:SetPoint("TOPLEFT", 6, -TOOLBAR_HEIGHT)
  listBox:SetPoint("BOTTOMRIGHT", -22, 6)

  columnGlow = inset:CreateTexture(nil, "OVERLAY")
  columnGlow:SetColorTexture(1, 0.82, 0, 0.1)
  columnGlow:Hide()

  gridSlider = CreateGridSlider(inset)
  frame.gridSlider = gridSlider

  emptyText = inset:CreateFontString(nil, "OVERLAY", "MSSFontDim")
  emptyText:SetPoint("CENTER", 0, 14)
  emptyText:SetWidth(width - SIDE_WIDTH - 80)

  -- адрес сайта с программой: только выделить и скопировать (Ctrl+C), изменить нельзя
  urlBox = Skin.EditBox(inset, 240, 24)
  urlBox:SetPoint("TOP", emptyText, "BOTTOM", 0, -10)
  urlBox:SetText(ns.PROGRAM_URL)
  urlBox:SetCursorPosition(0)
  urlBox:SetScript("OnTextChanged", function(self, userInput)
    if userInput and self:GetText() ~= ns.PROGRAM_URL then
      self:SetText(ns.PROGRAM_URL)
      self:HighlightText()
    end
  end)
  urlBox:SetScript("OnEditFocusGained", function(self) self:HighlightText() end)
  urlBox:SetScript("OnMouseUp", function(self) self:HighlightText() end)
  urlBox:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  urlBox:SetScript("OnEnterPressed", function(self) self:ClearFocus() end)
  urlBox:Hide()

  -- Внизу: отправка в таблицу, подсказка, настройки; редкое (чат, очистка журнала, отчёт об ошибке) — в настройках
  local syncButton = MakeButton(frame, "button_sync", 156, "tip_sync", function() ns.SyncNow() end)
  syncButton:SetPoint("BOTTOMLEFT", 12, 10)

  local gear = CreateFrame("Button", nil, frame)
  gear:SetSize(26, 26)
  gear:SetPoint("BOTTOMRIGHT", -12, 10)
  Skin.Fill(gear, "Round3", Skin.COLOR.editLine, 0, 0)
  Skin.Fill(gear, "Round3", Skin.COLOR.raised, 1, 1)
  local gearIcon = gear:CreateTexture(nil, "ARTWORK")
  gearIcon:SetSize(20, 20)
  gearIcon:SetPoint("CENTER")
  gearIcon:SetTexture(Skin.GEAR)
  gearIcon:SetVertexColor(0.85, 0.83, 0.77)
  gear:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")
  gear:SetScript("OnClick", function() UI.ToggleSettings() end)
  AttachTooltip(gear, "settings_title", "tip_settings")

  -- Без программы (или пока она не подключена к таблице) строка заметнее: значок предупреждения, оранжевый текст;
  -- аддон и так работает сам по себе, подробности — при наведении.
  -- Вид строки — по состоянию программы на момент показа окна (и смены языка): окно живёт весь сеанс.
  local hint = frame:CreateFontString(nil, "OVERLAY", "MSSFontSmall")
  hint:SetJustifyH("LEFT")
  hint:SetWordWrap(false)
  local warn = frame:CreateTexture(nil, "OVERLAY")
  warn:SetSize(16, 16)
  warn:SetTexture("Interface\\DialogFrame\\UI-Dialog-Icon-AlertNew")
  warn:SetPoint("LEFT", syncButton, "RIGHT", 10, 0)
  frame.offlineIcon = warn
  local hover = CreateFrame("Frame", nil, frame)
  hover:SetPoint("TOPLEFT", hint, "TOPLEFT", 0, 2)
  hover:SetPoint("BOTTOMRIGHT", hint, "BOTTOMRIGHT", 0, -2)
  hover:EnableMouse(true)
  local function ShowProgramState()
    local state = ns.ProgramState()
    local online = state == "online"
    hint:SetFontObject(online and MSSFontSmall or MSSFontWarn)
    hint:ClearAllPoints()
    hint:SetPoint("LEFT", online and syncButton or warn, "RIGHT", online and 10 or 6, 0)
    hint:SetPoint("RIGHT", gear, "LEFT", -10, 0)
    hint:SetText(L[online and "window_hint" or ("window_" .. state)])
    warn:SetShown(not online)
    hover:SetShown(not online)
    if not online then
      local old = state == "oldaddon" or state == "oldprogram"
      AttachTooltip(hover, old and "update_title" or "offline_title", "tip_" .. state)
    end
  end
  ShowProgramState()
  relabel[#relabel + 1] = ShowProgramState
  frame.ShowProgramState = ShowProgramState

  -- другой язык — списки собрать заново
  relabel[#relabel + 1] = function()
    shownList, shownChars = nil, nil
  end

  frame:SetScript("OnShow", function(self)
    SnapToPixels(self)
    self.ShowProgramState()
    UI.Refresh()
  end)
  local elapsedTotal = 0
  frame:SetScript("OnUpdate", function(_, elapsed)
    elapsedTotal = elapsedTotal + elapsed
    if elapsedTotal > 30 then   -- раз в 30 с — только шапка («до сброса недели»)
      elapsedTotal = 0
      UI.RefreshHeader()
    end
  end)
  frame:Hide()
end

-- Изменения журнала перерисовывают окно не чаще раза за кадр:
-- скан, добавивший 60 записей, — одна перерисовка, а не 60.
function UI.RequestRefresh()
  if refreshQueued or not frame or not frame:IsShown() then
    return
  end
  refreshQueued = true
  C_Timer.After(0, function()
    refreshQueued = false
    UI.Refresh()
  end)
end

function UI.Toggle()
  if not ns.GetDB() then
    return
  end
  if not frame then
    -- если окно не удалось создать, ошибка один раз появится в чате, игра не сломается
    if not ns.SafeCall(CreateWindow) or not frame then
      frame = nil
      return
    end
  end
  if frame:IsShown() then
    frame:Hide()
  else
    frame:Show()
  end
end

ns.ToggleWindow = UI.Toggle
ns.ConfirmResetWeek = UI.ConfirmResetWeek
ns.OnChange = UI.RequestRefresh

---------------------------------------------------------------------------
-- Настройки: язык, сообщения в чате, кнопка у миникарты (ПКМ по иконке, /mss settings)
---------------------------------------------------------------------------
local SETTINGS_NAME = "MountSheetSyncSettingsFrame"
local settingsFrame
local languageButtons = {}
local settingsQuiet, settingsMinimap
local gridButtons = {}

local function RefreshSettings()
  if not settingsFrame then
    return
  end
  local chosen = ns.LanguageSetting()
  for _, b in ipairs(languageButtons) do
    b:SetChecked(b.code == chosen)
  end
  local db = ns.GetDB()
  settingsQuiet:SetChecked(db and db.quiet and true or false)
  local mm = db and type(db.minimap) == "table" and db.minimap or {}
  settingsMinimap:SetChecked(not mm.hide)
  for _, b in ipairs(gridButtons) do
    b:SetChecked(b.view == (db and db.gridView or "scroll"))
  end
end

local function SettingsCheck(parent, y, key, onClick)
  local c = Skin.Check(parent, false)
  c:SetPoint("TOPLEFT", 20, y)
  Label(c.Text, key)
  c:SetScript("OnClick", onClick)
  return c
end

---------------------------------------------------------------------------
-- Сведения для отчёта об ошибке (кнопка в настройках): текст, который можно скопировать в сообщение на GitHub.
-- Без личного: ни имён персонажей, ни сервера, ни таблицы — только версии, состояние и числа.
---------------------------------------------------------------------------
local REPORT_NAME = "MountSheetSyncReportFrame"
local reportFrame, reportBox

local function Ago(stamp)
  local t = tonumber(stamp)
  if not t or t <= 0 then
    return "never"
  end
  local s = time() - t
  if s < 120 then
    return string.format("%d s ago", s)
  elseif s < 7200 then
    return string.format("%d min ago", math.floor(s / 60))
  elseif s < 172800 then
    return string.format("%d h ago", math.floor(s / 3600))
  end
  return string.format("%d days ago", math.floor(s / 86400))
end

local function Count(t)
  local n = 0
  if type(t) == "table" then
    for _ in pairs(t) do
      n = n + 1
    end
  end
  return n
end

function ns.BugReport()
  local db = ns.GetDB() or {}
  local lines = {}
  local function add(fmt, ...)
    lines[#lines + 1] = string.format(fmt, ...)
  end
  add("Mount Sheet Sync bug report")
  local version, build, _, toc = "?", "?", nil, "?"
  if GetBuildInfo then
    version, build, _, toc = GetBuildInfo()
  end
  add("Game: %s (build %s, interface %s), client %s", tostring(version), tostring(build), tostring(toc), tostring(GetLocale and GetLocale() or "?"))
  local mm = type(db.minimap) == "table" and db.minimap or {}
  add("Addon: %s, language %s (setting %s), chat messages %s, minimap button %s", tostring(ns.VERSION), tostring(ns.CurrentLanguage()),
    tostring(ns.LanguageSetting()), db.quiet and "off" or "on", mm.hide and "hidden" or "shown")
  local p = type(MountSheetSyncProgram) == "table" and MountSheetSyncProgram or nil
  add("Program: %s%s", ns.ProgramState(), p and string.format(" (version %s, sheet %s, last mark %s)", tostring(p.version),
    p.sheet == false and "not set up" or "connected", Ago(p.seen)) or " (no mark from the program)")
  local data = type(MountSheetSyncSheet) == "table" and MountSheetSyncSheet or nil
  if data then
    local rows = 0
    for _, ch in ipairs(type(data.chars) == "table" and data.chars or {}) do
      rows = rows + (type(ch) == "table" and type(ch.items) == "table" and #ch.items or 0)
    end
    add("List from the sheet: %s by program %s, characters %d, rows %d, attempt rows %s", Ago(data.generated), tostring(data.version),
      type(data.chars) == "table" and #data.chars or 0, rows, type(data.mounts) == "table" and tostring(#data.mounts) or "none")
  else
    add("List from the sheet: none")
  end
  local kills = type(db.kills) == "table" and db.kills or {}
  local kinds = {}
  for _, rec in ipairs(kills) do
    local k = tostring(rec.kind or "?")
    kinds[k] = (kinds[k] or 0) + 1
  end
  local parts = {}
  for k, n in pairs(kinds) do
    parts[#parts + 1] = k .. " " .. n
  end
  table.sort(parts)
  local last = kills[#kills]
  add("Journal: %d records (%s), this week %d, last %s", #kills, #parts > 0 and table.concat(parts, ", ") or "-",
    ns.CountThisWeek(), last and string.format("%s, %s", tostring(last.kind), Ago(last.time)) or "-")
  local col = type(db.collection) == "table" and db.collection or {}
  add("Characters remembered: %d; collection: %s mounts, %s", Count(db.chars), tostring(col.collected or "?"),
    col.updated and ("read " .. Ago(col.updated)) or "not read yet")
  local loaded = 0
  local getNum = C_AddOns and C_AddOns.GetNumAddOns
  local isLoaded = C_AddOns and C_AddOns.IsAddOnLoaded
  if getNum and isLoaded then
    for i = 1, getNum() do
      if isLoaded(i) then
        loaded = loaded + 1
      end
    end
  end
  add("Addons loaded: %d", loaded)
  local err = ns.lastError
  add("Last addon error: %s", err and string.format("%s (%s)", err.text, Ago(err.time)) or "none this session")
  return table.concat(lines, "\n")
end

local function ShowReport()
  if not reportFrame then
    reportFrame = Skin.Window(REPORT_NAME, 520, 300, "DIALOG")
    reportFrame:SetPoint("CENTER", 0, 40)
    local function title()
      reportFrame.titleText:SetText(L.report_title)
    end
    title()
    relabel[#relabel + 1] = title
    local hint = reportFrame:CreateFontString(nil, "OVERLAY", "MSSFontBody")
    hint:SetPoint("TOPLEFT", 18, -38)
    hint:SetPoint("TOPRIGHT", -18, -38)
    hint:SetJustifyH("LEFT")
    local function hintText()
      hint:SetText(string.format(L.report_hint, ns.ISSUES_URL))
    end
    hintText()
    relabel[#relabel + 1] = hintText
    local scroll = CreateFrame("ScrollFrame", nil, reportFrame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 16, -76)
    scroll:SetPoint("BOTTOMRIGHT", -34, 16)
    reportBox = CreateFrame("EditBox", nil, scroll)
    reportBox:SetMultiLine(true)
    reportBox:SetFontObject(ChatFontNormal)
    reportBox:SetWidth(460)
    reportBox:SetAutoFocus(false)
    reportBox:SetScript("OnEscapePressed", function()
      reportFrame:Hide()
    end)
    reportBox:SetScript("OnTextChanged", function(self, userInput)
      if userInput then
        self:SetText(reportFrame.text or "")   -- только для чтения: правка возвращается
        self:HighlightText()
      end
    end)
    scroll:SetScrollChild(reportBox)
  end
  reportFrame.text = ns.BugReport()
  reportBox:SetText(reportFrame.text)
  reportFrame:Show()
  reportBox:SetFocus()
  reportBox:HighlightText()
end
UI.ShowReport = ShowReport

local function CreateSettings()
  local codes = { "auto" }
  for _, code in ipairs(ns.LANGUAGES) do
    codes[#codes + 1] = code
  end
  local height = 100 + #codes * 24 + 70 + 96 + 86
  settingsFrame = Skin.Window(SETTINGS_NAME, 330, height, "DIALOG")
  settingsFrame:SetPoint("CENTER", 0, 60)
  local function title()
    settingsFrame.titleText:SetText(L.settings_title)
  end
  title()
  relabel[#relabel + 1] = title

  local head = settingsFrame:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  head:SetPoint("TOPLEFT", 20, -40)
  Label(head, "settings_language")

  for i, code in ipairs(codes) do
    local b = Skin.Check(settingsFrame, true)
    b:SetPoint("TOPLEFT", 26, -62 - (i - 1) * 24)
    b.code = code
    local function text()
      if code == "auto" then
        b.Text:SetText(string.format(L.settings_auto, ns.LANGUAGE_NAMES[ns.ClientLanguage()]))
      else
        b.Text:SetText(ns.LANGUAGE_NAMES[code])
      end
    end
    text()
    relabel[#relabel + 1] = text
    b:SetScript("OnClick", function()
      ns.SetLanguage(code)   -- надписи сменятся сразу (ns.OnLanguageChanged)
      RefreshSettings()
    end)
    languageButtons[#languageButtons + 1] = b
  end

  local gridHead = settingsFrame:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  gridHead:SetPoint("TOPLEFT", 20, -62 - #codes * 24 - 10)
  Label(gridHead, "settings_grid")
  for i, choice in ipairs({ { "scroll", "settings_grid_scroll", "tip_grid_scroll" }, { "all", "settings_grid_all", "tip_grid_all" } }) do
    local b = Skin.Check(settingsFrame, true)
    b:SetPoint("TOPLEFT", 26, -62 - #codes * 24 - 32 - (i - 1) * 24)
    b.view = choice[1]
    Label(b.Text, choice[2])
    AttachTooltip(b, choice[2], choice[3])
    b:SetScript("OnClick", function(self)
      UI.SetGridView(self.view)
      RefreshSettings()
    end)
    gridButtons[#gridButtons + 1] = b
  end

  local y = -62 - #codes * 24 - 96
  settingsQuiet = SettingsCheck(settingsFrame, y, "quiet", function(self)
    ns.SetQuiet(self:GetChecked())
  end)
  AttachTooltip(settingsQuiet, "quiet", "tip_quiet")
  settingsMinimap = SettingsCheck(settingsFrame, y - 26, "settings_minimap", function(self)
    local db = ns.GetDB()
    local shown = not (db and type(db.minimap) == "table" and db.minimap.hide)
    if shown ~= (self:GetChecked() and true or false) then
      ns.ToggleMinimapButton()
    end
  end)

  local reportButton = MakeButton(settingsFrame, "button_report", 200, "tip_report", function()
    ns.SafeCall(ShowReport)
  end)
  reportButton:SetPoint("BOTTOMLEFT", 20, 16)

  local clearButton = MakeButton(settingsFrame, "button_clear", 200, "tip_clear", ConfirmClear)
  clearButton:SetPoint("BOTTOMLEFT", 20, 46)

  local resetButton = MakeButton(settingsFrame, "button_reset", 200, "tip_reset", UI.ConfirmResetWeek)
  resetButton:SetPoint("BOTTOMLEFT", 20, 76)

  settingsFrame:SetScript("OnShow", RefreshSettings)
  settingsFrame:Hide()
end

function UI.ToggleSettings()
  if not ns.GetDB() then
    return
  end
  if not settingsFrame then
    if not ns.SafeCall(CreateSettings) or not settingsFrame then
      settingsFrame = nil
      return
    end
  end
  if settingsFrame:IsShown() then
    settingsFrame:Hide()
  else
    settingsFrame:Show()
  end
end

ns.ToggleSettings = UI.ToggleSettings

-- Смена языка: все надписи заново, окно перерисовать.
ns.OnLanguageChanged(function()
  for _, fn in ipairs(relabel) do
    fn()
  end
  if frame and frame:IsShown() then
    UI.Refresh()
  end
  RefreshSettings()
end)

---------------------------------------------------------------------------
-- Иконка для HidingBar и других панелей LibDataBroker
---------------------------------------------------------------------------
local LDB = LibStub and LibStub("LibDataBroker-1.1", true)
if LDB then
  LDB:NewDataObject(ADDON_NAME, {
    type = "launcher",
    label = "Mount Sheet Sync",
    icon = ICON,
    OnClick = function(_, button)
      if button == "RightButton" then
        UI.ToggleSettings()
      else
        UI.Toggle()
      end
    end,
    OnTooltipShow = function(tooltip)
      FillBrokerTooltip(tooltip)
    end,
  })
end

---------------------------------------------------------------------------
-- Меню аддонов у миникарты (строки AddonCompartmentFunc в .toc)
---------------------------------------------------------------------------
function MountSheetSync_OnAddonCompartmentClick(_, button)
  if button == "RightButton" then
    UI.ToggleSettings()
  else
    UI.Toggle()
  end
end

function MountSheetSync_OnAddonCompartmentEnter(_, button)
  GameTooltip:SetOwner(button, "ANCHOR_LEFT")
  FillBrokerTooltip(GameTooltip)
  GameTooltip:Show()
end

function MountSheetSync_OnAddonCompartmentLeave()
  GameTooltip:Hide()
end

---------------------------------------------------------------------------
-- Кнопка у миникарты. HidingBar по умолчанию собирает именно кнопки миникарты
-- (иконки DataBroker — только с отдельной галочкой), поэтому кнопка нужна своя.
-- Создаётся при загрузке файла, чтобы HidingBar увидел её при своём запуске.
-- Без таких панелей она стоит на краю миникарты; её можно перетащить мышью.
---------------------------------------------------------------------------
local DEFAULT_ANGLE = 200
local minimapButton, dragging

local function MinimapSettings()
  local db = ns.GetDB()
  if not db then
    return nil
  end
  if type(db.minimap) ~= "table" then
    db.minimap = {}
  end
  return db.minimap
end

local function PlaceMinimapButton()
  if not minimapButton then
    return
  end
  local settings = MinimapSettings()
  local angle = math.rad(settings and tonumber(settings.angle) or DEFAULT_ANGLE)
  local x, y = math.cos(angle), math.sin(angle)
  local shape = GetMinimapShape and GetMinimapShape() or "ROUND"
  if shape == "SQUARE" then
    -- на квадратной миникарте — по краю квадрата, а не круга
    x = math.max(-1, math.min(1, x * 1.4142))
    y = math.max(-1, math.min(1, y * 1.4142))
  end
  minimapButton:ClearAllPoints()
  minimapButton:SetPoint("CENTER", Minimap, "CENTER", x * (Minimap:GetWidth() / 2 + 5), y * (Minimap:GetHeight() / 2 + 5))
end

local function DragMinimapButton()
  local settings = MinimapSettings()
  local mx, my = Minimap:GetCenter()
  if not settings or not mx then
    return
  end
  local scale = Minimap:GetEffectiveScale()
  local cx, cy = GetCursorPosition()
  settings.angle = math.floor(math.deg(math.atan2(cy / scale - my, cx / scale - mx)) % 360 + 0.5)
  PlaceMinimapButton()
end

local function CreateMinimapButton()
  if not Minimap or minimapButton then
    return
  end
  local b = CreateFrame("Button", "MountSheetSyncMinimapButton", Minimap)
  b:SetSize(31, 31)
  b:SetFrameStrata("MEDIUM")
  b:SetFrameLevel(8)
  b:RegisterForClicks("AnyUp")
  b:RegisterForDrag("LeftButton")
  b:SetHighlightTexture(136477)   -- Interface\Minimap\UI-Minimap-ZoomButton-Highlight
  local background = b:CreateTexture(nil, "BACKGROUND")
  background:SetSize(24, 24)
  background:SetTexture(136467)   -- Interface\Minimap\UI-Minimap-Background
  background:SetPoint("CENTER")
  local icon = b:CreateTexture(nil, "ARTWORK")
  icon:SetSize(18, 18)
  icon:SetTexture(ICON)
  icon:SetTexCoord(0.05, 0.95, 0.05, 0.95)
  icon:SetPoint("CENTER")
  b.icon = icon
  local border = b:CreateTexture(nil, "OVERLAY")
  border:SetSize(50, 50)
  border:SetTexture(136430)       -- Interface\Minimap\MiniMap-TrackingBorder
  border:SetPoint("TOPLEFT")
  b.border = border
  b:SetScript("OnClick", function(_, button)
    if button == "RightButton" then
      UI.ToggleSettings()
    else
      UI.Toggle()
    end
  end)
  b:SetScript("OnEnter", function(self)
    if dragging then
      return
    end
    GameTooltip:SetOwner(self, "ANCHOR_LEFT")
    FillBrokerTooltip(GameTooltip)
    GameTooltip:AddLine(L.tip_drag, 0.6, 0.6, 0.6)
    GameTooltip:Show()
  end)
  b:SetScript("OnLeave", function()
    GameTooltip:Hide()
  end)
  b:SetScript("OnDragStart", function(self)
    dragging = true
    GameTooltip:Hide()
    self:SetScript("OnUpdate", DragMinimapButton)
  end)
  b:SetScript("OnDragStop", function(self)
    dragging = false
    self:SetScript("OnUpdate", nil)
  end)
  minimapButton = b
  PlaceMinimapButton()
end

CreateMinimapButton()

-- После загрузки сохранений: место кнопки и «спрятана ли она» (/mss minimap).
function ns.SetupMinimapButton()
  if not minimapButton then
    return
  end
  local settings = MinimapSettings()
  minimapButton:SetShown(not (settings and settings.hide))
  PlaceMinimapButton()
end

-- /mss minimap: спрятать или вернуть кнопку у миникарты.
function ns.ToggleMinimapButton()
  local settings = MinimapSettings()
  if not settings or not minimapButton then
    return nil
  end
  settings.hide = not settings.hide or nil
  ns.SetupMinimapButton()
  return not settings.hide
end
