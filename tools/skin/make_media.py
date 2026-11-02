"""White textures of the addon's own window style (Media/*.tga): colours come from SetVertexColor in Skin.lua.
Run: python tools/skin/make_media.py. Pure Python, 32-bit uncompressed TGA, 32x32 (WoW wants powers of two)."""
import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Media")
SIZE = 32
SAMPLES = 4


def rounded(radius):
    def inside(x, y):
        dx = max(radius - x, 0, x - (SIZE - radius))
        dy = max(radius - y, 0, y - (SIZE - radius))
        return dx * dx + dy * dy <= radius * radius
    return inside


def circle(r):
    return lambda x, y: (x - 16) ** 2 + (y - 16) ** 2 <= r * r


def segment(x, y, a, b, width):
    ax, ay, bx, by = a[0], a[1], b[0], b[1]
    t = ((x - ax) * (bx - ax) + (y - ay) * (by - ay)) / ((bx - ax) ** 2 + (by - ay) ** 2)
    t = max(0.0, min(1.0, t))
    return (x - ax - t * (bx - ax)) ** 2 + (y - ay - t * (by - ay)) ** 2 <= (width / 2) ** 2


def check(x, y):
    return segment(x, y, (7, 17), (13, 23), 4) or segment(x, y, (13, 23), (25, 9), 4)


def close(x, y):
    return segment(x, y, (9, 9), (23, 23), 4) or segment(x, y, (23, 9), (9, 23), 4)


def arrow_down(x, y):
    # a triangle pointing down, top edge at y=9, tip at y=23
    if y < 9 or y > 23:
        return False
    half = 10 * (23 - y) / 14
    return abs(x - 16) <= half


def gear(x, y):
    dx, dy = x - 16, y - 16
    d = math.hypot(dx, dy)
    if d < 5:
        return False
    if d <= 10:
        return True
    if d > 14.5:
        return False
    angle = math.atan2(dy, dx) % (math.pi / 4)
    return abs(angle - math.pi / 8) <= 0.27


SHAPES = {
    "Round3": rounded(3),
    "Round6": rounded(6),
    "Pill": rounded(11),
    "Dot": circle(15),
    "RadioMark": circle(7),
    "CheckMark": check,
    "Close": close,
    "ArrowDown": arrow_down,
    "Gear": gear,
}


def write_tga(path, fn):
    # image descriptor 0x28: 8 alpha bits, origin at the top left
    head = bytes([0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, SIZE, 0, SIZE, 0, 32, 0x28])
    out = bytearray(head)
    n = SAMPLES * SAMPLES
    for py in range(SIZE):
        for px in range(SIZE):
            hit = 0
            for sy in range(SAMPLES):
                for sx in range(SAMPLES):
                    if fn(px + (sx + 0.5) / SAMPLES, py + (sy + 0.5) / SAMPLES):
                        hit += 1
            out += bytes([255, 255, 255, round(255 * hit / n)])
    with open(path, "wb") as f:
        f.write(out)


if __name__ == "__main__":
    for name, fn in SHAPES.items():
        write_tga(os.path.join(OUT, name + ".tga"), fn)
    print("wrote", len(SHAPES), "textures to", OUT)
