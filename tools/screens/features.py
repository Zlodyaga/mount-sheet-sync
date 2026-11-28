"""The "What it does" picture for the CurseForge description: eight cards instead of a wall of text.

Each card is a title, one line and a piece of the real thing: crops of the addon's English pictures (docs/screens/en,
made by english.py), chat lines with the addon's own texts (Locale.enUS.lua), the drop notice and two map sketches
(the game's map is not ours to draw). Fonts are the addon's own (Media).

    python3 tools/screens/features.py     -> docs/screens/en/features.png

Run english.py first when the window changed: the crops come from its pictures. Crop boxes are in their pixels (2x).
"""
import base64
import html
import io
import os
import re
import sys

from PIL import Image
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import render   # noqa: E402  (fonts, the browser)

EN = os.path.join(ROOT, "docs", "screens", "en")
OUT = os.path.join(EN, "features.png")
W = 1200


def locale():
    """Locale.enUS.lua: key = "text" pairs (only plain one-line strings)."""
    src = io.open(os.path.join(ROOT, "Locale.enUS.lua"), encoding="utf-8").read()
    return {k: v.replace('\\"', '"') for k, v in re.findall(r'^\s*(\w+)\s*=\s*"((?:[^"\\]|\\.)*)"\s*,', src, re.M)}


def crop(name, box):
    """A crop of an English window picture as a data: URI."""
    img = Image.open(os.path.join(EN, name)).convert("RGB").crop(box)
    buf = io.BytesIO()
    img.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def icon(mount_id):
    path = os.path.join(HERE, ".cache", "%d.jpg" % mount_id)   # icons.py's cache (game art, not in the repository)
    if not os.path.exists(path):
        return ""
    return "data:image/jpeg;base64," + base64.b64encode(open(path, "rb").read()).decode()


def e(s):
    return html.escape(s, quote=True)


def rgb(c, a=1):
    return "rgba(%d,%d,%d,%s)" % (c[0] * 255, c[1] * 255, c[2] * 255, a)


# Map.lua COLORS: a rare's route and spots
COLORS = [(1.00, 0.30, 0.30), (0.35, 0.65, 1.00), (0.35, 0.90, 0.45), (1.00, 0.82, 0.20), (0.80, 0.45, 1.00), (1.00, 0.55, 0.15)]


def chat(*parts):
    """One chat line: (text, colour) parts after the addon's prefix (MountSheetSync.lua PREFIX)."""
    spans = "".join("<span style='color:%s'>%s</span>" % (c, e(t)) for t, c in parts)
    return "<div class='chat'><span style='color:#4fc3f7'>Mount Sheet Sync:</span> %s</div>" % spans


def chat_block(*lines):
    """Several chat lines in one chat box (as they stand in the game's chat window)."""
    return "<div class='chat'>%s</div>" % "<br>".join(
        "<span style='color:#4fc3f7'>Mount Sheet Sync:</span> " + "".join("<span style='color:%s'>%s</span>" % (c, e(t)) for t, c in parts)
        for parts in lines)


def map_way():
    """The way there: a pinned entrance, the player's arrow and the game's guide line (a sketch)."""
    return """<svg viewBox='0 0 520 170' class='map'>
  <defs><radialGradient id='g1' cx='35%' cy='40%' r='80%'><stop offset='0' stop-color='#4a5a3a'/><stop offset='1' stop-color='#22281c'/></radialGradient></defs>
  <rect width='520' height='170' rx='8' fill='url(#g1)'/>
  <path d='M20 140 C90 120 120 60 210 70 S340 140 420 90 S500 40 515 30' stroke='#6b7a52' stroke-width='14' fill='none' opacity='.55'/>
  <path d='M60 120 C140 110 200 80 300 85 S390 60 430 52' stroke='#ffd200' stroke-width='3' stroke-dasharray='8 7' fill='none'/>
  <g transform='translate(60 120) rotate(-20)'><path d='M0 -12 L8 10 L0 5 L-8 10 Z' fill='#ffd200' stroke='#000' stroke-width='1.5'/></g>
  <g transform='translate(430 52)'><circle r='15' fill='#1a1a1a' stroke='#ffd200' stroke-width='3'/><circle r='5' fill='#ffd200'/>
    <path d='M-8 13 L0 28 L8 13' fill='#ffd200'/></g>
  <text x='402' y='100' fill='#f0e6c8' font-size='15' font-family='PT Sans Bold'>Icecrown Citadel</text>
</svg>"""


