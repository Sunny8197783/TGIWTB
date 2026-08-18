"""
클립의 프레임들을 한 장의 PNG 격자로 합쳐 눈으로 검수할 수 있게 한다.

행 = 방향, 열 = 프레임. 방향별 동작이 어긋나는지는 세로로 훑으면 바로 보인다.
(자동 검사 check_sprite_consistency.py 는 '수치'만 보므로, 동작이 자연스러운지는
 결국 이 시트를 눈으로 봐야 판단할 수 있다.)

의존성 없음 — PNG 디코더는 check_sprite_consistency 에서 가져오고 인코더는 zlib 로 직접 쓴다.

사용:
  python tools/contact_sheet.py <출력.png> <프레임폴더의부모> [방향 ...]
  예) python tools/contact_sheet.py out.png art/player/punch south north
      python tools/contact_sheet.py out.png art/player/punch          (있는 방향 전부)
"""
import sys, os, glob, zlib, struct

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from check_sprite_consistency import read_png_rgba, DIRS

# 픽셀아트라 최근접 확대. 104px 원본은 그대로 보면 너무 작다.
ZOOM = 2
# 프레임 사이 구분선(어두운 회색). 배경은 체크무늬 대신 단색 — 흰 티셔츠와 구분되게 남색.
BG = (24, 26, 38, 255)
GRID = (70, 74, 96, 255)


def write_png(path, w, h, px):
    raw = bytearray()
    for y in range(h):
        raw.append(0)                       # 필터 없음
        for x in range(w):
            raw += bytes(px[y * w + x])

    def chunk(typ, data):
        return (struct.pack(">I", len(data)) + typ + data
                + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF))

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 6)))
        f.write(chunk(b"IEND", b""))


def main():
    out, base = sys.argv[1], sys.argv[2]
    dirs = sys.argv[3:] or [d for d in DIRS if os.path.isdir(os.path.join(base, d))]

    rows = []
    for d in dirs:
        files = sorted(glob.glob(os.path.join(base, d, "frame_*.png")))
        if files:
            rows.append((d, files))
    if not rows:
        print(f"프레임 없음: {base}")
        return 1

    cw, ch, _ = read_png_rgba(rows[0][1][0])
    cols = max(len(f) for _, f in rows)
    cell_w, cell_h = cw * ZOOM + 1, ch * ZOOM + 1
    W, H = cols * cell_w + 1, len(rows) * cell_h + 1
    canvas = [BG] * (W * H)

    for x in range(0, W, cell_w):
        for y in range(H):
            canvas[y * W + x] = GRID
    for y in range(0, H, cell_h):
        for x in range(W):
            canvas[y * W + x] = GRID

    for r, (_, files) in enumerate(rows):
        for c, f in enumerate(files):
            fw, fh, px = read_png_rgba(f)
            ox, oy = c * cell_w + 1, r * cell_h + 1
            for y in range(fh * ZOOM):
                for x in range(fw * ZOOM):
                    sr, sg, sb, sa = px[(y // ZOOM) * fw + (x // ZOOM)]
                    if sa <= 16:
                        continue
                    canvas[(oy + y) * W + (ox + x)] = (sr, sg, sb, 255)

    write_png(out, W, H, canvas)
    print(f"{out}  {W}x{H}  행={[d for d, _ in rows]}  열={cols}프레임")
    return 0


if __name__ == "__main__":
    sys.exit(main())
