--[[
Mount Sheet Sync 3.0.4 — окно, иконка и настройки
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
local TAB_LEFT, TAB_ATTEMPTS, TAB_LOG = 1, 2, 3
local TAB_KEYS = { "left", "attempts", "log" }
local TAB_LABELS = { "tab_left", "tab_attempts", "tab_log" }
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

local function BossAndDiff(boss, diff)
  local text = Safe(boss)
  if Safe(diff) ~= "" then
    text = text .. " (" .. Safe(diff) .. ")"
  end
  return text
end

-- Журнал маунтов игры на этом маунте. Наше окно выше по слоям и закрыло бы журнал: прячем, а когда журнал закроют, возвращаем.
local reopenAfterJournal, journalHooked

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
  if CollectionsJournal and CollectionsJournal:IsShown() and frame:IsShown() then
    if not journalHooked then
      journalHooked = true
      CollectionsJournal:HookScript("OnHide", function()
        if reopenAfterJournal then
          reopenAfterJournal = false
          frame:Show()
        end
      end)
    end
    reopenAfterJournal = true
    frame:Hide()
  end
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

-- Подсказка строки: elem.tip = { заголовок, строка, … }; у строк с меню — ещё «ПКМ — не фармлю».
local function ShowRowTooltip(button)
  local elem = button.elem
  if not elem or not (elem.tip or elem.entry) then
    return
  end
  GameTooltip:SetOwner(button, "ANCHOR_CURSOR")
  if elem.tip then
    GameTooltip:SetText(elem.tip[1], 1, 0.82, 0)
    for i = 2, #elem.tip do
      GameTooltip:AddLine(elem.tip[i], 1, 1, 1, true)
    end
    if elem.mountID then
      GameTooltip:AddLine(L.tip_row_open, 0.6, 0.6, 0.6, true)
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

-- «N H M»: что ещё можно — цветом color, убитое — тёмным.
local function PartsText(killed, color)
  local out = {}
  for i, p in ipairs(ns.PARTS) do
    out[i] = "|c" .. (killed[p] and "ff5f5a4e" or color) .. p .. "|r"
  end
  return table.concat(out, " ")
end

-- Ячейки сетки у строки: кружок цвета класса — надо убить, чёрточка — уже убит;
-- у строки с раздельными сохранениями — буквы сложностей (в узких столбцах — сколько осталось).
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
    button:SetScript("OnClick", function(self, mouse)
      if mouse == "RightButton" then
        ShowRowMenu(self)
      elseif self.elem and self.elem.mountID then
        ShowMountInJournal(self.elem.mountID)
      end
    end)
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

-- Строка таблицы у персонажа: осталось — со счётчиком попыток, убита — серая, с галочкой.
local function RemainingRow(entry, menu, stripe)
  local cols, doneCols = LeftColumns()
  local item = entry.item
  local tip = { Safe(item.mount), BossAndDiff(item.boss, item.diff) }
  if Safe(item.place) ~= "" then
    tip[#tip + 1] = Safe(item.place)
  end
  local n, p, id = ns.ItemAttempts(item)
  if entry.done then
    local status = (entry.again and string.format(L.left_again, ns.FormatDuration(entry.again)) or L.left_done):gsub("^%s*—%s*", "")
    return { cols = doneCols, done = true, icon = DONE_TEXTURE, mountID = KnownMount(item.id or id), stripe = stripe, tip = tip, values = {
      boss = "|cff8c8c8c" .. Safe(item.boss) .. "|r",
      diff = "|cff8c8c8c" .. ShortDiff(item.diff) .. "|r",
      mount = "|cff8c8c8c" .. Safe(item.mount) .. "|r",
      status = "|cff8c8c8c" .. status .. "|r",
    } }
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
  return { cols = cols, icon = ns.MountIcon(item.id or id), mountID = KnownMount(item.id or id), stripe = stripe, tip = tip, entry = menu and entry or nil, values = {
    boss = Safe(item.boss),
    diff = diff,
    mount = "|cffc9a0ff" .. Safe(item.mount) .. "|r",
    place = "|cff8c8c8c" .. Safe(item.place) .. "|r",
    n = n and n > 0 and tostring(n) or "|cff8c8c8c-|r",
    chance = chance,
  } }
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
      if entry.item.kind == kind then
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
  for _, row in ipairs(rows) do
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
    list[#list + 1] = { cols = cols, names = names, killed = row.killed or false, icon = ns.MountIcon(item.id or id), mountID = KnownMount(item.id or id), stripe = stripe,
      tip = { Safe(item.mount), BossAndDiff(item.boss, item.diff), table.concat(shownNames, ", ") }, values = {
        boss = Safe(item.boss),
        diff = "|cffb7ae9a" .. ShortDiff(item.diff) .. "|r",
        mount = "|cffc9a0ff" .. Safe(item.mount) .. "|r",
        total = "|cffd8c98f" .. row.total .. "|r",
      } }
    stripe = not stripe
  end
  if #rows == 0 then
    list[#list + 1] = Line("|cff8cff8c" .. L.all_empty .. "|r")
  end
  return { list = list, cols = #rows > 0 and cols or nil, grid = #rows > 0,
    summary = "|cff8c8c8c" .. string.format(L.all_head, #rows, #all) .. "|r" }
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
    list[i] = { cols = cols, icon = ns.MountIcon(m.id), mountID = KnownMount(m.id), stripe = i % 2 == 0, tip = tip, bar = v, barColor = bar, values = {
      mount = "|cffc9a0ff" .. Safe(m.name) .. "|r",
      boss = Safe(m.boss),
      diff = "|cffb7ae9a" .. ShortDiff(m.diff) .. "|r",
      n = hasTries and tostring(m.n) or "|cff8c8c8c-|r",
      chance = "|cff" .. color .. (hasTries and ns.FormatChance(m.n, m.p) or "-") .. "|r",
      left = (m.left > 0 and "|cff7fdc7f" or "|cff8c8c8c") .. m.left .. "/" .. chars .. "|r",
    } }
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
  local showAll = selectedChar == ALL_CHARS
  local key = tostring(all) .. "|" .. tostring(sel) .. "|" .. tostring(showAll)
  if key == shownChars then
    return
  end
  shownChars = key
  local elems = {}
  if #all > 0 then
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
  local showAll = selectedChar == ALL_CHARS and #all > 0
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
    local point, _, relativePoint, x, y = self:GetPoint()
    ns.GetDB().window = { point = point, relativePoint = relativePoint, x = x, y = y }
  end)

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

  local hint = frame:CreateFontString(nil, "OVERLAY", "MSSFontSmall")
  hint:SetPoint("LEFT", syncButton, "RIGHT", 10, 0)
  hint:SetPoint("RIGHT", gear, "LEFT", -10, 0)
  hint:SetJustifyH("LEFT")
  hint:SetWordWrap(false)
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
