--[[
Mount Sheet Sync 3.0.2 — подсказки о маунтах
============================================
При наведении на рара (UnitClassification «rare» / «rareelite») со строки «Мировых боссов» — у скольких персонажей
он на этой неделе ещё не убит и его маунт: попытки и шанс за все попытки. Боссам подсказки юнита нет: в рейдах и
подземельях игра прячет аддонам имена НИП даже до боя (проверено 30.10.2026), сравнить их нельзя.
В Атласе приключений — значок-подкова под вкладками страницы босса (справа), на странице рейда / подземелья — маунты
всех его боссов; строки — в подсказке значка; на вкладке «Модель» значка нет.
Данные — из списка таблицы (ns.BossHint, Remaining.lua); без таблицы программы 2.8+ подсказки нет. «Секретные»
значения не сравниваем. /mss tip — в чат, что аддон видит при наведении.
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
function ns.BossHintLines(name, maxLines, kind)
  if type(name) ~= "string" or Secret(name) or name == "" or not ns.BossHint then
    return {}
  end
  local ok, hints = pcall(ns.BossHint, name)
  if not ok or type(hints) ~= "table" or #hints == 0 then
    return {}
  end
  local lines, left = {}, 0
  local shown = 0
  for _, h in ipairs(hints) do
    if kind and h.kind ~= kind then
      -- только строки этого вида (подсказка юнита — мировые: рары)
    elseif shown < (maxLines or MAX_LINES) then
      shown = shown + 1
      left = math.max(left, h.left or 0)
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
-- /mss tip — в чат, почему подсказка о боссе появилась или нет (проверка в игре; не сохраняется).
local function Debug(text)
  if ns.tipDebug and DEFAULT_CHAT_FRAME then
    DEFAULT_CHAT_FRAME:AddMessage("|cff33ff99Mount Sheet Sync|r tooltip: " .. text)
  end
end

-- Обычная строка, которую можно сравнивать (не «секретная» и не пустая).
local function Plain(v)
  if v == nil or Secret(v) or type(v) ~= "string" or v == "" then
    return nil
  end
  return v
end

local MAX_INSTANCE_LINES = 24
local MAX_UNIT_INSTANCE_LINES = 12

-- Маунты всех боссов подземелья (journalInstanceID): имя босса заголовком, под ним его строки.
local function InstanceLines(instanceID, limit)
  local lines = {}
  if EJ_GetEncounterInfoByIndex and instanceID then
    local index = 1
    while #lines < limit do
      local name = EJ_GetEncounterInfoByIndex(index, instanceID)
      if type(name) ~= "string" or Secret(name) or name == "" then
        break
      end
      local boss = ns.BossHintLines(name, 6)
      if #boss > 0 then
        lines[#lines + 1] = "|cffffd100" .. name:gsub("|", "||") .. "|r"
        for _, line in ipairs(boss) do
          lines[#lines + 1] = line
        end
      end
      index = index + 1
    end
  end
  return lines
end

local function OnUnitTooltip(tooltip, data)
  if not tooltip then
    return
  end
  local name, unit
  if tooltip.GetUnit then
    name, unit = tooltip:GetUnit()
  end
  if Secret(unit) then
    unit = nil
  end
  -- имя: из подсказки, из юнита или из первой строки данных подсказки — что игра отдаст не «секретным»
  local candidates = { name }
  if unit and UnitName then
    candidates[#candidates + 1] = UnitName(unit)
  end
  local lines = type(data) == "table" and not Secret(data.lines) and data.lines
  if type(lines) == "table" and type(lines[1]) == "table" then
    candidates[#candidates + 1] = lines[1].leftText
  end
  local plain
  for _, c in ipairs(candidates) do
    plain = Plain(c)
    if plain then
      break
    end
  end
  if not plain then
    Debug("the game hides this name (secret value)")
    return
  end
  if unit and UnitIsPlayer then
    local isPlayer = UnitIsPlayer(unit)
    if not Secret(isPlayer) and isPlayer then
      return
    end
  end
  -- только рары: у боссов в подземельях игра прячет имена (сравнить их нельзя), а мировые боссы и так в Атласе
  local class = unit and UnitClassification and UnitClassification(unit)
  if class == nil or Secret(class) or (class ~= "rare" and class ~= "rareelite") then
    Debug(string.format("«%s» is not a rare (%s) — the hint is only for rares", plain, class == nil and "nil" or Secret(class) and "secret" or class))
    return
  end
  local hint = ns.BossHintLines(plain, nil, "world")
  if #hint == 0 then
    local data0 = MountSheetSyncSheet
    Debug(string.format("«%s» — no uncollected mount with a chance (list from the sheet: %s)", plain,
      type(data0) == "table" and type(data0.mounts) == "table" and (#data0.mounts .. " rows") or "none"))
    return
  end
  Debug(string.format("«%s» — %d lines", plain, #hint))
  for _, line in ipairs(hint) do
    tooltip:AddLine(line, 1, 1, 1, true)
  end
end

if TooltipDataProcessor and TooltipDataProcessor.AddTooltipPostCall and Enum and Enum.TooltipDataType
  and Enum.TooltipDataType.Unit then
  TooltipDataProcessor.AddTooltipPostCall(Enum.TooltipDataType.Unit, function(tooltip, data)
    local ok, err = pcall(OnUnitTooltip, tooltip, data)
    if not ok then
      Debug("error: " .. tostring(err))
    end
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

-- Новые строки для значка: пустой список прячет значок (другой рейд или босс без маунтов — не старая подсказка).
local function JournalShow(lines)
  local info = EncounterJournal and EncounterJournal.encounter and EncounterJournal.encounter.info
  if not info then
    return
  end
  journalInfo = info
  journalLines = lines
  if #journalLines == 0 and not journalButton then
    return
  end
  JournalButton(info)
  JournalRefresh()
end

local function OnEncounter(encounterID)
  if not EJ_GetEncounterInfo then
    return
  end
  JournalShow(ns.BossHintLines(EJ_GetEncounterInfo(encounterID), 6))
end

-- Страница рейда / подземелья (ещё до выбора босса): маунты всех его боссов, по боссу заголовком.
local function OnInstance(instanceID)
  JournalShow(InstanceLines(instanceID, MAX_INSTANCE_LINES))
end

local function HookJournal()
  if journalHooked or type(EncounterJournal_DisplayEncounter) ~= "function" or not hooksecurefunc then
    return
  end
  journalHooked = true
  hooksecurefunc("EncounterJournal_DisplayEncounter", function(encounterID)
    pcall(OnEncounter, encounterID)
  end)
  if type(EncounterJournal_DisplayInstance) == "function" then
    hooksecurefunc("EncounterJournal_DisplayInstance", function(instanceID)
      pcall(OnInstance, instanceID)
    end)
  end
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
