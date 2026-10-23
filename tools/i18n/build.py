"""Build tray/src/i18n.json from ui*.txt (all languages) and log.txt (English) and check it against the sources:
every key present, nothing extra, same {n} placeholders, error prefixes translated consistently."""
import glob, json, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from extract import keys
OUT = os.path.join(HERE, "..", "..", "program", "i18n.json")
LANGS = ["en", "de", "fr", "es", "pt"]
PREFIXES = ["нет связи", "внутренняя ошибка", "вход в Google", "не выполнен вход"]

def parse(path):
    entries, key = {}, None
    for n, line in enumerate(open(path, encoding="utf-8").read().split("\n"), 1):
        if line.startswith("@ "):
            key = line[2:].replace("\\n", "\n")
            if key in entries:
                raise SystemExit("%s:%d duplicate key %r" % (path, n, key))
            entries[key] = {}
        elif re.match(r"^[a-z]{2}: ", line):
            entries[key][line[:2]] = line[4:].replace("\\n", "\n")
        elif line.strip():
            raise SystemExit("%s:%d bad line %r" % (path, n, line))
    return entries

def main():
    ui_keys, log_keys = keys()
    log_only = {k: v for k, v in log_keys.items() if k not in ui_keys}
    ui, log = {}, {}
    for p in sorted(glob.glob(os.path.join(HERE, "ui*.txt"))):
        ui.update(parse(p))
    log.update(parse(os.path.join(HERE, "log.txt")))
    problems = []
    ph = re.compile(r"\{\d+\}")
    for k, where in ui_keys.items():
        if k not in ui:
            problems.append("missing UI key (%s): %r" % (where, k))
            continue
        for lang in LANGS:
            if lang not in ui[k]:
                problems.append("no %s for %r" % (lang, k))
    for k, where in log_only.items():
        if k not in log:
            problems.append("missing log key (%s): %r" % (where, k))
    for k in ui:
        if k not in ui_keys:
            problems.append("UI key not in the sources: %r" % k)
    for k in log:
        if k not in log_only:
            problems.append("log key not in the sources (or already a UI key): %r" % k)
    for table in (ui, log):
        for k, tr in table.items():
            for lang, v in tr.items():
                if sorted(ph.findall(v)) != sorted(ph.findall(k)):
                    problems.append("placeholders differ (%s): %r -> %r" % (lang, k, v))
                if v != v.strip() and v.strip() == v.lstrip() and k == k.strip():
                    problems.append("trailing space (%s): %r" % (lang, v))
                if (k[:1] == " ") != (v[:1] == " ") or (k[-1:] == " ") != (v[-1:] == " "):
                    problems.append("edge spaces differ (%s): %r -> %r" % (lang, k, v))
    # errors are recognised by how they start (SyncWorker: Tr.StartsWith); error texts start lowercase
    for p in PREFIXES:
        for lang in LANGS:
            tp = ui[p][lang].lower()
            for k, tr in ui.items():
                if k[:1].islower() and k.lower().startswith(p.lower()) and not tr[lang].lower().startswith(tp):
                    problems.append("prefix %r (%s %r) not kept in %r" % (p, lang, tp, tr[lang]))
                if k[:1].islower() and not k.lower().startswith(p.lower()) and tr[lang].lower().startswith(tp):
                    problems.append("translation would look like a %r error (%s): %r -> %r" % (p, lang, k, tr[lang]))
    if problems:
        print("\n".join(problems))
        raise SystemExit(1)
    data = {lang: {k: ui[k][lang] for k in ui} for lang in LANGS}
    for k in log:
        data["en"][k] = log[k]["en"]
    text = json.dumps(data, ensure_ascii=False, indent=1, sort_keys=True) + "\n"
    if "--check" in sys.argv:
        same = os.path.exists(OUT) and open(OUT, encoding="utf-8").read() == text
        print("i18n.json is " + ("up to date" if same else "OUT OF DATE — run build.py"))
        raise SystemExit(0 if same else 1)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    print("i18n.json: %d UI texts x %d languages, %d log lines (en); written to %s" % (len(ui), len(LANGS), len(log), OUT))

main()
