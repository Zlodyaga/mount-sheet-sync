"""C# side of the addon data: SavedVariables parser (LuaData) and GameData (characters, collection).

  python3 test_gamedata.py      (needs tools/cs_host/GameHost.exe)
"""
import json, os, subprocess, sys, tempfile
import os
import paths

HERE = os.path.dirname(os.path.abspath(__file__))
HOST = os.path.join(paths.HOSTS, "GameHost.exe")
TMP = tempfile.mkdtemp(prefix="mss-gd-")
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


def host(*args):
    p = subprocess.run(["mono", HOST] + list(args), capture_output=True)
    return json.loads(p.stdout.decode("utf-8"))


def file(name, data):
    path = os.path.join(TMP, name)
    open(path, "wb").write(data if isinstance(data, bytes) else data.encode("utf-8"))
    return path


# 1. the file test_collection.py wrote (game format, addon 2.1: whole journal)
g = host(os.path.join(HERE, "sv_collection.lua"))
check("file: two characters sorted by name", [c["name"] for c in g["chars"]] == ["Мирна", "Эйра"], g["chars"])
check("file: collected names", g["mounts"] == sorted(["Гнедой конь", "Пепел Ал'ара", "Синий протодракон"]), g["mounts"])
check("file: drops (sourceType 1) by id with source and collected flag", [d["id"] for d in g["drops"]] == [69, 363, 777, 1471]
      and g["drops"][1]["got"] is True and g["drops"][3]["got"] is False and g["drops"][3]["from"].startswith("Добыча: Тюремщик"), g["drops"])
check("file: whole journal with types (vendor mount too)", [d["id"] for d in g["all"]] == [6, 69, 363, 777, 1471]
      and [d for d in g["all"] if d["id"] == 6][0]["type"] == 3, g["all"])
check("file: collected mount ids", g["ids"] == [6, 69, 363], g["ids"])
check("file: journal size and time", g["journal"] == 7 and g["updated"] > 1700000000)
st = {c["name"]: c["stats"] for c in g["chars"]}
check("file: kill statistics per character (addon 2.9)", st == {"Мирна": {"5970": 3}, "Эйра": {"5970": 13, "6168": 1234}}
      and all(c["statsAt"] > 1700000000 for c in g["chars"]), g["chars"])
two = file("two.lua", 'MountSheetSyncDB = { ["chars"] = { ["Эйра-Ревущий фьорд"] = { ["name"] = "Эйра", ["realm"] = "Ревущий фьорд",'
           ' ["seen"] = 9999999999, ["statsAt"] = 500, ["stats"] = { [5970] = 99 } } } }\n')
g2 = host(os.path.join(HERE, "sv_collection.lua"), two)
e = [c for c in g2["chars"] if c["name"] == "Эйра"][0]
check("two files, same character: the newer statistics win (not the newer «seen»)", e["stats"] == {"5970": 13, "6168": 1234}, e)
v20 = file("v20.lua", 'MountSheetSyncDB = {\n["collection"] = {\n["updated"] = 1792657686,\n["journal"] = 9,\n["collected"] = 1,\n'
           '["names"] = { "Пепел Ал\'ара" },\n["ids"] = { 69 },\n'
           '["drops"] = { { ["id"] = 69, ["name"] = "Пепел Ал\'ара", ["from"] = "Добыча: Кель\'тас", ["got"] = true },'
           ' { ["id"] = 363, ["name"] = "Синий протодракон", ["from"] = "Добыча: Скади" } },\n},\n}\n')
g = host(v20)
check("addon 2.0.0 file: drops still read, ids too", [d["id"] for d in g["drops"]] == [69, 363] and g["drops"][0]["got"] is True
      and g["ids"] == [69] and [d["type"] for d in g["all"]] == [1, 1], g)

# 2. Lua syntax corners
tricky = ("\ufeff-- comment\n--[[ block\ncomment ]]\nOther = { 1, 2 }\nMountSheetSyncDB = {\n"
          '  ["s"] = "q\\"uote \\\\ back\\nline \\065\\066 \\r",\n'
          "  ['single'] = 'it''s',\n" if False else
          "\ufeff-- comment\n--[[ block\ncomment ]]\nOther = { 1, 2 }\nMountSheetSyncDB = {\n"
          '  ["s"] = "q\\"uote \\\\ back\\nline \\065\\066",\n'
          '  ["long"] = [==[raw ]] text]==],\n'
          '  ["n"] = { -1.5, 2e3, 0x1F, 7 }, -- [list]\n'
          '  ["sparse"] = { [1] = "a", [3] = "c" };\n'
          '  ["nested"] = { ["x"] = { ["y"] = { true, false, nil, "z" } } },\n'
          '  bare = 5,\n'
          '}\n')
