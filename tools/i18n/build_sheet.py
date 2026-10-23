"""Build program/sheet-i18n.json — the texts of a new spreadsheet in other languages — and check them.

  sheet_words.txt    sheet names, headers, Yes/No, statuses and other values of the lists, the "Summary" sheet:
                     Russian → en, de, fr, es, pt (all five).
  sheet_content.txt  catalog texts (bosses, tips, skips…): Russian → English. German, French, Spanish and
                     Portuguese spreadsheets get English texts; boss and instance names come from the game
                     (the addon's Encounter Journal dump) when the game speaks the spreadsheet's language.
  mounts_en.txt      English mount names by mountID.

Checks: every Russian text of the catalog (except mount names) and of SheetBuilder.cs has a translation; within one
language no two words translate the same (the program reads a spreadsheet back by these words)."""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from sheet_files import LANGS, parse_words, parse_content, parse_mounts  # noqa: E402

ROOT = os.path.join(HERE, "..", "..")
OUT = os.path.join(ROOT, "program", "sheet-i18n.json")
CATALOG = os.path.join(ROOT, "program", "catalog.json")
BUILDER = os.path.join(ROOT, "program", "SheetBuilder.cs")
LOCALES = {"ru": "ru_RU", "en": "en_US", "de": "de_DE", "fr": "fr_FR", "es": "es_ES", "pt": "pt_BR"}
# Parts of statuses searched inside formulas (SEARCH, COUNTIF "…*"), not values of their own
PATTERNS = ["Недоступ", "Текущий"]
# Catalog-only columns (never written to a spreadsheet) and name prefixes the code looks for
INTERNAL = {"ID босса", "ID в журнале", "ID места", "Босс"}
CYR = re.compile("[А-Яа-яЁё]")


def main():
    words = parse_words()
    content = parse_content()
    mounts = parse_mounts()
    problems = []
    for k, tr in words.items():
        for lang in LANGS:
            if not tr.get(lang):
                problems.append("no %s for word %r" % (lang, k))
    for k in content:
        if k in words:
            problems.append("both a word and content: %r" % k)
    # one language: different words must not read back the same
    for lang in LANGS:
        seen = {}
        for k, tr in words.items():
            if k in PATTERNS:
                continue
            v = tr.get(lang, "").lower()
            if v in seen:
                problems.append("%s: %r and %r are both %r" % (lang, seen[v], k, tr[lang]))
            seen[v] = k
    # patterns: the status must contain the pattern, other statuses must not
    statuses = ["Фармится", "Фармится / нужен контент-группинг", "Текущий контент", "Фармится через Timelost Saddle",
                "Проверь доступность", "Недоступен", "Недоступен / BMAH"]
    for p in PATTERNS:
        for lang in LANGS:
            pv = words[p][lang].lower()
            for s in statuses:
                hit = pv in words[s][lang].lower()
                if hit != s.lower().startswith(p.lower()):
                    problems.append("pattern %r (%s %r) and status %r (%r)" % (p, lang, pv, s, words[s][lang]))
    cat = json.load(open(CATALOG, encoding="utf-8"))
    ids = set()
    for sheet in cat["sheets"]:
        cols = sheet["columns"]
        for c in cols:
            if CYR.search(c) and c not in words and c not in INTERNAL:
                problems.append("catalog column without a translation: %r" % c)
        for r in sheet["rows"]:
            for c, v in zip(cols, r):
                if c == "ID в игре":
                    if v:
                        ids.add(int(v))
                    continue
                if c == "Маунт" or not isinstance(v, str) or not CYR.search(v):
                    continue
                if v not in words and v not in content:
                    problems.append("catalog text without a translation (%s): %r" % (c, v))
    for i in sorted(ids - set(mounts)):
        problems.append("no English name for mount %d" % i)
    src = open(BUILDER, encoding="utf-8").read()
    for line in src.split("\n"):
        if re.search(r"throw |Tr\.|Log\.|^\s*//", line):
            continue
        for lit in re.findall(r'"((?:[^"\\]|\\.)*)"', line):
            inner = re.findall(r'\\"([^"\\]*)\\"', lit)   # a piece of a formula: its "…" strings
            for s in inner:
                s = re.sub(r"^<>|\*$", "", s)
                if CYR.search(s) and s not in words and s not in PATTERNS:
                    problems.append("SheetBuilder.cs formula text without a translation: %r" % s)
            if inner or '\\"' in lit:
                continue
            if CYR.search(lit) and lit not in words and lit not in PATTERNS and lit not in INTERNAL:
                problems.append("SheetBuilder.cs text without a translation: %r" % lit)
    used = set(content)
    if problems:
        print("\n".join(problems))
        raise SystemExit(1)
    data = {
        "locales": LOCALES,
        "words": {k: {lang: words[k][lang] for lang in LANGS} for k in words if k not in PATTERNS},
        "patterns": {k: {lang: words[k][lang] for lang in LANGS} for k in PATTERNS},
        "content": {k: content[k] for k in sorted(used)},
        "mounts": {str(i): mounts[i] for i in sorted(ids)},
    }
    text = json.dumps(data, ensure_ascii=False, indent=1, sort_keys=True) + "\n"
    if "--check" in sys.argv:
        same = os.path.exists(OUT) and open(OUT, encoding="utf-8").read() == text
        print("sheet-i18n.json is " + ("up to date" if same else "OUT OF DATE — run build_sheet.py"))
        raise SystemExit(0 if same else 1)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    print("sheet-i18n.json: %d words x %d languages, %d catalog texts (en), %d mounts; written to %s"
          % (len(data["words"]), len(LANGS), len(data["content"]), len(data["mounts"]), OUT))


main()
