--[[
Mount Sheet Sync 3.0.4 — своё оформление окон
=============================================
Окна рисуются своими текстурами и шрифтами, а не шаблонами Blizzard: тёмный фон, золотая рамка, плоские кнопки,
«таблетки» фильтров, круглые переключатели. Текстуры (Media/*.tga, белые, цвет — SetVertexColor) делает
tools/skin/make_media.py. Шрифты — PT Sans и Philosopher (SIL OFL, файлы без изменений, лицензии рядом в Media);
на китайском и корейском клиентах в них нет букв, там остаётся шрифт игры.
]]

local ADDON_NAME, ns = ...

local MEDIA = "Interface\\AddOns\\MountSheetSync\\Media\\"
local Skin = {}
ns.Skin = Skin

Skin.DOT = MEDIA .. "Dot"
Skin.ARROW = MEDIA .. "ArrowDown"
Skin.GEAR = MEDIA .. "Gear"

local function Hex(text)
  return { tonumber(text:sub(1, 2), 16) / 255, tonumber(text:sub(3, 4), 16) / 255, tonumber(text:sub(5, 6), 16) / 255 }
end

local COLOR = {
  window = Hex("12100d"), windowLine = Hex("6b6252"), title = Hex("1e1b16"), panel = Hex("0b0a08"), line = Hex("3d372c"),
  raised = Hex("1e1b16"), activeFill = Hex("33290f"), gold = Hex("ffd24a"), goldLine = Hex("c9a24a"), text = Hex("d9d3c4"),
  control = Hex("8a7f68"), editLine = Hex("5a5242"), redTop = Hex("8f1414"), redBottom = Hex("560707"), closeFill = Hex("7d1010"),
}
Skin.COLOR = COLOR

local MARGINS = { Round3 = 4, Round6 = 8, Pill = 12 }

---------------------------------------------------------------------------
-- Шрифты
---------------------------------------------------------------------------
local locale = GetLocale()
local NO_LETTERS = locale == "zhCN" or locale == "zhTW" or locale == "koKR"

local function MakeFont(name, file, size, color)
  local font = CreateFont(name)
  font:SetFont(NO_LETTERS and STANDARD_TEXT_FONT or (MEDIA .. file), size, "")
  font:SetTextColor(color[1], color[2], color[3])
end

MakeFont("MSSFontBody", "PTSans-Regular.ttf", 12, Hex("e4dfd2"))
MakeFont("MSSFontBold", "PTSans-Bold.ttf", 12, Hex("e4dfd2"))
MakeFont("MSSFontName", "PTSans-Regular.ttf", 13, Hex("e4dfd2"))
MakeFont("MSSFontStrong", "PTSans-Bold.ttf", 13, Hex("e4dfd2"))
MakeFont("MSSFontGold", "PTSans-Regular.ttf", 12, Hex("d8c98f"))
MakeFont("MSSFontSmall", "PTSans-Regular.ttf", 11, Hex("a89f8b"))
MakeFont("MSSFontHead", "PTSans-Bold.ttf", 11, Hex("a89f8b"))
MakeFont("MSSFontDim", "PTSans-Regular.ttf", 13, Hex("a89f8b"))
MakeFont("MSSFontTitle", "Philosopher-Bold.ttf", 14, COLOR.gold)
MakeFont("MSSFontButton", "Philosopher-Bold.ttf", 13, COLOR.gold)

---------------------------------------------------------------------------
-- Детали
---------------------------------------------------------------------------
-- Закрашенная форма с растягиваемой серединой: углы не искажаются при любом размере.
local function Fill(parent, shape, color, inset, sublevel, alpha)
  local t = parent:CreateTexture(nil, "BACKGROUND", nil, sublevel or 0)
  t:SetTexture(MEDIA .. shape)
  local m = MARGINS[shape]
  t:SetTextureSliceMargins(m, m, m, m)
  t:SetVertexColor(color[1], color[2], color[3], alpha or 1)
  inset = inset or 0
  t:SetPoint("TOPLEFT", inset, -inset)
  t:SetPoint("BOTTOMRIGHT", -inset, inset)
  return t
end
Skin.Fill = Fill

local function Hover(parent, shape)
  local t = parent:CreateTexture(nil, "HIGHLIGHT")
  t:SetTexture(MEDIA .. shape)
  local m = MARGINS[shape]
  t:SetTextureSliceMargins(m, m, m, m)
  t:SetAllPoints()
  t:SetVertexColor(1, 1, 1, 0.12)
  return t
end

-- Панель внутри окна: рамка и тёмный низ.
function Skin.Panel(parent)
  local panel = CreateFrame("Frame", nil, parent)
  Fill(panel, "Round3", COLOR.line, 0, 0)
  Fill(panel, "Round3", COLOR.panel, 1, 1)
  return panel
end

-- Кнопка: красная с золотой рамкой.
function Skin.Button(parent, width, height)
  local b = CreateFrame("Button", nil, parent)
  b:SetSize(width, height)
  Fill(b, "Round3", COLOR.goldLine, 0, 0)
  local body = Fill(b, "Round3", { 1, 1, 1 }, 1, 1)
  body:SetGradient("VERTICAL", CreateColor(COLOR.redBottom[1], COLOR.redBottom[2], COLOR.redBottom[3], 1),
    CreateColor(COLOR.redTop[1], COLOR.redTop[2], COLOR.redTop[3], 1))
  Hover(b, "Round3")
  local text = b:CreateFontString(nil, "OVERLAY", "MSSFontButton")
  text:SetPoint("CENTER")
  b:SetFontString(text)
  return b
end

-- Фильтр-«таблетка»; Skin.ChipState — выбран ли.
function Skin.Chip(parent, height)
  local b = CreateFrame("Button", nil, parent)
  b:SetHeight(height)
  b.line = Fill(b, "Pill", COLOR.line, 0, 0)
  b.fill = Fill(b, "Pill", COLOR.raised, 1, 1)
  Hover(b, "Pill")
  local text = b:CreateFontString(nil, "OVERLAY", "MSSFontBody")
  text:SetPoint("CENTER")
  b:SetFontString(text)
  b.label = text
  return b
end

function Skin.ChipState(b, on)
  local line, fill, text = on and COLOR.goldLine or COLOR.line, on and COLOR.activeFill or COLOR.raised, on and COLOR.gold or COLOR.text
  b.line:SetVertexColor(line[1], line[2], line[3])
  b.fill:SetVertexColor(fill[1], fill[2], fill[3])
  b.label:SetTextColor(text[1], text[2], text[3])
end

-- Вкладка: низ уходит под панель (у неё кадр выше), видна только верхняя часть со скруглением.
function Skin.Tab(parent)
  local b = CreateFrame("Button", nil, parent)
  b._isTab = true
  b.line = Fill(b, "Round3", COLOR.line, 0, 0)
  b.line:SetPoint("BOTTOMRIGHT", 0, -6)
  b.fill = Fill(b, "Round3", COLOR.raised, 1, 1)
  b.fill:SetPoint("BOTTOMRIGHT", -1, -6)
  local hover = b:CreateTexture(nil, "HIGHLIGHT")
  hover:SetAllPoints()
  hover:SetColorTexture(1, 1, 1, 0.06)
  local text = b:CreateFontString(nil, "OVERLAY", "MSSFontButton")
  text:SetPoint("CENTER", 0, 0)
  b:SetFontString(text)
  b.label = text
  return b
end

function Skin.TabState(b, on)
  local line, fill, text = on and COLOR.goldLine or COLOR.line, on and COLOR.activeFill or COLOR.raised, on and COLOR.gold or COLOR.text
  b.line:SetVertexColor(line[1], line[2], line[3])
  b.fill:SetVertexColor(fill[1], fill[2], fill[3])
  b.label:SetTextColor(text[1], text[2], text[3])
  b:SetHeight(on and 28 or 26)
  if on then
    b:Disable()
  else
    b:Enable()
  end
end

function Skin.TabWidth(b, minimum)
  b:SetWidth(math.max(minimum, b.label:GetStringWidth() + 28))
end

-- Галочка или круглый переключатель (radio = true): надпись справа — в c.Text, щелчок засчитывается и по ней.
function Skin.Check(parent, radio)
  local c = CreateFrame("CheckButton", nil, parent)
  c:SetSize(16, 16)
  if radio then
    local ring = c:CreateTexture(nil, "BACKGROUND", nil, 0)
    ring:SetTexture(Skin.DOT)
    ring:SetAllPoints()
    ring:SetVertexColor(COLOR.control[1], COLOR.control[2], COLOR.control[3])
    local inner = c:CreateTexture(nil, "BACKGROUND", nil, 1)
    inner:SetTexture(Skin.DOT)
    inner:SetPoint("TOPLEFT", 1, -1)
    inner:SetPoint("BOTTOMRIGHT", -1, 1)
    inner:SetVertexColor(COLOR.raised[1], COLOR.raised[2], COLOR.raised[3])
  else
    Fill(c, "Round3", COLOR.control, 0, 0)
    Fill(c, "Round3", COLOR.raised, 1, 1)
  end
  c:SetCheckedTexture(MEDIA .. (radio and "RadioMark" or "CheckMark"))
  c:GetCheckedTexture():SetVertexColor(COLOR.gold[1], COLOR.gold[2], COLOR.gold[3])
  c.Text = c:CreateFontString(nil, "OVERLAY", "MSSFontGold")
  c.Text:SetPoint("LEFT", c, "RIGHT", 6, 0)
  c:SetHitRectInsets(0, -220, 0, 0)
  return c
end

-- Поле ввода.
function Skin.EditBox(parent, width, height)
  local e = CreateFrame("EditBox", nil, parent)
  e:SetSize(width, height)
  e:SetFontObject(MSSFontBody)
  e:SetTextInsets(6, 6, 0, 0)
  e:SetAutoFocus(false)
  Fill(e, "Round3", COLOR.editLine, 0, 0)
  Fill(e, "Round3", COLOR.raised, 1, 1)
  return e
end

-- Окно: тень, рамка, полоса заголовка, крестик; перетаскивается и закрывается по Esc.
function Skin.Window(name, width, height, strata)
  local f = CreateFrame("Frame", name, UIParent)
  f:SetSize(width, height)
  f:SetFrameStrata(strata)
  f:SetToplevel(true)
  f:SetClampedToScreen(true)
  f:SetMovable(true)
  f:EnableMouse(true)
  f:RegisterForDrag("LeftButton")
  f:SetScript("OnDragStart", f.StartMoving)
  f:SetScript("OnDragStop", f.StopMovingOrSizing)
  tinsert(UISpecialFrames, name)

  local shadow = Fill(f, "Round6", { 0, 0, 0 }, 0, 0, 0.45)
  shadow:SetPoint("TOPLEFT", -5, 5)
  shadow:SetPoint("BOTTOMRIGHT", 5, -5)
  Fill(f, "Round6", COLOR.windowLine, 0, 1)
  Fill(f, "Round6", COLOR.window, 2, 2)
  local bar = f:CreateTexture(nil, "BACKGROUND", nil, 3)
  bar:SetColorTexture(COLOR.title[1], COLOR.title[2], COLOR.title[3])
  bar:SetPoint("TOPLEFT", 2, -2)
  bar:SetPoint("BOTTOMRIGHT", f, "TOPRIGHT", -2, -30)
  local barLine = f:CreateTexture(nil, "BACKGROUND", nil, 4)
  barLine:SetColorTexture(COLOR.line[1], COLOR.line[2], COLOR.line[3])
  barLine:SetPoint("TOPLEFT", 2, -30)
  barLine:SetPoint("BOTTOMRIGHT", f, "TOPRIGHT", -2, -31)

  f.titleText = f:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  f.titleText:SetPoint("CENTER", f, "TOP", 0, -16)

  local close = CreateFrame("Button", nil, f)
  close:SetSize(22, 22)
  close:SetPoint("TOPRIGHT", -6, -5)
  Fill(close, "Round3", COLOR.goldLine, 0, 0)
  Fill(close, "Round3", COLOR.closeFill, 1, 1)
  Hover(close, "Round3")
  local x = close:CreateTexture(nil, "ARTWORK")
  x:SetTexture(MEDIA .. "Close")
  x:SetSize(16, 16)
  x:SetPoint("CENTER")
  x:SetVertexColor(COLOR.gold[1], COLOR.gold[2], COLOR.gold[3])
  close:SetScript("OnClick", function()
    f:Hide()
  end)
  f.closeButton = close
  return f
end

-- Полоса прокрутки ScrollBox — шаблон игры, поэтому только тише и без стрелок; если шаблон изменится, останется как есть.
local function TintRegions(frame)
  for _, region in ipairs({ frame:GetRegions() }) do
    if region.SetDesaturated then
      region:SetDesaturated(true)
      region:SetVertexColor(COLOR.control[1], COLOR.control[2], COLOR.control[3])
    end
  end
end

function Skin.ScrollBar(bar)
  pcall(function()
    bar.Back:SetAlpha(0)
    bar.Forward:SetAlpha(0)
    TintRegions(bar.Track.Thumb)
  end)
end
