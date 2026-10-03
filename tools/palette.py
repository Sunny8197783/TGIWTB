"""캐릭터 시트의 색을 부위별로 센다 — 머리·옷 색 바꾸기 목록(data/player/appearance.json)을 고를 때.

    python tools/palette.py art/characters/hero/rot.png
    python tools/palette.py art/characters/hero/rot.png --mark out.png 170,205,0.25[,0.2]   # 색상 170~205°, 채도 0.25 이상(, 명도 0.2 이상)을 자홍으로
옷 색 바꾸기 규칙(appearance.json 의 accent)과 같은 판정이다 — scripts/Render/Recolor.cs.

머리: 북쪽 그림(뒤통수)의 위쪽 1/3. 몸통: 남쪽 그림의 가운데 띠. 칸 128x128, 세로 = 8방향.
"""
import colorsys
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "mapgen"))
import png  # noqa: E402

CELL = 128
DIRS = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]


def load(path):
    w, h, px = png.read_rgba(path)
    return [[c for p in px[y * w:(y + 1) * w] for c in p] for y in range(h)]


def region(img, d, top, bottom):
    """d 방향 첫 칸에서 실루엣 높이의 top~bottom 비율 띠의 색"""
    y0 = d * CELL
    ys = [y for y in range(y0, y0 + CELL) if any(img[y][x * 4 + 3] > 127 for x in range(CELL))]
    if not ys:
        return Counter()
    lo, hi = ys[0], ys[-1]
    c = Counter()
    for y in range(lo + int((hi - lo) * top), lo + int((hi - lo) * bottom) + 1):
        for x in range(CELL):
            r, g, b, a = img[y][x * 4:x * 4 + 4]
            if a > 127:
                c['#%02x%02x%02x' % (r, g, b)] += 1
    return c


def show(title, c):
    print(f'{title}: {len(c)} colors')
    for hexc, n in c.most_common(24):
        r, g, b = (int(hexc[i:i + 2], 16) for i in (1, 3, 5))
        print(f'  {hexc}  x{n:<4} lum {0.3 * r + 0.59 * g + 0.11 * b:5.1f}')


def in_accent(r, g, b, rule):
    """Recolor.cs 와 같은 판정: 색상 h0~h1° (h0 > h1 이면 0° 를 넘어 감싼다), 채도 s 이상, 명도 v 이상(생략 0)"""
    h0, h1, smin = rule[:3]
    vmin = rule[3] if len(rule) > 3 else 0.0
    h, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    h *= 360
    return sat >= smin and val >= vmin and (h0 <= h <= h1 if h0 <= h1 else (h >= h0 or h <= h1))


def mark(img, out, rule, scale=3):
    """8방향 첫 칸을 가로로 늘어놓고, 규칙에 걸리는 픽셀만 자홍으로 — 옷만 칠해지는지 눈으로 확인"""
    W, H = 8 * CELL * scale, CELL * scale
    px = [(90, 104, 90, 255)] * (W * H)
    for d in range(8):
        for y in range(CELL):
            for x in range(CELL):
                r, g, b, a = img[d * CELL + y][x * 4:x * 4 + 4]
                if a < 128:
                    continue
                c = (255, 0, 255, 255) if in_accent(r, g, b, rule) else (r, g, b, 255)
                for sy in range(scale):
                    row = (y * scale + sy) * W + (d * CELL + x) * scale
                    px[row:row + scale] = [c] * scale
    png.write_rgba(out, W, H, px)


if __name__ == '__main__':
    img = load(sys.argv[1])
    if len(sys.argv) > 3 and sys.argv[2] == '--mark':
        mark(img, sys.argv[3], tuple(float(v) for v in sys.argv[4].split(',')))
        sys.exit()
    show('hair (north, top 35%)', region(img, DIRS.index('north'), 0.0, 0.35))
    show('torso (south, 40-70%)', region(img, DIRS.index('south'), 0.40, 0.70))
    total = Counter()
    for d in range(8):
        total += region(img, d, 0.0, 1.0)
    print(f'whole sheet: {len(total)} colors')
