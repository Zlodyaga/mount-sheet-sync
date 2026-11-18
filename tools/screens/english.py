"""The addon's pictures in English, for the CurseForge gallery: test_addon_left.py dumps the window's layouts with the
addon in English (MSS_SCREEN_LANG=enUS: its own texts, plus the names of bosses, mounts and places as the program sends
them), then render.py draws them with the fixture's characters in Latin letters.

    python3 tools/screens/english.py     -> docs/screens/en/addon-*.png

Needs what test_addon_left.py needs (node, mono for the fixture) and playwright. The test's checks are about the Russian
window, so in this mode some of them fail — only the layouts matter here.
"""
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import names_en   # noqa: E402
import render     # noqa: E402

LAYOUTS = os.path.join(HERE, "layouts-en")
OUT = os.path.join(ROOT, "docs", "screens", "en")


def main():
    os.makedirs(LAYOUTS, exist_ok=True)
    env = dict(os.environ, MSS_SCREEN_LANG="enUS", PYTHONIOENCODING="utf-8")
    test = os.path.join(ROOT, "tools", "tests", "test_addon_left.py")
    subprocess.run([sys.executable, "-X", "utf8", test, "-", os.path.join(LAYOUTS, "layout_left.json")], env=env,
                   stdout=subprocess.DEVNULL)
    words = json.load(open(os.path.join(LAYOUTS, "names.json"), encoding="utf-8"))   # what the test gave the addon
    words.update(names_en.EXTRA["en"])
    fix = names_en.fix_text(words)
    render.main(layouts=LAYOUTS, out=OUT, fix_text=fix, names=words)
    # what is still in Cyrillic on the pictures (should be nothing but stand-ins the fixture has no English for)
    left = set()
    for name in [n for n in os.listdir(LAYOUTS) if n.startswith("layout_")]:
        text = fix(open(os.path.join(LAYOUTS, name), encoding="utf-8").read())
        left.update(re.findall(r'"text": "([^"]*[А-Яа-яЁё][^"]*)"', text))
    print("still Cyrillic:", len(left))
    for t in sorted(left)[:40]:
        print("  ", t)


if __name__ == "__main__":
    sys.exit(main())
