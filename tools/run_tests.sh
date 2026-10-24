#!/usr/bin/env bash
# Runs every test: the addon in a mock WoW (Lua via lupa), the program under mono (test build with -define:TESTS)
# against a mock Google, Code.gs in Node, and the UI walk-throughs in Xvfb. Scratch files: $MSS_TEST_TMP.
#   tools/run_tests.sh            everything
#   tools/run_tests.sh --no-ui    without the Xvfb tests
set -uo pipefail
cd "$(dirname "$0")/.."
export MSS_TEST_TMP=${MSS_TEST_TMP:-${TMPDIR:-/tmp}/mss-tests}
mkdir -p "$MSS_TEST_TMP"
EXE=$MSS_TEST_TMP/mss.exe
bash tools/build_test_exe.sh "$EXE" || exit 1
bash tools/cs_host/build.sh || exit 1
python3 tools/i18n/build.py --check || exit 1
python3 tools/i18n/build_sheet.py --check || exit 1
cd tools/tests
F=fixtures
failed=0
run() {
  local out
  out=$(timeout 900 python3 "$@" 2>&1)
  local code=$?
  echo "$1: $(echo "$out" | tail -1)"
  if [ $code -ne 0 ]; then
    echo "$out" | grep -E '^(FAIL|Traceback|  File|[A-Za-z]+Error)' | head -20
    failed=1
  fi
}
run test_addon2.py
run test_addon_left.py
run test_minimap.py
run test_collection.py
run test_locale.py
run test_gamedata.py
run test_collection_sync.py
run test_layout22.py
run e2e_google.py "$EXE"
run e2e_collection.py "$EXE" "$F/MountSheetSync-2.0-test.exe"
run e2e_chars.py "$EXE" "$F/MountSheetSync-2.1-test.exe"
run e2e_script.py "$EXE"
run e2e_lang.py "$EXE"
run e2e_update.py "$EXE"
if [ "${1:-}" != "--no-ui" ]; then
  run ui_google.py "$EXE"
  run ui_lang.py "$EXE"
  run ui_update.py "$EXE"
fi
exit $failed