def map_routes():
    """Rare routes: one rare's route between its spawns, another's cloud of sightings (Map.lua: line, dots, seen)."""
    line = [(40, 120), (90, 70), (160, 55), (230, 85), (250, 135), (180, 150), (110, 140)]
    pts = " ".join("%d,%d" % p for p in line + [line[0]])
    spawns = "".join("<circle cx='%d' cy='%d' r='7' fill='%s' stroke='#000' stroke-width='2'/>" % (x, y, rgb(COLORS[0]))
                     for x, y in line[::2])
    import random
    rnd = random.Random(7)
    cloud = "".join("<circle cx='%.0f' cy='%.0f' r='3.5' fill='%s'/>" % (390 + rnd.gauss(0, 45), 85 + rnd.gauss(0, 28), rgb(COLORS[1], 0.6))
                    for _ in range(40))
    return """<svg viewBox='0 0 520 170' class='map'>
  <defs><radialGradient id='g2' cx='60%' cy='50%' r='80%'><stop offset='0' stop-color='#3d4c5c'/><stop offset='1' stop-color='#1c232b'/></radialGradient></defs>
  <rect width='520' height='170' rx='8' fill='url(#g2)'/>
  <polyline points='{pts}' fill='none' stroke='{c}' stroke-width='3'/>{spawns}{cloud}
  <circle cx='390' cy='85' r='7' fill='{c2}' stroke='#000' stroke-width='2'/>
  <text x='22' y='26' fill='#f0e6c8' font-size='14' font-family='PT Sans Bold'>Time-Lost Proto-Drake</text>
  <text x='330' y='158' fill='#f0e6c8' font-size='14' font-family='PT Sans Bold'>Huolon (flies)</text>
</svg>""".format(pts=pts, c=rgb(COLORS[0]), spawns=spawns, cloud=cloud, c2=rgb(COLORS[1]))


def alert(L):
    """The drop notice (Alert.lua): the mount struck out, the attempts it took, how many are left."""
    return ("<div class='alert'><img src='%s'><div><div class='aname'><s>Experiment 12-B</s></div>"
            "<div class='aline'>%s</div><div class='aleft'>%s</div></div></div>") % (
        icon(445), e(L["alert_attempts"] % 112), e(L["alert_left"] % "211"))


def cards(L):
    gold, white, mount = "#ffd200", "#ffffff", "#c9a0ff"
    return [
        ("All your alts in one table", "Every boss still worth a run this week - and which characters still need it.",
         "<img class='shot' src='%s'>" % crop("addon-all.png", (982, 318, 2115, 625))),
        ("Attempt counter", "After each kill: which attempt it was, and the chance over all of them.",
         chat(("Experiment 12-B", mount), (" could drop: attempt 37 - chance over all attempts 31%", white)) +
         chat(("Heavenly Onyx Cloud Serpent", mount), (" could drop: attempt 56 - chance over all attempts 43%", white))),
        ("A reminder at the door", "Entering a raid or dungeon - the mounts from your list on this difficulty.",
         chat((L["enter_note"] % (L["enter_item"] % ("Invincible", "The Lich King", "1%", 87)), white)) +
         chat((L["enter_note"] % (L["enter_item"] % ("Mimiron's Head", "Yogg-Saron", "1%", 41)), white))),
        ("The way there", "Ctrl + click a row - the entrance, the rare or the world boss is pinned on the map.", map_way()),
        ("Rare routes on the map", "Where a rare spawns and how it moves; flying ones as a cloud of sightings.", map_routes()),
        ("When it drops", "A notice right in the game - and the Dropped tab keeps the attempt and the chance.", alert(L)),
        ("Your own run times", "Minutes per run, so you can farm by chance per minute.",
         "<img class='shot' src='%s'>" % crop("addon-attempts.png", (1657, 318, 2115, 670))),
        ("Coming from Rarity?", "Copy your attempt counts where Rarity has more - you see the list first.",
         "<img class='btn' src='%s'>" % crop("addon-attempts.png", (1830, 254, 2009, 304)) +
         chat_block([(L["rarity_plan"] % 2, white)], [("  Son of Galleon (Galleon): 33 > 41", white)],
                    [("  Heavenly Onyx Cloud Serpent (Sha of Anger): 55 > 62", white)], [(L["rarity_confirm"], gold)])),
    ]


