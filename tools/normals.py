"""스프라이트 법선 지도(_n.png)를 굽는다 — '종이 같음'을 없애는 첫째 수단 (shaders/sprite.gdshader).

판은 카메라를 향한 평면이라 빛이 전부 똑같이 닿는다. 그림의 알파를 여러 번 흐려
둥근 높이(알파를 흐린 언덕)를 만들고, 그림의 밝기 굴곡을 살짝 더해 법선을 뽑는다:
  나무·덤불·바위(둥근 것): 반지름 = 그림 크기의 35% (6~36px) → 해 쪽 반이 밝고 반대쪽이 어둡다
  건물·가구(각진 것): 반지름 5px → 가장자리만 둥글게 (베개처럼 부풀지 않게)
  캐릭터 시트: 칸마다 반지름 8px
결과: RG = 법선 xy*0.5+0.5 (x 오른쪽, y 위), B = 부피 깊이(0 = 판, 1 = 판에서 128px 앞), A = 원래 알파.
  법선 z 는 셰이더가 xy 로 되살린다. 깊이는 그림을 둥근 덩어리로 부풀릴 때 쓴다 (sprite.gdshader 의 relief):
  화면 픽셀은 그대로인데 그림자·가림·자기 그늘이 입체로 나온다. 둥근 것은 덩어리 반지름만큼, 각진 것은 얇게.
<이름>_n.png 로 옆에 저장.

  /c/Users/gram/AppData/Local/Programs/Python/Python313/python.exe tools/normals.py [--force]
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
HARD = ("house", "inn", "tavern", "bakery", "blacksmith", "shop", "store", "temple", "pagoda", "jhouse", "teahouse",
        "chalet", "lodge", "cottage", "greenhouse", "stable", "lighthouse", "windmill", "fountain", "well", "castle",
        "watchtower", "shrine", "torii", "pavilion", "dock", "boat", "bench", "fence", "stall", "cart", "crates",
        "barrel", "signpost", "lamp", "lantern", "planter", "buoy", "tent", "onsen", "log_seat", "campfire")
FLAT = ("petals", "lily_pad", "fairy_ring")


def box_blur(a, r):
    """r 반경 상자 흐림 (적분 영상 — 반경이 커도 빠르다)."""
    if r < 1:
        return a.copy()
    p = np.pad(a, r + 1, mode="constant")
    c = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    s = c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]
    return s[:a.shape[0], :a.shape[1]] / (k * k)


DEPTH_PX = 128.0   # B 채널 1.0 = 판에서 128px(4m) 앞


def dome(a, radius):
    """알파를 세 번 흐린 0..1 언덕 (외곽선 근처는 낮고 안쪽은 높다). 8방향 거리는 모가 났다."""
    h = a.astype(np.float32)
    r = max(1, int(radius / 2.2))
    for _ in range(3):
        h = box_blur(h, r)
    return np.sqrt(np.clip((h - 0.15) / 0.85, 0.0, 1.0))


def normal_map(rgba, radius, detail, depth_radius=None):
    a = rgba[..., 3] > 127
    height = dome(a, radius) * radius
    lum = (rgba[..., 0] * 0.3 + rgba[..., 1] * 0.59 + rgba[..., 2] * 0.11) / 255.0
    height += (lum - box_blur(lum, 2)) * detail
    height = np.where(a, height, 0.0)
    p = np.pad(height, 1, mode="edge")
    dx = (p[1:-1, 2:] - p[1:-1, :-2]) * 0.5
    dy = (p[:-2, 1:-1] - p[2:, 1:-1]) * 0.5   # 위가 + (그림의 y 는 아래로 자란다)
    n = np.stack([-dx, -dy, np.ones_like(dx)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    # 부피 깊이: 덩어리 크기(depth_radius)만큼 둥글게 — 법선(빛)은 잔 굴곡까지, 깊이는 큰 덩어리만
    dr = depth_radius if depth_radius else radius
    depth = np.where(a, dome(a, dr) * dr, 0.0)
    out = np.zeros(rgba.shape, np.uint8)
    out[..., :2] = np.clip((n[..., :2] * 0.5 + 0.5) * 255 + 0.5, 0, 255).astype(np.uint8)
    out[..., 2] = np.clip(depth / DEPTH_PX * 255 + 0.5, 0, 255).astype(np.uint8)
    out[..., 3] = rgba[..., 3]
    out[~a, :3] = (128, 128, 0)
    return out


def params(path):
    name = os.path.basename(path)
    if "characters" in path.replace("\\", "/"):
        return 8.0, 2.0
    if any(k in name for k in HARD):
        return 5.0, 1.5
    return None, 3.0


def main():
    force = "--force" in sys.argv
    files = glob.glob(os.path.join(ROOT, "art", "env", "*.png")) + glob.glob(os.path.join(ROOT, "art", "characters", "*", "*.png"))
    made = 0
    for f in sorted(files):
        if f.endswith("_n.png") or any(k in os.path.basename(f) for k in FLAT):
            continue
        dst = f[:-4] + "_n.png"
        if not force and os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(f):
            continue
        rgba = np.asarray(Image.open(f).convert("RGBA")).astype(np.float32)
        radius, detail = params(f)
        depth_radius = radius
        ys, xs = np.nonzero(rgba[..., 3] > 127)
        if len(xs) == 0:
            continue
        size = min(xs.max() - xs.min() + 1, ys.max() - ys.min() + 1)
        if radius is None:
            radius = float(np.clip(size * 0.35, 6, 36))
            # 둥근 것(나무·덤불·바위): 깊이는 덩어리 반지름에 가깝게 — 옆에서 본 그림자가 실제 수관만큼 두툼하다
            depth_radius = float(np.clip(size * 0.42, 6, 110))
        Image.fromarray(normal_map(rgba, radius, detail, depth_radius)).save(dst)
        made += 1
    print("법선 지도", made, "장")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        # 원판 하나: 가운데는 정면, 왼쪽 가장자리는 왼쪽(-x), 위 가장자리는 위(+y)를 본다
        yy, xx = np.mgrid[0:41, 0:41]
        img = np.zeros((41, 41, 4), np.float32)
        img[(xx - 20) ** 2 + (yy - 20) ** 2 <= 18 ** 2] = (200, 200, 200, 255)
        m = normal_map(img, 12, 0.0)
        n = m.astype(np.float32) / 255 * 2 - 1
        # 가운데는 정면(xy ≈ 0), 왼쪽 가장자리는 왼쪽(-x), 위 가장자리는 위(+y); 깊이는 가운데가 가장 깊고 바깥은 0
        assert abs(n[20, 20, 0]) < 0.1 and n[20, 3, 0] < -0.5 and n[3, 20, 1] > 0.5, (n[20, 20], n[20, 3], n[3, 20])
        assert m[20, 20, 2] > m[20, 6, 2] > 0 and m[0, 0, 2] == 0, (m[20, 20, 2], m[20, 6, 2])
        print("normals ok")
    else:
        main()
