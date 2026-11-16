--[[
Mount Sheet Sync — маршруты и места появления редких монстров на карте мира
============================================================================
Данные — ns.RARE_ROUTES (Routes.lua, генерируется): карта > маунты > места появления и линии маршрутов.
Справа на карте — маленький значок; он есть только на карте, где у несобранного маунта есть данные. Щелчок: маунт на
карте один — включить или выключить его маршрут; несколько — список с галочками, каждый включается отдельно.
Что включено, помнится по карте и маунту (db.routes[карта][маунт]); рисуется только для открытой сейчас карты, поэтому
на другой карте чужих линий нет, а при возврате всё как было.
Линии лежат на своём кадре поверх холста карты, выше слоя «изученной территории» (её текстуры — значки карты с высоким
уровнем кадра; линия ниже них видна только на неизученном). В поля WorldMapFrame ничего не пишем.
]]

local ADDON_NAME, ns = ...
local L = ns.L

local COLORS = {
  { 1.00, 0.30, 0.30 }, { 0.35, 0.65, 1.00 }, { 0.35, 0.90, 0.45 }, { 1.00, 0.82, 0.20 }, { 0.80, 0.45, 1.00 }, { 1.00, 0.55, 0.15 },
}
local LINE_THICKNESS, DOT_SIZE = 3, 14   -- в точках экрана: делим на масштаб холста, чтобы не толстели при приближении
local ICON = "Interface\\AddOns\\MountSheetSync\\Media\\Icon"
local DOT = "Interface\\COMMON\\Indicator-Gray"

local overlay, button, provider
local lines, dots = {}, {}
local shownMap   -- карта, для которой сейчас нарисовано (для проверок)

local function Collected(id)
  if not C_MountJournal or not C_MountJournal.GetMountInfoByID then
    return false
  end
  local ok, _, _, _, _, _, _, _, _, _, _, isCollected = pcall(C_MountJournal.GetMountInfoByID, id)
  return ok and isCollected == true
end

local function MountName(id)
  local ok, name = pcall(C_MountJournal.GetMountInfoByID, id)
  if ok and type(name) == "string" and name ~= "" then
    return name
  end
  return "#" .. tostring(id)
end

-- Карта материка показывает то же, что карты её зон: точки пересчитываются через место зоны на материке.
local PARENTS = { [424] = { 371, 418, 379, 388, 422 } }   -- Пандария: зоны завоевателей Зандалари
local merged = {}

local function Moved(points, minX, maxX, minY, maxY)
  local out = {}
  for i = 1, #points - 1, 2 do
    out[i] = (minX + points[i] / 100 * (maxX - minX)) * 100
    out[i + 1] = (minY + points[i + 1] / 100 * (maxY - minY)) * 100
  end
  return out
end

