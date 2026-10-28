--[[
Mount Sheet Sync 2.9.0 — подсказка о боссе
==========================================
При наведении на босса (и на странице босса в Атласе приключений) — строка о несобранном маунте с него:
сколько попыток, шанс за все попытки и у скольких персонажей из таблицы этот босс на этой неделе ещё не убит.
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

-- Строки подсказки для босса с таким именем (пусто — ничего не показываем).
function ns.BossHintLines(name)
  if type(name) ~= "string" or Secret(name) or name == "" or not ns.BossHint then
    return {}
  end
  local ok, hints = pcall(ns.BossHint, name)
  if not ok or type(hints) ~= "table" then
    return {}
  end
  local lines = {}
  for i, h in ipairs(hints) do
    if i > MAX_LINES then
      break
    end
    local mount = "|cffc9a0ff" .. tostring(h.name):gsub("|", "||") .. "|r"
    local text = string.format(L.boss_hint, mount, h.n, ns.FormatChance(h.n, h.p))
    if (h.left or 0) > 0 then
      text = text .. string.format(L.boss_hint_left, h.left)
    end
    lines[#lines + 1] = ICON .. text
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
-- Атлас приключений: строка под названием босса
---------------------------------------------------------------------------
local journalText
local journalHooked = false

local function OnEncounter(encounterID)
  local info = EncounterJournal and EncounterJournal.encounter and EncounterJournal.encounter.info
  if not info or not info.CreateFontString or not EJ_GetEncounterInfo then
    return
  end
  local name = EJ_GetEncounterInfo(encounterID)
  if not journalText then
    journalText = info:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    local title = info.encounterTitle
    if title then
      journalText:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -4)
    else
      journalText:SetPoint("TOPLEFT", info, "TOPLEFT", 20, -48)
    end
    journalText:SetJustifyH("LEFT")
  end
  local lines = ns.BossHintLines(name)
  journalText:SetText(table.concat(lines, "\n"))
  journalText:SetShown(#lines > 0)
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
watcher:SetScript("OnEvent", function(_, _, name)
  if name == "Blizzard_EncounterJournal" then
    pcall(HookJournal)
  end
end)
pcall(HookJournal)
