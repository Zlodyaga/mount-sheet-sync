--[[
Mount Sheet Sync 3.0.0 — окно, иконка и настройки
==================================================
  * окно (ЛКМ по иконке или /mss): слева персонажи из таблицы (сколько каждому осталось), справа вкладки
    «Осталось» (выбранному персонажу), «Все персонажи» (кому что ещё убить), «Попытки» (счётчик маунтов)
    и «Журнал» (записи аддона); внизу — частые действия и кнопка настроек;
  * списки — ScrollBox игры: кадры только для видимых строк, перестраиваются, только когда поменялись данные;
  * кнопка у миникарты — её забирают к себе HidingBar и похожие панели;
  * иконка LibDataBroker — для Titan Panel, ChocolateBar и т. п.;
  * пункт в меню аддонов у миникарты (Addon Compartment);
  * настройки (ПКМ по иконке, кнопка в окне или /mss settings): язык, сообщения в чате, кнопка у миникарты,
    очистка журнала, сведения для отчёта об ошибке.
Все надписи — из ns.L; при смене языка окно и настройки перерисовываются сразу.
]]

local ADDON_NAME, ns = ...

local ICON = "Interface\\AddOns\\MountSheetSync\\Media\\Icon"   -- the horseshoe, same as the tray program
local WINDOW_NAME = "MountSheetSyncFrame"
local WIDTH, HEIGHT = 820, 540
local SIDE_WIDTH = 180
local ROW_HEIGHT = 16
local CHAR_HEIGHT = 20
local NAMES_SHOWN = 3   -- «Все персонажи»: столько имён в строке, остальные — «и ещё N» (все — в подсказке)

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
local TAB_LEFT, TAB_ALL, TAB_ATTEMPTS, TAB_LOG = 1, 2, 3, 4
local TAB_KEYS = { "left", "all", "attempts", "log" }
local TAB_LABELS = { "tab_left", "tab_all", "tab_attempts", "tab_log" }

local UI = {}
local L = ns.L
local relabel = {}      -- функции, которые заново ставят надписи (смена языка)
local frame, listBox, charBox, emptyText, header1, header2, editBox, urlBox
local tabs = {}
local refreshQueued = false
local selectedChar      -- имя персонажа из таблицы, выбранного слева; nil — текущий
local shownList, shownChars   -- что сейчас в списках: перестраиваем только при изменении

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

-- Меню по правому щелчку на строке «Осталось» (только у текущего персонажа): не фармить строку или весь рейд / подземелье.
local function ShowRowMenu(button)
  local entry = button.elem and button.elem.entry
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

-- Подсказка строки: «ПКМ — не фармлю» или elem.tip = { заголовок, строка, … }.
local function ShowRowTooltip(button)
  local elem = button.elem
  if elem and elem.entry then
    GameTooltip:SetOwner(button, "ANCHOR_CURSOR")
    GameTooltip:SetText(L.tip_row_menu, 1, 1, 1)
    GameTooltip:Show()
  elseif elem and elem.tip then
    GameTooltip:SetOwner(button, "ANCHOR_CURSOR")
    GameTooltip:SetText(elem.tip[1], 1, 0.82, 0)
    for i = 2, #elem.tip do
      GameTooltip:AddLine(elem.tip[i], 1, 1, 1, true)
    end
    GameTooltip:Show()
  end
end

local function HideTooltip()
  GameTooltip:Hide()
end

-- Кадры строк, которые уже собраны (ScrollBox отдаёт и новые, и переиспользованные).
local built = setmetatable({}, { __mode = "k" })

-- Строка списка. ScrollBox переиспользует кадры: всё, что зависит от строки, ставится при каждом показе.
local function InitRow(button, elem)
  if not built[button] then
    built[button] = true
    button:SetHeight(ROW_HEIGHT)
    button.text = button:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    button.text:SetPoint("LEFT", 4, 0)
    button.text:SetPoint("RIGHT", -4, 0)
    button.text:SetJustifyH("LEFT")
    button.text:SetWordWrap(false)
    button.hl = button:CreateTexture(nil, "HIGHLIGHT")
    button.hl:SetAllPoints()
    button.hl:SetColorTexture(1, 1, 1, 0.08)
    button:RegisterForClicks("RightButtonUp")
    button:SetScript("OnClick", ShowRowMenu)
    button:SetScript("OnEnter", ShowRowTooltip)
    button:SetScript("OnLeave", HideTooltip)
  end
  button.elem = elem
  button.text:SetText(elem.text)
  button.hl:SetShown(elem.entry ~= nil or elem.tip ~= nil)
