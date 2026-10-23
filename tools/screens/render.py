"""Draw the addon's windows for the README from the layouts test_addon_left.py dumps (tools/layout_*.json):
the real frames, sizes, anchors and texts of the addon, in a modern skin (render.html). Not a game screenshot —
no Blizzard art is used.

  python3 tools/screens/render.py            -> docs/screens/*.png
"""
import json
import os
import sys

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "docs", "screens")
SCREENS = [
    # file, layout, selected tab, buttons drawn as primary
    ("addon-remaining.png", "layout_left.json", "Осталось", ["Отправить в таблицу"]),
    ("addon-log.png", "layout_log.json", "Журнал", ["Отправить в таблицу"]),
    ("addon-settings.png", "layout_settings.json", "", []),
]
W, H, MARGIN = 1000, 760, 36


def main():
    os.makedirs(OUT, exist_ok=True)
    page_html = open(os.path.join(HERE, "render.html"), encoding="utf-8").read()
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for name, layout_file, tab, primary in SCREENS:
            layout = json.load(open(os.path.join(ROOT, "tools", layout_file), encoding="utf-8"))
            page = browser.new_page(viewport={"width": W, "height": H}, device_scale_factor=2)
            inject = "<script>window.LAYOUT=%s;window.OPT=%s;</script>" % (
                json.dumps(layout, ensure_ascii=False), json.dumps({"w": W, "h": H, "tab": tab, "primary": primary}))
            page.set_content(page_html.replace("<body>", "<body>" + inject, 1))
            page.wait_for_timeout(300)
            r = page.evaluate("window.WINDOW_RECT")
            clip = {"x": max(0, r["l"] - MARGIN), "y": max(0, r["t"] - MARGIN),
                    "width": min(W, r["r"] + MARGIN) - max(0, r["l"] - MARGIN),
                    "height": min(H, r["b"] + MARGIN) - max(0, r["t"] - MARGIN)}
            page.screenshot(path=os.path.join(OUT, name), clip=clip)
            print(name, clip)
            page.close()
        browser.close()


if __name__ == "__main__":
    sys.exit(main())
