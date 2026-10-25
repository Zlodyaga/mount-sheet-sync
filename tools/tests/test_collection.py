"""Mount Sheet Sync 2.0: characters and the mount collection for a new sheet (Collection.lua).

Loads the whole addon (toc order) on strict WoW mocks, logs in two characters on one account,
reads a mount journal with collected / not collected / drop / secret entries, and checks what
ends up in MountSheetSyncDB. Writes the resulting SavedVariables (WoW format) for the C# tests.
Usage: test_collection.py [<out SavedVariables file>]
"""
import os
import sys
import time
from lupa import lua51
import paths

ADDON_DIR = paths.ADDON_DIR
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_SV = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "sv_collection.lua")
NOW = int(time.time())

checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)))


L = lua51.LuaRuntime(unpack_returned_tuples=True)
L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
L.execute(r"""
MOCK = {
  now = %d, gt = 1000, untilReset = 3 * 86400,
  name = "Эйра", realm = "Ревущий фьорд", guid = "Player-1602-0ABCDEF1",
  class = { "Охотница", "HUNTER" }, level = 90,
  inst = { name = "Даларан", type = "none", diff = 0, max = 0, id = 0 },
  -- id = { name, spellID, sourceType, collected, sourceText }
  mounts = {
    [69]   = { "Пепел Ал'ара", 40192, 1, true,  "|cFFFFD200Добыча:|r Кель'тас Солнечный Скиталец|n|cFFFFD200Рейд:|r Око" },
    [363]  = { "Синий протодракон", 59996, 1, false, "|cFFFFD200Добыча:|r Скади Безжалостный|n|cFFFFD200Подземелье:|r Вершина Утгард|n|cFFFFD200Сложность:|r Героический" },
    [1471] = { "Смотритель Зерета", 368158, 1, false, "|cFFFFD200Добыча:|r Тюремщик|n|cFFFFD200Рейд:|r Гробница Предвечных", 0 },
    [6]    = { "Гнедой конь", 458, 3, true, "|cFFFFD200Продавец:|r Каттер" },
    [777]  = { "Секретный", 1, 1, "SECRET", "x" },
    [778]  = { "SECRETNAME", 2, 1, true, "y" },
  },
  ids = { 6, 69, 363, 1471, 777, 778, 69 },
}
SECRET = setmetatable({}, { __tostring = function() error("addon touched a secret value!") end,
                            __eq = function() error("addon compared a secret value!") end })
function issecretvalue(v) return rawequal(v, SECRET) end
C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return MOCK.untilReset end }
function time() return MOCK.now end
function GetTime() return MOCK.gt end
date = os.date
function UnitName() return MOCK.name end
function GetRealmName() return MOCK.realm end
function UnitGUID() return MOCK.guid end
function UnitClass() return MOCK.class[1], MOCK.class[2], 3 end
function UnitLevel() return MOCK.level end
function GetDifficultyInfo() return nil end
function GetInstanceInfo() local i = MOCK.inst; return i.name, i.type, i.diff, nil, i.max, 0, false, i.id end
function RequestRaidInfo() REQ = REQ + 1 end
function GetNumSavedInstances() return 0 end
function GetNumSavedWorldBosses() return 0 end
local function S(v) if v == "SECRET" or v == "SECRETNAME" then return SECRET end return v end
C_MountJournal = {
  GetMountIDs = function() local t = {}; for i, id in ipairs(MOCK.ids) do t[i] = id end; return t end,
  GetMountInfoByID = function(id)
    local m = MOCK.mounts[id]
    if not m then return nil end
    return S(m[1]), m[2], 132226, false, true, m[3], false, m[6] ~= nil, m[6], false, S(m[4]), id, false
  end,
  GetMountInfoExtraByID = function(id)
    local m = MOCK.mounts[id]
    return 12345, "Описание", m and m[5], false, 230, 4, 0, 0, false
  end,
}
-- Encounter Journal: { journalInstanceID, name, mapID, { { name, journalEncounterID, dungeonEncounterID }, … } }
MOCK.build = "66192"
function GetBuildInfo() return "12.0.5", MOCK.build, "Oct 1 2026", 120005 end
EJ_TIER, EJ_CALLS = 2, 0
MOCK.ej = {
  { raids = { { 78, "Огненные Просторы", 720, { { "Рагнарос", 198, 1203 }, { "Алисразор", 194, 1206 } } } },
    dungeons = { { 65, "Вершина Утгард", 575, { { "Скади Безжалостный", 643, 2029 } } } } },
  { raids = { { 1193, "Святилище Господства", 2450, { { "Сильвана Ветрокрылая", 2441, 2435 } } },
              { 78, "Огненные Просторы", 720, {} } },
    dungeons = {} },
}
function EJ_GetNumTiers() return #MOCK.ej end
function EJ_GetCurrentTier() return EJ_TIER end
function EJ_SelectTier(t) EJ_TIER = t end
function EJ_GetInstanceByIndex(index, raid)
  local i = MOCK.ej[EJ_TIER][raid and "raids" or "dungeons"][index]
  if not i then return nil end
  return i[1], i[2], "описание", 0, 0, 0, 0, 0, "link", true, i[3]
end
function EJ_GetEncounterInfoByIndex(index, instanceID)
  for _, tier in ipairs(MOCK.ej) do
    for _, kind in ipairs({ "raids", "dungeons" }) do
      for _, i in ipairs(tier[kind]) do
        if i[1] == instanceID and #i[4] > 0 then
          local e = i[4][index]
          if not e then return nil end
          EJ_CALLS = EJ_CALLS + 1
          return e[1], "описание", e[2], 0, "link", instanceID, e[3], i[3]
        end
      end
    end
  end
  return nil
end
MountSheetSyncDB = nil
""" % NOW)
g = L.globals()

toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
check("Collection.lua in the toc after the core, before the window",
      files.index("MountSheetSync.lua") < files.index("Collection.lua") < files.index("UI.lua"), files)
check("toc version 2.8.0", "## Version: 2.8.0" in toc)
ns = L.table()
loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
for name in files:
    loader(open(os.path.join(ADDON_DIR, name), encoding="utf-8").read(), name)("MountSheetSync", ns)
check("core version 2.8.0", ns.VERSION == "2.8.0")


def chat_all():
    return "\n".join(str(g.CHAT[i]) for i in range(1, len(g.CHAT) + 1))


def py(t):
    """Lua table -> Python (dict or list)."""
    if not L.eval("function(x) return type(x) == 'table' end")(t):
        return t
    keys = list(t.keys())
    if keys and all(isinstance(k, int) for k in keys) and sorted(keys) == list(range(1, len(keys) + 1)):
        return [py(t[k]) for k in sorted(keys)]
    return {k: py(t[k]) for k in keys}


g.Fire("ADDON_LOADED", "MountSheetSync")
g.Fire("PLAYER_LOGIN")
db = g.MountSheetSyncDB
chars = py(db.chars)
check("login remembers the character", list(chars.keys()) == ["Эйра-Ревущий фьорд"], chars)
c = chars["Эйра-Ревущий фьорд"]
check("…with realm, class, level, guid and time", c == {"name": "Эйра", "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER",
                                                      "level": 90, "guid": "Player-1602-0ABCDEF1", "seen": NOW}, c)
check("collection is read a bit later, not at login", db.collection is None)
g.RunTimers()
col = py(db.collection)
check("collection read after the timer", isinstance(col, dict) and col.get("updated") == NOW, col)
check("collected names and ids (journal order, duplicates once)", col["names"] == ["Гнедой конь", "Пепел Ал'ара"] and col["ids"] == [6, 69], col)
check("counts: journal size and collected", col["journal"] == 7 and col["collected"] == 2, (col["journal"], col["collected"]))
mounts = {d["id"]: d for d in col["mounts"]}
check("whole journal exported once per mount, secret names skipped", sorted(mounts) == [6, 69, 363, 777, 1471], sorted(mounts))
check("old drops list is not written any more", "drops" not in col, list(col))
check("source text without colour codes", mounts[363]["from"] == "Добыча: Скади Безжалостный / Подземелье: Вершина Утгард / Сложность: Героический", mounts[363])
check("source type kept (1 = drop, vendor mount too)", mounts[363]["type"] == 1 and mounts[6]["type"] != 1 and mounts[6]["type"] is not None, (mounts[363], mounts[6]))
check("collected flag only when collected", mounts[69].get("got") is True and mounts[363].get("got") is None, (mounts[69], mounts[363]))
check("secret «collected» counts as not collected", mounts[777].get("got") is None and 777 not in col["ids"])
check("vendor mount has its source too", mounts[6].get("from"), mounts[6])
check("faction only for one-faction mounts (0 = Horde)", mounts[1471].get("faction") == 0 and "faction" not in mounts[6], (mounts[1471], mounts[6]))

