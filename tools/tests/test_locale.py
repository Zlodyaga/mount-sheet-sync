"""Mount Sheet Sync 2.3: addon languages — game language when known (ru/de/fr/es/pt), English otherwise;
the settings window (right-click on the icon, /mss settings, /mss lang) switches it at once."""
import os
import re
import time
from lupa import lua51
import paths

ADDON_DIR = paths.ADDON_DIR
HERE = os.path.dirname(os.path.abspath(__file__))
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


def new_game(locale, saved_db=None):
    L = lua51.LuaRuntime(unpack_returned_tuples=True)
    L.execute(open(os.path.join(HERE, "wow_mock.lua"), encoding="utf-8").read())
    L.execute(r"""
    MOCK_LOCALE = "%s"
    function issecretvalue() return false end
    C_DateAndTime = { GetSecondsUntilWeeklyReset = function() return 3 * 86400 end }
    function time() return %d end
    function GetTime() return 1000 end
    date = os.date
    function UnitName() return "Eira" end
    function GetRealmName() return "Silvermoon" end
    function UnitGUID() return "Player-1602-0ABCDEF1" end
    function GetDifficultyInfo() return nil end
    function GetInstanceInfo() return "Stormwind", "none", 0, "", 5, 0, false, 0 end
    function RequestRaidInfo() end
    function ReloadUI() RELOADED = true end
    function GetNumSavedInstances() return 0 end
    function GetNumSavedWorldBosses() return 0 end
    """ % (locale, NOW))
    if saved_db:
        L.execute(saved_db)
    g = L.globals()
    toc = open(os.path.join(ADDON_DIR, "MountSheetSync.toc"), encoding="utf-8").read().splitlines()
    files = [ln.strip().replace("\\", "/") for ln in toc if ln.strip() and not ln.startswith("#")]
    ns = L.table()
    loader = L.eval("function(src, name) return assert(loadstring(src, '@' .. name)) end")
    for name in files:
        loader(open(os.path.join(ADDON_DIR, name), encoding="utf-8").read(), name)("MountSheetSync", ns)
    L.eval("function(n) n.UsesListWords = function() return false end end")(ns)   # the texts of a player with a Google sheet
    global LUA_RAWGET
    LUA_RAWGET = L.eval("rawget")
    g.Fire("ADDON_LOADED", "MountSheetSync")
    g.Fire("PLAYER_LOGIN")
    g.RunTimers()
    return L, g, ns


def chat(g, n=None):
    lines = [str(g.CHAT[i]) for i in range(1, len(g.CHAT) + 1)]
    return lines if n is None else lines[-n:]


def window_texts(g):
    return {str(r._text) for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)] if r._text is not None}


def rg(r, k):
    return r._keys[k] if r._keys[k] is not None else LUA_RAWGET(r, k)


LUA_RAWGET = None


def lua_list(t):
    return [t[i] for i in range(1, len(t) + 1)]


# ---------------------------------------------------------------- 1. every language has every text
L, g, ns = new_game("ruRU")
T = ns.TRANSLATIONS
langs = lua_list(ns.LANGUAGES)
check("six languages: en, ru, de, fr, es, pt", langs == ["enUS", "ruRU", "deDE", "frFR", "esES", "ptBR"], langs)
en = T["enUS"]
keys = sorted(k for k in en.keys())
spec = re.compile(r"%[sd]")
for code in langs:
    t = T[code]
    missing = [k for k in keys if t[k] is None]
    extra = [k for k in t.keys() if en[k] is None]
    bad = [k for k in keys if t[k] is not None and spec.findall(str(t[k])) != spec.findall(str(en[k]))]
    check("%s: all %d texts, same %%s/%%d in the same order" % (code, len(keys)), not missing and not extra and not bad,
          (missing, extra, bad))

# a key written twice in one language: Lua keeps the last one silently (3.0 nearly lost the /mss chars title this way)
src = "\n".join(open(os.path.join(paths.ADDON_DIR, "Locale.%s.lua" % code), encoding="utf-8").read()
                for code in ("enUS", "ruRU", "deDE", "frFR", "esES", "ptBR"))
check("3.0.7: one text file per language", len(re.findall(r"^T\.\w+ = \{", src, re.M)) == 6)
for block in re.findall(r"^T\.(\w+) = \{\n(.*?)^\}", src, re.S | re.M):
    names = re.findall(r"^  (\w+) = ", block[1], re.M)
    dups = sorted({k for k in names if names.count(k) > 1})
    check("%s: no key written twice in its file" % block[0], not dups, dups)

