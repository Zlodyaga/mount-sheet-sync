"""Readers for the spreadsheet texts (sheet_words.txt, sheet_content.txt, mounts_en.txt) and a writer for
program/catalog.json that keeps its one-row-per-line layout."""
import json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
LANGS = ["en", "de", "fr", "es", "pt"]


def parse(path):
    entries, key = {}, None
    for n, line in enumerate(open(path, encoding="utf-8").read().split("\n"), 1):
        if line.startswith("@ "):
            key = line[2:]
            if key in entries:
                raise SystemExit("%s:%d duplicate key %r" % (path, n, key))
            entries[key] = {}
        elif re.match(r"^[a-z]{2}: ", line):
            if key is None:
                raise SystemExit("%s:%d translation before the first key" % (path, n))
            entries[key][line[:2]] = line[4:]
        elif line.strip():
            raise SystemExit("%s:%d bad line %r" % (path, n, line))
    return entries


def parse_words():
    return parse(os.path.join(HERE, "sheet_words.txt"))


def parse_content():
    return {k: v["en"] for k, v in parse(os.path.join(HERE, "sheet_content.txt")).items()}


def parse_mounts():
    out = {}
    for line in open(os.path.join(HERE, "mounts_en.txt"), encoding="utf-8"):
        line = line.rstrip("\n")
        if not line or line.startswith("#"):
            continue
        i, name = line.split("\t", 1)
        out[int(i)] = name
    return out


def dump_catalog(cat, path):
    d = lambda v: json.dumps(v, ensure_ascii=False)
    lines = ["{", '  "version": %s,' % d(cat["version"]), '  "sheets": [']
    for si, s in enumerate(cat["sheets"]):
        head = {k: v for k, v in s.items() if k != "rows"}
        lines.append("    " + d(head)[:-1] + ', "rows": [')
        for ri, r in enumerate(s["rows"]):
            lines.append("      " + d(r) + ("," if ri + 1 < len(s["rows"]) else ""))
        lines.append("    ]}" + ("," if si + 1 < len(cat["sheets"]) else ""))
    lines.append("  ],")
    lines.append('  "aliases": {')
    items = list(cat["aliases"].items())
    for i, (k, v) in enumerate(items):
        lines.append("    %s: %s" % (d(k), d(v)) + ("," if i + 1 < len(items) else ""))
    lines.append("  }")
    lines.append("}")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
