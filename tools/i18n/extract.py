"""Collect translation keys of the tray program: Tr._("…"), Tr.F("…"), Tr.L("…"), Tr.StartsWith(x, "…").
Prints JSON {"ui": [...], "log": [...]} (ui = all languages, log = English only)."""
import glob, json, os, re, sys
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "program")
LIT = r'"((?:[^"\\]|\\.)*)"'
CALL = re.compile(r'Tr\.(_|F|L)\(\s*' + LIT + r'|Tr\.StartsWith\([^,]+,\s*' + LIT)

def unescape(s):
    return re.sub(r'\\(u[0-9a-fA-F]{4}|.)', lambda m: {'n': '\n', 't': '\t', 'r': '\r', '"': '"', '\\': '\\', '0': '\0'}.get(
        m.group(1), chr(int(m.group(1)[1:], 16)) if m.group(1)[0] == 'u' else m.group(0)), s)

def keys(src=SRC):
    ui, log = {}, {}
    for path in sorted(glob.glob(os.path.join(src, "*.cs"))):
        text = open(path, encoding="utf-8-sig").read()
        for m in CALL.finditer(text):
            line_start = text.rfind("\n", 0, m.start()) + 1
            if text[line_start:m.start()].lstrip().startswith("//"):
                continue
            kind, lit = (m.group(1), m.group(2)) if m.group(1) else ("_", m.group(3))
            k = unescape(lit)
            where = "%s:%d" % (os.path.basename(path), text.count("\n", 0, m.start()) + 1)
            (log if kind == "L" else ui).setdefault(k, where)
    return ui, log

if __name__ == "__main__":
    ui, log = keys()
    only_log = {k: v for k, v in log.items() if k not in ui}
    json.dump({"ui": ui, "log": only_log}, sys.stdout, ensure_ascii=False, indent=1)
