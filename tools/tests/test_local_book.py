"""LocalBook, the data file of the «on this computer» mode, through tools/cs_host/LocalHost.exe: what reaches the disk, a stale copy
in memory, trial runs, requests that make sheets, writers taking turns, a damaged file.

  python3 test_local_book.py
"""
import json
import os
import shutil
import subprocess
import sys
import paths

W = paths.work("local_book")
shutil.rmtree(W, ignore_errors=True)
os.makedirs(W)
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:600]))


p = subprocess.run(["mono", os.path.join(paths.HOSTS, "LocalHost.exe"), W], capture_output=True)
if p.returncode != 0:
    print(p.stdout.decode("utf-8", "replace")[-800:], p.stderr.decode("utf-8", "replace")[-1500:])
r = json.loads(p.stdout.decode("utf-8"))

check("empty data: no sheets, not kept for the next call, nothing on the disk", r["emptySheets"] == 0 and r["emptyReusable"] is False and r["emptyWroteFile"] is False, r)
check("made from the catalog: the copy in memory is good to reuse", r["madeReusable"] is True)
check("…remembers who made it, its language, title and layout", r["creator"] != "" and r["langAfterLoad"] == "ru" and r["titleAfterLoad"] == "Тест" and r["layout"] == "5", r)
check("…reads back in the same shape, tick boxes stay booleans", r["sameShapeAfterLoad"] is True and r["typesKept"] is True, r)
check("an unsaved change: the copy is not reused", r["dirtyNotReusable"] is True)
check("…saved: the writer's copy is fine, the value is on the disk", r["writerReusable"] is True and r["valueSaved"] == "Да", r)
check("…and another copy in memory knows it is stale (What to farm and the sync loop can't overwrite each other)", r["otherCopyStale"] is True)
check("a trial run changes nothing on the disk", r["trialValueOnDisk"] == "Да" and r["trialPropertyOnDisk"] == "" and r["trialNotReusable"] is True, r)
check("properties are saved at once", r["propertySaved"] == "значение", r)
check("a new column is saved; a formula stays an empty, marked cell", r["columnSaved"] == "Новая" and r["formulaCellEmpty"] is True, r)
check("deleting a row is saved, its formula goes with it", r["rowDeleted"] is True and r["formulaGoneWithRow"] is True, r)
check("sheet requests: a new sheet at its index, format requests skipped", r["addedFirst"] == "Новый", r)
check("…a deleted column shifts cells and formulas, values keep their types", r["columnRemoved"] == "B" and r["formulaShifted"] is True and r["boolKept"] is True, r)
check("…a deleted sheet is gone", r["sheetRemoved"] is True)
check("writers take turns: the second waits for the first, the same thread can enter again", r["secondWaited"] is True and r["secondGotIn"] is True and r["sameThreadAgain"] is True, r)
check("a damaged file: read from the backup, kept aside as .damaged", r["healedFromBackup"] is True and r["damagedKept"] is True, r)
check("…and the next write doesn't overwrite the good backup with it", r["backupGood"] is True)
check("no file and no backup: a clear error", "не удалось прочитать" in r["missing"], r["missing"])

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
