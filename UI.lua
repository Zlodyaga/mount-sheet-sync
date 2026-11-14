--[[
Mount Sheet Sync 3.0.7 — окно, иконка и настройки: основа
==========================================================
Окно разнесено по файлам (порядок — как в .toc):
  UI.lua           — эта основа: размеры и константы, мелкие помощники (кнопки, подсказки, подтверждения), короткие названия,
                     открытие достижения, маунта и Атласа;
  UI.Route.lua     — путь к боссу: вход на карте, запасные точки, закрытые входы, Mapzeroth;
  UI.Rows.lua      — строки списков: ячейки, сетка «Все персонажи», щелчки и подсказки, левая панель персонажей;
  UI.Views.lua     — столбцы и содержимое вкладок: «Осталось», сетка, «Достижения», «Попытки», «Журнал», «Выпавшие»;
  UI.Window.lua    — рамка окна, шапка таблицы, UI.Refresh, выбор вкладки и персонажа, UI.Toggle;
  UI.Settings.lua  — окно настроек и сведения для отчёта об ошибке;
  UI.Minimap.lua   — значок LibDataBroker, меню аддонов у миникарты, своя кнопка.

Общее между файлами — три таблицы:
  UI (ns.UI)  — то, что зовут снаружи: UI.Toggle, UI.Refresh, UI.SelectTab, UI.ToggleSettings…;
  S (UI.state)   — состояние окна, которое меняется по ходу: кадры (S.frame, S.listBox…), выбранный персонаж, что сейчас
                   показано. Меняется — значит, локальной копией в другом файле его не взять;
  P (UI.private) — функции, константы и таблицы, которые определены один раз и нужны нескольким файлам. Файл отдаёт их
                   в конце («нужны следующим файлам окна») и берёт в начале (local Имя = P.Имя).
Так сделано из-за предела Lua 5.1: не больше 200 локальных переменных в одном файле (в одном UI.lua их было 196).
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
local TAB_LEFT, TAB_ACH, TAB_ATTEMPTS, TAB_LOG, TAB_DROPS = 1, 2, 3, 4, 5
local TAB_KEYS = { "left", "ach", "attempts", "log", "drops" }
local TAB_LABELS = { "tab_left", "tab_ach", "tab_attempts", "tab_log", "tab_drops" }
local ALL_CHARS = {}      -- selectedChar: первая строка слева, «Все персонажи»
local FILTER_KINDS = { "all", "raid", "dungeon", "world", "rare" }

local UI = {}
ns.UI = UI                       -- остальные файлы окна берут таблицу отсюда
UI.state, UI.private = {}, {}    -- S: общее состояние окна; P: функции и таблицы, нужные нескольким файлам
local S, P = UI.state, UI.private
local L = ns.L
local Skin = ns.Skin
local relabel = {}      -- функции, которые заново ставят надписи (смена языка)
-- Состояние окна (S): кадры S.frame, S.listBox, S.charBox, S.emptyText, S.header1, S.header2, S.editBox, S.urlBox,
-- S.toolbar, S.summaryText, S.focusText, S.attemptsBar, S.attemptsCount, S.logBar, S.headRow, S.gridSlider, S.columnGlow,
-- S.hideZeroCheck — их создаёт UI.Window.lua; ниже — остальное.
S.listWidth = WIDTH - SIDE_WIDTH - 62
local tabs = {}
local filterButtons = {}
local headCells = {}
S.refreshQueued = false
-- S.selectedChar — имя персонажа из таблицы, выбранного слева; nil — текущий; ALL_CHARS — все
-- S.shownList, S.shownChars — что сейчас в списках: перестраиваем только при изменении
local grid = { chars = {}, offset = 0, shown = 0, cell = GRID_SCROLL.cell, x = 0 }   -- сетка всех персонажей
-- S.shownHead — столбцы и подписи заголовка таблицы, что сейчас на экране

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
  local text = S.editBox:GetText()
  if ns.AddManual(text) then
    S.editBox:SetText("")
  end
  S.editBox:ClearFocus()
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
  if not (panel and panel:IsShown() and S.frame:IsShown()) then
    return
  end
  if not stepAsideHooked[panel] then
    stepAsideHooked[panel] = true
    panel:HookScript("OnHide", function(self)
      if stepAsideFor == self then
        stepAsideFor = nil
        S.frame:Show()
      end
    end)
  end
  stepAsideFor = panel
  S.frame:Hide()
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

-- нужны следующим файлам окна
P.ADDON_NAME, P.ICON, P.WINDOW_NAME, P.WIDTH, P.HEIGHT, P.SIDE_WIDTH = ADDON_NAME, ICON, WINDOW_NAME, WIDTH, HEIGHT, SIDE_WIDTH
P.ROW_HEIGHT, P.CHAR_HEIGHT, P.TOOLBAR_HEIGHT, P.HEAD_HEIGHT, P.SLIDER_ROOM, P.COLUMN_GAP = ROW_HEIGHT, CHAR_HEIGHT, TOOLBAR_HEIGHT, HEAD_HEIGHT, SLIDER_ROOM, COLUMN_GAP
P.CHANCE_BAR_WIDTH, P.GRID_SCROLL, P.GRID_ALL, P.MIN_TEXT_WIDTH, P.TAB_LEFT, P.TAB_ACH = CHANCE_BAR_WIDTH, GRID_SCROLL, GRID_ALL, MIN_TEXT_WIDTH, TAB_LEFT, TAB_ACH
P.TAB_DROPS, P.TAB_ATTEMPTS, P.TAB_KEYS, P.TAB_LABELS, P.ALL_CHARS, P.FILTER_KINDS, P.L = TAB_DROPS, TAB_ATTEMPTS, TAB_KEYS, TAB_LABELS, ALL_CHARS, FILTER_KINDS, L
P.Skin, P.relabel, P.tabs, P.filterButtons, P.headCells, P.grid = Skin, relabel, tabs, filterButtons, headCells, grid
P.FormatDuration, P.AttachTooltip, P.Label, P.MakeButton, P.FormatRow, P.FillBrokerTooltip = FormatDuration, AttachTooltip, Label, MakeButton, FormatRow, FillBrokerTooltip
P.ConfirmClear, P.SubmitManual, P.Safe, P.FirstLetters, P.ShortDiff, P.ShortBoss = ConfirmClear, SubmitManual, Safe, FirstLetters, ShortDiff, ShortBoss
P.BossAndDiff, P.StepAside, P.ShowAchievement, P.ShowMountInJournal, P.ShowInGuide = BossAndDiff, StepAside, ShowAchievement, ShowMountInJournal, ShowInGuide
