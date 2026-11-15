--[[
Mount Sheet Sync — кнопка у миникарты
Значок LibDataBroker, меню аддонов у миникарты и своя кнопка.
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local ADDON_NAME, ICON, L, FillBrokerTooltip = P.ADDON_NAME, P.ICON, P.L, P.FillBrokerTooltip

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
