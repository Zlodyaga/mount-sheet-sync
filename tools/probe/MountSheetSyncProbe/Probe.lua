-- Mount Sheet Sync Probe: a temporary addon that collects game data for checking Mount Sheet Sync.
-- Read-only: it only asks the game and writes MountSheetSyncProbeDB (SavedVariables); nothing leaves the computer.
-- On login (after 15 s) and on /mssprobe: this character's quests and achievements of the gated entrances
-- (Access.lua), where the character stands, and the dungeon entrances the game gives on every map.
-- All the time: rares under the mouse, targeted or on a nameplate — creature number, name and where the player was.

local PREFIX = "|cff33ff99MSS Probe|r: "

-- quests and achievements of Access.lua (addon 3.0.5): quests that open an entrance and the steps of their chains
local QUESTS = { 47221, 47835, 48506, 48507, 57010, 58582, 58496, 56376, 63985, 64844, 83137, 84910, 75918, 76317, 76318 }
local ACHIEVEMENTS = { 13553, 13700, 15514 }

local function Secret(v)
  return issecretvalue ~= nil and issecretvalue(v)
end

local function Call(f, ...)
  if type(f) ~= "function" then
    return nil
  end
  local ok, a, b, c = pcall(f, ...)
  if ok then
    return a, b, c
  end
  return nil
end

local function Print(text)
  if DEFAULT_CHAT_FRAME then
    DEFAULT_CHAT_FRAME:AddMessage(PREFIX .. text)
  end
end

local function DB()
  MountSheetSyncProbeDB = type(MountSheetSyncProbeDB) == "table" and MountSheetSyncProbeDB or {}
  local db = MountSheetSyncProbeDB
  db.version = 1
  db.chars = db.chars or {}
  db.rares = db.rares or {}
  return db
end

local function CharKey()
  local name, realm = Call(UnitName, "player"), Call(GetRealmName)
  if type(name) ~= "string" or Secret(name) then
    return nil
  end
  return name .. "-" .. tostring(realm or "?")
end

local function Round(v)
  return v and math.floor(v * 10000 + 0.5) / 10000 or nil
end

local function PlayerSpot()
  local map = C_Map and Call(C_Map.GetBestMapForUnit, "player")
  if not map then
    return nil
  end
  local pos = Call(C_Map.GetPlayerMapPosition, map, "player")
  local x, y
  if pos then
    local ok, px, py = pcall(function() return pos:GetXY() end)
    if ok then
      x, y = px, py
    end
  end
  if Secret(x) or Secret(y) then
    x, y = nil, nil
  end
  return map, Round(x), Round(y)
end

---------------------------------------------------------------------------
-- this character: quests, achievements, where it stands
---------------------------------------------------------------------------
local function Bool(v)
  if v == nil or Secret(v) then
    return nil
  end
  return v and true or false
end

local function CollectQuests(c)
  c.quests = {}
  local missing = 0
  for _, id in ipairs(QUESTS) do
    local q = {
      done = Bool(C_QuestLog and Call(C_QuestLog.IsQuestFlaggedCompleted, id)),
      account = Bool(C_QuestLog and Call(C_QuestLog.IsQuestFlaggedCompletedOnAccount, id)),
      inLog = Bool(C_QuestLog and Call(C_QuestLog.IsOnQuest, id)),
    }
    local title = C_QuestLog and Call(C_QuestLog.GetTitleForQuestID, id)
    if type(title) == "string" and title ~= "" and not Secret(title) then
      q.title = title
    else
      missing = missing + 1
      if C_QuestLog then
        Call(C_QuestLog.RequestLoadQuestByID, id)   -- the name comes from the server a moment later
      end
    end
    c.quests[id] = q
  end
  return missing
end

local function CollectAchievements(c)
  c.achievements = {}
  for _, id in ipairs(ACHIEVEMENTS) do
    local ok, _, name, _, completed, _, _, _, _, _, _, _, _, earnedByMe, earnedBy = pcall(GetAchievementInfo or error, id)
    if ok then
      c.achievements[id] = {
        name = (type(name) == "string" and not Secret(name)) and name or nil,
        completed = Bool(completed),
        earnedByMe = Bool(earnedByMe),
        earnedBy = (type(earnedBy) == "string" and not Secret(earnedBy)) and earnedBy or nil,
      }
    end
  end
end

local function CollectChar()
  local key = CharKey()
  if not key then
    return nil
  end
  local db = DB()
  local c = db.chars[key] or {}
  db.chars[key] = c
  local _, classFile = Call(UnitClass, "player")
  c.class = classFile
  c.faction = Call(UnitFactionGroup, "player")
  c.level = Call(UnitLevel, "player")
  c.locale = Call(GetLocale)
  local version, build = Call(GetBuildInfo)
  c.build = tostring(version or "?") .. " " .. tostring(build or "?")
  c.at = time()
  c.map, c.x, c.y = PlayerSpot()
  local missing = CollectQuests(c)
  CollectAchievements(c)
  return c, missing
end

