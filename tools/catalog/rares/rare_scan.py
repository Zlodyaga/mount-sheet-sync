"""List Rarity's open-world NPC mounts (not dungeon/raid bosses), with creature classification from the game's tables."""
import csv, io, json, os, re, sys

D = sys.argv[1]          # rarity folder (untrusted data: parsed as text only)
WAGO = sys.argv[2]       # mss-wago cache
CATALOG = sys.argv[3]

const = open(os.path.join(D, "SharedConstants.lua"), encoding="utf-8").read()
m = re.search(r"C\.UIMAPIDS = \{(.*?)\n\}", const, re.S)
UIMAP = {k: int(v) for k, v in re.findall(r"(\w+)\s*=\s*(\d+)", m.group(1))}

creatures = {}
with open(os.path.join(WAGO, "Creature-enUS.csv"), encoding="utf-8") as f:
    for r in csv.DictReader(io.StringIO(f.read())):
        creatures[int(r["ID"])] = (r["Name_lang"], int(r["Classification"] or 0))

mounts_by_spell = {}
with open(os.path.join(WAGO, "Mount-enUS.csv"), encoding="utf-8") as f:
    for r in csv.DictReader(io.StringIO(f.read())):
        mounts_by_spell[int(r["SourceSpellID"])] = (int(r["ID"]), r["Name_lang"])

cat = json.load(open(CATALOG, encoding="utf-8"))
have = set()
for s in cat["sheets"]:
    gi = s["columns"].index("ID в игре")
    for row in s["rows"]:
        for x in re.findall(r"\d+", str(row[gi])):
            have.add(int(x))


def entries(text):
    # top-level entries: ["Name"] = { ... }, (balanced braces)
    i = 0
    while True:
        m = re.compile(r'\n\t\["([^"]+)"\] = \{').search(text, i)
        if not m:
            return
        depth, j = 0, m.end() - 1
        while True:
            if text[j] == "{":
                depth += 1
            elif text[j] == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        yield m.group(1), text[m.end():j]
        i = j


out = []
for fn in sorted(os.listdir(D)):
    if fn in ("SharedConstants.lua", "const_try.lua") or not fn.endswith(".lua"):
        continue
    text = open(os.path.join(D, fn), encoding="utf-8").read()
    for name, body in entries(text):
        if "DETECTION_METHODS.NPC" not in body:
            continue
        if "encounterName" in body or "instanceDifficulties" in body or re.search(r"\bi = true", body):
            continue   # a boss in a dungeon or raid
        npcs = [int(x) for x in re.findall(r"\d+", (re.search(r"npcs = \{([^}]*)\}", body) or [None, ""])[1])]
        chance = re.search(r"\bchance = (\d+)", body)
        spell = re.search(r"spellId = (\d+)", body)
        quest = re.search(r"questId = \{([^}]*)\}", body)
        coords = re.findall(r"\{ m = CONSTANTS\.UIMAPIDS\.(\w+)(?:, x = ([\d.]+), y = ([\d.]+))?(?:, n = L\[\"([^\"]+)\"\])?", body)
        mount = mounts_by_spell.get(int(spell.group(1))) if spell else None
        cls = sorted({creatures.get(n, ("?", -1))[1] for n in npcs})
        names = sorted({creatures.get(n, ("?",))[0] for n in npcs})[:3]
        out.append({
            "file": fn[:-4], "item": name, "mount": mount, "in_catalog": bool(mount and mount[0] in have),
            "npcs": npcs, "n_npcs": len(npcs), "names": names, "class": cls,
            "chance": int(chance.group(1)) if chance else None, "quest": quest.group(1).strip() if quest else "",
            "coords": [(c[0], UIMAP.get(c[0]), c[1], c[2], c[3]) for c in coords][:3],
            "flags": [k for k in ("requiresAlliance", "requiresHorde", "equalOdds", "groupSize", "sourceText", "blackMarket", "enableCoin", "unique") if k in body],
        })

json.dump(out, open(os.path.join(D, "..", "rare_scan.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
for e in out:
    print("%-17s|%s|%-38s| npc %s x%d %s cls %s | 1/%s | q %s | %s %s" % (
        e["file"][:17], "C" if e["in_catalog"] else " ", (e["mount"] or ("?", e["item"]))[1][:38], e["npcs"][:2], e["n_npcs"],
        e["names"][:1], e["class"], e["chance"], e["quest"][:12],
        [(c[0][:14], c[2], c[3]) for c in e["coords"][:1]], e["flags"]))
print(len(out), "entries")
