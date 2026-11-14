--[[
Mount Sheet Sync 3.0.7 — оповещение о выпавшем маунте

Панель вверху экрана в оформлении окна: строка маунта, как в списке, медленно зачёркивается слева направо, счётчик
«Осталось собрать» становится на единицу меньше, справа — с какой попытки выпал. Два своих звука (Media\Drop.ogg — в
момент появления, Media\Strike.ogg — на зачёркивание; записи с Freesound, лицензия CC0).

Показывается только для маунтов из списка (у которых есть шанс и счётчик попыток). Выключается в настройках:
db.alert == false — без панели, db.alertSound == false — без звука. Проверка без маунта: /mss drop.
]]

local _, ns = ...
local L = ns.L
local Skin = ns.Skin
local COLOR = Skin.COLOR

local MEDIA = "Interface\\AddOns\\MountSheetSync\\Media\\"
local SOUND_DROP, SOUND_STRIKE = MEDIA .. "Drop.ogg", MEDIA .. "Strike.ogg"
local WIDTH, HEIGHT = 440, 74
-- время от появления панели, в секундах
local FADE_IN, STRIKE_AT, STRIKE_TIME, HOLD, FADE_OUT = 0.3, 0.9, 1.1, 5, 0.8
local TEST_MOUNT = 363   -- Непобедимый: для /mss drop

local frame, shownAt, current

local function Play(file)
  local db = ns.GetDB and ns.GetDB()
  if db and db.alertSound == false then
    return
  end
  if PlaySoundFile then
    pcall(PlaySoundFile, file, "SFX")
  end
end

local function Chance(p)
  p = tonumber(p)
  if not p or p <= 0 then
    return nil
  end
  local percent = p * 100
  return (percent >= 1 and string.format("%d", percent + 0.5) or string.format("%.1f", percent)) .. " %"
end

-- плавно в начале и в конце: черта не «выстреливает»
local function Ease(x)
  x = math.max(0, math.min(1, x))
  return x * x * (3 - 2 * x)
end

local function Create()
  local f = CreateFrame("Button", "MountSheetSyncAlertFrame", UIParent)
  f:SetSize(WIDTH, HEIGHT)
  f:SetPoint("TOP", UIParent, "TOP", 0, -150)
  f:SetFrameStrata("HIGH")
  f:SetClampedToScreen(true)
  Skin.Fill(f, "Round6", COLOR.goldLine, 0, 1)
  Skin.Fill(f, "Round6", COLOR.window, 1, 2)
  local bar = f:CreateTexture(nil, "BACKGROUND", nil, 3)
  bar:SetColorTexture(COLOR.title[1], COLOR.title[2], COLOR.title[3])
  bar:SetPoint("TOPLEFT", 2, -2)
  bar:SetPoint("BOTTOMRIGHT", f, "TOPRIGHT", -2, -24)

  f.head = f:CreateFontString(nil, "OVERLAY", "MSSFontGold")
  f.head:SetPoint("TOPLEFT", 12, -7)
  f.head:SetText("Mount Sheet Sync")
  f.left = f:CreateFontString(nil, "OVERLAY", "MSSFontSmall")
  f.left:SetPoint("TOPRIGHT", -12, -7)

  f.icon = f:CreateTexture(nil, "ARTWORK")
  f.icon:SetSize(34, 34)
  f.icon:SetPoint("TOPLEFT", 12, -32)
  f.icon:SetTexCoord(0.07, 0.93, 0.07, 0.93)

  f.name = f:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  f.name:SetPoint("LEFT", f.icon, "RIGHT", 10, 0)
  f.name:SetJustifyH("LEFT")
  f.name:SetWordWrap(false)

  f.tries = f:CreateFontString(nil, "OVERLAY", "MSSFontDim")
  f.tries:SetPoint("RIGHT", f, "TOPRIGHT", -12, -49)
  f.tries:SetJustifyH("RIGHT")

  -- черта: растёт от левого края имени
  f.line = f:CreateTexture(nil, "OVERLAY")
  f.line:SetColorTexture(COLOR.gold[1], COLOR.gold[2], COLOR.gold[3])
  f.line:SetHeight(2)
  f.line:SetPoint("LEFT", f.name, "LEFT", -2, -1)
  f.line:SetWidth(1)
  f.line:Hide()

  f:RegisterForClicks("AnyUp")
  f:SetScript("OnClick", function(self)
    self:Hide()
  end)
  f:Hide()
  return f
