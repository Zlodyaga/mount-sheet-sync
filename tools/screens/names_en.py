"""Russian > English names for the English screenshots: what the program sends the addon as `names` (SheetData.Names),
taken from program/sheet-i18n.json (bosses, mounts, places, rewards, conditions from the game's tables, and the catalog
texts), and the fixture's made-up characters and realm in Latin letters (they are not game data).
"""
import io
import json
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

# the fixture's characters (tools/tests/addon_env.py, status_left.json) — longer names first, so «Каэлиия» is not «Каэлия»+«и»
PEOPLE = [("Каэлиия", "Kaeliia"), ("Каэлия", "Kaelia"), ("Элтара", "Eltara"), ("Эллин", "Ellin"), ("Эйра", "Eira"),
          ("Мирна", "Mirna"), ("Альт", "Alt"), ("Ревущий фьорд", "Howling Fjord")]


def names(lang="en"):
    data = json.load(io.open(os.path.join(ROOT, "program", "sheet-i18n.json"), encoding="utf-8"))
    out = {}
    for group in (data.get("names") or {}).values():
        if isinstance(group, dict):
            for entry in group.values():
                if isinstance(entry, dict) and entry.get("ru") and entry.get(lang):
                    out[entry["ru"]] = entry[lang]
    if lang == "en":
        out.update({k: v for k, v in (data.get("content") or {}).items() if isinstance(v, str)})
    else:
        out.update((data.get("contentL10n") or {}).get(lang) or {})
    for ru, words in (data.get("words") or {}).items():
        if isinstance(words, dict) and words.get(lang):
            out.setdefault(ru, words[lang])
    # the catalog's own Russian wording («Поводья бронзового дракона») > the game's name by the mount's and boss's IDs
    catalog = json.load(io.open(os.path.join(ROOT, "program", "catalog.json"), encoding="utf-8"))
    by_id = (data.get("names") or {})
    mounts, bosses = by_id.get("mounts") or {}, by_id.get("bosses") or {}
    for sheet in catalog.get("sheets") or []:
        cols = sheet.get("columns") or []
        at = {name: i for i, name in enumerate(cols)}
        for row in sheet.get("rows") or []:
            def cell(name):
                i = at.get(name)
                return row[i] if i is not None and i < len(row) else ""
            m = mounts.get(str(cell("ID в игре"))) or {}
            if cell("Маунт") and m.get(lang):
                out.setdefault(cell("Маунт"), m[lang])
            for key in ("ID в журнале", "ID босса"):
                b = bosses.get(str(cell(key))) or {}
                if cell("Босс / достижение / сундук") and b.get(lang):
                    out.setdefault(cell("Босс / достижение / сундук"), b[lang])
                    break
    return out


def mounts_by_id(lang="en"):
    """{ "<mount ID>": "<name>" } from the game's table (sheet-i18n.json)."""
    data = json.load(io.open(os.path.join(ROOT, "program", "sheet-i18n.json"), encoding="utf-8"))
    out = {}
    for k, v in ((data.get("names") or {}).get("mounts") or {}).items():
        if isinstance(v, dict) and v.get(lang):
            out[str(k)] = v[lang]
    for k, v in (data.get("mounts") or {}).items():
        if lang == "en" and isinstance(v, str):
            out.setdefault(str(k), v)
    return out


# difficulties as the test's mock of the game names them (GetDifficultyInfo), and its log's made-up bosses
EXTRA = {"en": {"Эпохальный": "Mythic", "Героический": "Heroic", "Обычный": "Normal", "Путешествие во времени": "Timewalking",
                "Торгаст": "Torghast", "Сын Галлеона": "Son of Galleon", "Галлеон": "Galleon",
                "Король Имирон": "King Ymiron",
                "Мастер оков": "Patrician Cromwell"}}


def fix_text(words):
    """For render.main: the layout's JSON text with the known Russian names in English, longest first, and Latin people."""
    keys = sorted(words, key=len, reverse=True)

    def fix(text):
        for k in keys:
            if k in text:
                text = text.replace(k, words[k])
        return latin_people(text)
    return fix


def latin_people(text):
    for ru, en in PEOPLE:
        text = text.replace(ru, en)
    # the grid's short column names: the first letters of a name, between colour codes («|cffaad372Эйр|r»)
    def short(m):
        part = m.group(2)
        for ru, en in PEOPLE:
            if ru.startswith(part):
                return m.group(1) + en[:len(part)] + m.group(3)
        return m.group(0)
    text = re.sub(r"(\|c[0-9a-fA-F]{8})([А-Яа-яЁё]+)(\|r)", short, text)
    return re.sub(r'("text": ")([А-Яа-яЁё]+)(")', short, text)   # and without colour codes
