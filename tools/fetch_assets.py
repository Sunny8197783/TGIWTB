"""PixelLab 결과물을 내려받아 art/ 에 놓는다. 목록은 tools/assets.json.

항목 종류:
  {"dest": "art/env/tree_oak_0.png", "object": "<object id>"}          단방향 오브젝트
  {"sheet": "art/characters/hero/run.png", "char": "<id>", "anim": "hero_run_v3b"}   PixelLab 애니메이션 이름
  {"sheet": "art/characters/hero/rot.png", "char": "<id>", "rotations": true}
      망친 방향만 다시 뽑았으면 "redo": {"west": "rg_attack3c"} (그 방향만 다른 애니메이션 이름에서 가져온다)
      다시 뽑아도 푸른 마법 빛이 남으면 "deglow": ["south"] — 그 방향에서 채도 높은 청록·파랑 픽셀을 지운다 (푸른 옷·눈이 없는 캐릭터만)
  {"object": "<id>", "grid": 3, "cells": ["bush_0", "bush_pink_0", ...]}   작은 소품 여럿을 한 장에 격자로 뽑은 것.
      가로·세로 칸 수가 다르면 "cols"/"rows". 이름은 왼쪽 위부터 읽는 순서.
      칸마다 잘라 art/env/<이름>.png 로. 빈 이름("")은 건너뛴다. 한 번 생성으로 소품 9개 — 화풍도 저절로 맞는다.

캐릭터는 PixelLab 이 요청 때마다 만들어 주는 zip(모든 방향·애니메이션, 이름별 폴더)을 받아 푼다.

캐릭터 프레임 원본은 art_src/ (Godot 가 무시) 에 받아 두고, 애니메이션마다 시트 한 장으로 묶는다:
가로 = 프레임, 세로 = 8방향(DIRS 순서), 칸 = CELL x CELL.
PixelLab 은 애니메이션마다 캔버스 크기가 다르다(원화 64, v3 88, 스켈레톤 96). 그대로 쓰면 동작이 바뀔 때
캐릭터가 몇 픽셀씩 튄다. 그래서 원화(rot)의 발밑 가운데를 칸의 ANCHOR 에 두고, 각 애니메이션은
방향마다 첫 프레임을 원화에 겹쳐 가장 잘 맞는 자리에 놓는다(align, 결과는 offsets.json 에 캐시).
없는 방향은 원화로 채운다 — 좌우 뒤집기는 칼 쥔 손이 바뀌어서 쓰지 않는다.

오브젝트는 투명 여백을 잘라 낸다. 단 아래쪽 기준(발/밑동)과 좌우 가운데는 지킨다 —
판을 그림의 아랫변 가운데에 세우기 때문에, 한쪽만 자르면 소품이 옆으로 밀리거나 공중에 뜬다.
"""
import json
import os
import subprocess
import sys
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "mapgen"))
import png  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OBJ = "https://backblaze.pixellab.ai/file/pixellab-characters/objects/b2f2eb25-bc38-463e-a6e5-95708f359131/{id}/rotations/unknown.png"
CHAR = "https://backblaze.pixellab.ai/file/pixellab-characters/b2f2eb25-bc38-463e-a6e5-95708f359131/{char}"
DIRS = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]
SRC = os.path.join(ROOT, "art_src")
CELL = 128
ANCHOR = (64, 116)  # 칸 안에서 발밑 가운데. scripts/Render/CharacterSprite.cs 와 같아야 한다


def download(url, dest):
    # 이 환경의 파이썬(msys)에는 인증서 묶음이 없어 urllib 이 TLS 검증에 실패한다. curl 은 된다.
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    subprocess.run(["curl", "-sfL", "--max-time", "60", "-o", dest, url], check=True)


def despeckle(path, keep=0.04):
    """본체에서 떨어져 떠 있는 작은 조각(흩날리는 꽃잎 등)을 지운다 — 판이 흔들릴 때 허공에 붙은 점처럼 보인다.
    가장 큰 덩어리의 keep 배보다 작은 연결 덩어리만 지운다."""
    w, h, px = png.read_rgba(path)
    seen = [False] * (w * h)
    comps = []
    for start in range(w * h):
        if seen[start] or px[start][3] <= 8:
            continue
        stack, comp = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            comp.append(i)
            x, y = i % w, i // w
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1), (x + 1, y + 1), (x - 1, y - 1), (x + 1, y - 1), (x - 1, y + 1)):
                if 0 <= nx < w and 0 <= ny < h:
                    j = ny * w + nx
                    if not seen[j] and px[j][3] > 8:
                        seen[j] = True
                        stack.append(j)
        comps.append(comp)
    if len(comps) < 2:
        return
    biggest = max(len(c) for c in comps)
    px = list(px)
    removed = 0
    for c in comps:
        if len(c) < biggest * keep:
            for i in c:
                px[i] = (0, 0, 0, 0)
            removed += 1
    if removed:
        png.write_rgba(path, w, h, px)