end

local function LeftText(n, changed)
  if not n then
    return ""
  end
  return string.format(L.alert_left, (changed and "|cffffd24a" or "|cffe4dfd2") .. tostring(n) .. "|r")
end

local function OnUpdate(self)
  local t = GetTime() - shownAt
  local info = current
  if t < FADE_IN then
    self:SetAlpha(t / FADE_IN)
  elseif t < STRIKE_AT + STRIKE_TIME + HOLD then
    self:SetAlpha(1)
  end
  if t >= STRIKE_AT and not info.striking then
    info.striking = true
    self.line:Show()
    Play(SOUND_STRIKE)
  end
  if info.striking and not info.struck then
    local full = math.min(self.name:GetStringWidth() + 4, WIDTH - 150)
    local part = Ease((t - STRIKE_AT) / STRIKE_TIME)
    self.line:SetWidth(math.max(1, full * part))
    if part >= 1 then
      info.struck = true
      self.left:SetText(LeftText(info.left and math.max(0, info.left - 1), true))
      self.name:SetTextColor(0.66, 0.62, 0.55)
    end
  end
  local out = t - (STRIKE_AT + STRIKE_TIME + HOLD)
  if out >= FADE_OUT then
    self:Hide()
  elseif out > 0 then
    self:SetAlpha(1 - out / FADE_OUT)
  end
end

--- Показать оповещение. info = { name, icon, n (попыток), p (шанс 0..1), left (сколько было в списке несобранных) }.
function ns.ShowDrop(info)
  if type(info) ~= "table" or not info.name then
    return false
  end
  local db = ns.GetDB and ns.GetDB()
  if db and db.alert == false and not info.test then
    return false
  end
  frame = frame or Create()
  current = { left = tonumber(info.left) }
  shownAt = GetTime()
  frame.name:SetText(tostring(info.name))
  frame.name:SetTextColor(COLOR.gold[1], COLOR.gold[2], COLOR.gold[3])
  frame.icon:SetTexture(info.icon or 134400)   -- 134400 — знак вопроса игры
  frame.left:SetText(LeftText(current.left, false))
  local n, chance = tonumber(info.n), Chance(info.p)
  local tries = n and n > 0 and string.format(L.alert_attempts, n) or ""
  frame.tries:SetText(chance and tries ~= "" and (tries .. ", " .. chance) or chance or tries)
  frame.line:SetWidth(1)
  frame.line:Hide()
  frame:SetAlpha(0)
  frame:SetScript("OnUpdate", OnUpdate)
  frame:Show()
  Play(SOUND_DROP)
  return true
end

--- Что известно о маунте до того, как он станет собранным: строка вкладки «Попытки» и сколько в ней несобранных.
--- Звать до ns.CollectionAdd: собранные из этого списка пропадают.
function ns.DropInfo(mountID)
  if not mountID or not ns.GetAttemptsList then
    return nil
  end
  local list = ns.GetAttemptsList()
  for _, e in ipairs(list) do
    if tonumber(e.id) == tonumber(mountID) then
      return { name = e.name, n = e.n, p = e.p, left = #list }
    end
  end
  return nil
end

--- /mss drop — оповещение с выдуманным выпадением: посмотреть и послушать, ничего не записывается.
function ns.TestDrop()
  local name, icon
  if C_MountJournal and C_MountJournal.GetMountInfoByID then
    local ok, n, _, i = pcall(C_MountJournal.GetMountInfoByID, TEST_MOUNT)
    if ok and type(n) == "string" and not (issecretvalue and issecretvalue(n)) then
      name, icon = n, i
    end
  end
  local list = ns.GetAttemptsList and ns.GetAttemptsList() or {}
  return ns.ShowDrop({ name = name or L.alert_test_name, icon = icon, n = 147, p = 0.01, left = math.max(#list, 1), test = true })
end
