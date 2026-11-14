--[[
Mount Sheet Sync 3.0.7 — окно: путь к боссу
Ctrl + щелчок по строке: вход в подземелье на карте, запасные точки, закрытые входы, Mapzeroth.
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local L, Safe, StepAside = P.L, P.Safe, P.StepAside

-- Вход в рейд или подземелье: игра отдаёт входы по картам. 946 — карта всего мира: обходим все карты в ней, не только
-- зоны — вход в «Душу Дракона» есть лишь на карте Пещер Времени. Ошибка на одной карте не обрывает поиск по остальным.
local WORLD_MAP = 946
local MAP_ZONE = 3   -- Enum.UIMapType.Zone
local entrances, entranceByName, entranceList = {}, {}, {}

-- Входы, которых игра может не отдать (значка на карте нет: персонаж там не бывал, или вообще), — запасная точка у
-- каждого рейда и подземелья каталога: ns.ENTRANCES из Entrances.lua (его пишет tools/catalog/entrances.py по таблицам
-- игры). Сначала всегда спрашиваем игру.
-- Мировой босс, у которого в журнале игры нет карты (UiMapID 0). Координаты — Warcraft Wiki.
local KNOWN_BOSS_SPOTS = {
  [2210] = { map = 864, x = 0.44, y = 0.558 },      -- Краулок, Вол'дун
}

-- Строчные буквы и для кириллицы: string.lower в игре меняет только латиницу.
local function Lower(s)
  s = s:lower():gsub("\208([\144-\175])", function(c)
    local b = c:byte()
    return b < 160 and ("\208" .. string.char(b + 32)) or ("\209" .. string.char(b - 32))
  end)
  return (s:gsub("\208\129", "\209\145"))   -- Ё
end

-- Знаки, которых нет среди ASCII: кавычки-ёлочки и лапки, тире, многоточие, апостроф, неразрывный пробел.
local WIDE_MARKS = { "\194\171", "\194\187", "\226\128\156", "\226\128\157", "\226\128\158", "\226\128\147", "\226\128\148",
  "\226\128\166", "\226\128\153", "\226\128\152", "\194\160" }

-- Имя без пробелов, знаков и регистра: «Операция: Мехагон» и «Операция "Мехагон"», «Стратхольм – черный ход» и
-- «Стратхольм - черный ход» — одно и то же.
local function NameKey(name)
  local s = Lower(tostring(name or ""))
  for _, mark in ipairs(WIDE_MARKS) do
    s = s:gsub(mark, "")
  end
  return (s:gsub("[%p%s]", ""))
end

local function ScanEntrances()
  entranceList = {}
  local ok, maps = pcall(C_Map.GetMapChildrenInfo, WORLD_MAP, nil, true)
  for _, map in ipairs(ok and type(maps) == "table" and maps or {}) do
    pcall(function()
      for _, e in ipairs(C_EncounterJournal.GetDungeonEntrancesForMap(map.mapID) or {}) do
        entranceList[#entranceList + 1] = { name = e.name, id = e.journalInstanceID, map = map.mapID, mapName = map.name }
        local spot = { map = map.mapID, zone = map.mapType == MAP_ZONE, poi = e.areaPoiID, x = e.position.x, y = e.position.y }
        local old = entrances[e.journalInstanceID]
        if not old or (spot.zone and not old.zone) then   -- метка на карте зоны удобнее
          entrances[e.journalInstanceID] = spot
        end
        if e.name and e.name ~= "" then
          entranceByName[NameKey(e.name)] = entrances[e.journalInstanceID]
        end
      end
    end)
  end
end

local function FindEntrance(instance, name)
  -- запасной путь — по названию: у части подземелий в журнале и на карте разные номера
  local function Known()
    return entrances[instance] or (name and entranceByName[NameKey(name)]) or nil
  end
  if not Known() then
    ScanEntrances()   -- нет в найденном — обходим карты заново: игра могла ещё не отдать их в прошлый раз
  end
  return Known() or (ns.ENTRANCES and ns.ENTRANCES[instance])
end

-- /mss route <часть названия>: что игра отдаёт о входах — для разбора «На карте не нашлось» (по-английски, как /mss tip).
function ns.RouteDebug(text)
  local key = NameKey(text)
  ScanEntrances()
  local maps = {}
  for _, e in ipairs(entranceList) do
    maps[e.map] = true
  end
  local nMaps = 0
  for _ in pairs(maps) do
    nMaps = nMaps + 1
  end
  ns.Print(string.format("route: %d entrances on %d maps; looking for \"%s\"", #entranceList, nMaps, Safe(text)))
  if key == "" then
    return
  end
  local shown = 0
  for _, e in ipairs(entranceList) do
    if shown < 8 and NameKey(e.name):find(key, 1, true) then
      shown = shown + 1
      ns.Print(string.format("  entrance \"%s\": journal %s, map %s (%s)", Safe(e.name), tostring(e.id), tostring(e.map), Safe(e.mapName)))
    end
  end
  if shown == 0 then
    ns.Print("  no entrance by that name on any map")
  end
  local data = MountSheetSyncSheet
  local seen, rows = {}, 0
  for _, ch in ipairs(type(data) == "table" and type(data.chars) == "table" and data.chars or {}) do
    for _, item in ipairs(type(ch) == "table" and type(ch.items) == "table" and ch.items or {}) do
      local place = type(item) == "table" and item.place
      if rows < 4 and type(place) == "string" and NameKey(place):find(key, 1, true) and not seen[place] then
        seen[place] = true
        rows = rows + 1
        local inst = ns.ItemInstance(item)
        ns.Print(string.format("  row \"%s\": journal %s (%s), map %s", Safe(place), tostring(inst), Safe(inst and ns.InstanceName(inst) or "?"), tostring(item.im)))
      end
    end
  end
end

-- Игрок на этой карте или на карте внутри неё? У Пещер Времени две карты с одним названием и одной родительской (74 и 75),
-- вход записан на одной: стоя на другой, игрок тоже внутри.
local function PlayerInside(map)
  local ok, inside = pcall(function()
    local here = C_Map.GetBestMapForUnit and C_Map.GetBestMapForUnit("player")
    local target = C_Map.GetMapInfo(map)
    local steps = 0
    while here and steps < 20 do
      if here == map then
        return true
      end
      local info = C_Map.GetMapInfo(here)
      if info and target and info.name == target.name and info.parentMapID == target.parentMapID then
        return true
      end
      here = info and info.parentMapID ~= here and info.parentMapID or nil
      steps = steps + 1
    end
    return false
  end)
  return ok and inside or false
end

-- Вход в пещеру на карте зоны: ссылки на вложенную карту у Пещер Времени в данных игры нет, а сам вход в рейд лежит глубоко
-- под землёй. Точка — из навигации игры (WaypointSafeLocs 7655 «Caverns of Time»).
local CAVE_MOUTHS = {
  [74] = { x = 0.6525, y = 0.4979 },
  [75] = { x = 0.6525, y = 0.4979 },
}

-- Вход лежит на карте внутри другой зоны (Пещеры Времени в Танарисе): куда идти снаружи — та же точка на карте зоны.
-- nil — карта входа сама зона или игрок уже внутри неё.
local function OuterStep(e)
  if PlayerInside(e.map) then
    return nil   -- уже внутри — сразу ко входу
  end
  local ok, step = pcall(function()
    local inner = C_Map.GetMapInfo(e.map)
    local outer = inner and inner.parentMapID and C_Map.GetMapInfo(inner.parentMapID)
    if not (outer and outer.mapType == MAP_ZONE) then
      return nil
    end
    local outerMap = outer.mapID or inner.parentMapID
    -- лучше всего — значок входа в пещеру на карте зоны (ссылка на вложенную карту): там, где в неё входят
    if C_Map.GetMapLinksForMap then
      for _, link in ipairs(C_Map.GetMapLinksForMap(outerMap) or {}) do
        local pos = link.linkedUiMapID == e.map and link.position
        local x, y
        if pos then
          x, y = pos.x, pos.y
          if not x and pos.GetXY then
            x, y = pos:GetXY()
          end
        end
        if x and y then
          return { map = outerMap, x = x, y = y, name = inner.name, zone = outer.name }
        end
      end
    end
    local mouth = CAVE_MOUTHS[e.map]
    if mouth then
      return { map = outerMap, x = mouth.x, y = mouth.y, name = inner.name, zone = outer.name }
    end
    -- иначе — где на карте зоны лежит сам вход (точка внутри пещеры, может быть в стороне от её входа)
    local minX, maxX, minY, maxY = C_Map.GetMapRectOnMap(e.map, outerMap)
    if not (minX and maxX and minY and maxY) then
      return nil
    end
    return { map = outerMap, x = minX + e.x * (maxX - minX), y = minY + e.y * (maxY - minY),
             name = inner.name, zone = outer.name }
  end)
  return ok and step or nil
end

-- Босс на карте зоны (мировые боссы: входа у них нет).
local bossSpots = {}

local function FindBossSpot(encounter)
  if bossSpots[encounter] then
    return bossSpots[encounter]
  end
  local ok = pcall(function()
    for _, map in ipairs(C_Map.GetMapChildrenInfo(WORLD_MAP, Enum.UIMapType.Zone, true) or {}) do
      for _, e in ipairs(C_EncounterJournal.GetEncountersOnMap(map.mapID) or {}) do
        if not bossSpots[e.encounterID] then
          bossSpots[e.encounterID] = { map = map.mapID, x = e.mapX, y = e.mapY }
        end
      end
    end
  end)
  return ok and bossSpots[encounter] or KNOWN_BOSS_SPOTS[encounter]
end

-- Своя метка на карте, выбранная целью.
local function PinAndTrack(map, x, y)
  return pcall(function()
    assert(C_Map.CanSetUserWaypointOnMap(map))
    C_Map.SetUserWaypoint(UiMapPoint.CreateFromCoordinates(map, x, y))
    C_SuperTrack.SetSuperTrackedUserWaypoint(true)
  end)
end

-- Путь через Mapzeroth (отдельный аддон, автор разрешил связь 12.11.2026): метка на карте и план Mapzeroth к ней —
-- он ведёт и через порталы, а свою метку на карте игра ведёт только вблизи. Mapzeroth не установлен, выключен
-- в настройках (db.mapzeroth = false) или не нашёл пути — false, и путь строится как раньше. Его внутренности
-- (MapzerothAddon: GetWaypoint, Journey:PlanFromHere, Navigation:Start, Navigator:Show) могут поменяться — всё под pcall.
local Mz = {}   -- всё про Mapzeroth в одной таблице: у главного блока Lua 5.1 предел в 200 локальных

function Mz.On()
  local db = ns.GetDB and ns.GetDB()
  return not (db and db.mapzeroth == false)
end

function Mz.Addon()
  local mz = rawget(_G, "MapzerothAddon")
  if type(mz) == "table" and type(mz.GetWaypoint) == "function" and type(mz.Journey) == "table"
      and type(mz.Journey.PlanFromHere) == "function" and type(mz.Navigation) == "table"
      and type(mz.Navigation.Start) == "function" then
    return mz
  end
  return nil
end

function Mz.Via(map, x, y, name)
  local mz = Mz.On() and Mz.Addon()
  if not (mz and map and x and y) or not PinAndTrack(map, x, y) then
    return false
  end
  local ok, started = pcall(function()
    local wp = mz:GetWaypoint()
    if type(wp) ~= "table" or not wp.id then
      return false
    end
    local entry = { name = name, nodeID = wp.id, nodeIDs = { wp.id }, group = "waypoint", relevant = true, pick = true,
                    dest = wp, key = "waypoint" }
    local plan = mz.Journey:PlanFromHere(entry)
    if type(plan) ~= "table" or type(plan.steps) ~= "table" or #plan.steps == 0 then
      return false
    end
    mz.Navigation:Start(entry, plan)
    if type(mz.Navigator) == "table" and type(mz.Navigator.Show) == "function" then
      mz.Navigator:Show()
    end
    return true
  end)
  if ok and started == true then
    ns.Print(string.format(L.route_mapzeroth, name))
    return true
  end
  return false
end

-- Континент карты (тип Continent у одного из её родителей), nil — не узнать.
function Mz.Continent(map)
  for _ = 1, 10 do
    local ok, info = pcall(C_Map.GetMapInfo, map)
    info = ok and map and info or nil
    if not info then
      return nil
    end
    if info.mapType == Enum.UIMapType.Continent then
      return info.mapID
    end
    map = info.parentMapID
  end
  return nil
end

-- Цель на другом континенте, а Mapzeroth нет: один раз за сеанс подсказать, что через порталы ведёт он.
function Mz.Hint(map)
  if Mz.hinted or not Mz.On() or Mz.Addon() then
    return
  end
  local here = C_Map.GetBestMapForUnit and C_Map.GetBestMapForUnit("player")
  local a, b = Mz.Continent(here), Mz.Continent(map)
  if a and b and a ~= b then
    Mz.hinted = true
    ns.Print(L.route_get_mapzeroth)
  end
end

-- Показать путь: игра ведёт к отметке входа, а если не вышло (не изучена территория и т. п.) — своя метка на карте у входа.
-- У мировых боссов входа нет: метка там, где их показывает игра.
-- Путь, ждущий второго шага: { entrance, name, inner }. routeOwn — цель меняет сам аддон, это не выбор игрока.
local pendingRoute, routeOwn, routeWatcher = nil, false, nil

-- Путь прямо ко входу: метка входа, а если игра её не ведёт — своя метка у входа.
local function TrackEntrance(e, name)
  local tracked = pcall(function()
    assert(e.poi)   -- запасная точка (ns.ENTRANCES): значка игры нет, вести не к чему
    C_SuperTrack.SetSuperTrackedMapPin(Enum.SuperTrackingMapPinType.AreaPOI, e.poi)
    local _, id = C_SuperTrack.GetSuperTrackedMapPin()
    assert(id == e.poi)
  end)
  if tracked then
    ns.Print(string.format(L.route_set, name))
    return
  end
  ns.Print(string.format(PinAndTrack(e.map, e.x, e.y) and L.route_pin or L.route_none, name))
end

-- Второй шаг: игрок внутри зоны со входом — путь ко входу.
local function StepInside()
  local route = pendingRoute
  if route and not routeOwn and PlayerInside(route.entrance.map) then
    pendingRoute = nil
    routeOwn = true
    ns.Print(string.format(L.route_inside, route.inner, route.name))
    TrackEntrance(route.entrance, route.name)
    routeOwn = false
  end
end

-- Событие смены зоны приходит не всегда (попал внутрь порталом): пока путь не завершён, раз в 2 секунды проверяем сами.
local routeTicking = false
local function TickRoute()
  StepInside()
  if pendingRoute then
    C_Timer.After(2, TickRoute)
  else
    routeTicking = false
  end
end

-- Игрок вошёл в зону со входом — второй шаг. Игрок сам выбрал другую цель — ждать больше нечего.
local function WatchRoute()
  if not routeTicking then
    routeTicking = true
    C_Timer.After(2, TickRoute)
  end
  if routeWatcher then
    return
  end
  routeWatcher = CreateFrame("Frame")
  for _, event in ipairs({ "ZONE_CHANGED", "ZONE_CHANGED_INDOORS", "ZONE_CHANGED_NEW_AREA", "SUPER_TRACKING_CHANGED" }) do
    pcall(routeWatcher.RegisterEvent, routeWatcher, event)
  end
  routeWatcher:SetScript("OnEvent", function(_, event)
    local route = pendingRoute
    if not route or routeOwn then
      return
    end
    if event == "SUPER_TRACKING_CHANGED" then
      -- метку у входа игра снимает сама, когда до неё дошли: это не повод забыть путь; другая цель — повод
      local ok, other = pcall(function()
        return C_SuperTrack.IsSuperTrackingAnything() and not C_SuperTrack.IsSuperTrackingUserWaypoint()
      end)
      if ok and other then
        pendingRoute = nil
      end
      return
    end
    StepInside()
  end)
end

local accessAsked, accessAskedAt = nil, 0   -- вход, про который только что сказали «не пройдено»

-- Следующий шаг цепочки доступа (Access.lua): задание уже взято — цель на него (путь покажет игра); иначе метка там,
-- где его выдают; места нет — слова, где искать.
local function ShowAccessStep(step, inLog)
  if not step then
    return
  end
  local title = ns.QuestTitle(step)
  -- слова о месте (step.where): у шага без точки — вместо метки, у шага с точкой — вместе с ней (метка у входа туда,
  -- где задание выдают: Зал Сердца)
  local where = step.where and L["access_where_" .. tostring(step.where)]
  if inLog and pcall(C_SuperTrack.SetSuperTrackedQuestID, step.quest) then
    ns.Print(string.format(L.access_step_track, title))
  elseif step.map and Mz.Via(step.map, step.x, step.y, title) then
    if where then
      ns.Print(string.format(L.access_step_text, title, where))
    end
  elseif step.map and PinAndTrack(step.map, step.x, step.y) then
    if where then
      ns.Print(string.format(L.route_boss, title))
      ns.Print(string.format(L.access_step_text, title, where))
    else
      ns.Print(string.format(L.access_step_pin, title))
    end
    Mz.Hint(step.map)
  else
    ns.Print(string.format(L.access_step_text, title, where or "?"))
  end
end

local function ShowRoute(elem)
  pendingRoute = nil   -- новый путь заменяет недоделанный
  ns.Count("route")
  if elem.spot then
    -- рар: точка из каталога (ns.RARE_SPOTS); точек появления несколько — карта его зоны
    local name, s = Safe(elem.bossName), elem.spot
    if s.x and Mz.Via(s.map, s.x, s.y, name) then
      return
    elseif s.x and PinAndTrack(s.map, s.x, s.y) then
      ns.Print(string.format(L.route_boss, name))
      Mz.Hint(s.map)
    elseif not s.x and pcall(OpenWorldMap, s.map) then
      ns.Print(string.format(L.route_zone, name))
      StepAside(WorldMapFrame)
    else
      ns.Print(string.format(L.route_none, name))
    end
    return
  end
  if elem.world then
    local name = Safe(elem.bossName)
    local spot = elem.encounter and FindBossSpot(elem.encounter)
    if spot and Mz.Via(spot.map, spot.x, spot.y, name) then
      return
    elseif spot and PinAndTrack(spot.map, spot.x, spot.y) then
      ns.Print(string.format(L.route_boss, name))
      Mz.Hint(spot.map)
    else
      ns.Print(string.format(L.route_none, name))
    end
    return
  end
  local instance = elem.instance
  local name = Safe(elem.placeName) ~= "" and Safe(elem.placeName) or ns.InstanceName(instance) or "?"
  -- вход, закрытый сюжетом или фазой (Access.lua): сначала — что сделать; «не пройдено» — без метки, но повторный
  -- Ctrl + щелчок в течение минуты ставит её всё равно (номер задания мог устареть)
  local access = ns.AccessCheck and ns.AccessCheck(instance)
  if access and access.hint ~= "" then
    local now = GetTime and GetTime() or 0
    if access.open == false and not (accessAsked == instance and now - accessAskedAt < 60) then
      accessAsked, accessAskedAt = instance, now
      ns.Print(string.format(L.access_need, name, access.hint))
      ShowAccessStep(access.step, access.stepInLog)
      return
    end
    if access.open == nil then
      ns.Print(string.format(L.access_note, name, access.hint))
    elseif access.after then
      ns.Print(string.format(L.access_note, name, access.after))
    end
  end
  local e = FindEntrance(instance, ns.InstanceName(instance) or elem.placeName)
  if not e then
    ns.Print(string.format(L.route_none, name))
    return
  end
  routeOwn = true   -- своя метка для Mapzeroth — не выбор игрока
  local viaMz = Mz.Via(e.map, e.x, e.y, name)
  routeOwn = false
  if viaMz then
    return
  end
  Mz.Hint(e.map)
  local outer = OuterStep(e)
  if not outer then
    TrackEntrance(e, name)
    return
  end
  -- в два шага, но одним щелчком: сначала метка у зоны со входом (Пещеры Времени), а когда игрок в неё войдёт,
  -- аддон сам переключит путь на вход (RouteWatch)
  routeOwn = true
  local pinned = PinAndTrack(outer.map, outer.x, outer.y)
  routeOwn = false
  if not pinned then
    ns.Print(string.format(L.route_none, Safe(outer.name)))
    return
  end
  pendingRoute = { entrance = e, name = name, inner = Safe(outer.name) }
  WatchRoute()
  ns.Print(string.format(L.route_first, Safe(outer.name), Safe(outer.zone), name))
end

-- нужны следующим файлам окна
P.Mz, P.ShowRoute = Mz, ShowRoute
