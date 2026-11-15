--[[
Mount Sheet Sync — окошко «Сколько попыток»
Правый щелчок по строке вкладки «Попытки»: число попыток можно поправить (например, убийства до установки аддона,
которых нет в статистике игры). Число уходит программе записью журнала (ns.SetAttempts), в окне видно сразу.
]]

local _, ns = ...
local UI = ns.UI
local S, P = UI.state, UI.private
local L, Skin, relabel, Label, MakeButton = P.L, P.Skin, P.relabel, P.Label, P.MakeButton

local FRAME_NAME = "MountSheetSyncAttemptsFrame"
local frame, box, nameText
local editing   -- строка вкладки «Попытки» (ns.GetAttemptsList), которую правят

local function Save()
  local n = tonumber(box:GetText())
  if not editing or not n or n < 0 then
    return
  end
  if ns.SetAttempts(editing.raw, n) then
    frame:Hide()
    if S.frame and S.frame:IsShown() then
      UI.Refresh()
    end
  end
end

local function Create()
  frame = Skin.Window(FRAME_NAME, 320, 150, "DIALOG")
  frame:SetPoint("CENTER", 0, 80)
  local function title()
    frame.titleText:SetText(L.attempts_edit_title)
  end
  title()
  relabel[#relabel + 1] = title

  nameText = frame:CreateFontString(nil, "OVERLAY", "MSSFontTitle")
  nameText:SetPoint("TOPLEFT", 20, -42)
  nameText:SetPoint("RIGHT", -20, 0)
  nameText:SetJustifyH("LEFT")
  nameText:SetWordWrap(false)

  local hint = frame:CreateFontString(nil, "OVERLAY", "MSSFontDim")
  hint:SetPoint("TOPLEFT", 20, -66)
  Label(hint, "attempts_edit_hint")

  box = Skin.EditBox(frame, 90, 24)
  box:SetPoint("BOTTOMLEFT", 20, 18)
  box:SetMaxLetters(6)
  box:SetNumeric(true)
  box:SetScript("OnEnterPressed", Save)
  box:SetScript("OnEscapePressed", function()
    frame:Hide()
  end)

  local save = MakeButton(frame, "button_save_attempts", 120, nil, Save)
  save:SetPoint("LEFT", box, "RIGHT", 10, 0)
  frame:Hide()
end

-- m — строка из ns.GetAttemptsList() (m.raw — строка счётчика из списка программы).
function UI.EditAttempts(m)
  if type(m) ~= "table" or type(m.raw) ~= "table" then
    return
  end
  if not frame then
    if not ns.SafeCall(Create) or not frame then
      frame = nil
      return
    end
  end
  editing = m
  nameText:SetText("|cffc9a0ff" .. P.Safe(m.name) .. "|r")
  box:SetText(tostring(m.n or 0))
  frame:Show()
  box:SetFocus()
  box:HighlightText()
end

P.SaveAttempts = Save   -- для проверок (test_addon_farm.py)
