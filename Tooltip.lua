--[[
Mount Sheet Sync 2.9.1 — подсказка о боссе
==========================================
При наведении на босса — у скольких персонажей из таблицы он на этой неделе ещё не убит и по строке на
несобранный маунт с него: сколько попыток и шанс за все попытки. В Атласе приключений — значок-подкова под
вкладками страницы босса (справа, «Обзор / Добыча / Модель»), те же строки — в его подсказке; на вкладке «Модель»
значка нет. Так он не перекрывает ни текст, ни фильтры добычи, ни модель.
Данные — из списка таблицы (ns.BossHint, Remaining.lua); без таблицы программы 2.8+ подсказки нет. Имена,
которые игра прячет («секретные» значения в бою), не трогаем.
]]

local ADDON_NAME, ns = ...
local L = ns.L

local ICON = "|TInterface\\AddOns\\MountSheetSync\\Media\\Icon:0|t "
local MAX_LINES = 3

local function Secret(v)
  return issecretvalue ~= nil and issecretvalue(v)
end

-- Строки подсказки для босса с таким именем (пусто — ничего не показываем): сначала «ещё не убит у N
-- персонажей», потом по строке на маунт.
function ns.BossHintLines(name, maxLines)
  if type(name) ~= "string" or Secret(name) or name == "" or not ns.BossHint then
    return {}
  end
  local ok, hints = pcall(ns.BossHint, name)
  if not ok or type(hints) ~= "table" or #hints == 0 then
    return {}
  end
  local lines, left = {}, 0
  for i, h in ipairs(hints) do
    left = math.max(left, h.left or 0)
    if i <= (maxLines or MAX_LINES) then
      local mount = "|cffc9a0ff" .. tostring(h.name):gsub("|", "||") .. "|r"
      local n = h.n or 0
      local tail = n > 0 and string.format(L.boss_hint, n, ns.FormatChance(n, h.p)) or L.boss_hint_none
      lines[#lines + 1] = ICON .. mount .. " — " .. tail
    end
  end
  if left > 0 then
    table.insert(lines, 1, "|cff9d9d9d" .. string.format(L.boss_hint_left, left) .. "|r")
  end
  return lines
end

---------------------------------------------------------------------------
-- Подсказка юнита
---------------------------------------------------------------------------
local function OnUnitTooltip(tooltip)
  if not tooltip or not tooltip.GetUnit then
    return
  end
  local name, unit = tooltip:GetUnit()
  if not unit or Secret(unit) or Secret(name) then
    return
  end
  local isPlayer = UnitIsPlayer and UnitIsPlayer(unit)
  if Secret(isPlayer) or isPlayer then
    return
  end
  for _, line in ipairs(ns.BossHintLines(name)) do
    tooltip:AddLine(line, 1, 1, 1, true)
  end
end

if TooltipDataProcessor and TooltipDataProcessor.AddTooltipPostCall and Enum and Enum.TooltipDataType
  and Enum.TooltipDataType.Unit then
  TooltipDataProcessor.AddTooltipPostCall(Enum.TooltipDataType.Unit, function(tooltip)
    pcall(OnUnitTooltip, tooltip)
  end)
end

---------------------------------------------------------------------------
-- Атлас приключений: значок слева от выбора сложности, строки — в его подсказке
---------------------------------------------------------------------------
local journalButton
local journalLines = {}
local journalHooked = false
local journalInfo

-- Значок виден, если про босса есть что сказать и открыта не вкладка «Модель».
local function JournalRefresh()
  if not journalButton then
    return
  end
  local model = journalInfo and journalInfo.model
  local onModel = model and model.IsShown and model:IsShown()
  journalButton:SetShown(#journalLines > 0 and not onModel)
end

local function JournalButton(info)
  if journalButton then
    return journalButton
  end
  local b = CreateFrame("Button", nil, info)
  b:SetSize(22, 22)
  -- под последней вкладкой справа (Модель); в клиенте без вкладок — у правого верхнего края страницы
  local tab = info.modelTab or info.lootTab or info.bossTab or info.overviewTab
  if tab then
    b:SetPoint("TOP", tab, "BOTTOM", 0, -8)
  else
    b:SetPoint("TOPRIGHT", info, "TOPRIGHT", -12, -12)
  end
  if info.model and info.model.HookScript then
    info.model:HookScript("OnShow", JournalRefresh)
    info.model:HookScript("OnHide", JournalRefresh)
  end
  local icon = b:CreateTexture(nil, "ARTWORK")
  icon:SetPoint("TOPLEFT", b, "TOPLEFT", 0, 0)
  icon:SetPoint("BOTTOMRIGHT", b, "BOTTOMRIGHT", 0, 0)
  icon:SetTexture("Interface\\AddOns\\MountSheetSync\\Media\\Icon")
  b:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
    GameTooltip:SetText("Mount Sheet Sync")
    for _, line in ipairs(journalLines) do
      GameTooltip:AddLine(line, 1, 1, 1, true)
    end
    GameTooltip:Show()
  end)
  b:SetScript("OnLeave", function()
    GameTooltip:Hide()
  end)
  journalButton = b
  return b
end

local function OnEncounter(encounterID)
  local info = EncounterJournal and EncounterJournal.encounter and EncounterJournal.encounter.info
  if not info or not EJ_GetEncounterInfo then
    return
  end
  local name = EJ_GetEncounterInfo(encounterID)
  journalInfo = info
  journalLines = ns.BossHintLines(name, 6)
  if #journalLines == 0 and not journalButton then
    return
  end
  JournalButton(info)
  JournalRefresh()
end

local function HookJournal()
  if journalHooked or type(EncounterJournal_DisplayEncounter) ~= "function" or not hooksecurefunc then
    return
  end
  journalHooked = true
  hooksecurefunc("EncounterJournal_DisplayEncounter", function(encounterID)
    pcall(OnEncounter, encounterID)
  end)
end

-- Атлас грузится по требованию: ждём его загрузки (или он уже загружен).
local watcher = CreateFrame("Frame")
watcher:RegisterEvent("ADDON_LOADED")
watcher:SetScript("OnEvent", function(self, _, name)
  if name == "Blizzard_EncounterJournal" then
    pcall(HookJournal)
  end
  if journalHooked then
    self:UnregisterEvent("ADDON_LOADED")
  end
end)
pcall(HookJournal)
if journalHooked then
  watcher:UnregisterEvent("ADDON_LOADED")
end