end

local DONE_MARK = "|TInterface\\RaidFrame\\ReadyCheck-Ready:12:12|t"

local function ClassColored(name, classFile)
  local color = classFile and RAID_CLASS_COLORS and RAID_CLASS_COLORS[classFile]
  if color and color.colorStr then
    return "|c" .. color.colorStr .. Safe(name) .. "|r"
  end
  return Safe(name)
end

-- Строка персонажа слева: имя в цвете класса, справа — сколько осталось (или галочка).
local function InitCharRow(button, elem)
  if not built[button] then
    built[button] = true
    button:SetHeight(CHAR_HEIGHT)
    button.sel = button:CreateTexture(nil, "BACKGROUND")
    button.sel:SetAllPoints()
    button.sel:SetColorTexture(1, 0.82, 0, 0.18)
    button.hl = button:CreateTexture(nil, "HIGHLIGHT")
    button.hl:SetAllPoints()
    button.hl:SetColorTexture(1, 1, 1, 0.08)
    button.name = button:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
    button.name:SetPoint("LEFT", 6, 0)
    button.name:SetPoint("RIGHT", -30, 0)
    button.name:SetJustifyH("LEFT")
    button.name:SetWordWrap(false)
    button.count = button:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    button.count:SetPoint("RIGHT", -6, 0)
    button:SetScript("OnClick", function(self)
      UI.SelectChar(not self.elem.current and self.elem.name or nil)   -- свой персонаж — снова «текущий»
    end)
    button:SetScript("OnEnter", function(self)
      local r = self.elem.r
      GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
      GameTooltip:SetText(Safe(self.elem.name), 1, 1, 1)
      if r.state == "ok" then
        GameTooltip:AddLine(string.format(L.tip_char, r.left, r.total), 1, 0.82, 0)
      end
      GameTooltip:Show()
    end)
    button:SetScript("OnLeave", HideTooltip)
  end
  button.elem = elem
  button.name:SetText(ClassColored(elem.name, elem.classFile))
  local r = elem.r
  if r.state ~= "ok" then
    button.count:SetText("")
  elseif r.left > 0 then
    button.count:SetText(tostring(r.left))
  else
    button.count:SetText(DONE_MARK)
  end
  button.sel:SetShown(elem.selected)
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

