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
local alertRarityCheck, showOffCheck, tellGroupCheck, tellGuildCheck
local defaultOn = {}   -- галочки «включено, пока не выключили»: { check, поле db }
local gridButtons = {}
local detailsButtons = {}

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
  tellGroupCheck:SetChecked(db and db.tellGroup and true or false)
  tellGuildCheck:SetChecked(db and db.tellGuild and true or false)
  showOffCheck:SetChecked(db and db.showOff and true or false)
  for _, b in ipairs(gridButtons) do
    b:SetChecked(b.view == (db and db.gridView or "scroll"))
  end
  for _, b in ipairs(detailsButtons) do
    b:SetChecked(b.details == (db and db.detailsKey or "alt"))
  end
end

local COLUMN_LEFT, COLUMN_RIGHT = 20, 320   -- окно настроек — два столбца

local function SettingsCheck(parent, y, key, onClick, x)
  local c = Skin.Check(parent, false)
  c:SetPoint("TOPLEFT", (x or COLUMN_RIGHT) + 6, y)
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

-- what: nil — сведения для отчёта об ошибке; "check" — самопроверка (/mss check, Check.lua)
local reportWhat, reportRelabel
local function ShowReport(what)
  if not reportFrame then
    reportFrame = Skin.Window(REPORT_NAME, 520, 300, "DIALOG")
    reportFrame:SetPoint("CENTER", 0, 40)
    local function title()
      reportFrame.titleText:SetText(reportWhat == "check" and L.check_title or L.report_title)
    end
    title()
    relabel[#relabel + 1] = title
    local hint = reportFrame:CreateFontString(nil, "OVERLAY", "MSSFontBody")
    hint:SetPoint("TOPLEFT", 18, -38)
    hint:SetPoint("TOPRIGHT", -18, -38)
    hint:SetJustifyH("LEFT")
    local function hintText()
      hint:SetText(reportWhat == "check" and L.check_hint or string.format(L.report_hint, ns.ISSUES_URL))
    end
    hintText()
    relabel[#relabel + 1] = hintText
    reportRelabel = function()
      title()
      hintText()
    end
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
  reportWhat = what
  reportRelabel()
  reportFrame.text = what == "check" and ns.SelfCheck() or ns.BugReport()
  reportBox:SetText(reportFrame.text)
  reportFrame:Show()
  reportBox:SetFocus()
  reportBox:HighlightText()
end
UI.ShowReport = ShowReport
function ns.ShowCheck()
  ShowReport("check")
end

local function CreateSettings()
  local codes = { "auto" }
  for _, code in ipairs(ns.LANGUAGES) do
    codes[#codes + 1] = code
  end
  local height = math.max(500, 62 + #codes * 24 + 98 + 44 + 138)   -- справа до -474 (галочки «в группу / в гильдию»)
  settingsFrame = Skin.Window(SETTINGS_NAME, 620, height, "DIALOG")
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

  -- Заголовок раздела: слева язык и данные, справа — окно, оповещение и прочее.
  local function Head(key, x, y)
    local h = settingsFrame:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
    h:SetPoint("TOPLEFT", x, y)
    Label(h, key)
    return h
  end

  -- «Все персонажи»: вид сетки и строки, которые никто не фармит
  Head("settings_grid", COLUMN_RIGHT, -40)
  for i, choice in ipairs({ { "scroll", "settings_grid_scroll", "tip_grid_scroll" }, { "all", "settings_grid_all", "tip_grid_all" } }) do
    local b = Skin.Check(settingsFrame, true)
    b:SetPoint("TOPLEFT", COLUMN_RIGHT + 6, -62 - (i - 1) * 24)
    b.view = choice[1]
    Label(b.Text, choice[2])
    AttachTooltip(b, choice[2], choice[3])
    b:SetScript("OnClick", function(self)
      UI.SetGridView(self.view)
      RefreshSettings()
    end)
    gridButtons[#gridButtons + 1] = b
  end
  showOffCheck = SettingsCheck(settingsFrame, -110, "set_show_off", function(self)
    local db = ns.GetDB()
    if db then
      db.showOff = self:GetChecked() and true or nil
      if S.frame and S.frame:IsShown() then
        UI.Refresh()
      end
    end
  end)
  AttachTooltip(showOffCheck, "set_show_off", "set_show_off_tip")

  -- оповещение о выпавшем маунте (Alert.lua)
  Head("settings_alert_head", COLUMN_RIGHT, -146)
  DefaultOnCheck(settingsFrame, -168, "settings_alert", "alert", "tip_alert")
  DefaultOnCheck(settingsFrame, -192, "settings_alert_sound", "alertSound", "tip_alert")
  alertRarityCheck = SettingsCheck(settingsFrame, -216, "settings_alert_rarity", function(self)
    local db = ns.GetDB()
    if db then
      db.alertRarity = self:GetChecked() and true or nil   -- по умолчанию выключено: показываем и вместе с Rarity
    end
  end)
  AttachTooltip(alertRarityCheck, "settings_alert_rarity", "tip_alert_rarity")
  local testButton = MakeButton(settingsFrame, "button_alert_test", 200, "tip_alert_test", function()
    ns.SafeCall(ns.TestDrop)
  end)
  testButton:SetPoint("TOPLEFT", COLUMN_RIGHT + 6, -244)

  Head("settings_other", COLUMN_RIGHT, -284)
  settingsQuiet = SettingsCheck(settingsFrame, -306, "quiet", function(self)
    ns.SetQuiet(self:GetChecked())
  end)
  AttachTooltip(settingsQuiet, "quiet", "tip_quiet")
  settingsMinimap = SettingsCheck(settingsFrame, -330, "settings_minimap", function(self)
    local db = ns.GetDB()
    local shown = not (db and type(db.minimap) == "table" and db.minimap.hide)
    if shown ~= (self:GetChecked() and true or false) then
      ns.ToggleMinimapButton()
    end
  end)
  DefaultOnCheck(settingsFrame, -354, "settings_mapzeroth", "mapzeroth", "tip_mapzeroth")
  local tipsCheck = DefaultOnCheck(settingsFrame, -378, "settings_tips", "tips", "tip_settings_tips")
  DefaultOnCheck(settingsFrame, -402, "settings_login", "loginNote", "tip_settings_login")
  -- о выпавшем маунте — в группу и в гильдию (Alert.lua, ns.TellDrop): каждое своей галочкой, по умолчанию выключено
  tellGroupCheck = SettingsCheck(settingsFrame, -426, "settings_tell_group", function(self)
    local db = ns.GetDB()
    if db then
      db.tellGroup = self:GetChecked() and true or nil
    end
  end)
  AttachTooltip(tellGroupCheck, "settings_tell_group", "tip_settings_tell")
  tellGuildCheck = SettingsCheck(settingsFrame, -450, "settings_tell_guild", function(self)
    local db = ns.GetDB()
    if db then
      db.tellGuild = self:GetChecked() and true or nil
    end
  end)
  AttachTooltip(tellGuildCheck, "settings_tell_guild", "tip_settings_tell")
  tipsCheck:HookScript("OnClick", function()
    if S.frame and S.frame:IsShown() then
      UI.Refresh(true)
    end
  end)

  -- слева под языком: неделя, журнал, отчёт
  Head("settings_data", COLUMN_LEFT, -62 - #codes * 24 - 14)
  local resetButton = MakeButton(settingsFrame, "button_reset", 200, "tip_reset", UI.ConfirmResetWeek)
  resetButton:SetPoint("TOPLEFT", COLUMN_LEFT + 6, -62 - #codes * 24 - 38)

  local clearButton = MakeButton(settingsFrame, "button_clear", 200, "tip_clear", ConfirmClear)
  clearButton:SetPoint("TOPLEFT", COLUMN_LEFT + 6, -62 - #codes * 24 - 68)

  local reportButton = MakeButton(settingsFrame, "button_report", 200, "tip_report", function()
    ns.SafeCall(ShowReport)
  end)
  reportButton:SetPoint("TOPLEFT", COLUMN_LEFT + 6, -62 - #codes * 24 - 98)

  -- подсказка строки окна: какой клавишей открываются подробности (UI.Rows.lua, DetailsNow); в сохранениях — только не Alt
  local detailsTop = -62 - #codes * 24 - 98 - 40
  Head("settings_details", COLUMN_LEFT, detailsTop)
  for i, choice in ipairs({ { "alt", "Alt" }, { "shift", "Shift" }, { "ctrl", "Ctrl" }, { "always" } }) do
    local b = Skin.Check(settingsFrame, true)
    b:SetPoint("TOPLEFT", COLUMN_LEFT + 6, detailsTop - 22 - (i - 1) * 24)
    b.details = choice[1]
    local function text()
      b.Text:SetText(choice[2] and string.format(L.settings_details_key, choice[2]) or L.settings_details_always)
    end
    text()
    relabel[#relabel + 1] = text
    AttachTooltip(b, "settings_details", "tip_settings_details")
    b:SetScript("OnClick", function(self)
      local db = ns.GetDB()
      if db then
        db.detailsKey = self.details ~= "alt" and self.details or nil
      end
      RefreshSettings()
    end)
    detailsButtons[#detailsButtons + 1] = b
  end

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