CSS = """
* { box-sizing: border-box; }
html, body { margin: 0; }
body { width: %dpx; background: #0d0b09; font-family: "PT Sans", sans-serif; color: #e8e2d4; padding: 26px; }
.head { font-family: "PT Sans Bold"; color: #ffd200; font-size: 30px; margin: 0 0 18px 6px; }
.grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.card { background: linear-gradient(180deg, #1b1712, #12100d); border: 1px solid #3a3226; border-radius: 10px; padding: 16px 18px 18px; }
.t { font-family: "PT Sans Bold"; color: #ffd200; font-size: 22px; }
.l { color: #cfc6b4; font-size: 16px; margin: 4px 0 12px; line-height: 1.3; }
.v { height: 190px; display: flex; flex-direction: column; justify-content: center; gap: 8px; overflow: hidden; }
.shot { max-width: 100%%; max-height: 176px; object-fit: contain; align-self: flex-start; border-radius: 6px; }
.btn { height: 40px; align-self: flex-start; border-radius: 6px; }
.map { width: 100%%; height: 170px; }
.chat { background: rgba(0,0,0,.55); padding: 6px 10px; border-radius: 4px; font-size: 16px; text-shadow: 1px 1px 0 #000; line-height: 1.35; }
.alert { display: flex; gap: 14px; align-items: center; background: rgba(10,10,10,.9); border: 2px solid #c9a227; border-radius: 8px;
  padding: 14px 18px; align-self: center; }
.alert img { width: 56px; height: 56px; border-radius: 6px; border: 1px solid #000; }
.aname { font-family: "PT Sans Bold"; font-size: 24px; color: #c9a0ff; }
.aname s { text-decoration-color: #ffd200; text-decoration-thickness: 3px; }
.aline { color: #ffd200; font-size: 18px; } .aleft { color: #b8b0a0; font-size: 15px; }
.foot { margin: 18px 6px 0; color: #b8b0a0; font-size: 17px; }
.foot b { color: #ffd200; font-family: "PT Sans Bold"; font-weight: normal; }
""" % W


def page(L):
    body = "".join("<div class='card'><div class='t'>%s</div><div class='l'>%s</div><div class='v'>%s</div></div>" % (e(t), e(l), v)
                   for t, l, v in cards(L))
    langs = "English - Русский - Deutsch - Français - Español - Português"
    return ("<!doctype html><html><head><meta charset='utf-8'><style>%s\n%s</style></head><body>"
            "<div class='grid'>%s</div>"
            "<div class='foot'><b>Six languages:</b> %s - the addon, the program and the spreadsheet.</div></body></html>") % (
        render.font_faces(), CSS, body, e(langs))


def main():
    L = locale()
    with sync_playwright() as p:
        browser = render.launch_browser(p)
        pg = browser.new_page(viewport={"width": W, "height": 900}, device_scale_factor=2)
        pg.set_content(page(L))
        pg.evaluate("document.fonts.ready")
        pg.wait_for_timeout(300)
        pg.screenshot(path=OUT, full_page=True)
        browser.close()
    print(OUT)


if __name__ == "__main__":
    main()
