"""Regression: the 1.x way (own sheet with Code.gs web app) still works in 2.0.
Settings from 1.4 (webAppUrl, no "mode") -> script mode; kills go through the Apps Script mock (Code.gs in Node).

  python3 e2e_script.py <MountSheetSync.exe>
"""
import json, os, shutil, subprocess, sys, time
import os
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("e2e_script")
PORT = 18933
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1200]))


shutil.rmtree(W, ignore_errors=True)
DATA = os.path.join(W, "data"); WOW = os.path.join(W, "WoW")
SV = os.path.join(WOW, "_retail_", "WTF", "Account", "A", "SavedVariables")
os.makedirs(DATA); os.makedirs(SV); os.makedirs(os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync"))
WB = os.path.join(W, "wb.json")
subprocess.run(["python3", os.path.join(HERE, "make_fixture.py"), WB, "real"], check=True)
url = "http://127.0.0.1:%d/macros/s/TEST/exec" % PORT
json.dump({"webAppUrl": url, "secret": "snel1Y-KWGZbBii0vKrBNGfT", "wowPath": WOW}, open(os.path.join(DATA, "settings.json"), "w"))
open(os.path.join(SV, "MountSheetSync.lua"), "w", encoding="utf-8").write(
    'MountSheetSyncDB = {\n["kills"] = {\n{\n["id"] = "K-s1",\n["time"] = %d,\n["char"] = "Мирна",\n["realm"] = "Ревущий фьорд",\n'
    '["boss"] = "Сильвана Ветрокрылая",\n["kind"] = "raid",\n["difficultyID"] = 16,\n["weekEnd"] = %d,\n}, -- [1]\n},\n'
    '["chars"] = {\n["Мирна-Ревущий фьорд"] = {\n["name"] = "Мирна",\n["realm"] = "Ревущий фьорд",\n},\n},\n}\n' % (NOW - 10, NOW + 86400))
mock = subprocess.Popen(["python3", os.path.join(HERE, "mock_server.py"), str(PORT), WB, os.path.join(W, "mock.log")])
time.sleep(0.8)
env = dict(os.environ, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru")
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY"):
    env.pop(k, None)
try:
    p = subprocess.run(["mono", EXE, "--data", DATA, "--once"], env=env, capture_output=True, timeout=120)
    out = (p.stdout + p.stderr).decode("utf-8", "replace")
    check("--once in script mode exits 0", p.returncode == 0, out[-1500:])
    check("talks to the web app", "связь есть" in out, out[-800:])
    wb = json.load(open(WB, encoding="utf-8"))
    mim = [s for s in wb["sheets"] if s["name"] == "Рейды Мирна"][0]["values"]
    row = [r for r in mim if len(r) > 3 and r[3] == "Месть"]
    check("kill marked «Да» through Code.gs", row and row[0][0] == "Да", row)
    st = json.load(open(os.path.join(DATA, "settings.json"), encoding="utf-8-sig"))
    check("1.4 settings stay in script mode", st.get("mode") in (None, "script"), st)
    check("no Google token needed", not os.path.exists(os.path.join(DATA, "google-token.dat")))
finally:
    mock.terminate()
passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