# ---------------------------------------------------------------- 2. language picked from the game client
for locale, want, sample in [("ruRU", "ruRU", "открыть окно"), ("deDE", "deDE", "Fenster öffnen"),
                             ("frFR", "frFR", "ouvrir la fenêtre"), ("esMX", "esES", "abrir la ventana"),
                             ("esES", "esES", "abrir la ventana"), ("ptBR", "ptBR", "abrir a janela"),
                             ("enGB", "enUS", "open the window"), ("zhCN", "enUS", "open the window"),
                             ("koKR", "enUS", "open the window")]:
    L, g, ns = new_game(locale)
    g.SlashCmdList["MOUNTSHEETSYNC"]("help")
    out = "\n".join(chat(g, 14))
    check("client %s → addon in %s (help: “%s”)" % (locale, want, sample), ns.CurrentLanguage() == want and sample in out, out[-400:])

# ---------------------------------------------------------------- 3. window, tooltip and records in German
L, g, ns = new_game("deDE")
g.SlashCmdList["MOUNTSHEETSYNC"]("")
texts = window_texts(g)
check("German window: buttons, tab, hint", {"An Tabelle senden", "In Sperren suchen", "Protokoll", "Von Hand markieren:",
                                           "Versuche"} <= texts, sorted(t for t in texts if len(t) < 40))
check("…no Russian left in the window", not any(re.search("[А-Яа-яЁё]", t) for t in texts if "Eira" not in t),
      [t for t in texts if re.search("[А-Яа-яЁё]", t)])
obj = L.eval("LibStub('LibDataBroker-1.1'):GetDataObjectByName('MountSheetSync')")
g.GameTooltip.lines = L.table()
obj.OnTooltipShow(g.GameTooltip)
tt = [str(g.GameTooltip.lines[i]) for i in range(1, len(g.GameTooltip.lines) + 1)]
check("German tooltip: left / right click hints", tt[-2] == "Linksklick — Fenster öffnen" and tt[-1] == "Rechtsklick — Einstellungen", tt)
ns.AddManual("Sha des Zorns")
check("German chat for a manual mark", "Sha des Zorns — von Hand für Eira markiert." in chat(g, 1)[0], chat(g, 2))

# ---------------------------------------------------------------- 4. settings: right click, radio buttons, live switch
obj.OnClick(None, "RightButton")
sf = g.MountSheetSyncSettingsFrame
check("right click on the DataBroker icon opens the settings", sf is not None and sf._shown)
check("settings title in German", sf.titleText._text == "Mount Sheet Sync — Einstellungen", sf.titleText._text)
radios = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
          if r._type == "CheckButton" and r._parent is not None and r._parent._uid == sf._uid and rg(r, "code") is not None]
labels = {str(rg(r, "code")): str(r.Text._text) for r in radios}
check("language list: game language + 6", labels.get("auto") == "Spielsprache (Deutsch)" and labels.get("ruRU") == "Русский"
      and labels.get("ptBR") == "Português" and len(labels) == 7, labels)
check("“game language” is the ticked one", [str(rg(r, "code")) for r in radios if r._checked] == ["auto"])
ru = [r for r in radios if rg(r, "code") == "ruRU"][0]
ru.MockClick(ru)
texts = window_texts(g)
check("pick Русский: the open window switches at once", {"Отправить в таблицу", "Найти в сохранениях", "Журнал",
                                                         "Не писать в чат"} <= texts and "An Tabelle senden" not in texts)
check("…the settings too, and only Русский is ticked", sf.titleText._text == "Mount Sheet Sync — настройки"
      and [str(rg(r, "code")) for r in radios if r._checked] == ["ruRU"] and labels and str(ru.Text._text) == "Русский")
check("…saved in SavedVariables", g.MountSheetSyncDB.language == "ruRU")
check("auto label follows the new language", str([r for r in radios if rg(r, "code") == "auto"][0].Text._text) == "Язык игры (Deutsch)")
L2, g2, ns2 = new_game("deDE", 'MountSheetSyncDB = { language = "ruRU", kills = {} }')
check("after a relog the choice stays (German client, Russian addon)", ns2.CurrentLanguage() == "ruRU")
LUA_RAWGET = L.eval("rawget")
auto = [r for r in radios if rg(r, "code") == "auto"][0]
auto.MockClick(auto)
check("back to “game language”: German again, setting cleared", ns.CurrentLanguage() == "deDE" and g.MountSheetSyncDB.language is None
      and "An Tabelle senden" in window_texts(g))
