--[[
Mount Sheet Sync — languages / языки
==========================================
The addon speaks the game client's language when it knows it (Russian, German,
French, Spanish, Portuguese) and English otherwise. The choice can be changed
in the settings (right-click on the addon icon or /mss settings).

ns.L[key] — text in the current language (English when a translation is missing).
Format strings keep the same order of %s / %d in every language.

Note: names from the Google sheet (bosses, mounts, places) are shown as they are
in the sheet — the addon translates only its own texts.
]]

local ADDON_NAME, ns = ...

ns.LANGUAGES = { "enUS", "ruRU", "deDE", "frFR", "esES", "ptBR" }
ns.LANGUAGE_NAMES = {
  enUS = "English",
  ruRU = "Русский",
  deDE = "Deutsch",
  frFR = "Français",
  esES = "Español",
  ptBR = "Português",
}

-- Game client locale > addon language (everything else > English).
local CLIENT = {
  enUS = "enUS", enGB = "enUS",
  ruRU = "ruRU",
  deDE = "deDE",
  frFR = "frFR",
  esES = "esES", esMX = "esES",
  ptBR = "ptBR", ptPT = "ptBR",
}

local T = ns.TRANSLATIONS   -- the texts: Locale.<language>.lua, loaded before this file


---------------------------------------------------------------------------
-- Choosing the language
---------------------------------------------------------------------------
local current = T.enUS
local currentCode = "enUS"

--- Texts for a player without a Google sheet: the program keeps the data on this computer, or there is no program yet.
--- With a sheet (Program.lua: storage is not "local") the texts above stay, they name the sheet.
local LIST_WORDS = ns.LIST_WORDS

--- No sheet to name: the program says so in Program.lua; no program at all counts too.
function ns.UsesListWords()
  local p = MountSheetSyncProgram
  return type(p) ~= "table" or p.storage == "local"
end

ns.L = setmetatable({}, {
  __index = function(_, key)
    if ns.UsesListWords() then
      local words = LIST_WORDS[currentCode] or LIST_WORDS.enUS
      if words[key] then
        return words[key]
      end
    end
    return current[key] or T.enUS[key] or key
  end,
})

--- Language of the game client, if the addon has it (otherwise English).
function ns.ClientLanguage()
  local locale = GetLocale and GetLocale() or "enUS"
  return CLIENT[locale] or "enUS"
end

--- The language now in use ("enUS", "ruRU", …).
function ns.CurrentLanguage()
  return currentCode
end

--- Saved choice: "auto" (game language) or a language code.
function ns.LanguageSetting()
  local db = ns.GetDB and ns.GetDB()
  local v = db and db.language
  return (type(v) == "string" and T[v]) and v or "auto"
end

-- Callbacks to re-label the window and the settings after a change.
local listeners = {}

function ns.OnLanguageChanged(fn)
  listeners[#listeners + 1] = fn
end

--- Use the saved setting (called after SavedVariables are loaded).
function ns.ApplyLanguage()
  local setting = ns.LanguageSetting()
  local code = setting == "auto" and ns.ClientLanguage() or setting
  currentCode = T[code] and code or "enUS"
  current = T[currentCode]
  for _, fn in ipairs(listeners) do
    if ns.SafeCall then
      ns.SafeCall(fn)
    else
      fn()
    end
  end
end

--- Save and apply a choice ("auto" or a code). Returns false for an unknown code.
function ns.SetLanguage(code)
  if code ~= "auto" and not T[code] then
    return false
  end
  local db = ns.GetDB and ns.GetDB()
  if db then
    db.language = code ~= "auto" and code or nil
  end
  ns.ApplyLanguage()
  return true
end

--- "ru", "russian", "русский", "ruRU" > "ruRU"; "auto" > "auto"; unknown > nil.
function ns.ParseLanguage(text)
  local lower = ns.LowerText or string.lower
  local t = lower(text or ""):gsub("^ +", ""):gsub(" +$", "")
  if t == "" or t == "auto" or t == "default" then
    return "auto"
  end
  for _, code in ipairs(ns.LANGUAGES) do
    if t == code:lower() or t == code:sub(1, 2):lower() or t == lower(ns.LANGUAGE_NAMES[code]) then
      return code
    end
  end
  local aliases = { english = "enUS", russian = "ruRU", ["русский"] = "ruRU", german = "deDE", french = "frFR",
                    spanish = "esES", ["español"] = "esES", portuguese = "ptBR", ["português"] = "ptBR" }
  return aliases[t]
end

-- ns.TRANSLATIONS and ns.LIST_WORDS are also read by the tests: every language must have every key
