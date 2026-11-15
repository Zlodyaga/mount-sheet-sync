"""The data folder the player chooses (program 2.10.2): the files are copied to the new folder, a pointer in the usual
folder sends the next start there, the old files stay, a folder that already has data is taken as it is, and going back
to the usual folder removes the pointer. The test build takes the «usual folder» from MSS_DEFAULT_DATA, never the real one.

  python3 e2e_data_folder.py <MountSheetSync.exe>
"""
import json
import os
import shutil
import subprocess
import sys
import paths

EXE = os.path.abspath(sys.argv[1])
W = paths.work("e2e_data_folder")
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1200]))


shutil.rmtree(W, ignore_errors=True)
USUAL = os.path.join(W, "usual")
NEW = os.path.join(W, "chosen folder")
OTHER = os.path.join(W, "other")
WOW = os.path.join(W, "WoW")
os.makedirs(os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync"))
os.makedirs(os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables"))
os.makedirs(USUAL)
json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(USUAL, "settings.json"), "w"))
ENV = dict(os.environ, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru", MSS_DEFAULT_DATA=USUAL, MSS_GOOGLE_TEST="http://127.0.0.1:18999")
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    ENV.pop(k, None)


def run(*args):
    p = subprocess.run(["mono", EXE] + list(args), env=ENV, capture_output=True, timeout=300)
    return p.returncode


def log_of(d):
    f = os.path.join(d, "sync.log")
    return open(f, "rb").read().decode("utf-8", "replace") if os.path.exists(f) else ""


POINTER = os.path.join(USUAL, "data-folder.txt")
# any command that only writes to the log shows which folder the program works in
code = run("--storage", "nowhere")
check("without --data the program works in the usual folder", code == 1 and "nowhere" in log_of(USUAL), log_of(USUAL)[-400:])
open(os.path.join(USUAL, "local-data.json"), "w").write("{}")
os.makedirs(os.path.join(USUAL, "backups"), exist_ok=True)
open(os.path.join(USUAL, "backups", "local-data-2026-11-01.json"), "w").write("{}")

code = run("--data-folder", NEW)
check("--data-folder: settings, data and the copies are in the new folder",
      code == 0 and all(os.path.exists(os.path.join(NEW, f)) for f in ("settings.json", "local-data.json", os.path.join("backups", "local-data-2026-11-01.json"))),
      (code, os.listdir(NEW) if os.path.isdir(NEW) else None, log_of(USUAL)[-400:]))
check("…a pointer in the usual folder names it", os.path.exists(POINTER) and os.path.normcase(open(POINTER, encoding="utf-8").read().strip()) == os.path.normcase(NEW),
      os.path.exists(POINTER) and open(POINTER, encoding="utf-8").read())
check("…the old files stay where they were, the pointer itself is not copied",
      os.path.exists(os.path.join(USUAL, "local-data.json")) and not os.path.exists(os.path.join(NEW, "data-folder.txt")))

size = len(log_of(NEW))
before = os.path.getmtime(os.path.join(USUAL, "sync.log"))
code = run("--storage", "nowhere")
check("the next start works in the chosen folder: its log grows, the old one does not",
      code == 1 and len(log_of(NEW)) > size and os.path.getmtime(os.path.join(USUAL, "sync.log")) == before, (code, len(log_of(NEW)), size))
code = run("--data-folder", NEW)
check("the same folder again: nothing to do", code == 0 and "уже в папке" in log_of(NEW)[-300:], log_of(NEW)[-300:])

# a folder that already has the program's data is taken as it is
os.makedirs(OTHER)
json.dump({"wowPath": WOW, "pollSeconds": 7}, open(os.path.join(OTHER, "settings.json"), "w"))
code = run("--data-folder", OTHER)
kept = json.load(open(os.path.join(OTHER, "settings.json"), encoding="utf-8"))
check("a folder that already has the program's data is used as it is, nothing is copied over it",
      code == 0 and kept.get("pollSeconds") == 7 and not os.path.exists(os.path.join(OTHER, "local-data.json")), kept)

code = run("--data-folder", USUAL)
check("back to the usual folder: the pointer is gone", code == 0 and not os.path.exists(POINTER))
shutil.rmtree(NEW)
open(POINTER, "w", encoding="utf-8").write(NEW)
size = len(log_of(USUAL))
code = run("--storage", "nowhere")
check("a pointer to a folder that is gone: the program works in the usual folder", code == 1 and len(log_of(USUAL)) > size and not os.path.exists(NEW), code)

# an option the program does not know must not open its window (a test run with an older build did, in the middle of a game)
size = len(log_of(USUAL))
code = run("--no-such-option")
check("an unknown option: the program says so and exits, no window", code == 2 and "--no-such-option" in log_of(USUAL)[size:], (code, log_of(USUAL)[size:][-200:]))

failed = [nm for nm, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