def trim(path, pad=1):
    w, h, px = png.read_rgba(path)
    rows = [y for y in range(h) if any(px[y * w + x][3] > 8 for x in range(w))]
    cols = [x for x in range(w) if any(px[y * w + x][3] > 8 for y in range(h))]
    if not rows:
        return w, h
    top, bottom = max(0, rows[0] - pad), min(h - 1, rows[-1] + pad)
    side = max(0, min(cols[0], w - 1 - cols[-1]) - pad)
    nw, nh = w - side * 2, bottom - top + 1
    out = [px[(top + y) * w + side + x] for y in range(nh) for x in range(nw)]
    png.write_rgba(path, nw, nh, out)
    return nw, nh


def main():
    manifest = json.load(open(os.path.join(ROOT, "tools", "assets.json"), encoding="utf-8"))
    force = "--force" in sys.argv
    # --only=글자: 경로에 이 글자가 든 항목만 (생성이 덜 끝난 캐릭터를 서 있는 원화로 묶어 버리지 않게)
    only = next((a[7:] for a in sys.argv if a.startswith("--only=")), None)
    for item in manifest:
        if only and only not in item.get("sheet", item.get("dest", "")):
            continue
        if "object" in item and "cells" in item:
            fetch_packed(item, force)
        elif "object" in item:
            dest = os.path.join(ROOT, item["dest"])
            if os.path.exists(dest) and not force:
                continue
            try:
                download(OBJ.format(id=item["object"]), dest)
            except subprocess.CalledProcessError:
                print("pending", item["dest"])  # 아직 생성 중
                continue
            if item.get("despeckle", True):
                despeckle(dest)
            size = trim(dest) if item.get("trim", True) else None
            print("object", item["dest"], size)
        elif "sheet" in item:
            fetch_character(item, force)


ZIP = "https://api.pixellab.ai/mcp/characters/{char}/download"


