"""Real mount icons for the pictures (render.py): the mount's name in a row > its number (the game's tables in
sheet-i18n.json and the catalog) > its spell (wago.tools, table Mount) > the icon's name (Wowhead's tooltip) > the
picture from Blizzard's own icon server. Downloaded once into tools/screens/.cache (not in the repository: it is game art).
"""
import base64
import csv
import io
import json
import os
import re
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CACHE = os.path.join(HERE, ".cache")
UA = {"User-Agent": "Mozilla/5.0 (mount-sheet-sync screenshots)"}


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=30).read()


def name_to_id(extra=None):
    """Every known name of a mount (all six languages, the catalog's wording, the test's) > its number."""
    data = json.load(io.open(os.path.join(ROOT, "program", "sheet-i18n.json"), encoding="utf-8"))
    out = {}
    for mid, names in ((data.get("names") or {}).get("mounts") or {}).items():
        for n in (names or {}).values():
            if isinstance(n, str) and n:
                out.setdefault(n, int(mid))
    for mid, n in (data.get("mounts") or {}).items():
        if isinstance(n, str):
            out.setdefault(n, int(mid))
    catalog = json.load(io.open(os.path.join(ROOT, "program", "catalog.json"), encoding="utf-8"))
    for sheet in catalog.get("sheets") or []:
        at = {c: i for i, c in enumerate(sheet.get("columns") or [])}
        for row in sheet.get("rows") or []:
            mid, name = row[at["ID в игре"]] if "ID в игре" in at else "", row[at["Маунт"]] if "Маунт" in at else ""
            if name and str(mid).isdigit():
                out.setdefault(name, int(mid))
    for ru, en in (extra or {}).items():   # the test's own wording (names.json): the same mount as its English name
        if en in out:
            out.setdefault(ru, out[en])
    return out


def spells():
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, "Mount-enUS.csv")
    if not os.path.exists(path):
        open(path, "wb").write(get("https://wago.tools/db2/Mount/csv?locale=enUS"))
    return {int(r["ID"]): int(r["SourceSpellID"]) for r in csv.DictReader(io.open(path, encoding="utf-8"))
            if r["ID"].isdigit() and r["SourceSpellID"].isdigit()}


def icon_png(mount_id, spell_of):
    """The icon as a data: URI, or None."""
    spell = spell_of.get(mount_id)
    if not spell:
        return None
    os.makedirs(CACHE, exist_ok=True)
    pic = os.path.join(CACHE, "%d.jpg" % mount_id)
    if not os.path.exists(pic):
        try:
            tip = json.loads(get("https://nether.wowhead.com/tooltip/spell/%d" % spell))
            name = re.sub(r"[^a-z0-9_\-]", "", (tip.get("icon") or "").lower())
            if not name:
                return None
            open(pic, "wb").write(get("https://render.worldofwarcraft.com/us/icons/56/%s.jpg" % name))
        except Exception as e:   # no icon: the stand-in letter stays
            print("no icon for mount", mount_id, e)
            return None
    return "data:image/jpeg;base64," + base64.b64encode(open(pic, "rb").read()).decode()


def for_layouts(texts, extra=None):
    """{ row's mount name: data URI } for every purple (mount) name in the layouts' JSON texts."""
    ids, spell_of = name_to_id(extra), spells()
    out = {}
    for text in texts:
        for label in set(re.findall(r"\|cffc9a0ff(.*?)\|r", text)):
            label = re.sub(r"\|T.*?\|t", "", label)
            mid = ids.get(label) or ids.get(re.sub(r"\s*\([^)]*\)$", "", label))   # «… (Cataclysm)»
            if mid and label not in out:
                uri = icon_png(mid, spell_of)
                if uri:
                    out[label] = uri
    return out
