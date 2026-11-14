--[[
Mount Sheet Sync — окно настроек и отчёт об ошибке
Язык, галочки, вид сетки, кнопки «Сброс недели» и «Очистить журнал», сведения для отчёта.
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local L, Skin, relabel, AttachTooltip, Label, MakeButton = P.L, P.Skin, P.relabel, P.AttachTooltip, P.Label, P.MakeButton
local ConfirmClear = P.ConfirmClear

---------------------------------------------------------------------------
-- Настройки: язык, сообщения в чате, кнопка у миникарты (ПКМ по иконке, /mss settings)
---------------------------------------------------------------------------
local SETTINGS_NAME = "MountSheetSyncSettingsFrame"
local settingsFrame
local languageButtons = {}
local settingsQuiet, settingsMinimap
local alertRarityCheck
local defaultOn = {}   -- галочки «включено, пока не выключили»: { check, поле db }
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
  for _, c in ipairs(defaultOn) do
    c.check:SetChecked(not (db and db[c.field] == false))
  end
  alertRarityCheck:SetChecked(db and db.alertRarity and true or false)
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

-- Галочка «включено, пока не выключили»: в сохранениях только false (нет поля — включено).
local function DefaultOnCheck(parent, y, key, field, tip)
  local c = SettingsCheck(parent, y, key, function(self)
    local db = ns.GetDB()
    if db then
      if self:GetChecked() then
        db[field] = nil
      else
        db[field] = false
      end
    end
  end)
  AttachTooltip(c, key, tip)
  defaultOn[#defaultOn + 1] = { check = c, field = field }
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
  local height = 100 + #codes * 24 + 70 + 96 + 86 + 26 + 52 + 26
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
  DefaultOnCheck(settingsFrame, y - 52, "settings_mapzeroth", "mapzeroth", "tip_mapzeroth")
  -- оповещение о выпавшем маунте (Alert.lua)
  DefaultOnCheck(settingsFrame, y - 78, "settings_alert", "alert", "tip_alert")
  DefaultOnCheck(settingsFrame, y - 104, "settings_alert_sound", "alertSound", "tip_alert")
  alertRarityCheck = SettingsCheck(settingsFrame, y - 130, "settings_alert_rarity", function(self)
    local db = ns.GetDB()
    if db then
      db.alertRarity = self:GetChecked() and true or nil   -- по умолчанию выключено: показываем и вместе с Rarity
    end
  end)
  AttachTooltip(alertRarityCheck, "settings_alert_rarity", "tip_alert_rarity")

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
  if S.frame and S.frame:IsShown() then
    UI.Refresh()
  end
  RefreshSettings()
end)
