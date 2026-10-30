-- Strict mocks of the WoW widget API used by Mount Sheet Sync.
-- Calling a method that is not listed here raises an error, so the tests
-- catch any API the addon uses that we have not checked against 12.1.5.

CHAT, TIMERS, FRAMES, ALL_REGIONS = {}, {}, {}, {}
REQ, RELOADED = 0, false
POPUPS = {}
MENUS = {}

-- Blizzard_Menu (11.0+): MenuUtil.CreateContextMenu(owner, generator(owner, rootDescription))
MenuUtil = {}
function MenuUtil.CreateContextMenu(owner, generator)
  assert(owner and type(generator) == "function", "mock: CreateContextMenu(owner, generator)")
  local root = { items = {} }
  function root:CreateTitle(t) self.items[#self.items + 1] = { kind = "title", text = t } end
  function root:CreateButton(t, cb)
    assert(type(t) == "string" and type(cb) == "function", "mock: CreateButton(text, callback)")
    local b = { kind = "button", text = t, cb = cb }; self.items[#self.items + 1] = b; return b
  end
  function root:CreateDivider() self.items[#self.items + 1] = { kind = "divider" } end
  generator(owner, root)
  MENUS[#MENUS + 1] = root
  return root
end

local Region = {}
Region.__index = function(self, key)
  local m = rawget(Region, key)
  if m ~= nil then return m end
  if type(key) == "string" and key:sub(1, 1) == "_" then return nil end   -- mock bookkeeping
  local kids = rawget(self, "_keys")
  if kids and kids[key] ~= nil then return kids[key] end
  error("mock: unknown widget method or key '" .. tostring(key) .. "' on " .. tostring(rawget(self, "_type")), 2)
end

local function NewRegion(kind, name, parent, template)
  local r = setmetatable({ _type = kind, _name = name, _parent = parent, _template = template,
                           _points = {}, _scripts = {}, _shown = true, _keys = {}, _children = {} }, Region)
  ALL_REGIONS[#ALL_REGIONS + 1] = r
  r._uid = #ALL_REGIONS
  if parent then parent._children[#parent._children + 1] = r end
  if name then _G[name] = r end
  return r
end
MockNewRegion = NewRegion

function Region:SetSize(w, h) self._w, self._h = w, h end
function Region:SetWidth(w) self._w = w end
function Region:SetHeight(h) self._h = h end
function Region:GetWidth() return self._w or 0 end
function Region:GetHeight() return self._h or 0 end
function Region:SetPoint(point, a, b, c, d)
  local rel, relPoint, x, y = self._parent, point, 0, 0
  if type(a) == "number" then x, y = a, b or 0
  elseif a ~= nil then
    rel = type(a) == "string" and _G[a] or a
    if type(b) == "string" then relPoint = b; x, y = c or 0, d or 0
    else x, y = b or 0, c or 0 end
  end
  self._points[#self._points + 1] = { point = point, rel = rel, relPoint = relPoint, x = x, y = y }
end
function Region:ClearAllPoints() self._points = {} end
function Region:GetPoint(i)
  local p = self._points[i or 1]
  if not p then return nil end
  return p.point, p.rel, p.relPoint, p.x, p.y
end
function Region:Show() self._shown = true; if self._scripts.OnShow then self._scripts.OnShow(self) end end
function Region:Hide() self._shown = false; if self._scripts.OnHide then self._scripts.OnHide(self) end end
function Region:IsShown() return self._shown end
function Region:SetShown(v) if v then self:Show() else self:Hide() end end
function Region:SetText(t) self._text = t; if self._fs then self._fs._text = t end end
-- Button:GetFontString() / FontString:GetStringWidth() (6 px per character in the mock)
function Region:GetFontString()
  assert(self._type == "Button" or self._type == "CheckButton", "mock: GetFontString on " .. tostring(self._type))
  if not self._fs then self._fs = NewRegion("FontString", nil, self, nil); self._fs._text = self._text end
  return self._fs
end
function Region:GetStringWidth()
  assert(self._type == "FontString", "mock: GetStringWidth on " .. tostring(self._type))
  return select(2, (self._text or ""):gsub("[^\128-\191]", "")) * 6
end
function Region:GetText() return self._text end
function Region:SetJustifyH(v) self._justify = v end
function Region:SetWordWrap(v) self._wrap = v end
-- frames
function Region:SetScript(k, fn) self._scripts[k] = fn end
function Region:GetScript(k) return self._scripts[k] end
function Region:HookScript(k, fn)
  local old = self._scripts[k]
  self._scripts[k] = function(...) if old then old(...) end fn(...) end
end
function Region:RegisterEvent(e) self._events = self._events or {}; self._events[e] = true end
function Region:UnregisterEvent(e) if self._events then self._events[e] = nil end end
function Region:CreateFontString(name, layer, font)
  local fs = NewRegion("FontString", name, self, nil); fs._font = font; return fs
end
function Region:SetFrameStrata(v) self._strata = v end
function Region:SetToplevel(v) end
function Region:SetClampedToScreen(v) end
function Region:SetMovable(v) self._movable = v end
function Region:EnableMouse(v) end
function Region:RegisterForDrag(b) self._drag = b end
function Region:StartMoving() self._moving = true end
function Region:StopMovingOrSizing() self._moving = false end
function Region:SetScrollChild(c) self._scrollChild = c end
function Region:SetVerticalScroll(v) self._vscroll = v end
function Region:SetID(i) self._id = i end
function Region:GetID() return self._id or 0 end
function Region:Enable() self._enabled = true end
function Region:Disable() self._enabled = false end
function Region:IsEnabled() return self._enabled ~= false end
function Region:SetChecked(v) self._checked = not not v end
function Region:GetChecked() return self._checked or false end
function Region:SetAutoFocus(v) end
function Region:SetMaxLetters(n) self._maxLetters = n end
function Region:ClearFocus() self._focus = false end
function Region:HighlightText(a, b) self._highlighted = true end
function Region:SetCursorPosition(n) self._cursor = n end
function Region:SetMultiLine(v) self._multiline = v end
function Region:SetFontObject(f) self._fontObject = f end
function Region:SetFocus() self._focus = true end
function Region:SetFrameLevel(v) self._level = v end
function Region:RegisterForClicks(...) self._clicks = { ... } end
function Region:SetHighlightTexture(t) self._highlight = t end
function Region:CreateTexture(name, layer)
  local t = NewRegion("Texture", name, self, nil); t._layer = layer; return t
end
function Region:SetTexture(t) self._texture = t end
function Region:SetTexCoord(...) self._texcoord = { ... } end
function Region:SetAllPoints(rel) self._points = { { point = "ALL", rel = rel or self._parent, relPoint = "ALL", x = 0, y = 0 } } end
function Region:SetColorTexture(r, g, b, a)
  assert(self._type == "Texture", "mock: SetColorTexture on " .. tostring(self._type))
  self._color = { r, g, b, a }
end
function Region:GetCenter() if self._center then return self._center[1], self._center[2] end return nil end
function Region:GetEffectiveScale() return self._scale or 1 end
-- test helpers (not WoW API)
function Region:MockClick(button)
  if self._type == "CheckButton" then self._checked = not self._checked end
  local fn = self._scripts.OnClick; assert(fn, "no OnClick on " .. tostring(self._text)); fn(self, button or "LeftButton", true)
end

local TEMPLATE_SETUP = {
  DefaultPanelTemplate = function(f)
    f._keys.TitleContainer = { TitleText = NewRegion("FontString", nil, f, nil) }
    f._keys.SetTitle = function(self, t) self._title = t end
  end,
  UIPanelCloseButtonDefaultAnchors = function(b) b:SetSize(24, 24); b:SetPoint("TOPRIGHT", 1, 0) end,
  UICheckButtonTemplate = function(c)
    c:SetSize(32, 32)
    local t = NewRegion("FontString", nil, c, nil); t._font = "GameFontNormalSmall"
    t:SetPoint("LEFT", c, "RIGHT", -2, 1)
    c._keys.Text = t
  end,
  -- PanelTabButtonTemplate: parentArray="Tabs", ButtonText parentKey="Text", textures Left/Right/…
  PanelTopTabButtonTemplate = function(b)
    b:SetSize(10, 32)
    local t = NewRegion("FontString", nil, b, nil); t._font = "GameFontNormalSmall"; t:SetPoint("CENTER", b, "CENTER", 0, 2)
    b._keys.Text = t
    for _, key in ipairs({ "Left", "Right", "Middle", "LeftActive", "RightActive", "MiddleActive" }) do
      local tex = NewRegion("Texture", nil, b, nil); tex:SetSize(key:find("Middle") and 1 or 20, 24)
      b._keys[key] = tex
    end
    local parent = b._parent
    if parent then
      local tabsArray = parent._keys.Tabs
      if not tabsArray then tabsArray = {}; parent._keys.Tabs = tabsArray end
      tabsArray[#tabsArray + 1] = b
    end
  end,
  UIPanelScrollFrameTemplate = function(s)
    local bar = NewRegion("Slider", nil, s, nil)
    bar:SetWidth(16); bar:SetPoint("TOPLEFT", s, "TOPRIGHT", 6, -16); bar:SetPoint("BOTTOMLEFT", s, "BOTTOMRIGHT", 6, 16)
    s._keys.ScrollBar = bar
  end,
  -- Blizzard_SharedXML ScrollBox (12.1.5), simplified: SetDataProvider hides the frames and calls the view's
  -- initializer for every element on a pooled frame (the game does it only for the visible ones). _pool holds the
  -- frames in element order, _inits counts initializer calls (tests check that nothing is rebuilt needlessly).
  WowScrollBoxList = function(box)
    box._pool, box._inits, box._vscroll = {}, 0, 0
    box._keys.SetDataProvider = function(self, dp, retain)
      local view = assert(rawget(self, "_view"), "mock: ScrollBox without a view (ScrollUtil.InitScrollBoxListWithScrollBar)")
      assert(type(dp) == "table" and dp._items, "mock: SetDataProvider(CreateDataProvider(list))")
      if not retain then self._vscroll = 0 end
      for _, f in ipairs(self._pool) do f:Hide() end
      for i, elem in ipairs(dp._items) do
        local f = self._pool[i]
        if not f then
          f = CreateFrame(view._frameType, nil, self)
          self._pool[i] = f
        end
        f:ClearAllPoints()
        f:SetPoint("TOPLEFT", self, "TOPLEFT", 0, -(i - 1) * view._extent)
        f:SetPoint("RIGHT", self, "RIGHT", 0, 0)
        f:Show()
        view._init(f, elem)
        self._inits = self._inits + 1
      end
      self._data = dp._items
    end
    box._keys.ScrollToBegin = function(self) self._vscroll = 0 end
  end,
  MinimalScrollBar = function(bar) bar:SetWidth(8) end,
}
local KNOWN_TEMPLATES = { BackdropTemplate = true, DefaultPanelTemplate = true, UIPanelButtonTemplate = true,
  UIPanelCloseButtonDefaultAnchors = true, UICheckButtonTemplate = true, InputBoxTemplate = true,
  UIPanelScrollFrameTemplate = true, InsetFrameTemplate = true, PanelTopTabButtonTemplate = true,
  WowScrollBoxList = true, MinimalScrollBar = true }

function CreateScrollBoxListLinearView()
  local view = {}
  function view:SetElementExtent(e) assert(type(e) == "number", "mock: SetElementExtent(height)"); self._extent = e end
  function view:SetElementInitializer(frameType, init)
    assert(type(frameType) == "string" and type(init) == "function", "mock: SetElementInitializer(frameTypeOrTemplate, initializer)")
    self._frameType, self._init = frameType, init
  end
  return view
end
function CreateDataProvider(list) return { _items = list or {} } end
ScrollBoxConstants = { RetainScrollPosition = true, DiscardScrollPosition = false }
ScrollUtil = {}
function ScrollUtil.InitScrollBoxListWithScrollBar(box, bar, view)
  assert(box._template == "WowScrollBoxList" and bar._template == "MinimalScrollBar", "mock: InitScrollBoxListWithScrollBar(box, bar, view)")
  assert(view._extent and view._init, "mock: the view needs an extent and an initializer first")
  box._view, box._bar = view, bar
end
function ScrollUtil.AddManagedScrollBarVisibilityBehavior(box, bar)
  assert(box._template == "WowScrollBoxList" and bar._template == "MinimalScrollBar")
  box._managedBar = true
end

-- Blizzard_SharedXML/Mainline/SharedUIPanelTemplates.lua (12.1.5), simplified
function PanelTemplates_SetNumTabs(frame, n)
  frame._keys.numTabs = n
  local tabsArray = frame._keys.Tabs
  assert(tabsArray and #tabsArray == n, "mock: frame.Tabs must hold the tab buttons")
  for i = 2, n do tabsArray[i]:SetPoint("TOPLEFT", tabsArray[i - 1], "TOPRIGHT", 3, 0) end
end
function PanelTemplates_SetTab(frame, id)
  frame._keys.selectedTab = id
  local tabsArray = frame._keys.Tabs
  for i = 1, frame._keys.numTabs or 0 do
    if i == id then tabsArray[i]:Disable() else tabsArray[i]:Enable() end
  end
end
function PanelTemplates_TabResize(tab, padding, absoluteSize, minWidth, maxWidth)
  local text = tab._text or ""
  local width = select(2, text:gsub("[^\128-\191]", "")) * 6 + 20 + (padding or 0)
  if minWidth and width < minWidth then width = minWidth end
  tab:SetWidth(width)
end

function CreateFrame(kind, name, parent, template)
  if template and not KNOWN_TEMPLATES[template] then error("mock: template not verified in 12.1.5: " .. template, 2) end
  local f = NewRegion(kind, name, parent, template)
  if template and TEMPLATE_SETUP[template] then TEMPLATE_SETUP[template](f) end
  FRAMES[#FRAMES + 1] = f
  return f
end

UIParent = NewRegion("Frame", "UIParent", nil, nil)
UIParent:SetSize(1920, 1080)
Minimap = NewRegion("Frame", "Minimap", UIParent, nil)
Minimap:SetSize(140, 140)
Minimap._center = { 1800, 950 }
CURSOR = { 0, 0 }
function GetCursorPosition() return CURSOR[1], CURSOR[2] end
UISpecialFrames = {}
ChatFontNormal = {}
function GetBuildInfo() return "12.1.5", "65000", "Oct 1 2026", 120105 end
-- hooksecurefunc: the original runs first, then the hook (like the game)
function hooksecurefunc(a, b, c)
  local t, name, hook = _G, a, b
  if type(a) == "table" then t, name, hook = a, b, c end
  local orig = t[name]
  assert(type(orig) == "function", "mock hooksecurefunc: no function " .. tostring(name))
  t[name] = function(...) local r = { orig(...) }; hook(...); return unpack(r) end
end
tinsert = table.insert
CANCEL = "Отмена"
MOCK_LOCALE = "ruRU"   -- tests of the Russian texts; test_locale.py switches it
function GetLocale() return MOCK_LOCALE end

GameTooltip = { lines = {}, shown = false }
function GameTooltip:SetOwner(owner, anchor) self.owner = owner; self.lines = {} end
function GameTooltip:SetText(t) self.lines = { t } end
function GameTooltip:AddLine(t) self.lines[#self.lines + 1] = t end
function GameTooltip:Show() self.shown = true end
function GameTooltip:Hide() self.shown = false end

function StaticPopup_ShowCustomGenericConfirmation(data)
  assert(type(data.text) == "string" and not data.text:find("%%"), "popup text must not contain % (it is a format string)")
  assert(type(data.callback) == "function")
  POPUPS[#POPUPS + 1] = data
end
StaticPopupDialogs = {}
function StaticPopup_Show() error("addon should use StaticPopup_ShowCustomGenericConfirmation in 12.1.5") end

function securecallfunction(f, ...) return f(...) end   -- used by CallbackHandler-1.0

function Fire(event, ...)
  for _, f in ipairs(FRAMES) do
    if f._events and f._events[event] and f._scripts.OnEvent then f._scripts.OnEvent(f, event, ...) end
  end
end
function RunTimers()
  local list = TIMERS; TIMERS = {}
  for _, fn in ipairs(list) do fn() end
end
DEFAULT_CHAT_FRAME = { AddMessage = function(_, m) CHAT[#CHAT + 1] = m end }
-- Как в игре 12.x: колбэк должен быть Lua-функцией аддона, функции игры (RequestRaidInfo и т. п.) не принимаются.
C_Timer = { After = function(sec, fn)
  local info = type(fn) == "function" and debug.getinfo(fn, "S")
  if type(sec) ~= "number" or not info or info.what == "C" or not tostring(info.source):find("^@") then
    error("Usage: C_Timer.After(seconds, callback)", 2)
  end
  TIMERS[#TIMERS + 1] = fn
end }
SlashCmdList = {}
