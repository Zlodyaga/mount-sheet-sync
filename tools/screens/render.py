"""Draw the addon's windows for the README and CurseForge from the layouts test_addon_left.py dumps (tools/layout_*.json):
the real frames, sizes, anchors, colours and texts of the addon, with its own fonts and Media textures (render.html).
Not a game screenshot: the characters come from the test fixture, and no Blizzard art is used, so mount icons are stand-ins.

  python3 tools/tests/test_addon_left.py     -> tools/layout_*.json
  python3 tools/screens/render.py            -> docs/screens/addon-*.png
"""
import base64
import glob
import json
import os
import struct
import sys
import zlib

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "docs", "screens")
MEDIA = os.path.join(ROOT, "Media")
SCREENS = [
    ("addon-remaining.png", "layout_left.json"),
    ("addon-all.png", "layout_all.json"),
    ("addon-attempts.png", "layout_attempts.json"),
    ("addon-log.png", "layout_log.json"),
    ("addon-settings.png", "layout_settings.json"),
]
W, H, MARGIN = 1140, 680, 28
FONTS = {"PTSans-Regular": "PT Sans", "PTSans-Bold": "PT Sans Bold"}


def tga_alpha_png(path):
    """The alpha of an uncompressed 32-bit TGA from Media as a PNG, for a CSS mask (the textures are white, the colour comes from the addon)."""
    data = open(path, "rb").read()
    id_len, kind, w, h, bpp, desc = data[0], data[2], *struct.unpack("<HHBB", data[12:18])
    if kind != 2 or bpp != 32:
        return None
    pixels = data[18 + id_len:]
    rows = [pixels[y * w * 4:(y + 1) * w * 4] for y in range(h)]
    if not desc & 0x20:
        rows.reverse()
    raw = b"".join(b"\0" + bytes(v for x in range(w) for v in (255, 255, 255, row[x * 4 + 3])) for row in rows)

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body))
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
    return "data:image/png;base64," + base64.b64encode(png).decode()


def font_faces():
    css = []
    for file, family in FONTS.items():
        body = base64.b64encode(open(os.path.join(MEDIA, file + ".ttf"), "rb").read()).decode()
        css.append('@font-face { font-family: "%s"; src: url(data:font/ttf;base64,%s); }' % (family, body))
    return "\n".join(css)


def main():
    os.makedirs(OUT, exist_ok=True)
    masks = {}
    for path in glob.glob(os.path.join(MEDIA, "*.tga")):
        png = tga_alpha_png(path)
        if png:
            masks[os.path.splitext(os.path.basename(path))[0]] = png
    page_html = open(os.path.join(HERE, "render.html"), encoding="utf-8").read().replace("/*FONT_FACES*/", font_faces())
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for name, layout_file in SCREENS:
            layout = json.load(open(os.path.join(ROOT, "tools", layout_file), encoding="utf-8"))
            page = browser.new_page(viewport={"width": W, "height": H}, device_scale_factor=2)
            inject = "<script>window.LAYOUT=%s;window.OPT=%s;window.MASKS=%s;</script>" % (
                json.dumps(layout, ensure_ascii=False), json.dumps({"w": W, "h": H, "fonts": FONTS}), json.dumps(masks))
            page.set_content(page_html.replace("<body>", "<body>" + inject, 1))
            page.evaluate("families => Promise.all(families.map(f => document.fonts.load('13px \"' + f + '\"', 'Яa')))", list(FONTS.values()))
            page.evaluate("window.draw()")
            page.wait_for_timeout(200)
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
