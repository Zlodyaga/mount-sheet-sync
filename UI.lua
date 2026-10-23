--[[
Mount Sheet Sync 2.3.0 — окно, иконка и настройки
==================================================
  * окно (ЛКМ по иконке или /mss): вкладки «Журнал» (записи аддона) и «Осталось»
    (что текущему персонажу ещё убить на этой неделе — из таблицы), все действия кнопками;
  * кнопка у миникарты — её забирают к себе HidingBar и похожие панели;
  * иконка LibDataBroker — для Titan Panel, ChocolateBar и т. п.;
  * пункт в меню аддонов у миникарты (Addon Compartment);
  * настройки (ПКМ по иконке или /mss settings): язык, сообщения в чате, кнопка у миникарты.
Все надписи — из ns.L; при смене языка окно и настройки перерисовываются сразу.
]]

local ADDON_NAME, ns = ...

local ICON = "Interface\\Icons\\Ability_Mount_RidingHorse"
local WINDOW_NAME = "MountSheetSyncFrame"
local WIDTH, HEIGHT = 600, 520
local ROW_HEIGHT = 16

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

local TAB_LOG, TAB_LEFT = 1, 2

local UI = {}
local L = ns.L
local relabel = {}      -- функции, которые заново ставят надписи (смена языка)
local frame, scroll, content, emptyText, header1, header2, quietCheck, editBox
local tabs = {}
local rows = {}
local rowButtons = {}   -- невидимые кнопки поверх строк: правый щелчок по строке «Осталось»
local rowEntries = {}
local refreshQueued = false

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
  local button = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
  button:SetSize(width, 24)
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
-- Меню по правому щелчку на строке «Осталось»: не фармить эту строку или весь рейд / подземелье.
local function ShowRowMenu(button)
  local entry = rowEntries[button.index]
  if not entry or entry.done or not MenuUtil or not MenuUtil.CreateContextMenu then
    return
  end
  local item = entry.item
  MenuUtil.CreateContextMenu(button, function(owner, root)
    root:CreateTitle(item.boss or "?")
    root:CreateButton(L.menu_not_row, function()
      ns.AddPlan(item, "row")
    end)
    if item.place and item.place ~= "" then
      root:CreateButton(string.format(L.menu_not_place, item.place), function()
        ns.AddPlan(item, "place")
      end)
    end
  end)
end

local function CreateRow(index)
  local row = content:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
  row:SetPoint("TOPLEFT", content, "TOPLEFT", 4, -(index - 1) * ROW_HEIGHT)
  row:SetPoint("RIGHT", content, "RIGHT", -4, 0)
  row:SetHeight(ROW_HEIGHT)
  row:SetJustifyH("LEFT")
  row:SetWordWrap(false)
  rows[index] = row
  local b = CreateFrame("Button", nil, content)
  b:SetPoint("TOPLEFT", content, "TOPLEFT", 0, -(index - 1) * ROW_HEIGHT)
  b:SetPoint("RIGHT", content, "RIGHT", 0, 0)
  b:SetHeight(ROW_HEIGHT)
  b:RegisterForClicks("RightButtonUp")
  b:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight", "ADD")
  b.index = index
  b:SetScript("OnClick", ShowRowMenu)
  b:SetScript("OnEnter", function(self)
    if rowEntries[self.index] and not rowEntries[self.index].done then
      GameTooltip:SetOwner(self, "ANCHOR_CURSOR")
      GameTooltip:SetText(L.tip_row_menu, 1, 1, 1)
      GameTooltip:Show()
    end
  end)
  b:SetScript("OnLeave", function()
    GameTooltip:Hide()
  end)
  rowButtons[index] = b
  return row
end

function UI.RefreshHeader()
  local player = ns.GetPlayer()
  header1:SetText(string.format("%s — %s", player.name or "?", player.realm or "?"))
  local untilReset = ns.SecondsUntilReset()
  header2:SetText(string.format(L.header_week, ns.CountThisWeek(), untilReset and FormatDuration(untilReset) or "?"))
end