L = host("--lua", file("tricky.lua", tricky))["lua"]
db = L["MountSheetSyncDB"]
check("strings: escapes, \\ddd bytes", db["s"] == 'q"uote \\ back\nline AB', db["s"])
check("long bracket strings", db["long"] == "raw ]] text", db["long"])
check("numbers: negative, exponent, hex", db["n"] == [-1.5, 2000, 31, 7], db["n"])
check("sparse numeric keys stay a table", db["sparse"] == {"1": "a", "3": "c"}, db["sparse"])
check("nested tables, nil kept as a hole", db["nested"]["x"]["y"] == [True, False, None, "z"], db["nested"])
check("bare name keys and several globals", db["bare"] == 5 and L["Other"] == [1, 2])

# 3. addon older than 2.0: characters from the kill journal
old = file("old.lua", 'MountSheetSyncDB = {\n["kills"] = {\n{\n["id"] = "K1",\n["char"] = "Элтара",\n["realm"] = "Гордунни",\n["boss"] = "Галлеон",\n["time"] = 100,\n}, -- [1]\n'
           '{\n["id"] = "K2",\n["char"] = "Элтара",\n["realm"] = "Гордунни",\n["boss"] = "Ша",\n["time"] = 200,\n}, -- [2]\n},\n["version"] = 1,\n}\n')
g = host(old)
check("1.x file: characters from kills (once each)", [(c["name"], c["realm"], c["fromKills"]) for c in g["chars"]] == [("Элтара", "Гордунни", True)], g["chars"])
check("1.x file: no collection yet", g["updated"] == 0 and g["mounts"] == [])

# 4. two WoW accounts
a1 = file("a1.lua", 'MountSheetSyncDB = {\n["chars"] = {\n["Эйра-Ревущий фьорд"] = {\n["name"] = "Эйра",\n["realm"] = "Ревущий фьорд",\n["level"] = 80,\n["seen"] = 10,\n},\n},\n'
          '["collection"] = {\n["updated"] = 50,\n["journal"] = 900,\n["names"] = {\n"Месть", -- [1]\n},\n},\n}\n')
a2 = file("a2.lua", 'MountSheetSyncDB = {\n["chars"] = {\n["Эйра-Ревущий фьорд"] = {\n["name"] = "Эйра",\n["realm"] = "Ревущий фьорд",\n["level"] = 90,\n["seen"] = 20,\n},\n'
          '["Мирна-Гордунни"] = {\n["name"] = "Мирна",\n["realm"] = "Гордунни",\n["seen"] = 5,\n},\n},\n'
          '["kills"] = {\n{\n["id"] = "K9",\n["char"] = "Мирна",\n["realm"] = "Гордунни",\n["boss"] = "x",\n}, -- [1]\n},\n'
          '["collection"] = {\n["updated"] = 70,\n["journal"] = 950,\n["names"] = {\n"Пепел Ал\'ара", -- [1]\n"Месть", -- [2]\n},\n},\n}\n')
broken = file("broken.lua", 'MountSheetSyncDB = {\n["chars"] = {\n')
g = host(a1, broken, a2)
check("two accounts: characters merged, newer data wins", [(c["name"], c["level"]) for c in g["chars"]] == [("Мирна", 0), ("Эйра", 90)], g["chars"])
check("two accounts: collections united", g["mounts"] == sorted(["Месть", "Пепел Ал'ара"]) and g["journal"] == 950 and g["updated"] == 70, g)
check("broken file: reported, the others still read", len(g["problems"]) == 1 and "broken.lua" in g["problems"][0], g["problems"])

# 5. the 1.x record reader sees only kills in a 2.0 file
r = host("--records", os.path.join(HERE, "sv_collection.lua"))["records"]
check("legacy record reader: characters and drops are not records", all("boss" in keys and "char" in keys and "id" in keys for keys in r) and len(r) >= 1, r)

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