def fetch_packed(item, force):
    """격자로 모아 뽑은 소품 시트를 칸마다 잘라 낸다. 칸 경계를 살짝 넘은 잎사귀는
    그 덩어리의 중심이 있는 칸으로 보낸다 (연결 요소 단위로 나눔)."""
    names = item["cells"]
    dests = [os.path.join(ROOT, "art", "env", f"{n}.png") for n in names if n]
    if all(os.path.exists(d) for d in dests) and not force:
        return
    sheet = os.path.join(SRC, "packed", f"{item['object']}.png")
    try:
        download(OBJ.format(id=item["object"]), sheet)
    except subprocess.CalledProcessError:
        print("pending", item["object"])
        return
    w, h, px = png.read_rgba(sheet)
    if item.get("clear_bg"):
        # 가끔 시트 전체를 눈밭 같은 판으로 칠해 내보낸다 — 가장자리에서부터 지운다 (외곽선에서 멈춘다)
        w, h, px = clear_bg((w, h, px), item.get("bg_tol", 48))
    cols = item.get("cols", item.get("grid"))
    rows = item.get("rows", item.get("grid"))
    cw, ch = w // cols, h // rows
    seen = [False] * (w * h)
    cells = {}
    for start in range(w * h):
        if seen[start] or px[start][3] <= 8:
            continue
        stack, comp = [start], []
        seen[start] = True
        while stack:
            i = stack.pop()
            comp.append(i)
            x, y = i % w, i // w
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h:
                        j = ny * w + nx
                        if not seen[j] and px[j][3] > 8:
                            seen[j] = True
                            stack.append(j)
        cx = sum(i % w for i in comp) / len(comp)
        cy = sum(i // w for i in comp) / len(comp)
        key = min(int(cy // ch), rows - 1) * cols + min(int(cx // cw), cols - 1)
        cells.setdefault(key, []).extend(comp)
    for k, name in enumerate(names):
        if not name or k not in cells:
            continue
        comp = cells[k]
        xs = [i % w for i in comp]
        ys = [i // w for i in comp]
        x0, x1, y0, y1 = min(xs) - 1, max(xs) + 1, min(ys) - 1, max(ys) + 1
        keep = set(comp)
        ow, oh = x1 - x0 + 1, y1 - y0 + 1
        out = [px[(y0 + y) * w + x0 + x] if ((y0 + y) * w + x0 + x) in keep else (0, 0, 0, 0)
               for y in range(oh) for x in range(ow)]
        dest = os.path.join(ROOT, "art", "env", f"{name}.png")
        png.write_rgba(dest, ow, oh, out)
        print("packed", name, (ow, oh))


def src_dir(item):
    """원본 프레임 폴더: art_src/<캐릭터 폴더>/<시트 이름>/<방향>/<i>.png"""
    sheet = item["sheet"]
    return os.path.join(SRC, os.path.basename(os.path.dirname(sheet)), os.path.splitext(os.path.basename(sheet))[0])


_zips = {}


def character_zip(char, force):
    """캐릭터 zip 을 한 번만 받는다. --force 가 아니면 art_src 에 있는 프레임은 다시 풀지 않는다."""
    if char not in _zips:
        path = os.path.join(SRC, f"{char}.zip")
        try:
            download(ZIP.format(char=char), path)
            _zips[char] = zipfile.ZipFile(path)
        except subprocess.CalledProcessError:
            # 그 캐릭터의 애니메이션을 만드는 중이면 zip 을 안 준다 — 이번엔 건너뛰고 있는 프레임으로만 묶는다
            print("pending (생성 중)", char)
            _zips[char] = None
    return _zips[char]


def fetch_character(item, force):
    # 이미 묶은 시트는 건너뛴다 (프레임을 고쳤으면 --force 또는 시트를 지우고) — 매번 zip 30개를 받고 다시 묶으면 느리다
    if os.path.exists(os.path.join(ROOT, item["sheet"])) and not force:
        return
    folder = src_dir(item)
    z = character_zip(item["char"], force)
    if z is None:
        pack(item)
        return
    names = z.namelist()
    got = 0
    for d in DIRS:
        if item.get("rotations"):
            members = [n for n in names if n.endswith(f"/rotations/{d}.png")]
        else:
            # redo: {"west": "rg_attack3c"} — 망친 방향만 다시 뽑은 애니메이션으로
            prefix = f"/animations/{item.get('redo', {}).get(d, item['anim'])}/{d}/"
            members = sorted(n for n in names if prefix in n)
        for i, m in enumerate(members):
            fdest = os.path.join(folder, d, f"{i}.png")
            if os.path.exists(fdest) and not force:
                continue
            os.makedirs(os.path.dirname(fdest), exist_ok=True)
            with open(fdest, "wb") as f:
                f.write(z.read(m))
            got += 1
    pack(item)
    print("sheet", item["sheet"], got, "new frames")


def frame_count(folder):
    counts = [len([f for f in os.listdir(os.path.join(folder, d)) if f.endswith(".png")])
              for d in DIRS if os.path.isdir(os.path.join(folder, d))]
    return max(counts) if counts else 1


def opaque_rows(w, h, px):
    return [y for y in range(h) if any(px[y * w + x][3] > 127 for x in range(w))]


def align(ref, img, guess, reach=12):
    """img 를 ref(같은 방향 원화) 위에 겹쳤을 때 색이 가장 많이 맞는 (dx, dy).
    PixelLab 은 캔버스 안 캐릭터 위치가 방식마다 달라서(스켈레톤은 가운데가 아님) 그림으로 맞춘다."""
    rw, rh, rpx = ref
    w, h, px = img
    ref_pts = {(x, y): rpx[y * rw + x] for y in range(rh) for x in range(rw) if rpx[y * rw + x][3] > 127}
    pts = [(x, y, px[y * w + x]) for y in range(h) for x in range(w) if px[y * w + x][3] > 127]
    best, best_off = -1, guess
    for dy in range(guess[1] - reach, guess[1] + reach + 1):
        for dx in range(guess[0] - reach, guess[0] + reach + 1):
            score = 0
            for x, y, c in pts:
                r = ref_pts.get((x + dx, y + dy))
                if r and abs(r[0] - c[0]) + abs(r[1] - c[1]) + abs(r[2] - c[2]) < 60:
                    score += 1
            if score > best:
                best, best_off = score, (dx, dy)
    return best_off


def clear_bg(img, tol=48):
    """PixelLab v3 가 가끔 칸 전체를 회색·남색 판으로 채워 내보낸다 — 네 귀퉁이가 불투명하면
    가장자리에서부터 그 색과 비슷한 픽셀을 지운다 (외곽선에서 멈춘다)."""
    w, h, px = img
    # 판은 캔버스보다 작은 사각형일 수 있다 — 불투명 영역의 네 귀퉁이를 본다
    xs = [x for y in range(h) for x in range(w) if px[y * w + x][3] > 127]
    ys = [y for y in range(h) for x in range(w) if px[y * w + x][3] > 127]
    if not xs:
        return img
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    corners = [px[y0 * w + x0], px[y0 * w + x1], px[y1 * w + x0], px[y1 * w + x1]]
    if any(c[3] < 128 for c in corners):
        return img
    seed = tuple(sum(c[i] for c in corners) // 4 for i in range(3))
    near = lambda c: c[3] > 127 and abs(c[0] - seed[0]) + abs(c[1] - seed[1]) + abs(c[2] - seed[2]) < tol
    px = list(px)
    stack = [(x, y) for x in range(x0, x1 + 1) for y in (y0, y1)] + [(x, y) for y in range(y0, y1 + 1) for x in (x0, x1)]
    while stack:
        x, y = stack.pop()
        i = y * w + x
        if not near(px[i]):
            continue
        px[i] = (0, 0, 0, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h:
                stack.append((nx, ny))
    return w, h, px


def deglow(img):
    """v3 가 칼끝에 그려 넣는 푸른 빛·고리를 지운다. 강철 날(옅은 회청색)은 채도가 낮아 남는다."""
    w, h, px = img
    def glow(c):
        r, g, b, a = c
        hi = max(g, b)
        return a > 127 and hi > 140 and b - r > 60 and hi - min(r, g, b) > 0.45 * hi
    return w, h, [(0, 0, 0, 0) if glow(c) else c for c in px]


def pack(item):
    folder = src_dir(item)
    # skip_first: v3 가 맨 앞에 붙인 서 있는 원화 칸을 뺀다 (달리기처럼 도는 동작에서 한 번씩 멈칫했다)
    start = 1 if item.get("skip_first") else 0
    frames = 1 if item.get("rotations") else frame_count(folder) - start
    rot_dir = os.path.join(SRC, os.path.basename(os.path.dirname(item["sheet"])), "rot")
    cache_path = os.path.join(folder, "offsets.json")
    cache = json.load(open(cache_path)) if os.path.exists(cache_path) else {}
    W, H = CELL * frames, CELL * len(DIRS)
    out = [(0, 0, 0, 0)] * (W * H)
    for row, d in enumerate(DIRS):
        rot_path = os.path.join(rot_dir, d, "0.png")
        if not os.path.exists(rot_path):
            print("rot 원화가 없어 빈 줄:", rot_path, "— rot 시트를 먼저 받는다")
            continue
        ref = png.read_rgba(rot_path)
        # 원화 자리: 캔버스 가운데와 발밑 줄을 ANCHOR 에
        rx = ANCHOR[0] - ref[0] // 2
        ry = ANCHOR[1] - max(opaque_rows(*ref))
        paths = [os.path.join(folder, d, f"{i}.png") for i in range(start, start + frames)]
        if item.get("rotations") or not all(os.path.exists(p) for p in paths):
            imgs, off = [ref] * frames, (0, 0)  # 아직 없는 방향은 서 있는 원화
        else:
            imgs = [clear_bg(png.read_rgba(p)) for p in paths]
            if d in item.get("deglow", ()):
                imgs = [deglow(im) for im in imgs]
            if d not in cache or d in item.get("redo", {}):
                w, h, px = imgs[0]
                guess = ((ref[0] - w) // 2, max(opaque_rows(*ref)) - max(opaque_rows(w, h, px)))
                cache[d] = align(ref, imgs[0], guess)
            off = tuple(cache[d])
        for col, (w, h, px) in enumerate(imgs):
            ox = col * CELL + rx + off[0]
            oy = row * CELL + ry + off[1]
            for y in range(h):
                ty = oy + y
                if not row * CELL <= ty < (row + 1) * CELL:
                    continue
                for x in range(w):
                    tx = ox + x
                    c = px[y * w + x]
                    if c[3] > 127 and col * CELL <= tx < (col + 1) * CELL:
                        out[ty * W + tx] = (c[0], c[1], c[2], 255)
    if cache:
        json.dump(cache, open(cache_path, "w"))
    dest = os.path.join(ROOT, item["sheet"])
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    png.write_rgba(dest, W, H, out)

if __name__ == "__main__":
    main()