-- Вкладка «Журнал»: записи аддона, новые сверху.
local function JournalLines(db)
  local lines = {}
  for i = #db.kills, 1, -1 do
    lines[#lines + 1] = FormatRow(db.kills[i])
  end
  return lines, L.journal_empty
end

-- Вкладка «Осталось»: строки «Нет» из таблицы, убитые после обновления списка — с галочкой внизу группы.
local function RemainingLines(r, entries)
  if r.state == "nodata" then
    return {}, L.left_nodata
  end
  if r.state == "nochar" then
    local name = r.name or "?"
    return {}, string.format(L.left_nochar, name)
  end
  local lines = {
    "|cff8c8c8c" .. string.format(L.left_head, r.left, r.total, ns.FormatWhen(r.generated)) .. "|r",
  }
  local note = ns.RemainingNote(r)
  if note then
    lines[#lines + 1] = (r.afterReset and "|cff8cff8c" or "|cffff9a4d") .. note .. "|r"
  end
  for _, kind in ipairs(ns.KIND_ORDER) do
    local group = {}
    for _, entry in ipairs(r.items) do
      if entry.item.kind == kind then
        group[#group + 1] = entry
      end
    end
    if #group > 0 then
      lines[#lines + 1] = "|cffffd200" .. ns.KindTitle(kind) .. "|r"
      for pass = 1, 2 do   -- сначала то, что осталось, потом убитые
        for _, entry in ipairs(group) do
          if entry.done == (pass == 2) then
            lines[#lines + 1] = "   " .. ns.FormatRemaining(entry)
            if kind ~= "world" and not entry.done then
              entries[#lines] = entry
            end
          end
        end
      end
    end
  end
  if r.total == 0 then
    lines[#lines + 1] = "|cff8c8c8c" .. L.left_norows .. "|r"
  elseif r.left == 0 then
    lines[#lines + 1] = "|cff8cff8c" .. L.left_alldone .. "|r"
  end
  return lines
end

local function UpdateTabTitle(r)
  local tab = tabs[TAB_LEFT]
  local title = r.state == "ok" and string.format(L.tab_left_n, r.left) or L.tab_left
  if tab:GetText() ~= title then
    tab:SetText(title)
    PanelTemplates_TabResize(tab, 0, nil, frame.minTabWidth)
  end
end

function UI.Refresh()
  if not frame or not frame:IsShown() then
    return
  end
  local db = ns.GetDB()
  UI.RefreshHeader()

  local width = scroll:GetWidth()
  content:SetWidth(width and width > 0 and width or WIDTH - 60)

  local remaining = ns.GetRemaining()
  UpdateTabTitle(remaining)
  local lines, empty
  local entries = {}
  if frame.selectedTab == TAB_LEFT then
    lines, empty = RemainingLines(remaining, entries)
  else
    lines, empty = JournalLines(db)
  end
  rowEntries = entries
  for i, text in ipairs(lines) do
    local row = rows[i] or CreateRow(i)
    row:SetText(text)
    row:Show()
    rowButtons[i]:SetShown(entries[i] ~= nil)
  end
  for i = #lines + 1, #rows do
    rows[i]:Hide()
    rowButtons[i]:Hide()
  end
  content:SetHeight(math.max(1, #lines * ROW_HEIGHT))
  emptyText:SetText(empty or "")
  emptyText:SetShown(#lines == 0)
  quietCheck:SetChecked(db.quiet and true or false)
end

function UI.SelectTab(id)
  ns.GetDB().tab = id == TAB_LEFT and "left" or nil
  PanelTemplates_SetTab(frame, id)
  scroll:SetVerticalScroll(0)
  UI.Refresh()
end

local function CreateWindow()
  frame = CreateFrame("Frame", WINDOW_NAME, UIParent, "DefaultPanelTemplate")
  frame:SetSize(WIDTH, HEIGHT)
  frame:SetFrameStrata("HIGH")
  frame:SetToplevel(true)
  frame:SetClampedToScreen(true)
  frame:SetMovable(true)
  frame:EnableMouse(true)
  frame:RegisterForDrag("LeftButton")
  frame:SetScript("OnDragStart", frame.StartMoving)
  frame:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local point, _, relativePoint, x, y = self:GetPoint()
    ns.GetDB().window = { point = point, relativePoint = relativePoint, x = x, y = y }
  end)

  local pos = ns.GetDB().window
  if type(pos) == "table" and pos.point then
    frame:SetPoint(pos.point, UIParent, pos.relativePoint or pos.point, pos.x or 0, pos.y or 0)
  else
    frame:SetPoint("CENTER")
  end

  if frame.SetTitle then
    frame:SetTitle("Mount Sheet Sync")
  elseif frame.TitleContainer and frame.TitleContainer.TitleText then
    frame.TitleContainer.TitleText:SetText("Mount Sheet Sync")
  end
  CreateFrame("Button", nil, frame, "UIPanelCloseButtonDefaultAnchors")
  tinsert(UISpecialFrames, WINDOW_NAME)   -- закрывается по Esc

  -- Шапка
  header1 = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  header1:SetPoint("TOPLEFT", 18, -30)
  header2 = frame:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
  header2:SetPoint("TOPLEFT", 18, -48)

  -- Список: вкладки «Журнал» и «Осталось» над ним
  local inset = CreateFrame("Frame", nil, frame, "InsetFrameTemplate")
  inset:SetPoint("TOPLEFT", 12, -92)
  inset:SetPoint("BOTTOMRIGHT", -12, 146)

  frame.minTabWidth = 90
  for i, key in ipairs({ "tab_log", "tab_left" }) do
    local tab = CreateFrame("Button", nil, frame, "PanelTopTabButtonTemplate")
    tab:SetID(i)
    tab:SetText(L[key])
    PanelTemplates_TabResize(tab, 0, nil, frame.minTabWidth)
    if i == TAB_LOG then
      relabel[#relabel + 1] = function()
        tab:SetText(L.tab_log)
        PanelTemplates_TabResize(tab, 0, nil, frame.minTabWidth)
      end
    end
    tab:SetScript("OnClick", function(self)
      UI.SelectTab(self:GetID())
    end)
    tabs[i] = tab
  end
  tabs[TAB_LOG]:SetPoint("BOTTOMLEFT", inset, "TOPLEFT", 8, -2)
  PanelTemplates_SetNumTabs(frame, #tabs)   -- вторая вкладка встаёт справа от первой
  PanelTemplates_SetTab(frame, ns.GetDB().tab == "left" and TAB_LEFT or TAB_LOG)

  scroll = CreateFrame("ScrollFrame", nil, inset, "UIPanelScrollFrameTemplate")
  scroll:SetPoint("TOPLEFT", 6, -6)
  scroll:SetPoint("BOTTOMRIGHT", -28, 6)
  content = CreateFrame("Frame", nil, scroll)
  content:SetSize(WIDTH - 60, 1)
  scroll:SetScrollChild(content)

  emptyText = inset:CreateFontString(nil, "OVERLAY", "GameFontDisable")
  emptyText:SetPoint("CENTER")
  emptyText:SetWidth(WIDTH - 80)

  -- Отметить вручную
  local manualLabel = frame:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  manualLabel:SetPoint("BOTTOMLEFT", 18, 118)
  Label(manualLabel, "manual_label")

  editBox = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
  editBox:SetSize(196, 22)
  editBox:SetPoint("LEFT", manualLabel, "RIGHT", 12, 0)
  editBox:SetAutoFocus(false)
  editBox:SetMaxLetters(60)
  editBox:SetScript("OnEnterPressed", SubmitManual)
  editBox:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)

  local addButton = MakeButton(frame, "button_mark", 110, "tip_mark", SubmitManual)
  addButton:SetPoint("BOTTOMRIGHT", -16, 112)

  -- Основные действия
  local syncButton = MakeButton(frame, "button_sync", 156, "tip_sync", function() ns.SyncNow() end)
  syncButton:SetPoint("BOTTOMLEFT", 16, 78)

  local scanButton = MakeButton(frame, "button_scan", 156, "tip_scan", function() ns.RequestScan() end)
  scanButton:SetPoint("LEFT", syncButton, "RIGHT", 6, 0)

  local resetButton = MakeButton(frame, "button_reset", 140, "tip_reset", UI.ConfirmResetWeek)
  resetButton:SetPoint("BOTTOMRIGHT", -16, 78)

  -- Настройки
  quietCheck = CreateFrame("CheckButton", nil, frame, "UICheckButtonTemplate")
  quietCheck:SetSize(26, 26)
  quietCheck:SetPoint("BOTTOMLEFT", 14, 43)
  if quietCheck.Text then
    Label(quietCheck.Text, "quiet")
  end
  quietCheck:SetScript("OnClick", function(self)
    ns.SetQuiet(self:GetChecked())
  end)
  AttachTooltip(quietCheck, "quiet", "tip_quiet")

  local clearButton = MakeButton(frame, "button_clear", 140, "tip_clear", ConfirmClear)
  clearButton:SetPoint("BOTTOMRIGHT", -16, 44)

  local hint = frame:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
  hint:SetPoint("BOTTOMLEFT", 18, 12)
  hint:SetPoint("BOTTOMRIGHT", -18, 12)
  hint:SetJustifyH("LEFT")
  Label(hint, "window_hint")

  frame:SetScript("OnShow", UI.Refresh)
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
end

local function SettingsCheck(parent, y, key, onClick)
  local c = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
  c:SetSize(24, 24)
  c:SetPoint("TOPLEFT", 20, y)
  if c.Text then
    Label(c.Text, key)
  end
  c:SetScript("OnClick", onClick)
  return c
end

local function CreateSettings()
  local codes = { "auto" }
  for _, code in ipairs(ns.LANGUAGES) do
    codes[#codes + 1] = code
  end
  local height = 92 + #codes * 24 + 70
  settingsFrame = CreateFrame("Frame", SETTINGS_NAME, UIParent, "DefaultPanelTemplate")
  settingsFrame:SetSize(330, height)
  settingsFrame:SetFrameStrata("DIALOG")
  settingsFrame:SetToplevel(true)
  settingsFrame:SetClampedToScreen(true)
  settingsFrame:SetMovable(true)
  settingsFrame:EnableMouse(true)
  settingsFrame:RegisterForDrag("LeftButton")
  settingsFrame:SetScript("OnDragStart", settingsFrame.StartMoving)
  settingsFrame:SetScript("OnDragStop", settingsFrame.StopMovingOrSizing)
  settingsFrame:SetPoint("CENTER", 0, 60)
  local function title()
    if settingsFrame.SetTitle then
      settingsFrame:SetTitle(L.settings_title)
    elseif settingsFrame.TitleContainer and settingsFrame.TitleContainer.TitleText then
      settingsFrame.TitleContainer.TitleText:SetText(L.settings_title)
    end
  end
  title()
  relabel[#relabel + 1] = title
  CreateFrame("Button", nil, settingsFrame, "UIPanelCloseButtonDefaultAnchors")
  tinsert(UISpecialFrames, SETTINGS_NAME)

  local head = settingsFrame:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  head:SetPoint("TOPLEFT", 20, -34)
  Label(head, "settings_language")

  for i, code in ipairs(codes) do
    local b = CreateFrame("CheckButton", nil, settingsFrame, "UICheckButtonTemplate")
    b:SetSize(24, 24)
    b:SetPoint("TOPLEFT", 26, -54 - (i - 1) * 24)
    b.code = code
    local function text()
      if b.Text then
        if code == "auto" then
          b.Text:SetText(string.format(L.settings_auto, ns.LANGUAGE_NAMES[ns.ClientLanguage()]))
        else
          b.Text:SetText(ns.LANGUAGE_NAMES[code])
        end
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

  local y = -54 - #codes * 24 - 14
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
  ns.broker = LDB:NewDataObject(ADDON_NAME, {
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
