"""Fill the catalog's boss and instance IDs from BigWigs / LittleWigs boss modules.

    python3 tools/catalog/encounter_ids.py <folder with BigWigs*, LittleWigs clones> [--write]

For each catalog row the English boss name (tools/i18n/sheet_content.txt) is looked up among the modules'
BigWigs:NewBoss("<name>", <instance map ID>, <journal encounter ID>) and mod:SetEncounterID(<dungeonEncounterID>).
The catalog gets three columns that never reach the spreadsheet:
  "ID босса"     dungeonEncounterID (ENCOUNTER_END / BOSS_KILL), several joined with "/";
  "ID в журнале" journalEncounterID (Encounter Journal), several joined with "/";
  "ID места"     instance map ID (EJ_GetInstanceByIndex, 11th value).
The program uses them to name bosses and instances in the game language (from the addon's journal dump) and to
recognise kills by encounter ID. OVERRIDES below are IDs seen in real kill records (encounterID + boss name).
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
CATALOG = os.path.join(ROOT, "program", "catalog.json")
sys.path.insert(0, os.path.join(ROOT, "tools", "i18n"))
from sheet_files import parse_content, dump_catalog  # noqa: E402

# World bosses: BigWigs knows their journal IDs but not the encounter IDs; these come from the addon's kill log.
OVERRIDES = {"Sha of Anger": {"enc": [1564]}, "Galleon": {"enc": [1563], "j": [725]}, "Nalak": {"enc": [1571]},
             "Oondasta": {"enc": [1587]}}
# Place → BigWigs folder when the names differ
FOLDERS = {"the eye tempest keep": "theeye", "eye of eternity": "northrend", "the obsidian sanctum": "northrend",
           "onyxias lair": "northrend", "vault of archavon": "northrend", "throne of the four winds": "throne",
           "mogushan vaults": "mogushan", "the nighthold": "nighthold", "antorus the burning throne": "antorus",
           "nyalotha the waking city": "nyalotha", "amirdrassil the dreams hope": "amirdrassil",
           "nerubar palace": "nerubarpalace", "liberation of undermine": "liberationofundermine",
           "magisters terrace bc": "magistersterrace", "magisters terrace midnight": "magistersterrace",
           "return to karazhan": "karazhan", "kings rest": "kingsrest", "the underrot": "underrot",
           "operation mechagon": "mechagon", "the vortex pinnacle": "vortexpinnacle", "the stonecore": "stonecore",
           "tazavesh the veiled market": "tazavesh", "sethekk halls": "sethekkhalls",
           "kun lai summit": "pandaria", "valley of the four winds": "pandaria", "isle of giants": "pandaria",
           "isle of thunder": "pandaria", "spires of arak": "draenor", "voldun": "azeroth", "uldum": "azeroth"}


def norm(s):
    return re.sub(r"[^a-z0-9]+", " ", s.lower().replace("'", "")).strip()


def modules(folder):
    out = []
    for repo in sorted(os.listdir(folder)):
        if not (repo.startswith("BigWigs") or repo == "LittleWigs"):
            continue
        for d, _, files in os.walk(os.path.join(folder, repo)):
            for f in files:
                if not f.endswith(".lua") or "Core" in d.split(os.sep):
                    continue
                t = open(os.path.join(d, f), encoding="utf-8", errors="replace").read()
                m = re.search(r'BigWigs:NewBoss\(\s*"([^"]+)"\s*,\s*(-?\d+)\s*(?:,\s*(-?\d+))?', t)
                if not m:
                    continue
                enc = [int(x) for x in re.findall(r"engageId\s*=\s*(\d+)", t)]
                for s in re.findall(r"SetEncounterID\(([^)\n]*)\)", t):
                    nums = re.findall(r"\d+", s)
                    if nums:
                        enc.append(int(nums[-1]))   # "isClassic and A or B" → B (retail)
                enc = [e for e in enc if e > 0]
                out.append({"name": m.group(1), "map": int(m.group(2)), "j": int(m.group(3) or 0),
                            "enc": enc, "repo": repo, "dir": norm(os.path.basename(d)).replace(" ", "")})
    return out


def find(mods, boss, place, raid):
    key = norm(boss)
    want = FOLDERS.get(norm(place), norm(place).replace(" ", ""))
    cands = [m for m in mods if norm(m["name"]) == key
             or re.fullmatch(re.escape(key) + r" (raid|classic|.*terrace|den|hyjal)", norm(m["name"]))]
    if not cands:
        return None
    def score(m):
        s = 0
        if m["dir"] == want or want.startswith(m["dir"]) or m["dir"].startswith(want):
            s += 4
        if (m["repo"] == "LittleWigs") != raid:
            s += 2
        if m["repo"] != "BigWigs_Classic":
            s += 1
        return s
    cands.sort(key=score, reverse=True)
    return cands[0]


def main():
    folder = sys.argv[1]
    mods = modules(folder)
    content = parse_content()
    cat = json.load(open(CATALOG, encoding="utf-8"))
    for sheet in cat["sheets"]:
        cols = sheet["columns"]
        for extra in ("ID босса", "ID в журнале", "ID места"):
            if extra not in cols:
                cols.append(extra)
                for r in sheet["rows"]:
                    r.append("")
        cb = [i for i, c in enumerate(cols) if c.startswith("Босс")][0]
        cp = 1
        ce, cj, cm = cols.index("ID босса"), cols.index("ID в журнале"), cols.index("ID места")
        for r in sheet["rows"]:
            boss_en = content.get(r[cb], r[cb])
            encs, js, maps = [], [], []
            for part in boss_en.split(" / "):
                m = find(mods, part.strip(), r[cp], sheet["key"] == "raids")
                over = OVERRIDES.get(part.strip(), {})
                if m is None and not over:
                    continue
                e = over.get("enc", m["enc"] if m else [])
                j = over.get("j", [m["j"]] if m and m["j"] > 0 else [])
                encs += [x for x in e if x not in encs]
                js += [x for x in j if x not in js]
                if m and m["map"] > 0 and m["map"] not in maps:
                    maps.append(m["map"])
                print("%-9s %-34s %-34s enc=%s j=%s map=%s %s" % (sheet["key"], r[cp][:34], part[:34], e, j,
                      m and m["map"], m and m["repo"]))
            r[ce] = "/".join(str(x) for x in encs)
            r[cj] = "/".join(str(x) for x in js)
            r[cm] = str(maps[0]) if len(maps) == 1 else ""
        # rows without a boss of their own (achievements, chests): the instance of another row at the same place
        place_map = {}
        for r in sheet["rows"]:
            if r[cm]:
                place_map.setdefault(r[cp], r[cm])
        for r in sheet["rows"]:
            if not r[cm] and r[cp] in place_map:
                r[cm] = place_map[r[cp]]
    if "--write" in sys.argv:
        dump_catalog(cat, CATALOG)
        print("written", CATALOG)


main()
