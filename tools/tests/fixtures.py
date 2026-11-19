"""Fixture files kept in git (status_left.json, wb_left.json, sv_collection.lua): the tests that make them write into a
scratch folder and only compare with the copy in git, so a test run leaves the working tree clean. A copy that no longer
matches (the catalog, Code.gs port or addon changed) fails a check; refresh it on purpose:

    MSS_UPDATE_FIXTURES=1 python tools/tests/test_addon_left.py      # wb_left.json, status_left.json
    MSS_UPDATE_FIXTURES=1 python tools/tests/test_collection.py      # sv_collection.lua

Times differ from run to run and are not compared: unix times in SavedVariables, "generated" in the status reply.
"""
import json
import os
import re
import shutil

import paths

HERE = paths.HERE
UPDATE = os.environ.get("MSS_UPDATE_FIXTURES") == "1"
UNIX_TIME = re.compile(rb"\b1[6-9]\d{8}\b")   # 2020..2033


def scratch(name):
    """Where a test writes a fixture it makes (the scratch folder of the tests, not the repository)."""
    path = paths.work(os.path.join("fixtures", name))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    return path


def _same(name, made, kept):
    if name.endswith(".json"):
        a, b = json.loads(made.decode("utf-8")), json.loads(kept.decode("utf-8"))
        if isinstance(a, dict) and isinstance(b, dict):
            a.pop("generated", None)
            b.pop("generated", None)
        return a == b
    return UNIX_TIME.sub(b"T", made) == UNIX_TIME.sub(b"T", kept)


def compare(name, made_path):
    """(ok, note): does the copy in git (tools/tests/<name>) match the one just made? With MSS_UPDATE_FIXTURES=1 the
    copy in git is replaced first, so the check passes and git shows what changed."""
    kept_path = os.path.join(HERE, name)
    made = open(made_path, "rb").read()
    if UPDATE:
        shutil.copyfile(made_path, kept_path)
        return True, "updated"
    if not os.path.exists(kept_path):
        return False, "missing: run with MSS_UPDATE_FIXTURES=1"
    if _same(name, made, open(kept_path, "rb").read()):
        return True, "up to date"
    return False, "outdated: run with MSS_UPDATE_FIXTURES=1 and commit it (made: %s)" % made_path
