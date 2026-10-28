"""Kill statistics of the game for the catalog's random-drop mounts (attempts before the addon was installed).

    python3 tools/catalog/statistic_ids.py [--write]     # --check: only compare with catalog.json / Statistics.lua

The game keeps per-character kill counts as statistics (Achievements → Statistics → Dungeons & Raids, GetStatistic(id)).
For each catalog row whose mount drops by chance, STATS lists the statistics whose kills were attempts for it: only
the difficulties the row names (no Raid Finder where the mount does not drop there). Rows whose boss has no statistic
of its own (Attumen, Anzu, Skadi, rares, trash, Torghast…) or whose statistic also counts difficulties without the
mount (Magisters' Terrace, Oculus random dungeon, Tazavesh, Mechagon Junkyard) are left out.

IDs and names come from the game data (Achievement table, flag 1 = statistic; wago.tools, build of 2026-10-28).
--write puts them into catalog.json as the catalog-only column "ID статистики" ("5970/5971") and writes the list of
all IDs to Statistics.lua (the addon reads exactly these).
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
CATALOG = os.path.join(ROOT, "program", "catalog.json")
LUA = os.path.join(ROOT, "Statistics.lua")
sys.path.insert(0, os.path.join(ROOT, "tools", "i18n"))
from sheet_files import dump_catalog  # noqa: E402

COLUMN = "ID статистики"

# sheet key → mountID → statistic IDs   (# the statistics' English names)
STATS = {
    "raids": {
        183: [1088],                                  # Kael'thas Sunstrider kills (Tempest Keep)
        246: [1391, 1394],                            # Malygos kills (10 / 25 player)
        247: [1391, 1394],
        287: [1753, 1754, 2870, 3236, 4074, 4075, 4657, 4658],   # Archavon / Emalon / Koralon / Toravon (10, 25)
        349: [1098],                                  # Onyxia kills (Onyxia's Lair)
        304: [2869, 2883],                            # Yogg-Saron kills (Ulduar 10 / 25 player)
        363: [4688],                                  # Victories over the Lich King (Heroic Icecrown 25 player)
        396: [5576, 5577],                            # Al'Akir kills / Heroic kills
        415: [5976, 5977],                            # Ragnaros kills / Heroic kills (Firelands)
        425: [5970, 5971],                            # Alysrazor kills / Heroic kills
        445: [6161, 6162],                            # Ultraxion kills / Heroic kills
        442: [6167, 6168],                            # Deathwing kills / Heroic kills (Dragon Soul)
        444: [6168],                                  # Deathwing Heroic kills
        478: [6797, 6798, 7923, 7924],                # Elegon kills (10/25, Normal/Heroic)
        531: [8149, 8150, 8151, 8152],                # Horridon kills (10/25, Normal/Heroic)
        543: [8169, 8170, 8171, 8172],                # Ji-Kun kills (10/25, Normal/Heroic)
        559: [8637, 8638],                            # Garrosh Hellscream (10/25 Heroic — Mythic counts there)
        613: [9365],                                  # Warlord Blackhand kills (Mythic)
        751: [10252],                                 # Archimonde kills (Mythic Hellfire Citadel)
        791: [10978, 10979, 10980],                   # Gul'dan kills (Normal / Heroic / Mythic)
        633: [10980],                                 # Gul'dan kills (Mythic)
        899: [11894, 11895, 11896],                   # Mistress Sassz'ine kills (Normal / Heroic / Mythic)
        971: [11957, 11958, 11959],                   # Hounds of Sargeras kills (Normal / Heroic / Mythic)
        954: [11986],                                 # Argus the Unmaker kills (Mythic)
        1217: [13372, 13373, 13374],                  # Mekkatorque kills (Normal / Heroic / Mythic)
        1219: [13382],                                # Lady Jaina Proudmoore kills (Mythic)
        1293: [14138],                                # N'Zoth the Corruptor kills (Mythic)
        1471: [15176],                                # Sylvanas Windrunner Kills (Mythic)
        1500: [15145, 15146, 15147],                  # The Nine Kills (Normal / Heroic / Mythic)
        1587: [15467],                                # The Jailer (Mythic)
        1818: [19386],                                # Fyrakk the Blazing kills (Mythic)
        2219: [40295, 40296, 40297, 40298],           # Queen Ansurek kills (all difficulties)
        2223: [40298],                                # Queen Ansurek kills (Mythic)
        2507: [41327, 41328, 41329, 41330],           # Chrome King Gallywix (all difficulties)
        2487: [41330],                                # Chrome King Gallywix (Mythic)
        2569: [41664],                                # Dimensius, the All-Devouring (Mythic)
    },
    "dungeons": {
        69: [1097],                                   # Rivendare kills (Stratholme)
        1040: [12763],                                # Dazar, The First King kills (Mythic King's Rest)
        1053: [12745],                                # Unbound Abomination kills (Mythic The Underrot)
        995: [12752],                                 # Harlan Sweete kills (Mythic Freehold)
        1406: [14404],                                # Nalthor the Rimebinder kills (Mythic)
        2119: [40722],                                # Void Speaker Eirich (Mythic The Stonevault)
        2204: [20484],                                # The Darkness kills (Mythic Darkflame Cleft)
        2817: [61217],                                # Degentrius kills (Mythic Magisters' Terrace)
        2805: [41295],                                # The Restless Heart kills (Mythic Windrunner Spire)
        3058: [62287],                                # Zul'jan kills (Mythic Altar of Fangs)
    },
    "world": {
        473: [6989],                                  # Sha of Anger kills (Kun-Lai Summit)
        515: [6990],                                  # Salyis's Warband kills (Valley of the Four Winds)
        533: [8147],                                  # Oondasta kills
        542: [8146],                                  # Nalak kills
        634: [9279],                                  # Rukhmar kills (Spires of Arak)
    },
}


def all_ids():
    return sorted({i for sheet in STATS.values() for ids in sheet.values() for i in ids})


def lua_text():
    ids = all_ids()
    lines = ["-- Mount Sheet Sync: kill statistics the addon reads for the attempt counter.",
             "-- Generated by tools/catalog/statistic_ids.py from the same list as program/catalog.json — do not edit.",
             "local _, ns = ...", "", "ns.STATISTIC_IDS = {"]
    for i in range(0, len(ids), 10):
        lines.append("  " + ", ".join(str(x) for x in ids[i:i + 10]) + ",")
    lines.append("}")
    return "\r\n".join(lines) + "\r\n"


def main():
    cat = json.load(open(CATALOG, encoding="utf-8"))
    problems = []
    for sheet in cat["sheets"]:
        cols = sheet["columns"]
        if COLUMN not in cols:
            cols.append(COLUMN)
            for r in sheet["rows"]:
                r.append("")
        cid, cs = cols.index("ID в игре"), cols.index(COLUMN)
        want = STATS.get(sheet["key"], {})
        seen = set()
        for r in sheet["rows"]:
            while len(r) < len(cols):
                r.append("")
            mount = r[cid]
            ids = want.get(int(mount)) if isinstance(mount, (int, float)) and mount else None
            value = "/".join(str(x) for x in ids) if ids else ""
            if r[cs] != value:
                problems.append("%s %s: %r → %r" % (sheet["key"], r[2], r[cs], value))
                r[cs] = value
            if ids:
                seen.add(int(mount))
        for mount in sorted(set(want) - seen):
            problems.append("%s: mountID %d is not in the catalog" % (sheet["key"], mount))
    old = open(LUA, encoding="utf-8", newline="").read() if os.path.exists(LUA) else ""
    if old != lua_text():
        problems.append("Statistics.lua differs from the list")
    if "--write" in sys.argv:
        dump_catalog(cat, CATALOG)
        with open(LUA, "w", encoding="utf-8", newline="") as f:
            f.write(lua_text())
        print("written %s and %s (%d statistics)" % (CATALOG, LUA, len(all_ids())))
        return 0
    for p in problems:
        print(p)
    return 1 if problems else 0


sys.exit(main())