---------------------------------------------------------------------------
-- dungeon entrances on every map (a few maps per frame, so the game does not stutter)
---------------------------------------------------------------------------
local scanning = false

local function ScanEntrances(c, done)
  if scanning or not (C_Map and C_Map.GetMapChildrenInfo and C_EncounterJournal and C_EncounterJournal.GetDungeonEntrancesForMap) then
    return
  end
  scanning = true
  local maps, infos = {}, {}
  for _, root in ipairs({ 946, 947 }) do   -- the cosmic map and Azeroth: all their maps below
    for _, m in ipairs(Call(C_Map.GetMapChildrenInfo, root, nil, true) or {}) do
      if m.mapID and not infos[m.mapID] then
        infos[m.mapID] = m
        maps[#maps + 1] = m.mapID
      end
    end
  end
  local out, i, found = {}, 1, 0
  local function Step()
    for _ = 1, 40 do
      local map = maps[i]
      if not map then
        c.entrances = out
        c.entrancesAt = time()
        scanning = false
        if done then
          done(#maps, found)
        end
        return
      end
      i = i + 1
      local list = Call(C_EncounterJournal.GetDungeonEntrancesForMap, map)
      if type(list) == "table" and #list > 0 then
        local rows = {}
        for _, e in ipairs(list) do
          local x, y
          if e.position then
            local ok, px, py = pcall(function() return e.position:GetXY() end)
            if ok then
              x, y = Round(px), Round(py)
            end
          end
          rows[#rows + 1] = { inst = e.journalInstanceID, poi = e.areaPoiID, x = x, y = y,
                              name = (type(e.name) == "string" and not Secret(e.name)) and e.name or nil }
          found = found + 1
        end
        local info = infos[map]
        out[map] = { mapName = info and info.name, mapType = info and info.mapType, parent = info and info.parentMapID, list = rows }
      end
    end
    C_Timer.After(0, Step)
  end
  Step()
end

local function Collect(verbose)
  local c, missing = CollectChar()
  if not c then
    return
  end
  if missing and missing > 0 then
    -- quest names the client did not know yet: once more in a few seconds
    C_Timer.After(5, function()
      local again = CollectChar()
      if again and verbose then
        Print("quest names read again")
      end
    end)
  end
  ScanEntrances(c, function(maps, found)
    if verbose then
      Print(string.format("done: %d maps, %d entrances, %d quests, %d achievements. Log out or /reload to save it.",
        maps, found, #QUESTS, #ACHIEVEMENTS))
    end
  end)
end

---------------------------------------------------------------------------
-- rares: under the mouse, targeted, on a nameplate
---------------------------------------------------------------------------
local RARE = { rare = true, rareelite = true }

local function SeeUnit(unit)
  if not unit or not Call(UnitExists, unit) then
    return
  end
  local class = Call(UnitClassification, unit)
  if Secret(class) or not RARE[class] then
    return
  end
  local guid = Call(UnitGUID, unit)
  if type(guid) ~= "string" or Secret(guid) then
    return
  end
  local npc = guid:match("^Creature%-%d+%-%d+%-%d+%-%d+%-(%d+)%-") or guid:match("^Vehicle%-%d+%-%d+%-%d+%-%d+%-(%d+)%-")
  if not npc then
    return
  end
  local db = DB()
  local r = db.rares[npc]
  if not r then
    local n = 0
    for _ in pairs(db.rares) do
      n = n + 1
    end
    if n >= 500 then
      return
    end
    r = { spots = {} }
    db.rares[npc] = r
  end
  local name = Call(UnitName, unit)
  if type(name) == "string" and not Secret(name) then
    r.name = name
  end
  r.class = class
  r.seen = time()
  local map, x, y = PlayerSpot()
  if not (map and x and y) then
    return
  end
  for _, s in ipairs(r.spots) do
    if s.map == map and math.abs(s.x - x) < 0.02 and math.abs(s.y - y) < 0.02 then
      return   -- this spot is known already
    end
  end
  if #r.spots < 8 then
    r.spots[#r.spots + 1] = { map = map, x = x, y = y }
  end
end

---------------------------------------------------------------------------
local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_TARGET_CHANGED")
frame:RegisterEvent("UPDATE_MOUSEOVER_UNIT")
frame:RegisterEvent("NAME_PLATE_UNIT_ADDED")
frame:SetScript("OnEvent", function(_, event, unit)
  if event == "PLAYER_LOGIN" then
    C_Timer.After(15, function() Collect(false) end)
  elseif event == "PLAYER_TARGET_CHANGED" then
    pcall(SeeUnit, "target")
  elseif event == "UPDATE_MOUSEOVER_UNIT" then
    pcall(SeeUnit, "mouseover")
  elseif event == "NAME_PLATE_UNIT_ADDED" then
    pcall(SeeUnit, unit)
  end
end)

SLASH_MOUNTSHEETSYNCPROBE1 = "/mssprobe"
SlashCmdList["MOUNTSHEETSYNCPROBE"] = function()
  Print("collecting…")
  Collect(true)
end
