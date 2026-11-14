"""End-to-end local mode: data in a file on this computer, no Google. Created from the catalog (--create-local), the sync
loop marks kills, counts attempts and separate lockouts, characters change, a damaged file falls back to the backup; a
table from mock_google.py is copied over (--import-local) without counting its kills twice.

  python3 e2e_local.py <MountSheetSync.exe>
"""
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.request
from lupa import lua51
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("e2e_local")
PORT = 18935
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
WEEK_END = NOW + 3 * 86400
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
WOW = os.path.join(W, "WoW")
SV_DIR = os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
ADDON = os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (SV_DIR, ADDON):
    os.makedirs(d)
open(os.path.join(ADDON, "MountSheetSync.toc"), "w").write("## Interface: 120105\n")
ENV = dict(os.environ, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru", MSS_BROWSER_LOG=os.path.join(W, "browser.txt"))
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    ENV.pop(k, None)
# local mode must not need Google: nothing listens here, so any request would fail and show up in the log
ENV["MSS_GOOGLE_TEST"] = "http://127.0.0.1:%d" % (PORT + 1)


def new_data(name):
    d = os.path.join(W, name)
    os.makedirs(d)
    json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(d, "settings.json"), "w"))
    return d


def run(data, *args, env=None):
    # what the program logged during this run: its console output depends on the system's code page
    log = os.path.join(data, "sync.log")
    seen = os.path.getsize(log) if os.path.exists(log) else 0
    p = subprocess.run(["mono", EXE, "--data", data] + list(args), env=env or ENV, capture_output=True, timeout=300)
    text = ""
    if os.path.exists(log):
        with open(log, "rb") as f:
            f.seek(seen)
            text = f.read().decode("utf-8", "replace")
    return p.returncode, text


