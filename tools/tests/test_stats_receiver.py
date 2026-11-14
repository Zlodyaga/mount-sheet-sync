"""The receiver of anonymous statistics (tools/stats/Code.gs, a Google Apps Script web app) in node, with imitations
of the Google services: what it writes for a good message, what it refuses, what it cleans, and the yearly clean-up.

    python3 test_stats_receiver.py
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:800]))


p = subprocess.run(["node", os.path.join(HERE, "stats_receiver.js")], capture_output=True)
if p.returncode != 0:
    print(p.stderr.decode("utf-8", "replace")[-1500:])
r = json.loads(p.stdout.decode("utf-8"))
row = r["goodRow"]
text = [c.get("text") for c in row]

check("a good message is taken: a header row (frozen) and one row", r["goodOk"] is True and len(r["header"]) == 12 and r["frozen"] == 1, r["header"])
check("…every value is written as text: «2.10.2» must not turn into a date, an all-digit number into a number",
      all("text" in c for c in row), row)
check("…the day it came (no time), the number, versions, languages, mode, range of characters, Mapzeroth, the day of the counters",
      len(text[0]) == 10 and text[1:11] == ["0123456789abcdef0123456789abcdef", "2.10.2", "3.0.7", "11", "ru", "ruRU", "local", "2-5", "1", "2026-11-13"], text)
check("…only the counters the addon has, as whole numbers within limits", json.loads(text[11]) == {"open": 2, "tab_left": 5, "tab_log": 1}, text[11])
check("not JSON, an empty request, no installation number, another format version, an oversized body: refused, nothing written",
      [r["badJson"], r["empty"], r["noId"], r["wrongVersion"], r["tooLong"]] == [False] * 5 and r["rowsAfterBad"] == 2,
      (r["badJson"], r["empty"], r["noId"], r["wrongVersion"], r["tooLong"], r["rowsAfterBad"]))
dirty = [c.get("text", c.get("raw")) for c in r["dirtyRow"]]
check("values that do not fit their pattern are left empty (no formulas, markup or names get into the sheet)",
      r["dirtyOk"] is True and dirty[2:11] == ["", "", "", "", "", "", "", "", ""] and json.loads(dirty[11]) == {} and len(dirty) == 12, dirty)
check("the clean-up removes rows older than a year and keeps the header and the fresh rows",
      r["beforePrune"] == 5 and r["afterPrune"] == 3 and r["headerKept"] is True and r["oldGone"] is True, (r["beforePrune"], r["afterPrune"]))
check("setup run twice leaves one daily clean-up", r["triggers"] == ["prune"], r["triggers"])

failed = [nm for nm, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