local function MapList(mapID)
  local children = PARENTS[mapID]
  if not children or not (C_Map and C_Map.GetMapRectOnMap) then
    return ns.RARE_ROUTES[mapID]
  end
  if merged[mapID] then
    return merged[mapID]
  end
  local list, byMount = {}, {}
  for _, e in ipairs(ns.RARE_ROUTES[mapID] or {}) do
    list[#list + 1] = e
  end
  for _, child in ipairs(children) do
    local ok, minX, maxX, minY, maxY = pcall(C_Map.GetMapRectOnMap, child, mapID)
    if ok and type(minX) == "number" and type(maxY) == "number" and maxX > minX and maxY > minY then
      for _, e in ipairs(ns.RARE_ROUTES[child] or {}) do
        local to = byMount[e.mount]
        if not to then
          to = { mount = e.mount, spawns = {}, lines = {} }
          byMount[e.mount] = to
          list[#list + 1] = to
        end
        for _, v in ipairs(Moved(e.spawns or {}, minX, maxX, minY, maxY)) do
          to.spawns[#to.spawns + 1] = v
        end
        for _, points in ipairs(e.lines or {}) do
          to.lines[#to.lines + 1] = Moved(points, minX, maxX, minY, maxY)
        end
      end
    end
  end
  merged[mapID] = list
  return list
end

-- Несобранные маунты с данными для этой карты.
function ns.RouteEntries(mapID)
  local out = {}
  local list = mapID and ns.RARE_ROUTES and MapList(mapID)
  for _, e in ipairs(list or {}) do
    if not Collected(e.mount) then
      out[#out + 1] = e
    end
  end
  return out
end

local function State(mapID, create)
  local db = ns.GetDB()
  if not db then
    return nil
  end
  if type(db.routes) ~= "table" then
    if not create then
      return nil
    end
    db.routes = {}
  end
  if type(db.routes[mapID]) ~= "table" and create then
    db.routes[mapID] = {}
  end
  return db.routes[mapID]
end

function ns.RouteShown(mapID, mount)
  local state = State(mapID)
  return state and state[mount] == true or false
end

local function HideAll()
  for _, line in ipairs(lines) do
    line:Hide()
  end
  for _, dot in ipairs(dots) do
    dot:Hide()
  end
  shownMap = nil
end

local function Line(i)
  if not lines[i] then
    lines[i] = overlay:CreateLine(nil, "ARTWORK")
  end
  return lines[i]
end

-- Точка появления — маленький кадр: при наведении говорит, чей это рар. Щелчки проходят сквозь него к карте;
-- если клиент не умеет «только наведение», мышь точке не включаем вовсе, чтобы не мешать карте.
local function DotEnter(self)
  GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
  GameTooltip:SetText(MountName(self.mount), 1, 0.82, 0)
  GameTooltip:AddLine(string.format(L.map_routes_spawn, self.x, self.y), 1, 1, 1, true)
  GameTooltip:Show()
end

local function DotLeave()
  GameTooltip:Hide()
end

local function Dot(i)
  if not dots[i] then
    local f = CreateFrame("Frame", nil, overlay)
    f.tex = f:CreateTexture(nil, "OVERLAY")
    f.tex:SetAllPoints(f)
    f.tex:SetTexture(DOT)
    if f.SetMouseClickEnabled then
      f:EnableMouse(true)
      f:SetMouseClickEnabled(false)
      f:SetScript("OnEnter", DotEnter)
      f:SetScript("OnLeave", DotLeave)
    end
    dots[i] = f
  end
  return dots[i]
end

local function MapID()
  return WorldMapFrame and WorldMapFrame.GetMapID and WorldMapFrame:GetMapID() or nil
end

-- Всё заново для открытой карты: значок (есть ли что показывать) и линии включённых маунтов.
local function Refresh()
  if not overlay then
    return
  end
  HideAll()
  local mapID = MapID()
  local entries = ns.RouteEntries(mapID)
  local state = mapID and State(mapID)
  local any = false
  if #entries > 0 then
    local canvas = overlay:GetParent()
    local width, height = canvas:GetWidth(), canvas:GetHeight()
    local scale = canvas:GetScale()
    if not scale or scale <= 0 then
      scale = 1
    end
    local nLine, nDot, color = 0, 0, 0
    for _, e in ipairs(entries) do
      if state and state[e.mount] == true then
        any = true
        local first = color
        for _, points in ipairs(e.lines or {}) do
          color = color + 1
          local c = COLORS[(color - 1) % #COLORS + 1]
          for i = 1, #points - 3, 2 do
            nLine = nLine + 1
            local line = Line(nLine)
            line:SetColorTexture(c[1], c[2], c[3], 0.9)
            line:SetThickness(LINE_THICKNESS / scale)
            line:SetStartPoint("TOPLEFT", overlay, points[i] / 100 * width, -points[i + 1] / 100 * height)
            line:SetEndPoint("TOPLEFT", overlay, points[i + 2] / 100 * width, -points[i + 3] / 100 * height)
            line:Show()
          end
        end
        -- места появления: у маршрута — цвета своей линии (они идут первыми и в том же порядке)
        local nLines = #(e.lines or {})
        local spawns = e.spawns or {}
        for i = 1, #spawns - 1, 2 do
          local index = (i + 1) / 2
          local c
          if nLines > 0 then
            c = COLORS[(first + (index - 1) % nLines) % #COLORS + 1]
          else
            c = COLORS[first % #COLORS + 1]
          end
          nDot = nDot + 1
          local dot = Dot(nDot)
          dot.tex:SetVertexColor(c[1], c[2], c[3], 1)
          dot.mount, dot.x, dot.y = e.mount, spawns[i], spawns[i + 1]
          dot:SetSize(DOT_SIZE / scale, DOT_SIZE / scale)
          dot:ClearAllPoints()
          dot:SetPoint("CENTER", overlay, "TOPLEFT", spawns[i] / 100 * width, -spawns[i + 1] / 100 * height)
          dot:Show()
        end
        if nLines == 0 then
          color = color + 1
        end
      end
    end
    shownMap = mapID
  end
  button:SetShown(#entries > 0)
  button.icon:SetDesaturated(not any)
  button.icon:SetAlpha(any and 1 or 0.75)
end

function ns.SetRouteShown(mapID, mount, on)
  local state = State(mapID, true)
  if state then
    state[mount] = on and true or nil
    if not next(state) then
      ns.GetDB().routes[mapID] = nil
    end
  end
  Refresh()
end

local function OnClick(self)
  local mapID = MapID()
  local entries = ns.RouteEntries(mapID)
  if #entries == 0 then
    return
  end
  if #entries == 1 then
    ns.SetRouteShown(mapID, entries[1].mount, not ns.RouteShown(mapID, entries[1].mount))
    return
  end
  MenuUtil.CreateContextMenu(self, function(_, root)
    root:CreateTitle(L.map_routes_pick)
    for _, e in ipairs(entries) do
      local mount = e.mount
      root:CreateCheckbox(MountName(mount), function()
        return ns.RouteShown(mapID, mount)
      end, function()
        ns.SetRouteShown(mapID, mount, not ns.RouteShown(mapID, mount))
      end)
    end
  end)
end

local function Create()
  local canvas = WorldMapFrame:GetCanvas()
  overlay = CreateFrame("Frame", nil, canvas)
  overlay:SetAllPoints(canvas)
  -- выше текстур изученной территории (они — значки карты самого нижнего уровня), ниже значков заданий и игрока
  local level
  local manager = WorldMapFrame.GetPinFrameLevelsManager and WorldMapFrame:GetPinFrameLevelsManager()
  if manager and manager.GetValidFrameLevel then
    -- уровень обычных значков карты; нет такого названия в клиенте — следующий, в крайнем случае чуть выше «изученного»
    for _, kind in ipairs({ "PIN_FRAME_LEVEL_AREA_POI", "PIN_FRAME_LEVEL_MAP_LINK", "PIN_FRAME_LEVEL_MAP_EXPLORATION" }) do
      local ok, value = pcall(manager.GetValidFrameLevel, manager, kind)
      if ok and type(value) == "number" then
        level = kind == "PIN_FRAME_LEVEL_MAP_EXPLORATION" and value + 50 or value
        break
      end
    end
  end
  overlay:SetFrameLevel(level or (canvas:GetFrameLevel() + 500))

  local holder = WorldMapFrame.ScrollContainer or WorldMapFrame
  button = CreateFrame("Button", "MountSheetSyncMapButton", holder)
  button:SetSize(26, 26)
  button:SetPoint("RIGHT", holder, "RIGHT", -8, 0)
  button:SetFrameLevel((holder:GetFrameLevel() or 1) + 3000)   -- поверх холста и значков карты
  button.icon = button:CreateTexture(nil, "ARTWORK")
  button.icon:SetAllPoints(button)
  button.icon:SetTexture(ICON)
  button:SetScript("OnClick", OnClick)
  button:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_LEFT")
    GameTooltip:SetText(L.map_routes_title, 1, 0.82, 0)
    GameTooltip:AddLine(L.map_routes_tip, 1, 1, 1, true)
    GameTooltip:Show()
  end)
  button:SetScript("OnLeave", function()
    GameTooltip:Hide()
  end)
  button:Hide()

  -- карта сама зовёт нас при открытии, смене карты и масштаба — так значок и линии не «залипают» с прошлой карты
  provider = CreateFromMixins(MapCanvasDataProviderMixin)
  function provider:RefreshAllData()
    Refresh()
  end
  function provider:RemoveAllData()
    HideAll()
  end
  function provider:OnMapChanged()
    Refresh()
  end
  function provider:OnCanvasScaleChanged()
    Refresh()
  end
  WorldMapFrame:AddDataProvider(provider)
  Refresh()
end

-- Создать слой, когда карта мира есть (в 12.x она загружена к входу; иначе — ждём её аддон).
function ns.InitMapRoutes()
  if overlay then
    return true
  end
  if not (WorldMapFrame and WorldMapFrame.GetCanvas and WorldMapFrame.AddDataProvider and MapCanvasDataProviderMixin
      and CreateFromMixins and ns.RARE_ROUTES) then
    return false
  end
  local ok = ns.SafeCall and ns.SafeCall(Create) or (not ns.SafeCall and pcall(Create))
  if not ok or not button then
    overlay = nil
    return false
  end
  return true
end

ns.RefreshMapRoutes = Refresh

-- Для проверок: что сейчас нарисовано.
function ns.MapRoutesDrawn()
  local nLines, nDots = 0, 0
  for _, line in ipairs(lines) do
    if line:IsShown() then
      nLines = nLines + 1
    end
  end
  for _, dot in ipairs(dots) do
    if dot:IsShown() then
      nDots = nDots + 1
    end
  end
  return nLines, nDots, shownMap, button and button:IsShown() or false
end

local watcher = CreateFrame("Frame")
watcher:RegisterEvent("PLAYER_LOGIN")
watcher:RegisterEvent("ADDON_LOADED")
watcher:RegisterEvent("NEW_MOUNT_ADDED")
watcher:SetScript("OnEvent", function(self, event)
  if event == "NEW_MOUNT_ADDED" then
    Refresh()   -- маунт собран: его маршрут и, возможно, значок больше не нужны
  elseif ns.InitMapRoutes() then
    self:UnregisterEvent("PLAYER_LOGIN")
    self:UnregisterEvent("ADDON_LOADED")
  end
end)