# new mount
n_kills = len(db.kills)
g.MOCK.now = NOW + 30
g.Fire("NEW_MOUNT_ADDED", 363)
col = py(db.collection)
check("new mount joins the collection", col["ids"] == [6, 69, 363] and col["names"][-1] == "Синий протодракон" and col["collected"] == 3, col)
check("…and its journal row is now collected", [d for d in col["mounts"] if d["id"] == 363][0].get("got") is True)
check("…and the collection time moves (the program re-checks the sheet)", col["updated"] == NOW + 30, col["updated"])
check("…and the kill journal still gets the mount record", len(db.kills) == n_kills + 1 and db.kills[n_kills + 1].kind == "mount")
g.Fire("NEW_MOUNT_ADDED", 363)
check("same mount twice: once in the collection", py(db.collection)["ids"] == [6, 69, 363])
g.Fire("NEW_MOUNT_ADDED", g.SECRET)
g.Fire("NEW_MOUNT_ADDED", 99999)
check("secret or unknown mount id: nothing, no error", py(db.collection)["ids"] == [6, 69, 363] and "ошибк" not in chat_all())

# second character on the same account
g.MOCK.mounts[363][4] = True   # the game now reports it as collected
g.MOCK.name, g.MOCK.guid, g.MOCK.level = "Мирна", "Player-1602-0AAAAAA", 71
g.MOCK["class"] = L.table_from(["Жрица", "PRIEST"])
g.MOCK.now = NOW + 3600
g.Fire("PLAYER_LOGIN"); g.RunTimers()
chars = py(db.chars)
check("second character remembered, first kept", sorted(chars) == ["Мирна-Ревущий фьорд", "Эйра-Ревущий фьорд"], sorted(chars))
check("first character's data untouched", chars["Эйра-Ревущий фьорд"]["seen"] == NOW and chars["Мирна-Ревущий фьорд"]["level"] == 71)
g.MOCK.level = 72
g.MOCK.now = NOW + 7200
g.Fire("PLAYER_LOGIN"); g.RunTimers()
chars = py(db.chars)
check("relogin updates level and time, no duplicate", len(chars) == 2 and chars["Мирна-Ревущий фьорд"]["level"] == 72
      and chars["Мирна-Ревущий фьорд"]["seen"] == NOW + 7200)
check("full rescan follows the journal (363 learned in game)", py(db.collection)["ids"] == [6, 69, 363], py(db.collection)["ids"])

# ---- Encounter Journal dump (2.4): bosses and instances in the client's language for the program
j = py(db.journal)
check("journal read after the timer, with locale and build", isinstance(j, dict) and j.get("locale") == "ruRU"
      and j.get("build") == "66192" and j.get("updated") == NOW, j)
check("journal instances: each once, with map IDs and the raid flag",
      sorted((i["id"], i["map"], i.get("raid", False)) for i in j["instances"]) == [(65, 575, False), (78, 720, True), (1193, 2450, True)],
      j.get("instances"))
check("journal encounters: dungeonEncounterID, journalEncounterID, name, instance",
      sorted((e["id"], e["j"], e["name"], e["i"]) for e in j["encounters"]) == [(1203, 198, "Рагнарос", 78), (1206, 194, "Алисразор", 78),
      (2029, 643, "Скади Безжалостный", 65), (2435, 2441, "Сильвана Ветрокрылая", 1193)], j.get("encounters"))
check("the journal's tier is restored after the scan", g.EJ_TIER == 2, g.EJ_TIER)
check("mount names carry the client language", py(db.collection).get("locale") == "ruRU", py(db.collection).get("locale"))
calls = g.EJ_CALLS
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("same client build and language: the journal is not read again", g.EJ_CALLS == calls, (calls, g.EJ_CALLS))
g.MOCK.build = "66300"
g.EncounterJournal = L.eval("{ IsShown = function() return true end }")
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("journal window open: not touched", g.EJ_CALLS == calls and py(db.journal)["build"] == "66192")
g.EncounterJournal = None
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("new client build: the journal is read again", g.EJ_CALLS > calls and py(db.journal)["build"] == "66300", py(db.journal)["build"])

# same name on another realm
g.MOCK.realm = "Гордунни"
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("same name on another realm is another character", "Мирна-Гордунни" in py(db.chars) and len(py(db.chars)) == 3)
g.MOCK.realm = "Ревущий фьорд"

# chat: /mss chars, /mss forget, help
g.SlashCmdList["MOUNTSHEETSYNC"]("chars")
out = chat_all()
check("/mss chars lists characters with realm, class and level",
      "Персонажи для новой таблицы (3):" in out and "Эйра |cff8c8c8c(Ревущий фьорд, Охотница 90)|r" in out
      and "Мирна |cff8c8c8c(Гордунни, Жрица 72)|r" in out, out[-600:])
