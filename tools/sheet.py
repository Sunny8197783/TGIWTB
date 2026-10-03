"""여러 PNG 를 한 장에 격자로 모아 확대한다 — 에셋 검수용.

python tools/sheet.py out.png --scale 4 --cols 8 a.png b.png ...
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "mapgen"))
import png  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("files", nargs="+")
    ap.add_argument("--scale", type=int, default=4)
    ap.add_argument("--cols", type=int, default=8)
    ap.add_argument("--bg", default="96,110,96")
    a = ap.parse_args()
    bg = tuple(int(v) for v in a.bg.split(",")) + (255,)
    imgs = [png.read_rgba(f) for f in a.files]
    cw = max(w for w, _, _ in imgs)
    ch = max(h for _, h, _ in imgs)
    cols = min(a.cols, len(imgs))
    rows = (len(imgs) + cols - 1) // cols
    pad = 4
    W = cols * (cw * a.scale + pad) + pad
    H = rows * (ch * a.scale + pad) + pad
    out = [bg] * (W * H)
    for i, (w, h, px) in enumerate(imgs):
        ox = pad + (i % cols) * (cw * a.scale + pad) + (cw - w) * a.scale // 2
        oy = pad + (i // cols) * (ch * a.scale + pad) + (ch - h) * a.scale
        for y in range(h):
            for x in range(w):
                r, g, b, al = px[y * w + x]
                if al < 8:
                    continue
                t = al / 255.0
                c = (int(r * t + bg[0] * (1 - t)), int(g * t + bg[1] * (1 - t)), int(b * t + bg[2] * (1 - t)), 255)
                for sy in range(a.scale):
                    row = (oy + y * a.scale + sy) * W + ox + x * a.scale
                    for sx in range(a.scale):
                        out[row + sx] = c
    png.write_rgba(a.out, W, H, out)
    print(a.out, W, H)


if __name__ == "__main__":
    main()
