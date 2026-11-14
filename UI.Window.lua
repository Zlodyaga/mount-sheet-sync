--[[
Mount Sheet Sync 3.0.7 — окно: рамка, шапка таблицы, обновление
Создание окна и панелей, заголовки столбцов, UI.Refresh, выбор вкладки и персонажа, UI.Toggle.
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local WINDOW_NAME, WIDTH, HEIGHT, SIDE_WIDTH, ROW_HEIGHT, CHAR_HEIGHT = P.WINDOW_NAME, P.WIDTH, P.HEIGHT, P.SIDE_WIDTH, P.ROW_HEIGHT, P.CHAR_HEIGHT
local TOOLBAR_HEIGHT, HEAD_HEIGHT, SLIDER_ROOM, TAB_LEFT, TAB_ACH, TAB_ATTEMPTS = P.TOOLBAR_HEIGHT, P.HEAD_HEIGHT, P.SLIDER_ROOM, P.TAB_LEFT, P.TAB_ACH, P.TAB_ATTEMPTS
local TAB_DROPS = P.TAB_DROPS
local TAB_KEYS, TAB_LABELS, ALL_CHARS, FILTER_KINDS, L, Skin = P.TAB_KEYS, P.TAB_LABELS, P.ALL_CHARS, P.FILTER_KINDS, P.L, P.Skin
local relabel, tabs, filterButtons, headCells, grid, AttachTooltip = P.relabel, P.tabs, P.filterButtons, P.headCells, P.grid, P.AttachTooltip
local Label, MakeButton, SubmitManual, FirstLetters, HideTooltip, FocusColumn = P.Label, P.MakeButton, P.SubmitManual, P.FirstLetters, P.HideTooltip, P.FocusColumn
local DrawGrid, InitRow, ClassColored, InitCharRow, JournalView, RemainingView = P.DrawGrid, P.InitRow, P.ClassColored, P.InitCharRow, P.JournalView, P.RemainingView
local AchievementsView, GridView, AttemptsState, AttemptsView = P.AchievementsView, P.GridView, P.AttemptsState, P.AttemptsView

---------------------------------------------------------------------------
-- Заголовок таблицы
---------------------------------------------------------------------------
local function HeadCell(i)
  local cell = headCells[i]
  if not cell then
    cell = CreateFrame("Button", nil, S.headRow)
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
  if S.shownHead then
    DrawHead(S.shownHead, nil, true)
  end
  S.listBox:ForEachFrame(function(button)
    if button.elem and button.elem.names then
      DrawGrid(button, button.elem)
    end
  end)
end

-- Части окна вокруг списка: что нужно этому виду.
local function ApplyChrome(view)
  local top = TOOLBAR_HEIGHT + (view.cols and HEAD_HEIGHT or 0)
  local useSlider = view.grid and grid.overflow and true or false
  S.listBox:ClearAllPoints()
  S.listBox:SetPoint("TOPLEFT", 6, -top)
  S.listBox:SetPoint("BOTTOMRIGHT", -22, useSlider and SLIDER_ROOM + 6 or 6)
  S.summaryText:SetText(view.summary or "")
  S.summaryText:SetShown(not view.attempts and not view.log)
  S.focusText:SetText("")
  S.focusText:SetShown(view.grid and true or false)
  S.columnGlow:Hide()
  S.attemptsBar:SetShown(view.attempts and true or false)
  S.logBar:SetShown(view.log and true or false)
  S.headRow:SetShown(view.cols ~= nil)
  S.shownHead = view.cols
  if view.cols then
    DrawHead(view.cols, view.attempts and AttemptsState() or nil, view.grid)
  end
  if view.attempts then
    S.attemptsCount:SetText(view.count and string.format(L.attempts_count, view.count) or "")
    local st = AttemptsState()
    for _, b in ipairs(filterButtons) do
      Skin.ChipState(b, b.kind == st.kind)
    end
    S.hideZeroCheck:SetChecked(st.hideZero and true or false)
  end
  S.gridSlider:SetShown(useSlider)
  if useSlider then
    S.gridSlider:ClearAllPoints()
    S.gridSlider:SetPoint("BOTTOMLEFT", 6 + grid.x, 6)
    S.gridSlider:SetWidth(grid.shown * grid.cell)
    S.gridSlider:SetMinMaxValues(0, #grid.chars - grid.shown)
    S.gridSlider:SetValue(grid.offset)
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
    Skin.TabWidth(tab, S.frame.minTabWidth)
  end
end

-- Выбранный слева персонаж — его строка из ns.GetAllRemaining(); nil — текущий (или «Все персонажи»).
local function SelectedEntry(all)
  if not S.selectedChar or S.selectedChar == ALL_CHARS then
    return nil
  end
  for _, c in ipairs(all) do
    if c.name == S.selectedChar then
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
  local showAll = S.selectedChar == ALL_CHARS and #all > 1
  local key = tostring(all) .. "|" .. tostring(sel) .. "|" .. tostring(showAll)
  if key == S.shownChars then
    return
  end
  S.shownChars = key
  local elems = {}
  if #all > 1 then   -- один персонаж: сравнивать не с кем, сразу его «Осталось»
    elems[1] = { all = true, total = TotalLeft(all), selected = showAll }
  end
  for i, c in ipairs(all) do
    elems[#elems + 1] = { name = c.name, classFile = c.classFile, r = c.r, current = c.current, index = i,
      selected = (not showAll and ((sel and c == sel) or (not sel and c.current))) or false }
  end
  S.charBox:SetDataProvider(CreateDataProvider(elems), ScrollBoxConstants.RetainScrollPosition)
end

-- Списки перестраиваются, только когда поменялись их данные (кэши Remaining.lua отдают ту же таблицу):
-- шапка раз в 30 с, новая запись или открытие окна без изменений не трогают кадры.
function UI.Refresh()
  if not S.frame or not S.frame:IsShown() then
    return
  end
  local db = ns.GetDB()
  UI.RefreshHeader()
  local all = ns.GetAllRemaining()
  local sel = SelectedEntry(all)
  RefreshChars(all, sel)
  local own = ns.GetRemaining()
  local showAll = S.selectedChar == ALL_CHARS and #all > 1
  UpdateTabTitle(sel and sel.r or own, showAll and TotalLeft(all) or nil)

  local tab = S.frame.selectedTab
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
  elseif tab == TAB_DROPS then
    local rows = ns.GetDrops()
    local newest = rows and rows[1]
    key = "drops|" .. tostring(rows and #rows) .. "|" .. tostring(newest and newest.id) .. "|" .. ns.CurrentLanguage()
    build = function() return UI.DropsView(rows) end
  else
    local last = db.kills[#db.kills]
    key = "log|" .. #db.kills .. "|" .. tostring(last and last.id)
    build = function() return JournalView(db) end
  end
  if key == S.shownList then
    return
  end
  S.shownList = key
  local view = build()
  ApplyChrome(view)
  S.listBox:SetDataProvider(CreateDataProvider(view.list), ScrollBoxConstants.RetainScrollPosition)
  S.emptyText:SetText(view.empty or "")
  S.emptyText:SetShown(#view.list == 0)
  S.urlBox:SetShown(#view.list == 0 and view.url and true or false)
end

local function ShowTabState(id)
  S.frame.selectedTab = id
  for i, tab in ipairs(tabs) do
    Skin.TabState(tab, i == id)
  end
end

function UI.SelectTab(id)
  ns.GetDB().tab = TAB_KEYS[id]
  ns.Count("tab_" .. tostring(TAB_KEYS[id]))
  ShowTabState(id)
  S.shownList = nil
  S.listBox:ScrollToBegin()
  UI.Refresh()
end

-- Щелчок слева: «Осталось» персонажа (name = nil — текущий) или всех (ALL_CHARS).
function UI.SelectChar(name)
  S.selectedChar = name
  if S.frame.selectedTab ~= TAB_LEFT then
    UI.SelectTab(TAB_LEFT)
    return
  end
  S.shownList = nil
  S.listBox:ScrollToBegin()
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
  S.listBox:ScrollToBegin()
  UI.Refresh()
end

function UI.FilterAttempts(kind)
  AttemptsState().kind = kind
  S.listBox:ScrollToBegin()
  UI.Refresh()
end

-- Настройки: вид сетки всех персонажей («scroll» — по умолчанию, «all» — все столбцы сразу).
function UI.SetGridView(view)
  ns.GetDB().gridView = view == "all" and "all" or nil
  grid.offset = 0
  S.shownList = nil
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
  S.toolbar = CreateFrame("Frame", nil, inset)
  S.toolbar:SetPoint("TOPLEFT", 6, -2)
  S.toolbar:SetPoint("RIGHT", -6, 0)
  S.toolbar:SetHeight(TOOLBAR_HEIGHT - 4)

  S.summaryText = S.toolbar:CreateFontString(nil, "OVERLAY", "MSSFontBody")
  S.summaryText:SetPoint("LEFT", 4, 0)
  S.summaryText:SetJustifyH("LEFT")
  S.frame.summaryText = S.summaryText
  S.focusText = S.toolbar:CreateFontString(nil, "OVERLAY", "MSSFontBody")
  S.focusText:SetPoint("RIGHT", -4, 0)
  S.focusText:SetJustifyH("RIGHT")

  S.attemptsBar = CreateFrame("Frame", nil, S.toolbar)
  S.attemptsBar:SetAllPoints()
  local previous
  for _, kind in ipairs(FILTER_KINDS) do
    local key = kind == "all" and "attempts_filter_all" or ("kind_" .. kind)
    local b = Skin.Chip(S.attemptsBar, 22)
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
  S.hideZeroCheck = Skin.Check(S.attemptsBar, false)
  S.hideZeroCheck:SetPoint("LEFT", previous, "RIGHT", 14, 0)
  Label(S.hideZeroCheck.Text, "attempts_hide_zero")
  S.hideZeroCheck:SetScript("OnClick", function(self)
    AttemptsState().hideZero = self:GetChecked() and true or false
    S.listBox:ScrollToBegin()
    UI.Refresh()
  end)
  S.attemptsCount = S.attemptsBar:CreateFontString(nil, "OVERLAY", "MSSFontSmall")
  S.attemptsCount:SetPoint("RIGHT", -4, 0)

  S.logBar = CreateFrame("Frame", nil, S.toolbar)
  S.logBar:SetAllPoints()
  local manualLabel = S.logBar:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  manualLabel:SetPoint("LEFT", 4, 0)
  Label(manualLabel, "manual_label")

  S.editBox = Skin.EditBox(S.logBar, 220, 24)
  S.editBox:SetPoint("LEFT", manualLabel, "RIGHT", 12, 0)
  S.editBox:SetMaxLetters(60)
  S.editBox:SetScript("OnEnterPressed", SubmitManual)
  S.editBox:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)

  local addButton = MakeButton(S.logBar, "button_mark", 100, "tip_mark", SubmitManual)
  addButton:SetPoint("LEFT", S.editBox, "RIGHT", 8, 0)

  local scanButton = MakeButton(S.logBar, "button_scan", 156, "tip_scan", function() ns.RequestScan() end)
  scanButton:SetPoint("LEFT", addButton, "RIGHT", 6, 0)

  local line = S.toolbar:CreateTexture(nil, "ARTWORK")
  line:SetPoint("BOTTOMLEFT", -5, -2)
  line:SetPoint("BOTTOMRIGHT", 5, -2)
  line:SetHeight(1)
  line:SetColorTexture(1, 1, 1, 0.06)
end

local function CreateWindow()
  -- маленький экран или крупный масштаб: окно уже, столбцы названий отдают ширину сами
  local screen = UIParent:GetWidth() or 0
  local width = screen > 0 and math.min(WIDTH, screen - 20) or WIDTH
  S.listWidth = width - SIDE_WIDTH - 62
  S.frame = Skin.Window(WINDOW_NAME, width, HEIGHT, "HIGH")
  S.frame.titleText:SetText("Mount Sheet Sync")
  S.frame:SetScript("OnDragStop", function(self)
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
  S.frame.SnapToPixels = SnapToPixels

  local pos = ns.GetDB().window
  if type(pos) == "table" and pos.point then
    S.frame:SetPoint(pos.point, UIParent, pos.relativePoint or pos.point, pos.x or 0, pos.y or 0)
  else
    S.frame:SetPoint("CENTER")
  end

  -- Шапка: одной строкой, чтобы не отнимать высоту у списка
  S.header1 = S.frame:CreateFontString(nil, "OVERLAY", "MSSFontStrong")
  S.header1:SetPoint("LEFT", S.frame, "TOPLEFT", 16, -48)
  S.header2 = S.frame:CreateFontString(nil, "OVERLAY", "MSSFontGold")
  S.header2:SetPoint("RIGHT", S.frame, "TOPRIGHT", -16, -48)

  -- Слева — все персонажи из таблицы
  local charsTitle = S.frame:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  charsTitle:SetPoint("LEFT", S.frame, "TOPLEFT", 18, -80)
  Label(charsTitle, "side_title")
  local side = Skin.Panel(S.frame)
  side:SetPoint("TOPLEFT", 12, -94)
  side:SetPoint("BOTTOMLEFT", 12, 44)
  side:SetWidth(SIDE_WIDTH)
  S.charBox = CreateList(side, CHAR_HEIGHT, InitCharRow)
  S.charBox:SetPoint("TOPLEFT", 4, -4)
  S.charBox:SetPoint("BOTTOMRIGHT", -14, 4)

  -- Справа — список, вкладки над ним (низ вкладок уходит под панель: у неё кадр выше)
  local inset = Skin.Panel(S.frame)
  inset:SetFrameLevel(S.frame:GetFrameLevel() + 5)
  inset:SetPoint("TOPLEFT", side, "TOPRIGHT", 10, 0)
  inset:SetPoint("BOTTOMRIGHT", -12, 44)

  S.frame.minTabWidth = 90
  for i, key in ipairs(TAB_LABELS) do
    local tab = Skin.Tab(S.frame)
    tab:SetID(i)
    tab:SetText(L[key])
    Skin.TabWidth(tab, S.frame.minTabWidth)
    if i ~= TAB_LEFT then   -- у «Осталось» заголовок с числом ставит UpdateTabTitle
      relabel[#relabel + 1] = function()
        tab:SetText(L[key])
        Skin.TabWidth(tab, S.frame.minTabWidth)
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
    S.selectedChar = ALL_CHARS
    ns.GetDB().tab = "left"
  end
  ShowTabState(selected)

  CreateToolbar(inset)

  S.headRow = CreateFrame("Frame", nil, inset)
  S.headRow:SetPoint("TOPLEFT", 6, -TOOLBAR_HEIGHT)
  S.headRow:SetPoint("RIGHT", -22, 0)
  S.headRow:SetHeight(HEAD_HEIGHT)
  local headLine = S.headRow:CreateTexture(nil, "ARTWORK")
  headLine:SetPoint("BOTTOMLEFT")
  headLine:SetPoint("BOTTOMRIGHT")
  headLine:SetHeight(1)
  headLine:SetColorTexture(Skin.COLOR.line[1], Skin.COLOR.line[2], Skin.COLOR.line[3])

  S.listBox = CreateList(inset, ROW_HEIGHT, InitRow)
  S.listBox:SetPoint("TOPLEFT", 6, -TOOLBAR_HEIGHT)
  S.listBox:SetPoint("BOTTOMRIGHT", -22, 6)

  S.columnGlow = inset:CreateTexture(nil, "OVERLAY")
  S.columnGlow:SetColorTexture(1, 0.82, 0, 0.1)
  S.columnGlow:Hide()

  S.gridSlider = CreateGridSlider(inset)
  S.frame.gridSlider = S.gridSlider

  S.emptyText = inset:CreateFontString(nil, "OVERLAY", "MSSFontDim")
  S.emptyText:SetPoint("CENTER", 0, 14)
  S.emptyText:SetWidth(width - SIDE_WIDTH - 80)

  -- адрес сайта с программой: только выделить и скопировать (Ctrl+C), изменить нельзя
  S.urlBox = Skin.EditBox(inset, 240, 24)
  S.urlBox:SetPoint("TOP", S.emptyText, "BOTTOM", 0, -10)
  S.urlBox:SetText(ns.PROGRAM_URL)
  S.urlBox:SetCursorPosition(0)
  S.urlBox:SetScript("OnTextChanged", function(self, userInput)
    if userInput and self:GetText() ~= ns.PROGRAM_URL then
      self:SetText(ns.PROGRAM_URL)
      self:HighlightText()
    end
  end)
  S.urlBox:SetScript("OnEditFocusGained", function(self) self:HighlightText() end)
  S.urlBox:SetScript("OnMouseUp", function(self) self:HighlightText() end)
  S.urlBox:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  S.urlBox:SetScript("OnEnterPressed", function(self) self:ClearFocus() end)
  S.urlBox:Hide()

  -- Внизу: отправка в таблицу, подсказка, настройки; редкое (чат, очистка журнала, отчёт об ошибке) — в настройках
  local syncButton = MakeButton(S.frame, "button_sync", 156, "tip_sync", function() ns.SyncNow() end)
  syncButton:SetPoint("BOTTOMLEFT", 12, 10)

  local gear = CreateFrame("Button", nil, S.frame)
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
  local hint = S.frame:CreateFontString(nil, "OVERLAY", "MSSFontSmall")
  hint:SetJustifyH("LEFT")
  hint:SetWordWrap(false)
  local warn = S.frame:CreateTexture(nil, "OVERLAY")
  warn:SetSize(16, 16)
  warn:SetTexture("Interface\\DialogFrame\\UI-Dialog-Icon-AlertNew")
  warn:SetPoint("LEFT", syncButton, "RIGHT", 10, 0)
  S.frame.offlineIcon = warn
  local hover = CreateFrame("Frame", nil, S.frame)
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
  S.frame.ShowProgramState = ShowProgramState

  -- другой язык — списки собрать заново
  relabel[#relabel + 1] = function()
    S.shownList, S.shownChars = nil, nil
  end

  S.frame:SetScript("OnShow", function(self)
    SnapToPixels(self)
    self.ShowProgramState()
    UI.Refresh()
  end)
  local elapsedTotal = 0
  S.frame:SetScript("OnUpdate", function(_, elapsed)
    elapsedTotal = elapsedTotal + elapsed
    if elapsedTotal > 30 then   -- раз в 30 с — только шапка («до сброса недели»)
      elapsedTotal = 0
      UI.RefreshHeader()
    end
  end)
  S.frame:Hide()
end

-- Изменения журнала перерисовывают окно не чаще раза за кадр:
-- скан, добавивший 60 записей, — одна перерисовка, а не 60.
function UI.RequestRefresh()
  if S.refreshQueued or not S.frame or not S.frame:IsShown() then
    return
  end
  S.refreshQueued = true
  C_Timer.After(0, function()
    S.refreshQueued = false
    UI.Refresh()
  end)
end

function UI.Toggle()
  if not ns.GetDB() then
    return
  end
  if not S.frame then
    -- если окно не удалось создать, ошибка один раз появится в чате, игра не сломается
    if not ns.SafeCall(CreateWindow) or not S.frame then
      S.frame = nil
      return
    end
  end
  if S.frame:IsShown() then
    S.frame:Hide()
  else
    ns.Count("open")
    S.frame:Show()
  end
end

ns.ToggleWindow = UI.Toggle
ns.ConfirmResetWeek = UI.ConfirmResetWeek
ns.OnChange = UI.RequestRefresh