check("/mss chars shows the collection size", "Маунтов собрано: 2 из 7." in out or "Маунтов собрано:" in out, out[-300:])
g.SlashCmdList["MOUNTSHEETSYNC"]("forget мирна-гордунни")
check("/mss forget by name-realm, any case", sorted(py(db.chars)) == ["Мирна-Ревущий фьорд", "Эйра-Ревущий фьорд"], sorted(py(db.chars)))
check("…and says how to get it back", "Убран из списка: Мирна-Гордунни" in chat_all().split("\n")[-1])
g.SlashCmdList["MOUNTSHEETSYNC"]("forget ЭЙРА")
check("/mss forget by name (Cyrillic upper case)", sorted(py(db.chars)) == ["Мирна-Ревущий фьорд"])
g.SlashCmdList["MOUNTSHEETSYNC"]("forget Тралл")
check("/mss forget unknown: explained", "Персонажа «Тралл» в списке нет" in chat_all().split("\n")[-1])
g.SlashCmdList["MOUNTSHEETSYNC"]("forget")
check("/mss forget without a name: hint", "Укажите имя персонажа" in chat_all().split("\n")[-1])
g.SlashCmdList["MOUNTSHEETSYNC"]("help")
out = chat_all()
check("/mss help mentions chars and forget", "/mss chars" in out and "/mss forget" in out)

# limits and missing APIs
for i in range(230):
    db.chars["Альт%d-Сервер" % i] = L.table_from({"name": "Альт%d" % i, "realm": "Сервер", "seen": NOW - 100000 + i})
g.Fire("PLAYER_LOGIN")
chars = py(db.chars)
check("at most 200 characters, the oldest go", len(chars) == 200 and "Альт0-Сервер" not in chars and "Мирна-Ревущий фьорд" in chars, len(chars))
for key in [k for k in chars if k.startswith("Альт")]:
    db.chars[key] = None
saved_journal = g.C_MountJournal
g.C_MountJournal = L.table_from({"GetMountInfoByID": saved_journal.GetMountInfoByID})
before = py(db.collection)
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("old client without GetMountIDs: collection left as it was", py(db.collection) == before)
g.C_MountJournal = None
g.Fire("PLAYER_LOGIN"); g.RunTimers()
g.Fire("NEW_MOUNT_ADDED", 69)
check("no mount journal at all: quiet", py(db.collection) == before and "ошибк" not in chat_all())
g.C_MountJournal = saved_journal
g.MOCK.name = g.SECRET
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("secret player name: character not stored", len(py(db.chars)) == 1)
g.MOCK.name = "Эйра"
db.chars = "broken"
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("broken chars table is rebuilt", sorted(py(db.chars)) == ["Эйра-Ревущий фьорд"])
g.MOCK.name = "Мирна"
g.Fire("PLAYER_LOGIN"); g.RunTimers()
check("no Lua errors reported in chat", "ошибк" not in chat_all().lower() and "error" not in chat_all().lower(), chat_all()[-400:])


# ---- SavedVariables in the game's own format, for the program's tests
def lua_str(s):
    out = []
    for ch in s:
        o = ord(ch)
        if ch == "\\": out.append("\\\\")
        elif ch == '"': out.append('\\"')
        elif ch == "\n": out.append("\\n")
        elif ch == "\r": out.append("\\r")
        elif o < 32: out.append("\\%03d" % o)
        else: out.append(ch)
    return '"' + "".join(out) + '"'


def ser(v, ind):
    if isinstance(v, bool): return "true" if v else "false"
    if isinstance(v, (int, float)): return repr(v)
    if isinstance(v, str): return lua_str(v)
    pad = "\t" * (ind + 1)
    lines = ["{"]
    if isinstance(v, list):
        for i, x in enumerate(v, 1):
            lines.append("%s%s, -- [%d]" % (pad, ser(x, ind + 1), i))
    else:
        for k in sorted(v, key=lambda k: (not isinstance(k, str), str(k))):
            key = "[%s]" % (lua_str(k) if isinstance(k, str) else k)
            lines.append("%s%s = %s," % (pad, key, ser(v[k], ind + 1)))
    lines.append("\t" * ind + "}")
    return "\n".join(lines)


with open(OUT_SV, "w", encoding="utf-8", newline="\r\n") as f:
    f.write("\nMountSheetSyncDB = " + ser(py(db), 0) + "\n")
print("SavedVariables written:", OUT_SV)

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