-- Вкладка «Журнал»: записи аддона, новые сверху.
local function JournalList(db)
  local list = {}
  for i = #db.kills, 1, -1 do
    list[#list + 1] = Line(FormatRow(db.kills[i]))
  end
  return list, L.journal_empty
end

-- Вкладка «Осталось»: строки «Нет» из таблицы, убитые после обновления списка — с галочкой внизу группы.
-- own — список текущего персонажа: только у него правый щелчок «Не фармлю».
local function RemainingList(r, own)
  if r.state == "nodata" then
    return {}, L.left_nodata .. "\n\n" .. L.left_getprogram, true
  end
  if r.state == "nochar" then
    return {}, string.format(L.left_nochar, r.name or "?")
  end
  local list = { Line("|cff8c8c8c" .. string.format(L.left_head, r.left, r.total, ns.FormatWhen(r.generated)) .. "|r") }
  local note = ns.RemainingNote(r)
  if note then
    list[#list + 1] = Line((r.afterReset and "|cff8cff8c" or "|cffff9a4d") .. note .. "|r")
  end
  for _, kind in ipairs(ns.KIND_ORDER) do
    local group = {}
    for _, entry in ipairs(r.items) do
      if entry.item.kind == kind then
        group[#group + 1] = entry
      end
    end
    if #group > 0 then
      list[#list + 1] = Line("|cffffd200" .. ns.KindTitle(kind) .. "|r")
      for pass = 1, 2 do   -- сначала то, что осталось, потом убитые
        for _, entry in ipairs(group) do
          if entry.done == (pass == 2) then
            local menu = own and kind ~= "world" and not entry.done
            list[#list + 1] = Line("   " .. ns.FormatRemaining(entry), menu and entry or nil)
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
  return list
end

-- «Злодяга, Жрица, Охотник и ещё 12»
local function Names(names)
  local shown = {}
  for i = 1, math.min(#names, NAMES_SHOWN) do
    shown[i] = Safe(names[i])
  end
  local text = table.concat(shown, ", ")
  if #names > NAMES_SHOWN then
    text = string.format(L.all_more, text, #names - NAMES_SHOWN)
  end
  return text
end

-- Вкладка «Все персонажи»: строка таблицы и под ней — у кого она ещё не убита (все имена — в подсказке).
local function SummaryList(rows, chars)
  local list = { Line("|cff8c8c8c" .. string.format(L.all_head, #rows, chars) .. "|r") }
  local kind
  for _, row in ipairs(rows) do
    if row.kind ~= kind then
      kind = row.kind
      list[#list + 1] = Line("|cffffd200" .. ns.KindTitle(kind) .. "|r")
    end
    local text = ns.FormatRemaining({ item = row.item })
    local all = {}
    for i, name in ipairs(row.names) do
      all[i] = Safe(name)
    end
    local tip = { text, table.concat(all, ", ") }
    list[#list + 1] = Line("   " .. text, nil, tip)
    list[#list + 1] = Line("      |cff8c8c8c" .. Names(row.names) .. "|r", nil, tip)
  end
  if #rows == 0 then
    list[#list + 1] = Line("|cff8cff8c" .. L.all_empty .. "|r")
  end
  return list
end

-- Вкладка «Попытки»: маунт — босс (сложность) · попыток, шанс; сколько персонажей ещё могут — в подсказке.
local function AttemptsList(rows)
  if #rows == 0 then
    return {}, L.attempts_empty
  end
  local list = { Line("|cff8c8c8c" .. string.format(L.attempts_head, #rows) .. "|r") }
  for _, m in ipairs(rows) do
    local text = "   |cffc9a0ff" .. Safe(m.name) .. "|r"
    if Safe(m.boss) ~= "" then
      text = text .. " — " .. Safe(m.boss)
    end
    if Safe(m.diff) ~= "" then
      text = text .. " |cff8c8c8c(" .. Safe(m.diff) .. ")|r"
    end
    local count = m.n > 0 and string.format(L.boss_hint, m.n, ns.FormatChance(m.n, m.p)) or L.boss_hint_none
    local tip = { Safe(m.name), count }
    if m.left > 0 then
      tip[#tip + 1] = string.format(L.boss_hint_left, m.left)
    end
    list[#list + 1] = Line(text .. " |cff8c8c8c·|r " .. count, nil, tip)
  end
  return list
end

local function UpdateTabTitle(r)
  local tab = tabs[TAB_LEFT]
  local title = r.state == "ok" and string.format(L.tab_left_n, r.left) or L.tab_left
  if tab:GetText() ~= title then
    tab:SetText(title)
    PanelTemplates_TabResize(tab, 0, nil, frame.minTabWidth)
  end
end

-- Выбранный слева персонаж — его строка из ns.GetAllRemaining(); nil — текущий.
local function SelectedEntry(all)
  if not selectedChar then
    return nil
  end
  for _, c in ipairs(all) do
    if c.name == selectedChar then
      return not c.current and c or nil
    end
  end
  return nil
end

local function RefreshChars(all, sel)
  local key = tostring(all) .. "|" .. tostring(sel)
  if key == shownChars then
    return
  end
  shownChars = key
  local elems = {}
  for _, c in ipairs(all) do
    elems[#elems + 1] = { name = c.name, classFile = c.classFile, r = c.r, current = c.current,
      selected = (sel and c == sel) or (not sel and c.current) or false }
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
  UpdateTabTitle(own)

  local tab = frame.selectedTab
  local key, build
  if tab == TAB_LEFT then
    local r = sel and sel.r or own
    key = "left|" .. tostring(r)
    build = function() return RemainingList(r, not sel) end
  elseif tab == TAB_ALL and type(MountSheetSyncSheet) ~= "table" then
    key = "all|nodata"
    build = function() return RemainingList({ state = "nodata" }) end
  elseif tab == TAB_ALL then
    local rows = ns.GetSummary()
    key = "all|" .. tostring(rows)
    build = function() return SummaryList(rows, #all) end
  elseif tab == TAB_ATTEMPTS then
    local rows = ns.GetAttemptsList()
    key = "attempts|" .. tostring(rows)
    build = function() return AttemptsList(rows) end
  else
    local last = db.kills[#db.kills]
    key = "log|" .. #db.kills .. "|" .. tostring(last and last.id)
    build = function() return JournalList(db) end
  end
  if key == shownList then
    return
  end
  shownList = key
  local list, empty, showUrl = build()
  listBox:SetDataProvider(CreateDataProvider(list), ScrollBoxConstants.RetainScrollPosition)
  emptyText:SetText(empty or "")
  emptyText:SetShown(#list == 0)
  urlBox:SetShown(#list == 0 and showUrl and true or false)
end

function UI.SelectTab(id)
  ns.GetDB().tab = TAB_KEYS[id]
  PanelTemplates_SetTab(frame, id)
  shownList = nil
  listBox:ScrollToBegin()
  UI.Refresh()
end

-- Щелчок по персонажу слева: его «Осталось» (name = nil — текущий персонаж).
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
  return box
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

  -- Слева — персонажи из таблицы
  local charsTitle = frame:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  charsTitle:SetPoint("TOPLEFT", 20, -74)
  Label(charsTitle, "chars_title")
  local side = CreateFrame("Frame", nil, frame, "InsetFrameTemplate")
  side:SetPoint("TOPLEFT", 12, -92)
  side:SetPoint("BOTTOMLEFT", 12, 100)
  side:SetWidth(SIDE_WIDTH)
  charBox = CreateList(side, CHAR_HEIGHT, InitCharRow)
  charBox:SetPoint("TOPLEFT", 4, -4)
  charBox:SetPoint("BOTTOMRIGHT", -18, 4)

  -- Справа — список, вкладки над ним
  local inset = CreateFrame("Frame", nil, frame, "InsetFrameTemplate")
  inset:SetPoint("TOPLEFT", side, "TOPRIGHT", 6, 0)
  inset:SetPoint("BOTTOMRIGHT", -12, 100)

  frame.minTabWidth = 90
  for i, key in ipairs(TAB_LABELS) do
    local tab = CreateFrame("Button", nil, frame, "PanelTopTabButtonTemplate")
    tab:SetID(i)
    tab:SetText(L[key])
    PanelTemplates_TabResize(tab, 0, nil, frame.minTabWidth)
    if i ~= TAB_LEFT then   -- у «Осталось» заголовок с числом ставит UpdateTabTitle
      relabel[#relabel + 1] = function()
        tab:SetText(L[key])
        PanelTemplates_TabResize(tab, 0, nil, frame.minTabWidth)
      end
    end
    tab:SetScript("OnClick", function(self)
      UI.SelectTab(self:GetID())
    end)
    tabs[i] = tab
  end
  tabs[1]:SetPoint("BOTTOMLEFT", inset, "TOPLEFT", 8, -2)
  PanelTemplates_SetNumTabs(frame, #tabs)   -- остальные вкладки встают справа от первой
  local selected = TAB_LEFT
  for i, key in ipairs(TAB_KEYS) do
    if ns.GetDB().tab == key then
      selected = i
    end
  end
  PanelTemplates_SetTab(frame, selected)

  listBox = CreateList(inset, ROW_HEIGHT, InitRow)
  listBox:SetPoint("TOPLEFT", 6, -6)
  listBox:SetPoint("BOTTOMRIGHT", -22, 6)

  emptyText = inset:CreateFontString(nil, "OVERLAY", "GameFontDisable")
  emptyText:SetPoint("CENTER", 0, 14)
  emptyText:SetWidth(WIDTH - SIDE_WIDTH - 80)

  -- адрес сайта с программой: только выделить и скопировать (Ctrl+C), изменить нельзя
  urlBox = CreateFrame("EditBox", nil, inset, "InputBoxTemplate")
  urlBox:SetSize(240, 22)
  urlBox:SetPoint("TOP", emptyText, "BOTTOM", 0, -10)
  urlBox:SetAutoFocus(false)
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

  -- Отметить вручную
  local manualLabel = frame:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  manualLabel:SetPoint("BOTTOMLEFT", 18, 76)
  Label(manualLabel, "manual_label")

  editBox = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
  editBox:SetSize(240, 22)
  editBox:SetPoint("LEFT", manualLabel, "RIGHT", 12, 0)
  editBox:SetAutoFocus(false)
  editBox:SetMaxLetters(60)
  editBox:SetScript("OnEnterPressed", SubmitManual)
  editBox:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)

  local addButton = MakeButton(frame, "button_mark", 110, "tip_mark", SubmitManual)
  addButton:SetPoint("LEFT", editBox, "RIGHT", 8, 0)

  -- Основные действия; редкое (чат, очистка журнала, отчёт об ошибке) — в настройках
  local syncButton = MakeButton(frame, "button_sync", 156, "tip_sync", function() ns.SyncNow() end)
  syncButton:SetPoint("BOTTOMLEFT", 16, 38)

  local scanButton = MakeButton(frame, "button_scan", 156, "tip_scan", function() ns.RequestScan() end)
  scanButton:SetPoint("LEFT", syncButton, "RIGHT", 6, 0)

  local resetButton = MakeButton(frame, "button_reset", 140, "tip_reset", UI.ConfirmResetWeek)
  resetButton:SetPoint("LEFT", scanButton, "RIGHT", 6, 0)

  local gear = CreateFrame("Button", nil, frame)
  gear:SetSize(24, 24)
  gear:SetPoint("BOTTOMRIGHT", -18, 38)
  local gearIcon = gear:CreateTexture(nil, "ARTWORK")
  gearIcon:SetAllPoints()
  gearIcon:SetTexture("Interface\\Icons\\INV_Misc_Gear_01")
  gearIcon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
  gear:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")
  gear:SetScript("OnClick", function() UI.ToggleSettings() end)
  AttachTooltip(gear, "settings_title", "tip_settings")

  local hint = frame:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
  hint:SetPoint("BOTTOMLEFT", 18, 14)
  hint:SetPoint("BOTTOMRIGHT", -18, 14)
  hint:SetJustifyH("LEFT")
  -- Без программы (или пока она не подключена к таблице) — та же неброская строка, но о том, что аддон
  -- работает сам по себе; подробности — при наведении.
  local state = ns.ProgramState()
  if state == "online" then
    Label(hint, "window_hint")
  else
    Label(hint, "window_" .. state)
    local hover = CreateFrame("Frame", nil, frame)
    hover:SetPoint("TOPLEFT", hint, "TOPLEFT", 0, 2)
    hover:SetPoint("BOTTOMRIGHT", hint, "BOTTOMRIGHT", 0, -2)
    hover:EnableMouse(true)
    local old = state == "oldaddon" or state == "oldprogram"
    AttachTooltip(hover, old and "update_title" or "offline_title", "tip_" .. state)
  end

  -- другой язык — списки собрать заново
  relabel[#relabel + 1] = function()
    shownList, shownChars = nil, nil
  end

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
  local getNum = C_AddOns and C_AddOns.GetNumAddOns or GetNumAddOns
  local isLoaded = C_AddOns and C_AddOns.IsAddOnLoaded or IsAddOnLoaded
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
    reportFrame = CreateFrame("Frame", REPORT_NAME, UIParent, "DefaultPanelTemplate")
    reportFrame:SetSize(520, 300)
    reportFrame:SetFrameStrata("DIALOG")
    reportFrame:SetToplevel(true)
    reportFrame:SetClampedToScreen(true)
    reportFrame:SetMovable(true)
    reportFrame:EnableMouse(true)
    reportFrame:RegisterForDrag("LeftButton")
    reportFrame:SetScript("OnDragStart", reportFrame.StartMoving)
    reportFrame:SetScript("OnDragStop", reportFrame.StopMovingOrSizing)
    reportFrame:SetPoint("CENTER", 0, 40)
    local function title()
      if reportFrame.SetTitle then
        reportFrame:SetTitle(L.report_title)
      elseif reportFrame.TitleContainer and reportFrame.TitleContainer.TitleText then
        reportFrame.TitleContainer.TitleText:SetText(L.report_title)
      end
    end
    title()
    relabel[#relabel + 1] = title
    CreateFrame("Button", nil, reportFrame, "UIPanelCloseButtonDefaultAnchors")
    tinsert(UISpecialFrames, REPORT_NAME)
    local hint = reportFrame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    hint:SetPoint("TOPLEFT", 18, -32)
    hint:SetPoint("TOPRIGHT", -18, -32)
    hint:SetJustifyH("LEFT")
    local function hintText()
      hint:SetText(string.format(L.report_hint, ns.ISSUES_URL))
    end
    hintText()
    relabel[#relabel + 1] = hintText
    local scroll = CreateFrame("ScrollFrame", nil, reportFrame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 16, -70)
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
  local height = 92 + #codes * 24 + 70 + 66
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

  local reportButton = MakeButton(settingsFrame, "button_report", 200, "tip_report", function()
    ns.SafeCall(ShowReport)
  end)
  reportButton:SetPoint("BOTTOMLEFT", 20, 16)

  local clearButton = MakeButton(settingsFrame, "button_clear", 200, "tip_clear", ConfirmClear)
  clearButton:SetPoint("BOTTOMLEFT", 20, 46)

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