fr = [r for r in radios if rg(r, "code") == "frFR"][0]
fr.MockClick(fr)
scan = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
        if r._type == "Button" and r._text == "Chercher dans les verrouillages"]
check("long French button text: the button grows", scan and scan[0]._w > 156, scan and scan[0]._w)

# ---------------------------------------------------------------- 5. quiet and minimap in the settings
q = [r for r in [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
     if r._type == "CheckButton" and r._parent is not None and r._parent._uid == sf._uid and rg(r, "code") is None and rg(r, "view") is None]
check("six more ticks: chat messages, minimap button, route through Mapzeroth, the drop notice, its sound, not with Rarity (3.0.7)", len(q) == 6, len(q))
q[0].MockClick(q[0])
check("“no chat messages” from the settings", g.MountSheetSyncDB.quiet is True)
q[1].MockClick(q[1])
check("minimap button hidden from the settings", g.MountSheetSyncMinimapButton._shown is False)
q[1].MockClick(q[1])
check("…and shown again", g.MountSheetSyncMinimapButton._shown is True)
q[2].MockClick(q[2])
check("«route through Mapzeroth» off from the settings: remembered", g.MountSheetSyncDB.mapzeroth is False)
q[2].MockClick(q[2])
check("…and on again (the default: nothing saved)", g.MountSheetSyncDB.mapzeroth is None)

# ---------------------------------------------------------------- 6. chat commands
L, g, ns = new_game("zhCN")
g.SlashCmdList["MOUNTSHEETSYNC"]("lang ru")
check("/mss lang ru", ns.CurrentLanguage() == "ruRU" and "Язык: Русский." in chat(g, 1)[0], chat(g, 2))
g.SlashCmdList["MOUNTSHEETSYNC"]("lang Español")
check("/mss lang Español", ns.CurrentLanguage() == "esES" and "Idioma: Español." in chat(g, 1)[0], chat(g, 2))
g.SlashCmdList["MOUNTSHEETSYNC"]("lang klingon")
check("/mss lang unknown: explained with the list", "klingon" in chat(g, 1)[0] and "de (Deutsch)" in chat(g, 1)[0], chat(g, 1))
g.SlashCmdList["MOUNTSHEETSYNC"]("lang auto")
check("/mss lang auto: Chinese client → English", ns.CurrentLanguage() == "enUS" and "Language: English." in chat(g, 1)[0], chat(g, 1))
g.SlashCmdList["MOUNTSHEETSYNC"]("settings")
check("/mss settings opens the settings", g.MountSheetSyncSettingsFrame is not None and g.MountSheetSyncSettingsFrame._shown)
g.MountSheetSync_OnAddonCompartmentClick("MountSheetSync", "RightButton")
check("addon compartment right click toggles the settings", not g.MountSheetSyncSettingsFrame._shown)
check("no Lua errors reported in chat", not any("error:" in t or "ошибка" in t for t in chat(g)), [t for t in chat(g) if "rror" in t])

# glyphs WoW's fonts do not have (the Russian client draws a box): middle dot, arrows
import glob, re
bad = []
for f in sorted(glob.glob(os.path.join(ADDON_DIR, "*.lua"))):
    in_block = False
    for n, line in enumerate(open(f, encoding="utf-8"), 1):
        code = line
        if in_block:
            if "]]" in code:
                in_block = False
            continue
        if code.lstrip().startswith("--[["):
            in_block = "]]" not in code
            continue
        code = re.sub(r"--.*$", "", code) if not re.search(r'"[^"]*--[^"]*"', code) else code
        for ch in "·→←↑↓":
            if ch in code:
                bad.append("%s:%d %r" % (os.path.basename(f), n, ch))
# words for a player without a Google sheet
LIST = ns.LIST_WORDS
codes = ["enUS", "ruRU", "deDE", "frFR", "esES", "ptBR"]
list_keys = {c: set(LIST[c].keys()) for c in codes}
check("list words: every language has the same keys", all(list_keys[c] == list_keys["enUS"] for c in codes), {c: sorted(list_keys["enUS"] ^ list_keys[c]) for c in codes})
base = ns.TRANSLATIONS
check("list words replace existing texts", all(base.enUS[k] is not None for k in list_keys["enUS"]))
fmt = lambda t: re.findall(r"%[sd]", str(t))
check("list words: the same %s / %d in the same order as the texts they replace",
      all(fmt(LIST[c][k]) == fmt(base.enUS[k]) for c in codes for k in list_keys["enUS"]), [(c, k) for c in codes for k in list_keys["enUS"] if fmt(LIST[c][k]) != fmt(base.enUS[k])][:5])
SHEET_WORD = re.compile(r"таблиц|sheet|Tabelle|tableau|tabla|planilha|Google", re.I)
check("list words don't name a sheet", not [(c, k) for c in codes for k in list_keys["enUS"] if SHEET_WORD.search(str(LIST[c][k]).replace("Mount Sheet Sync", ""))], [(c, k) for c in codes for k in list_keys["enUS"] if SHEET_WORD.search(str(LIST[c][k]).replace("Mount Sheet Sync", ""))][:5])
check("no glyphs missing from WoW fonts in list words", not [(c, k) for c in codes for k in list_keys["enUS"] if re.search("[·→←↑↓]", str(LIST[c][k]))])
# the difficulty column is narrow: a longer word is cut to "Torgh..."
long_diff = [(c, k, str(ns.TRANSLATIONS[c][k])) for c in ["enUS", "ruRU", "deDE", "frFR", "esES", "ptBR"]
             for k in ("diff_ach", "diff_any", "diff_removed", "diff_torghast") if len(str(ns.TRANSLATIONS[c][k])) > 5]
check("short difficulty words fit the column: at most 5 letters", not long_diff, long_diff)
check("no glyphs missing from WoW fonts in addon texts (· → … shown as a box)", not bad, bad[:5])

# every row the window shows has its boss and place in the six languages (reward rows go by their achievement; their place is not shown)
import json as _json
_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
_names = _json.load(open(os.path.join(_root, "tools", "catalog", "game_names.json"), encoding="utf-8"))
_catalog = _json.load(open(os.path.join(_root, "program", "catalog.json"), encoding="utf-8"))
_reward = re.compile(r"achievement|достижен|ahead of the curve|mythic\+|challenge mode|timed / chest", re.I)
_four, _gaps = ("de", "fr", "es", "pt"), []
for _sheet in _catalog["sheets"]:
    _cols = _sheet["columns"]
    _col = lambda name: _cols.index(name) if name in _cols else -1
    _boss = next(i for i, c in enumerate(_cols) if c.startswith("Босс"))
    for _r in _sheet["rows"]:
        if _col("Тип источника") >= 0 and _reward.search(str(_r[_col("Тип источника")] or "")):
            continue
        _j = re.findall(r"\d+", str(_r[_col("ID в журнале")] or ""))
        _m = re.findall(r"\d+", str(_r[_col("ID места")] or ""))
        _b = _names["texts"].get(_r[_boss]) or (len(_j) == 1 and _names["bosses"].get(_j[0])) or {}
        _p = (len(_m) == 1 and _names["places"].get(_m[0])) or _names["texts"].get(_r[1]) or {}
        if not all(_b.get(l) for l in _four):
            _gaps.append("boss: " + _r[_boss])
        if not all(_p.get(l) for l in _four + ("ru",)):
            _gaps.append("place: " + _r[1])
check("every row of the window has its boss and place translated into all six languages", not _gaps, sorted(set(_gaps)))
_rare = [s for s in _catalog["sheets"] if s["key"] == "rare"][0]
_rid = _rare["columns"].index("ID в игре")
_untranslated = [_r[2] for _r in _rare["rows"] if len(_names["mounts"].get(str(_r[_rid]), {})) != 6]
check("every rare monster's mount has its name in all six languages (game_names.json)", len(_rare["rows"]) > 40 and not _untranslated, _untranslated)
check("every reward achievement has its requirement in all six languages", all(len(_names["conditions"].get(str(r["id"]), {})) == 6 for r in _names["rewards"].values()),
      [r["id"] for r in _names["rewards"].values() if len(_names["conditions"].get(str(r["id"]), {})) != 6])

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
raise SystemExit(0 if passed == len(checks) else 1)