def lua(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return str(v)
    return '"' + str(v).replace("\\", "\\\\").replace('"', '\\"') + '"'


CHARS = [{"name": n, "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}
         for n in ("Эйра", "Мирна")]


def write_sv(kills=(), chars=CHARS):
    L = ["MountSheetSyncDB = {", '\t["chars"] = {']
    for c in chars:
        L.append("\t\t[%s] = {" % lua(c["name"] + "-" + c["realm"]))
        L += ["\t\t\t[%s] = %s," % (lua(k), lua(v)) for k, v in c.items()]
        L.append("\t\t},")
    L += ["\t},", '\t["collection"] = { ["updated"] = %d, ["journal"] = 10, ["collected"] = 1, ["names"] = { "Пепел Ал\'ара" }, ["ids"] = { 183 }, ["mounts"] = {} },' % (NOW - 3600),
          '\t["kills"] = {']
    for k in kills:
        L.append("\t\t{ " + " ".join("[%s] = %s," % (lua(a), lua(b)) for a, b in k.items()) + " },")
    L += ["\t},", "}"]
    open(os.path.join(SV_DIR, "MountSheetSync.lua"), "w", encoding="utf-8", newline="\r\n").write("\n".join(L) + "\n")


def kill(uid, boss, diff=16, char="Эйра"):
    return {"id": uid, "time": NOW - 60, "char": char, "realm": "Ревущий фьорд", "charKey": "Player-1-A", "source": "encounter",
            "boss": boss, "kind": "raid", "difficultyID": diff, "weekEnd": WEEK_END, "instance": "Святилище Господства"}


def book(data):
    return json.load(open(os.path.join(data, "local-data.json"), encoding="utf-8"))


def sheet(data, name):
    return [s for s in book(data)["sheets"] if s["name"] == name][0]["rows"]


def row_of(rows, boss):
    return [r for r in rows if any(isinstance(v, str) and v.startswith(boss) for v in r)][0]


def column(rows, title):
    return [i for i, v in enumerate(rows[0]) if str(v).lower().startswith(title)][0]


def attempts(data, boss):
    rows = sheet(data, "Рейды")
    return row_of(rows, boss)[column(rows, "попыток")]


def sheet_lua():
    return open(os.path.join(ADDON, "SheetData.lua"), encoding="utf-8").read()


# ---------------------------------------------------------------- a new player: nothing to set up
N = new_data("newplayer")
write_sv(chars=[])
code, out = run(N, "--once")
check("a new player: works before the addon knows anyone, no file yet", code == 0 and not os.path.exists(os.path.join(N, "local-data.json")), out[-600:])
settings = json.load(open(os.path.join(N, "settings.json"), encoding="utf-8-sig"))
check("…no sign-in asked", "Не выполнен вход" not in out and "войдите" not in out, out[-400:])
write_sv([kill("N1", "Сильвана Ветрокрылая")])
code, out = run(N, "--once")
check("the addon knows characters: the data makes itself", code == 0 and os.path.exists(os.path.join(N, "local-data.json")) and "созданы на этом компьютере" in out, out[-600:])
check("…and the kill from the journal is marked at once", "Да" in row_of(sheet(N, "Рейды Эйра"), "Сильвана Ветрокрылая"), row_of(sheet(N, "Рейды Эйра"), "Сильвана Ветрокрылая"))
check("…the list is there for the addon", 'name = "Эйра"' in sheet_lua())
O = new_data("oldplayer")
settings = json.load(open(os.path.join(O, "settings.json"), encoding="utf-8-sig"))
settings["spreadsheetId"] = "abc"
json.dump(settings, open(os.path.join(O, "settings.json"), "w"))
code, out = run(O, "--once")
check("settings with a table and no mode stay on Google, nothing local appears", not os.path.exists(os.path.join(O, "local-data.json")), out[-400:])

# ---------------------------------------------------------------- created here, no Google at all
A = new_data("local")
write_sv()
code, out = run(A, "--create-local")
check("--create-local: created without Google", code == 0 and os.path.exists(os.path.join(A, "local-data.json")), out[-800:])
settings = json.load(open(os.path.join(A, "settings.json"), encoding="utf-8-sig"))
check("…and switched to it", settings.get("mode") == "local", settings.get("mode"))
b = book(A)
names = [s["name"] for s in b["sheets"]]
check("the same sheets as a new table", all(n in names for n in ("Рейды", "Рейды Эйра", "Рейды Мирна", "Подземелья", "Подземелья Эйра", "Мировые боссы", "Сводка")), names)
check("layout is current, so the sync loop has nothing to upgrade", b["properties"].get("layout") == "6", b["properties"])
rows = sheet(A, "Рейды Эйра")
check("character sheet: «Девятка» row from the catalog, with «Фармлю»", any("Девятка" in r for r in rows) and "Фармлю" in rows[0], rows[0])
check("no default sheet and no empty rows", all(s["rows"] and all(len(r) == len(s["rows"][0]) for r in s["rows"]) for s in b["sheets"]))
code, out = run(A, "--create-local")
check("a second --create-local replaces nothing", code == 1 and "уже есть" in out, out[-400:])

code, out = run(A, "--test")
check("--test works", code == 0, out[-500:])
code, out = run(A, "--once")
check("--once: list made, no upgrade, Google never asked", code == 0 and "Таблица обновлена" not in out and "нет связи" not in out, out[-800:])
check("SheetData.lua for the addon", "Сильвана Ветрокрылая" in sheet_lua() and 'name = "Эйра"' in sheet_lua(), sheet_lua()[:300])

# 2.10.2: anonymous statistics — nothing without a «yes», then one message a day to the receiver, no names in it
import http.server
import threading
STATS = []


class StatsReceiver(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        STATS.append(self.rfile.read(int(self.headers.get("Content-Length") or 0)).decode("utf-8"))
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", "11")
        self.end_headers()
        self.wfile.write(b'{"ok":true}')

    def log_message(self, *args):
        pass


stats_server = http.server.HTTPServer(("127.0.0.1", PORT + 2), StatsReceiver)
threading.Thread(target=stats_server.serve_forever, daemon=True).start()
STATS_ENV = dict(ENV, MSS_STATS_URL="http://127.0.0.1:%d/exec" % (PORT + 2))


def set_stats(choice, install=None, day=None):
    f = os.path.join(A, "settings.json")
    d = json.load(open(f, encoding="utf-8"))
    d.pop("stats", None), d.pop("installId", None)
    if choice:
        d["stats"] = choice
    if install:
        d["installId"] = install
    json.dump(d, open(f, "w", encoding="utf-8"))
    state = os.path.join(A, "stats.json")
    if day and os.path.exists(state):
        st = json.load(open(state, encoding="utf-8"))
        st["day"] = day
        json.dump(st, open(state, "w", encoding="utf-8"))


run(A, "--once", env=STATS_ENV)
check("2.10.2: statistics: the player was not asked yet — nothing is sent", STATS == [], STATS)
set_stats("no")
run(A, "--once", env=STATS_ENV)
check("…the player said no — nothing is sent", STATS == [], STATS)
set_stats("yes", "0123456789abcdef0123456789abcdef")
code, out = run(A, "--once", env=STATS_ENV)
msg = json.loads(STATS[0]) if STATS else {}
check("…the player said yes: one message with the number, the versions, the mode and a range of characters",
      len(STATS) == 1 and msg.get("v") == 1 and msg.get("id") == "0123456789abcdef0123456789abcdef" and msg.get("mode") == "local"
      and re.match(r"^\d+\.\d+\.\d+$", str(msg.get("program"))) and msg.get("chars") in ("1", "2-5", "6-10", "11+") and msg.get("lang") == "ru",
      (out[-300:], STATS[:1]))
check("…no character names or realms in it", STATS and "Эйра" not in STATS[0] and "Ревущий" not in STATS[0] and "\\u042d" not in STATS[0], STATS[:1])
check("…what was sent is kept for «Что отправляется…»", os.path.exists(os.path.join(A, "stats.json"))
      and json.load(open(os.path.join(A, "stats.json"), encoding="utf-8")).get("last", {}).get("id") == msg.get("id"))
run(A, "--once", env=STATS_ENV)
check("…once a day: a second pass the same day sends nothing", len(STATS) == 1, len(STATS))
set_stats("yes", "0123456789abcdef0123456789abcdef", day="2020-01-01")
run(A, "--once", env=STATS_ENV)
check("…the next day it sends again", len(STATS) == 2, len(STATS))
stats_server.shutdown()
stats_server.server_close()   # the port refuses connections from here on
set_stats("yes", "0123456789abcdef0123456789abcdef", day="2020-01-01")
code, out = run(A, "--once", env=STATS_ENV)
check("…a receiver that is down does not break the pass and is not retried the same day",
      code == 0 and json.load(open(os.path.join(A, "stats.json"), encoding="utf-8")).get("day") != "2020-01-01", out[-400:])
set_stats(None)


def sheet_rows():
    lua = lua51.LuaRuntime(unpack_returned_tuples=True)
    lua.execute(sheet_lua())
    sheet = lua.globals().MountSheetSyncSheet
    return {ch.name: [ch["items"][i] for i in range(1, len(ch["items"]) + 1)] for ch in [sheet.chars[k] for k in range(1, len(sheet.chars) + 1)]}


rows_by_char = sheet_rows()
mirna = rows_by_char["Мирна"]
check("every row of the catalog has its mount's ID", all(i.id for i in mirna), [i.mount for i in mirna if not i.id][:5])
PREFIXES = ("Слава герою", "Слава рейдеру", "Мастер ключей", "Мифик ключей", "Легенда ключей", "Герой своего времени", "Притворяйся", "Безупречный мастер")
not_marked = [i.boss for i in mirna if str(i.boss).startswith(PREFIXES) and i.src != "reward"]
check("achievements and season rewards are all marked as not a boss kill", not not_marked, not_marked)
KILLS = ("Малигос", "Альтаирий", "Анзу", "Сильвана Ветрокрылая", "Сартарион", "Ловчий Аттумен", "Камнедроб", "Мешок Каши", "Король Дазар")
wrongly = [i.boss for i in mirna if str(i.boss).startswith(KILLS) and i.src == "reward"]
check("boss drops are not", not wrongly and any(str(i.boss).startswith("Малигос") for i in mirna), wrongly)
no_place = [str(i.boss) for i in mirna if i.src != "reward" and i.kind in ("raid", "dungeon") and not i.je and not i.im]
check("boss rows without a place in the guide: only those the guide doesn't have (Vault of Archavon, Torghast, any Timewalking)", len(no_place) <= 3, no_place)
check("the tray's list counts what can be killed", ("Мирна %d" % len([i for i in mirna if i.src != "reward"])) in out, out[-400:])
check("…achievements are marked as not a boss kill", 'src = "reward"' in sheet_lua() and re.search(r'boss = "Слава герою",.{0,600}?src = "reward"', sheet_lua(), re.S) is not None)
check("…and rows say where to find them in the guide", "je = " in sheet_lua() and "im = " in sheet_lua())
check("…without the arrow the game's fonts can't draw", "\u2192" not in sheet_lua() and "Мифик ключей > Затерянное" in sheet_lua())
check("…and with a mount ID for rows that have no chance too", re.search(r'mount = "Вулканический дракон",.{0,400}?id = \d', sheet_lua(), re.S) is not None or sheet_lua().count("id = ") >= 100, sheet_lua().count("id = "))
program_lua = open(os.path.join(ADDON, "Program.lua"), encoding="utf-8").read()
check("Program.lua: the addon sees the program as connected", "sheet = true" in program_lua.replace('["sheet"]', "sheet"), program_lua[:300])
check("…and knows the data is on this computer, so it won't name a sheet", 'storage = "local"' in program_lua, program_lua[:300])

before = attempts(A, "Сильвана Ветрокрылая")
write_sv([kill("K1", "Сильвана Ветрокрылая")])
code, out = run(A, "--once")
rows = sheet(A, "Рейды Эйра")
check("a kill marks the row of that character", code == 0 and "Да" in row_of(rows, "Сильвана Ветрокрылая"), (out[-500:], row_of(rows, "Сильвана Ветрокрылая")))
check("…not of another one", "Да" not in row_of(sheet(A, "Рейды Мирна"), "Сильвана Ветрокрылая")[:3])
check("…and counts an attempt", attempts(A, "Сильвана Ветрокрылая") == (before or 0) + 1, (before, attempts(A, "Сильвана Ветрокрылая")))
check("the previous version stays as a backup", os.path.exists(os.path.join(A, "local-data.json.bak")))
sent = open(os.path.join(A, "sent-ids-local.txt"), encoding="utf-8").read()
check("sent records have their own list", "K1" in sent, sent[:200])
code, out = run(A, "--once")
check("the same record is not counted again", attempts(A, "Сильвана Ветрокрылая") == (before or 0) + 1)

# ---------------------------------------------------------------- separate lockouts
kills = [kill("K1", "Сильвана Ветрокрылая"), kill("K2", "Девятка", 14)]
write_sv(kills)
code, out = run(A, "--once")
rows = sheet(A, "Рейды Эйра")
parts = column(rows, "n/h/m")
check("Normal kill of a Normal/Heroic/Mythic row: the letter is kept, the row stays open", code == 0 and row_of(rows, "Девятка")[parts] == "N" and "Да" not in row_of(rows, "Девятка"), row_of(rows, "Девятка"))
check("…the list tells the addon", '["sep"]' in sheet_lua() or "sep" in sheet_lua())
kills += [kill("K3", "Девятка", 15), kill("K4", "Девятка", 16)]
write_sv(kills)
code, out = run(A, "--once")
rows = sheet(A, "Рейды Эйра")
check("all three: the row closes", "Да" in row_of(rows, "Девятка"), row_of(rows, "Девятка"))

# ---------------------------------------------------------------- characters and the collection
spec = [{"name": "Эйра", "raids": True, "dungeons": True, "world": True}, {"name": "Новая", "raids": True, "dungeons": False, "world": True}]
path = os.path.join(W, "chars.json")
json.dump(spec, open(path, "w", encoding="utf-8"), ensure_ascii=False)
code, out = run(A, "--set-chars", path)
b = book(A)
names = [s["name"] for s in b["sheets"]]
check("--set-chars: new sheet, the old one gone", code == 0 and "Рейды Новая" in names and "Рейды Мирна" not in names and "Подземелья Новая" not in names, (names, out[-500:]))
world = sheet(A, "Мировые боссы")
check("…world columns follow", "Новая" in world[0] and "Мирна" not in world[0] and "Эйра" in world[0], world[0])
check("…and every world row has the new tick box", all(r[world[0].index("Новая")] is False for r in world[1:] if r[3]), [r[world[0].index("Новая")] for r in world[1:4]])
check("Эйра keeps her marks", "Да" in row_of(sheet(A, "Рейды Эйра"), "Сильвана Ветрокрылая"))
code, out = run(A, "--sync-collection")
check("--sync-collection works", code == 0 and "Сверка с коллекцией" in out, out[-500:])

check("a Russian list carries English names for an addon set to English", '["Сильвана Ветрокрылая"] = "Sylvanas Windrunner"' in sheet_lua(), sheet_lua()[-600:])
check("…German, French, Spanish and Portuguese ones from the game's tables too", all(s in sheet_lua() for s in ('["Сильвана Ветрокрылая"] = "Sylvanas Windläufer"', '["Dragon Soul"] = "Drachenseele"', '["Dragon Soul"] = "Душа Дракона"')), sheet_lua()[-600:])
check("…and a reward row knows its achievement for the click in the addon", 'ach = 2136' in sheet_lua(), sheet_lua()[:300])
check("every reward row goes to the addon's «Achievements» tab, raids too (their reset is «once»), in its table",
      '"Слава рейдеру Души Дракона"' in sheet_lua() and 'group = "raid"' in sheet_lua() and 'group = "mplus"' in sheet_lua() and 'group = "other"' in sheet_lua(), sheet_lua()[-400:])
rewards_lua = sheet_lua()[sheet_lua().find("rewards = {"):]
check("…without rewards the game no longer gives (status «Недоступен»)", "Дань бессмертию" not in rewards_lua and "Завоеватель испытаний" not in rewards_lua, rewards_lua[:300])
check("…past seasons' mounts go by the Timelost Saddle and its achievement of this season", 'via = "saddle"' in rewards_lua and "ach = 63690" in rewards_lua, rewards_lua[:300])
check("achievement requirements come in the six languages, keyed by achievement", sheet_lua().count('["ach:63690"] = "') == 6, sheet_lua().count('["ach:63690"]'))
check("names outside the game's journal are translated too: rares, chests, zones", all(w in sheet_lua() for w in ("Ishak von den Vier Winden", "Ewiger Verderber", "Schrecken der Nacht", "Eregos' Lager")), None)

# ---------------------------------------------------------------- new characters join by themselves
everyone = CHARS + [dict(CHARS[0], name="Тарен")]
write_sv(kills, chars=everyone)
code, out = run(A, "--once")
names = [s["name"] for s in book(A)["sheets"]]
check("a new character without kills joins by themselves, in all groups", code == 0 and "Рейды Тарен" in names and "Подземелья Тарен" in names and "Тарен" in sheet(A, "Мировые боссы")[0], (names, out[-500:]))
check("…the addon's list has them", "Тарен" in sheet_lua(), sheet_lua()[:300])
check("a character the player removed does not come back", "Рейды Мирна" not in names and "Мирна" in book(A)["properties"].get("skippedCharacters", ""), (names, book(A)["properties"]))
everyone = everyone + [dict(CHARS[0], name="Вера")]
kills = kills + [kill("K6", "Тюремщик", char="Вера")]
write_sv(kills, chars=everyone)
code, out = run(A, "--once")
check("the kill of a new character is not lost: the sheet comes first", code == 0 and "Да" in row_of(sheet(A, "Рейды Вера"), "Тюремщик"), out[-500:])
kills = kills + [kill("K7", "Тюремщик", char="Мирна")]
write_sv(kills, chars=everyone)
code, out = run(A, "--once")
check("a removed character can still play: nothing breaks, no sheet appears", code == 0 and "Рейды Мирна" not in [s["name"] for s in book(A)["sheets"]], out[-500:])
spec = [{"name": n, "raids": True, "dungeons": True, "world": True} for n in ("Эйра", "Новая", "Тарен", "Вера", "Мирна")]
json.dump(spec, open(path, "w", encoding="utf-8"), ensure_ascii=False)
code, out = run(A, "--set-chars", path)
check("a tick brings them back and they are no longer skipped", code == 0 and "Рейды Мирна" in [s["name"] for s in book(A)["sheets"]] and "Мирна" not in book(A)["properties"].get("skippedCharacters", ""), (out[-400:], book(A)["properties"]))
# 2.10.1: «add new characters automatically» off — a new character stays out until added by hand
st = json.load(open(os.path.join(A, "settings.json"), encoding="utf-8-sig"))
st["autoAddCharacters"] = False
json.dump(st, open(os.path.join(A, "settings.json"), "w"))
everyone = everyone + [dict(CHARS[0], name="Чужая")]
write_sv(kills, chars=everyone)
code, out = run(A, "--once")
check("2.10.1: auto-add off: a new character is not added", code == 0 and "Рейды Чужая" not in [x["name"] for x in book(A)["sheets"]], out[-400:])
st.pop("autoAddCharacters")
json.dump(st, open(os.path.join(A, "settings.json"), "w"))
everyone = everyone[:-1]
write_sv(kills, chars=everyone)
spec = [s for s in spec if s["name"] != "Тарен"]
json.dump(spec, open(path, "w", encoding="utf-8"), ensure_ascii=False)
code, out = run(A, "--set-chars", path)
code, out = run(A, "--once")
check("removing one more: remembered across runs", code == 0 and "Рейды Тарен" not in [s["name"] for s in book(A)["sheets"]], out[-400:])

# ---------------------------------------------------------------- removed and ticked back: this week's marks come back
n_before = attempts(A, "Сильвана Ветрокрылая")


def tried(boss):
    rows = sheet(A, "Рейды Эйра")
    return row_of(rows, boss)[column(rows, "пытался")]


check("(before) Эйра has her kill marked", tried("Сильвана Ветрокрылая") == "Да", tried("Сильвана Ветрокрылая"))
json.dump([x for x in spec if x["name"] != "Эйра"], open(path, "w", encoding="utf-8"), ensure_ascii=False)
code, out = run(A, "--set-chars", path)
json.dump(spec, open(path, "w", encoding="utf-8"), ensure_ascii=False)
code, out = run(A, "--set-chars", path)
check("ticked back: the sheets are new, without marks", code == 0 and tried("Сильвана Ветрокрылая") != "Да", (tried("Сильвана Ветрокрылая"), out[-300:]))
code, out = run(A, "--once")
check("…the next pass marks this week's kills again", code == 0 and tried("Сильвана Ветрокрылая") == "Да", (tried("Сильвана Ветрокрылая"), out[-500:]))
check("…without counting the attempt twice", attempts(A, "Сильвана Ветрокрылая") == n_before, (n_before, attempts(A, "Сильвана Ветрокрылая")))
check("…and only once: the note is gone", not [f for f in os.listdir(A) if f.endswith(".replay")], os.listdir(A))

# ---------------------------------------------------------------- a damaged file
good = open(os.path.join(A, "local-data.json"), encoding="utf-8").read()
open(os.path.join(A, "local-data.json"), "w", encoding="utf-8").write(good[:len(good) // 2])
write_sv(kills + [kill("K5", "Тюремщик")])
code, out = run(A, "--once")
check("a damaged file: the backup is read and the work goes on", code == 0 and "резервную копию" in out, out[-800:])
check("…the file is whole again", json.loads(open(os.path.join(A, "local-data.json"), encoding="utf-8").read())["format"] == 1)
check("…the damaged one is kept aside", os.path.exists(os.path.join(A, "local-data.json.damaged")))
check("…and the backup is not overwritten by it", json.loads(open(os.path.join(A, "local-data.json.bak"), encoding="utf-8").read())["format"] == 1)

# ---------------------------------------------------------------- another language
E = new_data("english")
write_sv()
code, out = run(E, "--create-local", "--sheet-lang", "en")
check("English: created", code == 0, out[-500:])
check("…sheets are kept in the program's inner form", "Рейды Эйра" in [s["name"] for s in book(E)["sheets"]] and book(E)["lang"] == "en", [s["name"] for s in book(E)["sheets"]][:3])
code, out = run(E, "--once")
check("…the addon gets the English names", code == 0 and 'spreadsheet = "WoW Mounts"' in sheet_lua() and "Sylvanas Windrunner" in sheet_lua(), sheet_lua()[:400])
check("…and Russian ones for an addon set to Russian", '["Sylvanas Windrunner"] = "Сильвана Ветрокрылая"' in sheet_lua(), sheet_lua()[-600:])

# ---------------------------------------------------------------- a table from Google moves here
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
G = new_data("google")
mock = subprocess.Popen([sys.executable, os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock_state.json"), os.path.join(W, "mock.log")])
time.sleep(0.8)
GENV = dict(ENV, MSS_GOOGLE_TEST=BASE)
try:
    write_sv([kill("G1", "Сильвана Ветрокрылая")])
    code, out = run(G, "--import-local", env=GENV)
    check("nothing to move without a table", code == 1 and "переносить нечего" in out, out[-400:])
    proc = subprocess.Popen(["mono", EXE, "--data", G, "--google-login"], env=GENV, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    browser = ENV["MSS_BROWSER_LOG"]
    deadline = time.time() + 60
    while time.time() < deadline and proc.poll() is None:
        if os.path.exists(browser) and open(browser, encoding="utf-8").read().strip():
            OPENER.open(open(browser, encoding="utf-8").read().strip().splitlines()[-1], timeout=20).read()
            break
        time.sleep(0.2)
    proc.communicate(timeout=60)
    code, out = run(G, "--create-sheet", "Маунты", env=GENV)
    check("a table in Google", code == 0, out[-500:])
    code, out = run(G, "--once", env=GENV)
    check("…with one kill in it", code == 0, out[-500:])
    state = lambda: json.loads(OPENER.open(BASE + "/__state", timeout=10).read())
    google_book = list(state()["spreadsheets"].values())[0]
    code, out = run(G, "--import-local", env=GENV)
    check("--import-local: copied and switched", code == 0 and json.load(open(os.path.join(G, "settings.json"), encoding="utf-8-sig")).get("mode") == "local", out[-500:])
    check("the kill came along", "Да" in row_of(sheet(G, "Рейды Эйра"), "Сильвана Ветрокрылая"))
    n = attempts(G, "Сильвана Ветрокрылая")
    check("…and its attempt", n == 1, n)
    code, out = run(G, "--once", env=ENV)   # no Google from here on
    check("already sent records are not counted again, and Google is not needed", code == 0 and attempts(G, "Сильвана Ветрокрылая") == 1, (out[-500:], attempts(G, "Сильвана Ветрокрылая")))
    write_sv([kill("G1", "Сильвана Ветрокрылая"), kill("G2", "Тюремщик")])
    code, out = run(G, "--once", env=ENV)
    check("a new kill stays here", code == 0 and "Да" in row_of(sheet(G, "Рейды Эйра"), "Тюремщик"), out[-400:])
    check("…the table in Google is not touched", list(state()["spreadsheets"].values())[0] == google_book)
    code, out = run(G, "--import-local", env=GENV)
    check("a second copy replaces nothing", code == 1 and "уже есть" in out, out[-400:])
    code, out = run(G, "--import-local", "--yes", env=GENV)
    check("--import-local --yes: the copy replaces the data, the old data is kept aside", code == 0 and os.path.exists(os.path.join(G, "local-data.before-import.json")), out[-400:])
    code, out = run(G, "--once", env=ENV)
    check("…a kill marked only on this computer is marked in the new copy too", code == 0 and "Да" in row_of(sheet(G, "Рейды Эйра"), "Тюремщик") and attempts(G, "Сильвана Ветрокрылая") == 1, out[-400:])
    code, out = run(G, "--storage", "google", env=GENV)
    check("--storage google: back to the table", code == 0 and json.load(open(os.path.join(G, "settings.json"), encoding="utf-8-sig")).get("mode") == "google", out[-400:])
    code, out = run(G, "--storage", "local", env=GENV)
    check("--storage local: and here again, the data is still there", code == 0 and "Да" in row_of(sheet(G, "Рейды Эйра"), "Сильвана Ветрокрылая"), out[-400:])
    code, out = run(G, "--storage", "disk", env=GENV)
    check("an unknown storage is refused", code == 1, out[-300:])
    # one character: the «left» formula is a number, not TRUE/FALSE, or the column would look like another character's tick boxes
    write_sv(chars=CHARS[:1])
    code, out = run(G, "--create-sheet", "Один персонаж", env=GENV)
    one_book = [b for b in state()["spreadsheets"].values() if b.get("title") == "Один персонаж"]
    check("a table for one character", code == 0 and one_book, out[-400:])
    world_sheet = [x for x in one_book[0]["sheets"] if x["title"] == "Мировые боссы"][0]
    formula = [v.get("formulaValue") for v in world_sheet["cells"].values() if "LAMBDA" in str(v.get("formulaValue"))]
    check("…its «left» formula makes a number with one tick box", formula and "N(pa=FALSE)" in formula[0], formula[:1])
finally:
    mock.terminate()

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
