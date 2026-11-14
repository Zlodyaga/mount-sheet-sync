--[[
Mount Sheet Sync 3.0.7 — окно: строки списков
Что рисует строку и что делает щелчок по ней: ячейки, сетка «Все персонажи», подсказки, левая панель персонажей.
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local ROW_HEIGHT, CHAR_HEIGHT, TOOLBAR_HEIGHT, SLIDER_ROOM, GRID_SCROLL, ALL_CHARS = P.ROW_HEIGHT, P.CHAR_HEIGHT, P.TOOLBAR_HEIGHT, P.SLIDER_ROOM, P.GRID_SCROLL, P.ALL_CHARS
local L, Skin, grid, FormatDuration, Safe, ShowAchievement = P.L, P.Skin, P.grid, P.FormatDuration, P.Safe, P.ShowAchievement
local ShowMountInJournal, ShowInGuide, ShowRoute = P.ShowMountInJournal, P.ShowInGuide, P.ShowRoute

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
    S.columnGlow:Hide()
    S.focusText:SetText("")
    return
  end
  S.columnGlow:ClearAllPoints()
  S.columnGlow:SetPoint("TOPLEFT", 6 + grid.x + (slot - 1) * grid.cell, -TOOLBAR_HEIGHT)
  S.columnGlow:SetPoint("BOTTOMLEFT", 6 + grid.x + (slot - 1) * grid.cell, grid.overflow and SLIDER_ROOM + 6 or 6)
  S.columnGlow:SetWidth(grid.cell)
  S.columnGlow:Show()
  S.focusText:SetText(string.format(L.all_focus, c.colored))
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
        if S.selectedChar == ALL_CHARS then
          FocusColumn(e.index)
        end
      end
      GameTooltip:Show()
    end)
    button:SetScript("OnLeave", function()
      HideTooltip()
      if S.selectedChar == ALL_CHARS then
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
  S.header1:SetText(string.format("%s — %s", player.name or "?", player.realm or "?"))
  local untilReset = ns.SecondsUntilReset()
  S.header2:SetText(string.format(L.header_week, ns.CountThisWeek(), untilReset and FormatDuration(untilReset) or "?"))
end

local function Line(text, entry, tip)
  return { text = text, entry = entry, tip = tip }
end

local function GroupLine(kind)
  return { text = "|cffffd200" .. ns.KindTitle(kind) .. "|r", group = true }
end

-- нужны следующим файлам окна
P.HideTooltip, P.FocusColumn, P.PartsText, P.DrawGrid, P.InitRow, P.DONE_TEXTURE = HideTooltip, FocusColumn, PartsText, DrawGrid, InitRow, DONE_TEXTURE
P.ClassColored, P.InitCharRow, P.Line, P.GroupLine = ClassColored, InitCharRow, Line, GroupLine
