"""하루미 대륙 지도 생성기 (1024x1024m). 설계와 근거: docs/design/WORLD.md

카메라는 북쪽(-z)을 본다. 남쪽이 낮고 북쪽으로 갈수록 계단처럼 높아진다:
  남쪽 바다·하루미 마을 → 초원(가운데)·벚꽃 골짜기(서)·꽃 들판(동) → 절벽 A
  → 단풍 협곡(북서)·고목의 숲·안개 호수(북동) → 절벽 B → 서리 고원(설산)
절벽은 남쪽을 보고 서서 폭포가 화면에 보인다. 길이 절벽을 지나는 곳은 긴 오르막.

출력 (data/world/):
  terrain.f32  꼭짓점 높이 (W+1)*(H+1) float32, 행 우선(z 바깥, x 안쪽)
  water.f32    꼭짓점 수면 높이, 물 없음 = -1000
  ground.u8    칸 지면 종류 W*H
  zones.u8     8m 칸 지역 번호 (이름은 meta.zone_names)
  props.json   소품 배치
  meta.json    크기·시작점·폭포·몬스터·전망 지점·지역
미리보기: docs/design/map_preview.png

numpy·Pillow 필요: 이 노트북은 윈도우 파이썬 3.13 에 깔려 있다.
  /c/Users/gram/AppData/Local/Programs/Python/Python313/python.exe tools/mapgen/mapgen.py
"""
import json
import math
import os
import random
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(__file__))
try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("numpy·Pillow 가 필요하다 — 윈도우 파이썬 3.13 으로 돌린다 (파일 맨 위 설명)")
from noise import (catmull, fbm, gauss, hash01, lerp, meander, n1, point_dist,  # noqa: E402
                   poly_field, ridged, smoothstep)

W = H = 1024
SEA = 0.0
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "data", "world")
ZONE_CELL = 8
ROAD_D = None   # 칸마다 가장 가까운 길 가장자리까지 거리 (main 이 채운다)

# 지면 종류. shaders/terrain.gdshader·World/GrassBuilder.cs 의 번호와 같아야 한다.
(GRASS, FOREST, MEADOW, DIRT, COBBLE, SAND, SAKURA, AUTUMN, GRAVEL, PEBBLE,
 SNOW, TULIP, LAVENDER, MOSS, ROCKY, STONE, GINKGO, WHEAT, ICE, FROSTGRASS, GOLDGRASS) = range(21)

# 꼭짓점 격자 / 칸 격자 좌표
VX, VZ = np.meshgrid(np.arange(W + 1, dtype=np.float64), np.arange(H + 1, dtype=np.float64))
CX, CZ = np.meshgrid(np.arange(W, dtype=np.float64) + 0.5, np.arange(H, dtype=np.float64) + 0.5)
VSHAPE, CSHAPE = VX.shape, CX.shape


# --- 큰 윤곽: 해안선과 절벽선 --------------------------------------------------

def wiggle(t, seed, big, mid, small=0.0):
    """선을 크게·중간·잘게 흔든다 — 자로 그은 해안선·절벽선은 인공적으로 보인다.
    잘게(파장 12m) 흔들면 화면에서 톱니로 보였다: small 은 파장 30m 짜리로 조금만."""
    v = big * n1(t * 0.0045, seed) + mid * n1(t * 0.02, seed + 1)
    if small:
        v = v + small * n1(t * 0.035, seed + 2)
    return v


def pinned(base_fn, t, pins, width=26.0):
    """흔든 선을 몇 군데(폭포·마을·곶) 원하는 값에 붙들어 맨다."""
    v = base_fn(t)
    for pt, target in pins:
        v = v + (target - float(base_fn(np.array(pt, np.float64)))) * np.exp(-((t - pt) / width) ** 2)
    return v


def spline1d(pts):
    """(t, 값) 점들을 지나는 매끈한 곡선 → t 배열을 받는 함수. 해안의 만·곶을 손으로 그린다."""
    line = catmull(pts, 1.0)
    ts = np.array([p[0] for p in line])
    vs = np.array([p[1] for p in line])
    return lambda t: np.interp(t, ts, vs)


# 서쪽 해안 (z, x): 북쪽은 노을 만이 깊이 들어오고(노을 절벽 100,372 의 북북서가 바다),
# 남쪽으로 신사 곶 → 작은 모래 만(z≈570) → 바위 곶 → 큰 모래 만(z≈772) → 남서 끝
WEST_PTS = [(0, 232), (64, 212), (128, 190), (192, 182), (256, 195), (320, 166), (352, 104), (384, 50), (416, 34),
            (450, 44), (490, 40), (522, 30), (548, 46), (566, 84), (578, 96), (592, 84), (606, 50), (640, 22),
            (690, 14), (730, 24), (770, 58), (820, 70), (860, 52), (900, 34), (950, 40), (1000, 56), (1024, 64)]
# 동쪽 해안 (z, x): 등대 곶(z≈420)이 튀어나오고, 북쪽엔 넓은 만, 남쪽엔 좁은 후미
EAST_PTS = [(0, 990), (80, 1000), (160, 1002), (230, 1004), (270, 996), (300, 984), (330, 966), (352, 960), (380, 972),
            (405, 1004), (420, 1018), (440, 1012), (470, 996), (520, 986), (580, 990), (640, 996), (680, 990), (700, 972),
            (716, 960), (732, 970), (760, 994), (820, 996), (880, 984), (940, 978), (1024, 978)]
_west, _east = spline1d(WEST_PTS), spline1d(EAST_PTS)


def west_coast(z):
    """서쪽 물가의 x."""
    return _west(z) + wiggle(z, 101, 0, 10, 3)


def east_coast(z):
    """동쪽 물가의 x. z≈420 에 등대 곶."""
    return _east(z) + wiggle(z, 106, 0, 10, 3) * (1 - np.exp(-((z - 420) / 30) ** 2))


def south_coast(x):
    """남쪽 물가의 z. 마을 앞(x≈500)은 항구 자리로 붙든다."""
    base = lambda t: 938 + wiggle(t, 111, 80, 22, 3)  # noqa: E731
    return pinned(base, x, [(505, 954)], 60)


def cliff_a(x):
    """초원 → 절벽 위 고원. 폭포(x≈602·206)와 거울 호수 북쪽은 붙들어 둔다."""
    base = lambda t: 438 + wiggle(t, 121, 110, 28, 2.5)  # noqa: E731
    return pinned(base, x, [(206, 436), (602, 437), (650, 432), (1000, 448)], 34)


def cliff_b(x):
    """고원 → 서리 고원. 북쪽 강 폭포(x≈606)·옛 성터·안개 호수 뒤는 붙들어 둔다."""
    base = lambda t: 206 + wiggle(t, 131, 100, 26, 2.5)  # noqa: E731
    return pinned(base, x, [(606, 212), (680, 216), (820, 204), (480, 232)], 34)


# 길이 절벽을 넘는 곳: (x 가운데, 반폭). 이 띠에서는 절벽 대신 긴 오르막.
RAMPS_A = [(302, 22), (708, 22), (884, 26)]
RAMPS_B = [(540, 22), (330, 26)]


def ramp_mask(x, ramps):
    m = np.zeros_like(x)
    for rx, rw in ramps:
        m = np.maximum(m, np.exp(-((x - rx) / rw) ** 2))
    return m


def tier_step(d, ramp, x, seed, half=5.0, ramp_half=34.0):
    """절벽 한 단: d = z - 절벽선(남쪽이 +). 0(아래) → 1(위).
    구간마다 생김새가 다르다 — 깎아지른 한 면 / 2·3·4단 바위턱, 너비도 3~8m.
    같은 3단 턱이 지도 끝까지 이어지면 줄무늬가 되풀이되어 보였다."""
    half_x = half * (0.6 + 0.95 * smoothstep(-0.5, 0.5, n1(x * 0.009, seed + 6)))
    w = half_x + (ramp_half - half_x) * ramp
    t = smoothstep(w, -w, d)
    tj = np.clip(t + 0.1 * n1(x * 0.05, seed) * t * (1 - t) * 4, 0, 1)

    def steps(n):
        f = tj * n
        q = (np.floor(f) + smoothstep(0.3, 0.7, f - np.floor(f))) / n
        return np.where(tj >= 1, 1.0, q)

    sel = n1(x * 0.006, seed + 5)
    k1 = smoothstep(-0.25, -0.4, sel)                       # 깎아지른 면
    k4 = smoothstep(0.25, 0.4, sel)                         # 4단
    k2 = smoothstep(-0.05, -0.2, sel) * (1 - k1)            # 2단
    k3 = np.clip(1 - k1 - k2 - k4, 0, 1)                    # 3단
    q = k1 * t + k2 * steps(2) + k3 * steps(3) + k4 * steps(4)
    return q * (1 - ramp) + t * ramp


# --- 장소 -----------------------------------------------------------------

PLAZA = (520.0, 885.0)
# 평평하게 고르는 자리: (x, z, 반지름, 목표 높이 또는 None=가운데 높이)
PADS = [
    (PLAZA[0], PLAZA[1], 66, None),   # 하루미 마을
    (222, 602, 40, None),             # 하나미 마을
    (815, 652, 42, None),             # 꽃 마을
    (560, 114, 42, None),             # 눈꽃 마을
    (570, 648, 20, None),             # 초원 쉼터 (갈림길)
    (220, 286, 22, None),             # 단풍 산사
    (452, 572, 12, None),             # 야영지
    (690, 762, 12, None),             # 거석 원
    (820, 252, 22, None),             # 옛 성터
    (790, 126, 44, None),             # 얼어붙은 호수
    (690, 160, 14, None),             # 온천
]

# 언덕: (x, z, 반지름, 높이)
BUMPS = [
    (420, 722, 30, 7.0),    # 약속의 언덕
    (690, 762, 24, 3.5),    # 거석 언덕
    (360, 522, 22, 6.0),    # 망루 언덕
    (110, 486, 26, 9.0),    # 신사 언덕
    (880, 562, 22, 6.0),    # 풍차 언덕
    (934, 656, 18, 4.5),
    (782, 744, 16, 3.5),
    (1000, 420, 18, 5.0),   # 등대 곶
    (300, 760, 30, 4.0),
    (650, 840, 26, 3.0),
    (250, 524, 18, 3.0),    # 천년 벚나무
]

# 바위 언덕: (x, z, 반지름, 높이). 길·마을·강을 비켜서
TORS = [(322, 540, 11, 5.5), (466, 760, 9, 4.5), (612, 708, 10, 5.0), (380, 900, 8, 4.0), (742, 880, 10, 5.0),
        (902, 600, 8, 4.0), (104, 724, 10, 5.0), (68, 520, 9, 6.0), (336, 474, 8, 4.5), (440, 470, 10, 5.0),
        (946, 470, 8, 5.0), (240, 840, 10, 4.5),
        (400, 300, 10, 6.0), (560, 396, 9, 5.0), (760, 400, 10, 6.0), (150, 410, 8, 5.0),
        (700, 70, 12, 7.0), (460, 80, 10, 6.0), (880, 120, 12, 8.0)]

LAKES = {
    "mirror": dict(c=(598.0, 480.0), r=(66.0, 42.0), seed=31),  # 거울 호수: 큰 폭포가 떨어진다
    "misty": dict(c=(655.0, 298.0), r=(52.0, 32.0), seed=37),   # 안개 호수: 북쪽 절벽에서 폭포
    "pond": dict(c=(466.0, 584.0), r=(10.0, 7.0), seed=39),     # 야영지 연못
    # 들판 곳곳의 작은 못 — 갈대·수련, 버드나무 (넓은 풀밭이 끝없이 같아 보이지 않게)
    "pond_w": dict(c=(318.0, 690.0), r=(15.0, 9.0), seed=43),
    "pond_e": dict(c=(764.0, 572.0), r=(12.0, 8.0), seed=45),
    "pond_s": dict(c=(370.0, 852.0), r=(13.0, 8.0), seed=47),
    "pond_k": dict(c=(124.0, 668.0), r=(11.0, 7.0), seed=49),
    "tarn": dict(c=(420.0, 150.0), r=(20.0, 12.0), seed=51),    # 서리 고원 산정 호수
}
ISLET_C, ISLET_R = (612.0, 486.0), 5.0

# 강: 흐르는 방향으로. 수면은 지형을 따라 '내려가기만' 하게 계산하고, 절벽에서 뚝 떨어지는 곳이 폭포가 된다.
RIVERS = [
    dict(name="north", pts=[(586, 18), (589, 70), (596, 125), (603, 175), (606, 214), (616, 246), (636, 274)],
         bed=1.8, bank=5.4, to="misty"),
    dict(name="falls", pts=[(640, 320), (624, 360), (610, 398), (603, 428), (600, 448)],
         bed=2.2, bank=5.0, frm="misty", to="mirror", meander=3.0),
    dict(name="south", pts=[(584, 514), (568, 540), (556, 566), (543, 612), (532, 662), (522, 722), (512, 782),
                            (503, 838), (494, 888), (486, 940), (480, 1012)],
         bed=2.6, bank=6.4, frm="mirror"),
    dict(name="west", pts=[(312, 30), (302, 90), (292, 150), (282, 200), (268, 248), (250, 298), (232, 350),
                           (216, 398), (207, 432), (201, 462), (193, 520), (182, 590), (170, 668), (162, 748),
                           (157, 828), (151, 900), (146, 1012)],
         bed=2.4, bank=6.2),
    # 초원을 비스듬히 가로지르는 개울 — 넓은 풀밭에 물줄기·징검다리·다리가 생겨 걸음의 박자가 바뀐다
    dict(name="creek", pts=[(338, 566), (372, 588), (404, 606), (436, 628), (466, 652), (494, 672), (521, 690)],
         bed=1.0, bank=3.4, meander=12.0, wave=70.0, to="south"),
    # 꽃 들판 동쪽 실개울: 등대 곶 남쪽 바다로
    dict(name="brook", pts=[(842, 548), (870, 590), (900, 628), (918, 690), (926, 760), (930, 830), (934, 900), (936, 992)],
         bed=0.9, bank=3.0, meander=12.0, wave=70.0),
]
# 강을 곧게 펴 두는 곳 (x, z, 반지름): 마을 한가운데·폭포 앞 — 굽이가 집·광장·폭포 자리를 덮치지 않게
RIVER_CALM = [(PLAZA[0], PLAZA[1], 90), (222, 602, 60), (206, 436, 40), (602, 440, 40), (606, 212, 34), (300, 140, 30)]

# 길: (이름, 점들, 반폭, 종류). street = 마을 안 포석, trail = 좁은 오솔길
ROADS = [
    ("main", [(532, 800), (542, 750), (556, 700), (566, 652), (610, 622), (650, 596), (680, 556), (696, 512),
              (704, 470), (708, 440), (710, 410), (706, 380), (712, 350), (726, 322), (728, 296), (708, 262),
              (664, 244), (620, 238), (580, 232), (550, 218), (540, 196), (542, 175), (552, 150), (560, 128)], 1.6, "road"),
    ("west", [(566, 652), (530, 648), (490, 644), (440, 640), (380, 630), (320, 620), (270, 610), (240, 604),
              (200, 600), (178, 598), (150, 590), (128, 560), (118, 522), (114, 498)], 1.5, "road"),
    ("autumn", [(240, 604), (262, 560), (284, 512), (298, 470), (302, 440), (302, 410), (292, 380), (270, 352),
                (240, 330), (230, 312), (226, 296), (214, 276), (210, 262), (216, 254)], 1.4, "road"),
    ("east", [(566, 652), (612, 672), (660, 684), (720, 676), (780, 660), (815, 652), (850, 640), (872, 604),
              (880, 572), (892, 530), (900, 496), (886, 470), (872, 440), (868, 410), (850, 380), (816, 352),
              (780, 332), (750, 318), (726, 304)], 1.5, "road"),
    ("cape", [(868, 410), (910, 414), (950, 418), (986, 422)], 1.2, "trail"),
    ("ring2", [(270, 352), (330, 362), (390, 360), (440, 350), (490, 338), (540, 332), (590, 340), (625, 346),
               (660, 352), (712, 350)], 1.3, "road"),
    ("ring3", [(270, 352), (296, 300), (314, 252), (318, 216), (322, 186), (350, 150), (410, 126), (470, 114),
               (522, 112)], 1.3, "road"),
    ("frost_e", [(600, 110), (660, 118), (720, 124), (752, 128)], 1.2, "road"),
    ("coast_w", [(434, 905), (380, 925), (320, 930), (260, 926), (200, 920), (150, 915), (108, 900)], 1.2, "trail"),
    ("coast_e", [(632, 902), (700, 915), (780, 912), (850, 895), (910, 860), (950, 800)], 1.2, "trail"),
    ("t_tree", [(430, 641), (428, 690), (422, 712)], 0.9, "trail"),
    ("t_stones", [(660, 684), (676, 720), (686, 750)], 0.9, "trail"),
    ("t_camp", [(490, 644), (472, 612), (456, 578)], 0.9, "trail"),
    ("t_tower", [(440, 640), (392, 592), (368, 540)], 0.9, "trail"),
    ("t_lake", [(610, 622), (618, 580), (622, 534)], 0.9, "trail"),
    ("t_sakura", [(270, 610), (262, 570), (254, 536)], 0.9, "trail"),
    ("t_mill", [(850, 640), (900, 650), (928, 654)], 0.9, "trail"),
    ("t_castle", [(780, 332), (800, 300), (814, 268)], 0.9, "trail"),
    ("t_spring", [(660, 118), (680, 140), (688, 152)], 0.9, "trail"),
    ("t_misty", [(712, 350), (690, 340), (662, 336)], 0.9, "trail"),
    ("t_sunset", [(232, 318), (190, 336), (150, 352), (114, 366), (100, 374)], 0.9, "trail"),
    # 마을 골목 (포석)
    ("s_harumi_n", [(PLAZA[0], PLAZA[1] - 6), (526, 840), (532, 800)], 1.6, "street"),
    ("s_harumi_e", [(PLAZA[0] + 6, PLAZA[1]), (560, 882), (600, 890), (632, 902)], 1.4, "street"),
    ("s_harumi_w", [(PLAZA[0] - 6, PLAZA[1]), (490, 888), (460, 896), (434, 905)], 1.4, "street"),
    ("s_harumi_s", [(PLAZA[0] - 2, PLAZA[1] + 6), (508, 920), (502, 944)], 1.4, "street"),
    ("s_harumi_back", [(526, 852), (560, 848), (594, 856), (612, 866)], 1.1, "street"),
    ("s_hanami_s", [(226, 604), (232, 636), (238, 662)], 1.2, "street"),
    ("s_hanami_n", [(222, 602), (214, 574)], 1.1, "street"),
    ("s_flower_s", [(815, 652), (820, 692)], 1.1, "street"),
    ("s_flower_n", [(815, 652), (804, 622)], 1.1, "street"),
    ("s_snow", [(522, 112), (560, 112), (600, 110)], 1.4, "street"),
]

# 꽃밭·밀밭: (가운데 x, z, 반폭, 반깊이, 종류, 기울기). 둥근 네모를 잡음으로 흔든 모양 — 자로 그은 네모 밭은 인공적이었다
FIELDS = [
    (774, 725, 34, 27, TULIP, 0.2), (859, 713, 38, 23, TULIP, -0.15), (805, 793, 42, 24, TULIP, 0.1),
    (906, 737, 24, 34, LAVENDER, 0.3), (726, 618, 26, 18, LAVENDER, -0.25),
    (638, 815, 38, 21, WHEAT, -0.1), (432, 827, 34, 21, WHEAT, 0.15), (673, 738, 27, 18, WHEAT, 0.25),
]


def field_mask(X, Z, f, pad=0.0):
    """밭 하나의 모양: 둥근 네모(초타원)를 비스듬히 돌리고 가장자리를 잡음으로 흔든다."""
    cx, cz, rx, rz, kind, rot = f
    c, sn = math.cos(rot), math.sin(rot)
    dx, dz = X - cx, Z - cz
    u, v = (dx * c + dz * sn) / (rx + pad), (-dx * sn + dz * c) / (rz + pad)
    r = (np.abs(u) ** 3 + np.abs(v) ** 3) ** (1 / 3)
    return r + 0.12 * fbm(X * 0.05, Z * 0.05, int(cx) + int(cz), 3) < 1.0


ZONE_NAMES = ["", "바람의 초원", "하루미 마을", "남쪽 해변", "벚꽃 골짜기", "하나미 마을", "꽃 들판", "꽃 마을",
              "등대 곶", "거울 호수", "단풍 협곡", "고목의 숲", "안개 호수", "서리 고원", "눈꽃 마을"]


def lake_d(X, Z, lake):
    (cx, cz), (rx, rz) = lake["c"], lake["r"]
    lx, lz = (X - cx) / rx, (Z - cz) / rz
    return np.sqrt(lx * lx + lz * lz) + 0.12 * fbm(X * 0.05, Z * 0.05, lake["seed"], 3)


def bilinear(grid, x, z):
    x = min(max(x, 0.0), W - 0.001)
    z = min(max(z, 0.0), H - 0.001)
    ix, iz = int(x), int(z)
    fx, fz = x - ix, z - iz
    a, b = grid[iz, ix], grid[iz, ix + 1]
    c, d = grid[iz + 1, ix], grid[iz + 1, ix + 1]
    return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz


# --- 높이 ------------------------------------------------------------------

def build_height(river_lines):
    X, Z = VX, VZ
    # 남쪽 구릉: 넓은 기복 + 작은 언덕(BotW 의 작은 삼각형 — 걸음의 박자를 바꾼다)
    flat_east = smoothstep(700, 780, X) * (1 - smoothstep(930, 960, X))   # 꽃 들판은 완만하게
    h = (4.0 + 3.0 * fbm(X * 0.004, Z * 0.004, 5) + 2.5 * fbm(X * 0.015, Z * 0.015, 6) * (1 - 0.6 * flat_east)
         + 4.0 * smoothstep(900, 450, Z))
    for bx, bz, br, bh in BUMPS:
        h += bh * gauss(X - bx, Z - bz, br)
    rng = random.Random(9)
    # 구릉은 크기를 섞는다: 큰 언덕 몇 + 작은 둔덕 여럿 (한 크기의 혹만 70개 뿌리면 고른 곰보 땅이 됐다)
    for _ in range(14):
        bx, bz = rng.uniform(80, 940), rng.uniform(480, 900)
        h += rng.uniform(2.5, 6.0) * gauss(X - bx, Z - bz, rng.uniform(30, 60))
    for _ in range(46):
        bx, bz = rng.uniform(60, 960), rng.uniform(470, 920)
        h += rng.uniform(0.8, 2.6) * gauss(X - bx, Z - bz, rng.uniform(5, 16))
    # 바위 언덕(작은 메사): 비탈이 가팔라 바위가 드러나고 위는 풀밭 — 넓은 들판의 길잡이
    for tx, tz, tr, th in TORS:
        d = np.hypot(X - tx, Z - tz) + 3.5 * fbm(X * 0.08, Z * 0.08, int(tx), 2)
        h += th * smoothstep(tr, tr * 0.55, d)
    # 벚꽃 골짜기: 서쪽 강을 따라 골이 진다
    dw = river_lines["west"][0]
    dwf = np.where(np.isinf(dw), 999.0, dw)
    h -= 3.5 * np.exp(-(dwf / 45.0) ** 2)

    # 절벽 두 단
    ca, cb = cliff_a(X), cliff_b(X)
    ta = tier_step(Z - ca, ramp_mask(X, RAMPS_A), X, 113)
    tb = tier_step(Z - cb, ramp_mask(X, RAMPS_B), X, 123)
    rise_a = 16 + 4 * n1(X * 0.01, 41)
    rise_b = 22 + 6 * n1(X * 0.008, 42)
    t2 = 5 * fbm(X * 0.01, Z * 0.01, 31) - 5 * np.exp(-(dwf / 18.0) ** 2)   # 단풍 협곡: 강이 골을 판다
    # 설산: 능선 잡음의 잔 옥타브가 뾰족한 가시를 세웠다 — 옥타브를 줄이고 좌표를 비틀어 둥글고 긴 산줄기로
    wx = X + 30 * fbm(X * 0.006, Z * 0.006, 65, 2)
    wz = Z + 30 * fbm(X * 0.006, Z * 0.006, 66, 2)
    rid = ridged(wx * 0.009, wz * 0.009, 61, octaves=2) ** 1.4
    north = smoothstep(185, 15, Z)
    t3 = 5 * fbm(X * 0.015, Z * 0.015, 62, 3) + north * (14 + 40 * rid)
    h = h + ta * (rise_a + t2) + tb * (rise_b + t3 - t2 * 0.5)

    # 평평한 자리
    for px, pz, pr, target in PADS:
        d = np.hypot(X - px, Z - pz)
        m = smoothstep(pr, pr * 0.6, d)
        if target is None:
            target = float(h[int(pz), int(px)])
            if (px, pz) == PLAZA:
                target = 3.4
        tilt = (PLAZA[1] + 60 - Z) * 0.012 if (px, pz) == PLAZA else 0.0
        h = lerp(h, target + tilt, m)

    # 바다: 남쪽은 모래 해변, 나머지는 바위 절벽
    xw, xe, zs = west_coast(Z), east_coast(Z), south_coast(X)
    dws, des, dss = X - xw, xe - X, zs - Z
    d = np.minimum(np.minimum(dws, des), dss)
    beachy = ((dss <= np.minimum(dws, des)) | ((dws <= des) & (Z > 720))).astype(np.float64)
    coast_w = lerp(3.0, 34.0, beachy)
    land = smoothstep(-1.5, coast_w, d)
    sea_floor = -4.0 + 1.5 * fbm(X * 0.03, Z * 0.03, 41, 2)
    beach_h = lerp(0.35, h, smoothstep(0.0, coast_w, d))
    h = lerp(sea_floor, lerp(h, beach_h, beachy), land)
    return h, ta, tb


def carve_lakes(h, ta):
    surfaces = {}
    for name, lake in LAKES.items():
        ld = lake_d(VX, VZ, lake)
        ring = (ld > 1.05) & (ld < 1.25)
        surf = float(np.percentile(h[ring], 12)) - 0.6
        surfaces[name] = surf
        # 절벽 아래쪽에서만 판다 — 북쪽 가장자리는 절벽 밑동이 물가가 된다
        keep_cliff = 1 - smoothstep(0.2, 0.6, ta) if name == "mirror" else 1.0
        shore = smoothstep(1.15, 0.92, ld) * keep_cliff
        deep = smoothstep(0.95, 0.3, ld + 0.15 * fbm(VX * 0.1, VZ * 0.1, 33, 2))
        bed = surf - 0.5 - (3.0 if name != "pond" else 1.0) * deep
        h = np.where(shore > 0, lerp(h, bed, shore), h)
        # 둘레가 수면보다 낮으면 둑을 올린다 (물이 넘쳐 들판을 덮지 않게)
        rim = (ld > 1.0) & (ld < 1.4)
        h = np.where(rim, np.maximum(h, surf + 0.4 * smoothstep(1.0, 1.15, ld)), h)
    idist = np.hypot(VX - ISLET_C[0], VZ - ISLET_C[1])
    h = np.where(idist < ISLET_R + 2, lerp(h, surfaces["mirror"] + 0.7, smoothstep(ISLET_R + 2, ISLET_R - 1, idist)), h)
    return h, surfaces


def road_lines():
    out = []
    for name, pts, half, kind in ROADS:
        if kind == "street":
            line = catmull(pts, 1.0)
        else:
            line = meander(catmull(pts, 1.0), 2.5 if kind == "road" else 1.5, 45.0, zlib.crc32(name.encode()) % 1000)
        out.append((name, line, half, kind))
    return out


def flatten_roads(h, roads):
    """길 단면을 고른다 — 비탈을 가로지르는 길이 옆으로 기울지 않게. 오르막에서는 길이 그대로 비탈."""
    for name, line, half, kind in roads:
        prof = np.array([bilinear(h, x, z) for x, z in line])
        k = 9
        pad = np.pad(prof, (k, k), mode="edge")
        smooth = np.convolve(pad, np.ones(2 * k + 1) / (2 * k + 1), mode="valid")
        d, s = poly_field(line, half + 4.0, VSHAPE)
        fr = np.linspace(0, 1, len(line))
        target = np.interp(s, fr, smooth)
        m = np.where(np.isinf(d), 0.0, smoothstep(half + 3.5, half, d))
        h = lerp(h, target, m)
        grad = np.abs(np.diff(smooth))
        if len(grad) and grad.max() > 0.6:
            i = int(grad.argmax())
            print(f"  경고: 길 {name} 가장 가파른 곳 {grad[i]:.2f}m/m @ ({line[i][0]:.0f},{line[i][1]:.0f})")
    return h


STEP_M = 7.0   # 산속 강의 계단 높이 (m) = 폭포 하나의 높이


def river_line(r):
    """강 중심선. 사인 굽이(파장 110~190m, 진폭이 잡음으로 들쭉날쭉)를 옆으로 얹는다.
    잡음만으로 흔들면 파장이 강 길이만큼 길어 강 전체가 옆으로 밀리기만 하고 곧게 보였다.
    마을·폭포 앞은 곧게, 산속(z<195)은 조금만 굽는다."""
    base = catmull(r["pts"], 1.0)
    seed = zlib.crc32(r["name"].encode()) % 1000
    amp = r.get("meander", 30.0)
    out, cum, phase = [], 0.0, 0.0
    for i, (bx, bz) in enumerate(base):
        if i:
            step = math.hypot(bx - base[i - 1][0], bz - base[i - 1][1])
            cum += step
            lam = r.get("wave", 150.0) * (1.0 + 0.27 * float(n1(np.array(cum / 300.0), seed + 3)))
            phase += step / lam * math.tau
        a, b = base[max(0, i - 1)], base[min(len(base) - 1, i + 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dz) or 1.0
        k = 0.55 + 0.45 * float(n1(np.array(cum / 220.0), seed + 5))
        for cx, cz, cr in RIVER_CALM:
            k = min(k, float(smoothstep(cr * 0.5, cr, math.hypot(bx - cx, bz - cz))))
        k *= float(smoothstep(0, 40, cum)) * (0.3 if bz < 195 else 1.0)
        off = amp * k * math.sin(phase + seed)
        out.append((bx - dz / ln * off, bz + dx / ln * off))
    # 끝은 원래 자리로 (호수·바다·합류점)
    n = len(out)
    for i in range(max(0, n - 40), n):
        t = (n - 1 - i) / 40.0
        out[i] = (base[i][0] + (out[i][0] - base[i][0]) * t, base[i][1] + (out[i][1] - base[i][1]) * t)
    return out


def river_surfaces(h, lake_surf):
    rivers = []
    for r in RIVERS:
        line = river_line(r)
        surf = []
        cur = None
        for i, (x, z) in enumerate(line):
            g = bilinear(h, x, z) - 0.9
            inside = None
            for name, lake in LAKES.items():
                if lake_d(np.array(x), np.array(z), lake) < 0.95:
                    inside = name
            if inside is not None and inside in (r.get("frm"), r.get("to")):
                cur = lake_surf[inside] if cur is None or inside == r.get("frm") else min(cur, lake_surf[inside])
            elif cur is None:
                cur = g
            else:
                cur = max(min(cur - 0.0015, g), SEA + 0.05)
            surf.append(cur)
        surf = np.array(surf)
        # 산속(서리 고원, z<195): 수면을 계단으로 — 소(평평한 물) + 짧고 곧은 폭포.
        # 지형을 따라 내려가기만 하면 비탈 전체가 긴 폭포 하나로 늘어졌다. 계단 높이는 지형 아래로만 깎는다
        zs = np.array([p[1] for p in line])
        mount = zs < 195
        if mount.any():
            q = np.floor(surf / STEP_M) * STEP_M
            # 계단 아래(산 밖)도 마지막 계단보다 높아지면 안 된다 — 물이 거슬러 올라 둑 위로 넘쳤다
            surf = np.minimum.accumulate(np.where(mount, q, surf))
        rivers.append(dict(r, line=line, surf=surf))
    return rivers


def find_falls(rv):
    """수면이 8m 안에서 3m 넘게 떨어지는 곳 = 폭포."""
    line, surf = rv["line"], rv["surf"]
    falls, i, n = [], 0, len(line)
    while i < n - 1:
        j = i
        while j < n - 1 and surf[i] - surf[min(n - 1, j + 1)] < 3.0 and j - i < 8:
            j += 1
        if surf[i] - surf[min(n - 1, j + 1)] >= 3.0 and surf[i] > 1.5 and line[i][1] > 24:
            k = j + 1
            while k < n - 1 and surf[k] - surf[k + 1] > 0.15:
                k += 1
            xs = [p[0] for p in line[i:k + 1]]
            zs = [p[1] for p in line[i:k + 1]]
            if surf[i] - surf[k] < 4.0:
                i = k + 1
                continue
            falls.append({"x": round(sum(xs) / len(xs), 2), "top_z": round(min(zs), 2), "top_y": round(float(surf[i]), 2),
                          "bottom_y": round(float(surf[k]), 2), "width": round(rv["bed"] * 2 + 0.6, 2)})
            i = k + 1
        else:
            i += 1
    return falls


def carve_rivers(h, rivers, ta, tb):
    water = np.full(VSHAPE, -1000.0)
    in_cliff = ((ta > 0.03) & (ta < 0.97)) | ((tb > 0.03) & (tb < 0.97))
    for rv in rivers:
        line, surf, bed_w, bank = rv["line"], rv["surf"], rv["bed"], rv["bank"]
        d, s = poly_field(line, bank + 14.0, VSHAPE)
        has = ~np.isinf(d)
        fr = np.linspace(0, 1, len(line))
        sl = np.interp(s, fr, surf)
        dd = np.where(has, d, 999.0)
        # 폭: 흐름을 따라 0.75~1.3 배 (소에서 넓고 여울에서 좁다)
        L = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(line[:-1], line[1:]))
        wv = 1.0 + 0.3 * n1(s * L / 70.0, zlib.crc32(rv["name"].encode()) % 1000 + 7)
        bed_w = bed_w * wv
        bank = bank * (0.85 + 0.15 * wv)
        bed = sl - 0.8 - 0.5 * np.clip(1 - dd / bed_w, 0, 1)
        ch = smoothstep(bank, bed_w, dd)
        h = np.where(has & (dd < bank), lerp(h, np.minimum(h, bed), ch), h)
        # 둑: 물보다 확실히 위 (지형이 수면 근처일 때만 — 폭포 옆 바위턱에 벽이 서지 않게)
        near = has & (dd > bed_w + 0.6) & (dd < bank + 1) & (h > sl - 3)
        h = np.where(near, np.maximum(h, sl + 0.45 * smoothstep(bed_w + 0.6, bank, dd)), h)
        # 강 골짜기: 완만한 비탈로 깎는다
        val = has & (dd >= bank) & (h - sl < 8)
        h = np.where(val, np.minimum(h, sl + 0.6 + (dd - bank) * 0.35), h)
        wet = has & (dd < bed_w + 0.9) & (h < sl + 0.3) & (h > sl - 2.2) & ~in_cliff
        water = np.where(wet, np.maximum(water, sl), water)
    return h, water


def build_water(h, water, lake_surf, ta):
    for name, lake in LAKES.items():
        ld = lake_d(VX, VZ, lake)
        s = lake_surf[name]
        wet = (ld < 1.18) & (h > s - 4.5) & (h < s + 0.35)
        if name == "mirror":
            wet &= ta < 0.5
        water = np.where(wet, np.maximum(water, s), water)
    sea = (h < SEA + 0.35) & (water < -999)
    water = np.where(sea, SEA, water)
    return water


# --- 지면 종류 --------------------------------------------------------------

def biome_weights():
    xw = CX + 170 * fbm(CX * 0.004, CZ * 0.004, 71) + 50 * fbm(CX * 0.016, CZ * 0.016, 72)
    t2 = smoothstep(3, -3, CZ - cliff_a(CX))
    t3 = smoothstep(3, -3, CZ - cliff_b(CX))
    west = smoothstep(330, 270, xw)
    east = smoothstep(700, 760, xw)
    coast_e = smoothstep(915, 945, xw)
    a_split = smoothstep(440, 380, xw)
    m_split = smoothstep(590, 650, xw)
    w = {
        "sakura": (1 - t2) * west,
        "flower": (1 - t2) * east * (1 - coast_e),
        "grass": (1 - t2) * (1 - west) * (1 - east),
        "coast": (1 - t2) * east * coast_e + (t2 - t3) * coast_e * m_split,
        "autumn": (t2 - t3) * a_split,
        "ancient": (t2 - t3) * (1 - a_split) * (1 - m_split),
        "misty": (t2 - t3) * m_split * (1 - coast_e),
        "frost": t3,
    }
    return w


BIOME_GROUND = {"grass": GRASS, "sakura": SAKURA, "flower": MEADOW, "coast": GRASS,
                "autumn": AUTUMN, "ancient": MOSS, "misty": GRASS, "frost": SNOW}


def build_ground(h, water, roads, bw, lake_surf):
    hc = (h[:-1, :-1] + h[:-1, 1:] + h[1:, :-1] + h[1:, 1:]) * 0.25
    wc = np.maximum.reduce([water[:-1, :-1], water[:-1, 1:], water[1:, :-1], water[1:, 1:]])
    slope = np.maximum(np.abs(h[:-1, 1:] - h[:-1, :-1]), np.abs(h[1:, :-1] - h[:-1, :-1]))

    names = list(bw.keys())
    # 경계에서 덩어리째 섞이게 잡음을 더해 가장 센 생물군을 고른다
    stack = np.stack([bw[k] + 0.45 * fbm(CX * 0.04, CZ * 0.04, 200 + i, 3) * np.minimum(1, bw[k] * 4)
                      for i, k in enumerate(names)])
    pick = np.argmax(stack, axis=0)
    g = np.zeros(CSHAPE, np.uint8)
    for i, k in enumerate(names):
        g[pick == i] = BIOME_GROUND[k]
    n = fbm(CX * 0.03, CZ * 0.03, 51, 3)
    grove = grove_field()
    grove2 = smoothstep(0.1, 0.5, fbm(CX * 0.015, CZ * 0.015, 301, 3))
    is_ = {k: pick == i for i, k in enumerate(names)}
    g[is_["grass"] & (n > 0.28)] = MEADOW
    g[is_["grass"] & (grove > 0.6)] = FOREST
    # 벚꽃 바닥(꽃잎)은 벚나무 숲 밑에만 — 골짜기 전체가 분홍이면 단조롭다
    g[is_["sakura"] & (grove < 0.3)] = GRASS
    g[is_["sakura"] & (grove < 0.3) & (n > 0.25)] = MEADOW
    # 단풍 협곡: 빈터는 누렇게 익은 풀밭, 낙엽 바닥은 단풍 숲 밑에만 (협곡 전체가 한 색 판이면 평평해 보였다)
    g[is_["autumn"] & (grove < 0.3)] = GOLDGRASS
    # 은행잎 바닥은 은행나무 숲 밑에만 (넓은 땅이 통째로 노랗게 칠해지면 평평한 판으로 보였다)
    g[is_["autumn"] & (grove2 > 0.55) & (n > -0.2)] = GINKGO
    g[is_["ancient"] & (n > 0.2)] = FOREST
    g[is_["misty"] & (n > 0.3)] = MEADOW
    # 서리 고원: 눈밭 사이로 마른 풀이 비죽 나온 언저리(절벽 B 가까이·바람받이 얼룩), 바람에 눈이 벗겨진 바위
    fringe = smoothstep(cliff_b(CX) - 40, cliff_b(CX) - 4, CZ)
    g[is_["frost"] & (fbm(CX * 0.02, CZ * 0.02, 53, 3) + fringe * 0.7 > 0.42)] = FROSTGRASS
    g[is_["frost"] & ((slope > 1.3) | (hc > 92) | ((fbm(CX * 0.035, CZ * 0.035, 54, 3) > 0.48) & (slope > 0.45)))] = ROCKY
    for f in FIELDS:
        g[field_mask(CX, CZ, f)] = f[4]
    # 얼어붙은 호수
    g[np.hypot((CX - 790) / 40, (CZ - 126) / 26) + 0.1 * fbm(CX * 0.06, CZ * 0.06, 63, 2) < 1.0] = ICE
    # 해변·호숫가
    beach = (hc < 2.2) & (CZ > 700) & (np.minimum(south_coast(CX) - CZ, CX - west_coast(CZ)) < 40)
    g[beach] = SAND
    for name in ("mirror", "misty"):
        ld = lake_d(CX, CZ, LAKES[name])
        g[(ld > 1.0) & (ld < 1.08 + 0.05 * n) & (hc < lake_surf[name] + 0.6)] = PEBBLE
    # 길
    for name, line, half, kind in roads:
        d, s = poly_field(line, half + 1.0, CSHAPE, 0.5)
        jit = 0.35 * fbm(CX * 0.3, CZ * 0.3, 77, 2)
        m = d < half + jit
        if kind == "street":
            g[m] = COBBLE
        elif name in ("ring3", "frost_e", "main", "s_snow"):
            # 서리 고원(절벽 B 위)의 길은 다져진 돌길 — 흙길이 눈밭에서 갈색 띠로 튀었다
            frost = m & (CZ < cliff_b(CX) - 4)
            g[frost] = STONE
            g[m & ~frost & (slope > 0.32)] = STONE
            g[m & ~frost & (slope <= 0.32)] = DIRT
        else:
            g[m & (slope > 0.32)] = STONE
            g[m & (slope <= 0.32)] = DIRT
    g[np.hypot(CX - PLAZA[0], CZ - PLAZA[1]) < 10] = COBBLE
    # 물 밑
    under = (wc > -999) & (hc < wc - 0.05)
    g[under & (wc > 8)] = GRAVEL
    g[under & (wc <= 8) & (wc > 0.5)] = PEBBLE
    g[under & (wc <= 0.5)] = SAND
    return g, hc, wc, slope


def build_zones(bw):
    zs = ZONE_CELL
    zx, zz = np.meshgrid(np.arange(W // zs) * zs + zs / 2, np.arange(H // zs) * zs + zs / 2)
    ix = (zx).astype(int)
    iz = (zz).astype(int)
    order = ["grass", "sakura", "flower", "coast", "autumn", "ancient", "misty", "frost"]
    zone_of = {"grass": 1, "sakura": 4, "flower": 6, "coast": 8, "autumn": 10, "ancient": 11, "misty": 12, "frost": 13}
    stack = np.stack([bw[k][iz, ix] for k in order])
    pick = np.argmax(stack, axis=0)
    z = np.zeros(zx.shape, np.uint8)
    for i, k in enumerate(order):
        z[pick == i] = zone_of[k]
    z[(zz > south_coast(zx) - 40) & (z == 1)] = 3
    z[lake_d(zx, zz, LAKES["mirror"]) < 1.5] = 9
    z[lake_d(zx, zz, LAKES["misty"]) < 1.5] = 12
    for (cx, cz, r, idx) in ((PLAZA[0], PLAZA[1] - 10, 78, 2), (222, 602, 46, 5), (815, 652, 46, 7), (560, 118, 50, 14)):
        z[np.hypot(zx - cx, zz - cz) < r] = idx
    z[np.hypot(zx - 990, zz - 425) < 60] = 8
    return z


# --- 소품 -----------------------------------------------------------------

class Scatter:
    """겹치지 않게 뿌린다. 8m 버킷으로 가까운 것만 비교."""

    def __init__(self):
        self.items = []
        self.grid = {}

    def free(self, x, z, r):
        gx, gz = int(x // 8), int(z // 8)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for ox, oz, orr in self.grid.get((gx + dx, gz + dz), ()):
                    if (ox - x) ** 2 + (oz - z) ** 2 < (r + orr) ** 2:
                        return False
        return True

    def add(self, kind, x, z, r, **extra):
        self.grid.setdefault((int(x // 8), int(z // 8)), []).append((x, z, r))
        item = {"type": kind, "x": round(float(x), 2), "z": round(float(z), 2)}
        item.update(extra)
        self.items.append(item)


# (종류, 차지 반경, 1m² 당 밀도, 모양)
#   grove: 숲 덩어리 안에 몰린다 / lone: 트인 곳에 외딴 나무 / edge: 숲 가장자리 / under: 나무 밑
#   meadow: 꽃밭 얼룩 / water: 물가 / None: 고르게
FLORA = {
    "grass": [("tree_oak", 3.0, 0.010, "grove", "oak"), ("tree_birch", 2.2, 0.010, "grove", "birch"), ("tree_pine", 2.4, 0.008, "grove", "pine"),
              ("tree_apple", 2.4, 0.0015, "grove", "birch"), ("tree_birch", 2.2, 0.0008, "edge"), ("shrub", 1.0, 0.004, "edge"),
              ("wildflower_a", 0.45, 0.008, "meadow"), ("wildflower_b", 0.45, 0.008, "meadow"), ("tree_oak", 3.0, 0.0006, "lone"), ("tree_poplar", 2.2, 0.0010, "lone"),
              ("bush", 1.0, 0.010, "edge"), ("rock", 0.9, 0.0012, None), ("flowers_mix", 0.8, 0.008, "meadow"),
              ("flowers_yellow", 0.45, 0.010, "meadow"), ("dandelion", 0.45, 0.006, None), ("grass_tuft", 0.45, 0.02, None),
              ("flowers_white", 0.45, 0.005, "meadow"), ("pebbles", 0.4, 0.002, None), ("stone", 0.5, 0.0015, None),
              ("fern_small", 0.45, 0.01, "under"), ("mushrooms", 0.45, 0.004, "under"), ("stump", 0.8, 0.0004, "grove"),
              ("log", 0.9, 0.0003, "grove"), ("daisy", 0.4, 0.004, "meadow"), ("grass_tall", 0.45, 0.004, None)],
    "sakura": [("tree_sakura", 3.0, 0.022, "grove"), ("wildflower_a", 0.45, 0.004, None), ("wildflower_b", 0.45, 0.004, None), ("tree_sakura", 3.0, 0.0015, "lone"), ("bush_pink", 1.0, 0.010, "edge"),
               ("flowers_white", 0.5, 0.010, None), ("petals", 0.45, 0.03, "under"), ("flowers_pink", 0.45, 0.008, None),
               ("grass_tuft", 0.45, 0.015, None), ("rock", 0.9, 0.001, None)],
    "flower": [("tree_oak", 3.0, 0.0012, "lone"), ("wildflower_a", 0.45, 0.006, "meadow"), ("wildflower_b", 0.45, 0.006, "meadow"), ("tree_poplar", 2.2, 0.0008, "lone"), ("flowers_mix", 0.8, 0.008, "meadow"),
               ("flowers_yellow", 0.45, 0.008, "meadow"), ("flowers_blue", 0.45, 0.006, "meadow2"), ("flowers_pink", 0.45, 0.006, "meadow2"),
               ("lavender", 0.5, 0.005, "meadow2"), ("dandelion", 0.45, 0.004, None), ("bush", 1.0, 0.002, None), ("grass_tall", 0.45, 0.006, None),
               ("tulip", 0.45, 0.003, "meadow"), ("daisy", 0.45, 0.004, "meadow2"), ("sunflower", 0.5, 0.002, "meadow"),
               ("rose", 0.5, 0.001, None), ("bush_flower", 1.0, 0.003, "edge"), ("hydrangea", 0.5, 0.0015, "meadow2"),
               ("tree_apple", 2.4, 0.0006, "grove", "birch"), ("tree_birch", 2.2, 0.0008, "grove", "birch")],
    "coast": [("tree_pine", 2.4, 0.003, "grove"), ("tree_seapine", 3.0, 0.002, "lone"), ("rock", 0.9, 0.005, None), ("grass_tuft", 0.45, 0.03, None),
              ("bush", 1.0, 0.003, None), ("flowers_white", 0.45, 0.004, None)],
    "autumn": [("tree_maple", 3.0, 0.020, "grove"), ("tree_ginkgo", 2.6, 0.008, "grove2"), ("tree_birch_gold", 2.2, 0.004, "edge"), ("bush_orange", 1.0, 0.010, "edge"),
               ("mushrooms", 0.45, 0.010, "under"), ("fern_small", 0.45, 0.008, "under"), ("stone", 0.5, 0.003, None),
               ("rock", 0.9, 0.0015, None), ("grass_tuft", 0.45, 0.008, None), ("maple_sapling", 0.5, 0.003, "edge"),
               ("pumpkins", 0.6, 0.0004, None), ("stump", 0.8, 0.0006, "grove")],
    "ancient": [("tree_ancient", 3.6, 0.010, "grove"), ("tree_oak", 3.2, 0.018, "grove"), ("tree_pine", 2.4, 0.008, "grove"), ("fern", 0.9, 0.016, "under"),
                ("fern_small", 0.45, 0.016, None), ("mushrooms", 0.45, 0.014, "under"), ("rock", 0.9, 0.003, None),
                ("bush", 1.0, 0.008, "edge"), ("sapling", 0.5, 0.003, None), ("log", 0.9, 0.002, "grove"),
                ("mushroom_big", 0.7, 0.0015, "under"), ("mushroom_glow", 0.5, 0.0012, "under"), ("stump", 0.8, 0.002, "grove"),
                ("fiddlehead", 0.45, 0.006, "under"), ("grass_tall", 0.45, 0.006, None)],
    "misty": [("tree_willow", 3.0, 0.008, "water"), ("tree_pine", 2.4, 0.006, "grove", "pine"), ("shrub", 1.0, 0.004, "edge"), ("wildflower_b", 0.45, 0.006, None), ("tree_oak", 3.0, 0.005, "grove"), ("tree_birch", 2.2, 0.005, "grove"), ("flowers_blue", 0.45, 0.010, None),
              ("fern", 0.9, 0.005, None), ("grass_tuft", 0.45, 0.02, None), ("rock", 0.9, 0.0015, None),
              ("grass_tall", 0.45, 0.006, None), ("mushroom_glow", 0.5, 0.0005, None), ("log", 0.9, 0.0005, None)],
    "frost": [("tree_snowfir", 2.6, 0.016, "grove"), ("tree_snowfir", 2.6, 0.001, "lone"), ("tree_bare", 3.0, 0.0015, "lone"),
              ("snow_rock", 0.9, 0.003, None), ("snow_bush", 1.0, 0.005, "edge"), ("snow_sapling", 0.7, 0.004, "edge"),
              ("snow_stump", 0.8, 0.0012, "grove"), ("ice", 0.7, 0.0008, None)],
}


def grove_field():
    """숲 덩어리: 큰 숲(파장 ~80m) + 작은 덤불숲·빈터(~30m). 한 크기만 쓰면 숲이 다 같은 크기로 보였다."""
    return smoothstep(0.05, 0.45, 0.72 * fbm(CX * 0.012, CZ * 0.012, 300, 3) + 0.28 * fbm(CX * 0.035, CZ * 0.035, 303, 2) * 1.6)


def scatter_flora(sc, bw, hc, wc, slope, road_d, near_water, pads_mask, seed=7):
    grove = grove_field()
    grove2 = smoothstep(0.1, 0.5, fbm(CX * 0.015, CZ * 0.015, 301, 3))
    meadow = smoothstep(0.1, 0.4, fbm(CX * 0.03, CZ * 0.03, 302, 3))
    meadow2 = smoothstep(0.1, 0.4, fbm(CX * 0.025, CZ * 0.025, 304, 3))
    shape = {
        None: np.ones(CSHAPE), "grove": 0.05 + 1.6 * grove, "grove2": 0.05 + 1.6 * grove2, "lone": (1 - grove) ** 2,
        "edge": 0.2 + 3.2 * grove * (1 - grove), "under": 0.15 + 1.2 * grove, "meadow": 0.1 + 1.4 * meadow,
        "meadow2": 0.1 + 1.4 * meadow2, "water": 0.05 + 2.0 * near_water,
    }
    # 수종 띠: 숲마다 주인이 다르다
    sp1 = fbm(CX * 0.006, CZ * 0.006, 410, 2)
    sp2 = fbm(CX * 0.008, CZ * 0.008, 420, 2)
    species = {
        "birch": smoothstep(0.08, 0.22, sp1),
        "pine": smoothstep(-0.18, -0.32, sp2) * smoothstep(0.22, 0.08, sp1),
    }
    species["oak"] = np.clip(1 - species["birch"] - species["pine"], 0, 1)
    dry = (wc < -999) & (hc > SEA + 0.4)
    ok = dry & (slope < 0.9)
    # 마을 자리엔 큰 나무를 뿌리지 않는다 (정원수는 집마다 따로). 풀꽃은 그대로
    rng = np.random.default_rng(seed)
    # 큰 나무 먼저 전부, 그다음 작은 것 (먼저 놓인 덤불이 나무 자리를 다 막지 않게)
    jobs = []
    for biome, rules in FLORA.items():
        for rule in rules:
            kind, r, dens, mode = rule[:4]
            jobs.append((r <= 2, biome, kind, r, dens, mode, rule[4] if len(rule) > 4 else None))
    jobs.sort(key=lambda j: j[0])
    for small, biome, kind, r, dens, mode, sp in jobs:
        field = bw[biome] * shape[mode] * (ok if small else ok & ~pads_mask)
        if sp is not None:
            field = field * species[sp]
        fmax = float(field.max())
        if fmax <= 0:
            continue
        n = int(dens * fmax * W * H * 1.0)
        if n == 0:
            continue
        xs = rng.uniform(0, W, n)
        zs = rng.uniform(0, H, n)
        ix, iz = xs.astype(int), zs.astype(int)
        keep = rng.uniform(0, fmax, n) < field[iz, ix]
        clear = road_d[iz, ix] > (r + 1.4 if not small else 1.3)
        sel = np.nonzero(keep & clear)[0]
        for k in sel:
            x, z = float(xs[k]), float(zs[k])
            if sc.free(x, z, r):
                sc.add(kind, x, z, r, v=int(rng.integers(0, 8)))


def add_landmarks(sc, rng, hcell):
    def h_at(x, z):
        return float(hcell[min(H - 1, max(0, int(z))), min(W - 1, max(0, int(x)))])

    sc.add("lighthouse", 1002, 420, 5.0)
    sc.add("bench", 990, 412, 1.5, look="north")
    for i, (x, z) in enumerate(((880, 556), (934, 650), (782, 738))):
        sc.add("windmill", x, z, 5.0, v=i)
    sc.add("clocktower", 540, 872, 4.0, front_z=875)   # 하루미 광장 북동쪽 — 마을 어디서든 보이는 시계탑
    sc.add("warehouse", 528, 938, 4.0, front_z=941)
    sc.add("observatory", 600, 158, 3.5, front_z=161)  # 서리 고원 언덕 위 별 관측소
    sc.add("ice_shrine", 790, 94, 3.5, front_z=97)     # 얼어붙은 호수 북쪽 얼음 사당
    sc.add("chapel", 840, 678, 3.5, front_z=681)       # 꽃 마을 종탑 예배당
    # 여신상: 지역마다 갈림길 곁에 하나 — 기도하면 부활 지점, 깨운 곳끼리 빠른 이동 (World/Statues)
    for sid, name, x, z in STATUES:
        sc.add("goddess", x, z, 2.2, front_z=z + 1)
    sc.add("gazebo", 648, 338, 3.0, front_z=341)   # 안개 호수 남쪽 물가
    sc.add("shepherd_hut", 372, 690, 3.5, front_z=693)   # 초원 양치기 오두막 + 울타리·건초
    for k in range(6):
        sc.add("fence", 366 + k * 1.5, 697, 0.6)
    for x, z in ((380, 694), (383, 697)):
        sc.add("hay_bale", x, z, 1.0)
    sc.add("stump_house", 520, 316, 3.0, front_z=319)    # 고목의 숲 그루터기 집
    sc.add("fairy_ring", 452, 362, 2.5)                  # 고목의 숲 버섯 요정 고리
    sc.add("fairy_ring", 610, 368, 2.5)
    sc.add("ice_hut", 802, 132, 2.5, front_z=134)       # 얼어붙은 호수 얼음낚시
    sc.add("moon_deck", 150, 350, 3.5, front_z=353)      # 노을 길 달맞이 누대
    sc.add("boathouse", 466, 936, 3.5, front_z=939)      # 항구 서쪽 보트 창고
    sc.add("pass_gate", 542, 186, 2.0)                   # 서리 고원으로 오르는 고개 돌문
    sc.add("pass_gate", 324, 178, 2.0)
    sc.add("gazebo", 676, 470, 3.0, front_z=473)   # 거울 호수 동쪽 물가
    for x, z in ((815, 664), (816, 640), (506, 900)):
        sc.add("rose_arch", x, z, 1.5)
    sc.add("shrine", 110, 480, 3.5)
    # 신목: 금줄을 두른 큰 삼나무 — 신사·산사 곁
    for x, z in ((96, 474), (126, 470), (246, 276), (202, 270)):
        sc.add("tree_cedar", x, z, 3.0)
    for i, (x, z) in enumerate(((127, 556), (122, 540), (118, 524), (115, 508))):
        sc.add("torii", x, z, 2.2)
    sc.add("pavilion", ISLET_C[0], ISLET_C[1], 3.5)
    sc.add("dock", 622, 528, 2.5)
    sc.add("boat", 606, 530, 2.0)
    sc.add("fountain", PLAZA[0], PLAZA[1], 3.2)
    sc.add("dock_sea", 502, 952, 3.0)
    sc.add("fish_crates", 498, 946, 1.2)
    sc.add("buoy", 512, 966, 1.0)
    sc.add("buoy", 492, 972, 1.0)
    # 항구: 창고 앞 살림, 부두 둘레 갈매기
    for kind, x, z in (("lobster_trap", 520, 944), ("rope", 536, 945), ("oars", 512, 946), ("boat", 488, 958),
                       ("seagull", 506, 956), ("seagull", 470, 950), ("seagull", 560, 952), ("lobster_trap", 543, 943)):
        sc.add(kind, x, z, 0.6, v=1 if kind == "boat" else rng.randrange(2))
    # 바닷가 갈매기: 남쪽 모래사장에 드문드문
    for i in range(18):
        x = rng.uniform(80, 980)
        z = float(south_coast(np.array(x))) - rng.uniform(4, 14)
        if sc.free(x, z, 0.5):
            sc.add("seagull", x, z, 0.5, v=rng.randrange(2))
    # 거석 원: 일곱 개를 둥글게 (하나는 쓰러져 비었다)
    for i in range(8):
        if i == 5:
            continue
        a = i / 8 * math.tau + 0.3
        sc.add("standing_stone", 690 + math.cos(a) * 7, 762 + math.sin(a) * 5.5, 1.0, v=i % 4)
    sc.add("tent", 452, 568, 2.6)   # 천막 그림에 모닥불·통나무 의자가 들어 있다
    sc.add("barn", 690, 806, 4.0, front_z=809)
    # 과수원: 헛간 둘레와 꽃 마을 남쪽에 사과나무 줄
    for ox, oz in ((712, 790), (850, 700)):
        for i in range(3):
            for j in range(3):
                x, z = ox + i * 7 + rng.uniform(-1, 1), oz + j * 7 + rng.uniform(-1, 1)
                if sc.free(x, z, 2.4):
                    sc.add("tree_apple", x, z, 2.4)
    # 밭마다 허수아비 하나, 둘레엔 군데군데 끊긴 산울타리(덤불·작은 나무) — 밭과 밭 사이가 길로 읽힌다
    for f in FIELDS:
        cx, cz, rx, rz, kind, rot = f
        sc.add("scarecrow", cx + rng.uniform(-rx, rx) * 0.4, cz + rng.uniform(-rz, rz) * 0.4, 0.6)
        n = int((rx + rz) * 0.5)
        for k in range(n):
            a = k / n * math.tau + rng.uniform(-0.05, 0.05)
            if math.sin(a * 3 + cx) > 0.55:      # 울타리가 끊긴 곳 = 밭 드나드는 길
                continue
            u, v = math.cos(a) * (rx + 2.5), math.sin(a) * (rz + 2.5)
            x = cx + u * math.cos(rot) - v * math.sin(rot)
            z = cz + u * math.sin(rot) + v * math.cos(rot)
            kind2 = rng.choice(["bush", "bush", "shrub", "bush_flower"]) if rng.random() < 0.9 else "tree_poplar"
            r = 2.2 if kind2.startswith("tree") else 0.9
            if 0 <= int(x) < W and 0 <= int(z) < H and ROAD_D[int(z), int(x)] > r + 0.6 and sc.free(x, z, r):
                sc.add(kind2, x, z, r, v=rng.randrange(8))
    # 산사 앞 석등 길과 감·호박
    for i in range(4):
        for side in (-1, 1):
            sc.add("jlantern", 226 + side * 2.6, 290 + i * 5.0, 0.5)
    for x, z in ((216, 288), (233, 287), (238, 296)):
        sc.add(rng.choice(["pumpkins", "persimmon_basket"]), x, z, 0.6, v=rng.randrange(3))
    for x, z in ((682, 816), (699, 818), (706, 812)):
        sc.add("hay_bale", x, z, 1.0)
    sc.add("cart", 676, 812, 1.4)
    for x, z in ((300, 626), (268, 538), (424, 362), (156, 588)):
        sc.add("hokora", x, z, 2.0, front_z=z + 1)
    sc.add("watchtower_ruin", 360, 520, 4.0)
    # 작은 폐허 터: 길을 걷다 '저게 뭐지' 하고 들르는 곳 (BotW 의 작은 볼거리)
    for (rx, rz, n) in ((360, 520, 5), (440, 374, 6), (820, 272, 5), (128, 760, 4), (764, 862, 4), (300, 760, 5), (884, 300, 4)):
        for k in range(n):
            a = rng.uniform(0, math.tau)
            d = rng.uniform(5, 11)
            x, z = rx + math.cos(a) * d, rz + math.sin(a) * d
            kind = rng.choice(["ruin_pillar", "ruin_pillar", "ruin_wall", "standing_stone", "rock"])
            if sc.free(x, z, 1.2):
                sc.add(kind, x, z, 1.2, v=rng.randrange(4))
        if n >= 5 and sc.free(rx, rz + 14, 2.0):
            sc.add("ruin_arch", rx + rng.uniform(-3, 3), rz + 14, 2.0)
    sc.add("tree_lone", 421, 718, 4.5)
    sc.add("tree_sakura_giant", 252, 524, 5.5)
    sc.add("tree_world", 480, 296, 9.0)      # 세계수 (고목의 숲 한가운데)
    sc.add("tree_giant", 294, 674, 6.0)      # 초원 연못가의 큰 나무
    sc.add("temple", 224, 280, 4.5, front_z=283)
    sc.add("pagoda", 208, 300, 3.5)
    sc.add("torii", 226, 300, 2.2)
    sc.add("bench", 100, 368, 1.5, look="north")   # 노을 절벽: 북북서가 탁 트인 바다 (해가 지는 쪽)
    sc.add("castle_ruin", 820, 248, 6.0)
    sc.add("onsen", 690, 160, 4.0)
    sc.add("inn", 584, 638, 4.5, front_z=642)
    sc.add("stable", 552, 636, 3.5, front_z=640)
    sc.add("well", 570, 636, 1.6)
    for x, z in ((562, 660), (244, 610), (846, 646), (716, 356), (274, 358)):
        sc.add("signpost", x, z, 0.8)
    for x, z in ((424, 728), (664, 342), (560, 154), (114, 500)):
        sc.add("bench", x, z, 1.2, look="north")


# 여신상 (id, 이름, x, z). meta.statues 로 나가 게임이 읽는다
STATUES = [("harumi", "하루미 광장", 548, 898), ("meadow", "바람의 초원 쉼터", 576, 664), ("hanami", "하나미 마을", 252, 616),
           ("flower", "꽃 마을", 858, 628), ("cape", "등대 곶", 980, 438), ("autumn", "단풍 산길", 276, 364),
           ("ancient", "고목의 숲", 496, 352), ("misty", "안개 호숫가", 720, 362), ("snow", "눈꽃 마을", 548, 132)]

VILLAGES = {
    "harumi": dict(streets=["s_harumi_n", "s_harumi_e", "s_harumi_w", "s_harumi_back"],
                   kinds=["house_red", "house_blue", "house_green", "house_yellow", "bakery", "flower_shop", "inn",
                          "blacksmith", "general_store", "tavern", "house_stone", "house_fisher", "house_tower"],
                   center=PLAZA, radius=74),
    "hanami": dict(streets=["west", "s_hanami_s", "s_hanami_n"], kinds=["jhouse", "jhouse", "jhouse", "teahouse", "hokora"],
                   center=(222, 602), radius=44),
    "flower": dict(streets=["east", "s_flower_s", "s_flower_n"], kinds=["cottage", "cottage", "greenhouse",
                                                                       "house_yellow", "flower_shop", "barn"],
                   center=(815, 652), radius=46),
    "snow": dict(streets=["s_snow", "main"], kinds=["chalet", "chalet", "chalet", "lodge"], center=(560, 114), radius=46),
}
WIDE = {"inn": 4.5, "tavern": 4.5, "lodge": 4.5, "teahouse": 4.0, "greenhouse": 4.0, "barn": 4.0, "hokora": 2.6}
SHOPS = {"bakery", "general_store", "blacksmith", "flower_shop", "inn", "tavern", "teahouse", "lodge"}


def place_villages(sc, roads, water, hcell, slope):
    lines = {name: (line, half) for name, line, half, kind in roads}
    rng = random.Random(21)
    for vname, v in VILLAGES.items():
        kinds = list(v["kinds"])
        rng.shuffle(kinds)
        ki = 0
        cx, cz = v["center"]
        for street in v["streets"]:
            line, half = lines[street]
            acc, i = rng.uniform(0, 4), 1
            while i < len(line):
                x0, z0 = line[i - 1]
                x1, z1 = line[i]
                acc += math.hypot(x1 - x0, z1 - z0)
                i += 1
                if acc < 9:
                    continue
                if math.hypot(x1 - cx, z1 - cz) > v["radius"]:
                    continue
                dx, dz = x1 - x0, z1 - z0
                ln = math.hypot(dx, dz) or 1
                kind = kinds[ki % len(kinds)]
                hw = WIDE.get(kind, 3.4)
                setback = half + rng.uniform(1.2, 3.2)
                # 동서로 뻗은 길: 집은 북쪽에 앞을 길로 / 남북 길: 양옆 (집은 늘 남쪽을 본다 — 앞모습 그림뿐)
                if abs(dx) >= abs(dz):
                    sides = [(0, -1)]
                else:
                    # 남북 길: 한쪽만 (가끔 양쪽) — 양옆에 같은 높이로 짝지어 서면 바둑판처럼 보인다
                    sides = [rng.choice(((1, 0), (-1, 0)))]
                    if rng.random() < 0.3:
                        sides.append((-sides[0][0], 0))
                placed = False
                for sx, sz in sides:
                    if sz:
                        front = z1 - setback
                        hx, hz = x1, front - 2.5
                    else:
                        hx, hz = x1 + sx * (setback + hw), z1 + rng.uniform(-1.5, 3.5)
                        front = hz + 2.5
                    xi, zi = int(hx), int(hz)
                    if not (0 <= xi < W and 0 <= zi < H):
                        continue
                    if water[zi, xi] > -999 or slope[zi, xi] > 0.5 or not sc.free(hx, hz, hw + 0.6):
                        continue
                    if any(point_dist(hx, hz, l) < hw + h2 + 0.3 for l, h2 in lines.values()):
                        continue
                    sc.add(kind, hx, hz, hw, front_z=round(front, 2), v=rng.randrange(8))
                    house_yard(sc, kind, hx, front, hw, rng, vname)
                    placed = True
                    ki += 1
                    kind = kinds[ki % len(kinds)]
                    hw = WIDE.get(kind, 3.4)
                if placed:
                    acc = -rng.uniform(1.0, 6.0)


# 마을마다 마당 살림·정원수가 다르다 (같은 화분이 마을마다 줄지어 있으면 반복이 보인다)
YARD = {
    "harumi": dict(props=["planter", "barrel", "crates", "bench", "flowers_mix", "bush", "street_lamp", "cart", "birdhouse",
                          "watering_can", "lobster_trap", "rope"],
                   garden=["flowers_mix", "flowers_yellow", "flowers_pink", "flowers_white", "lavender"], trees=["tree_oak", "tree_poplar"]),
    "hanami": dict(props=["jlantern", "paper_lantern", "twig_pot", "barrel", "bench", "bush_pink"],
                   garden=["flowers_pink", "flowers_white", "fern_small", "petals"], trees=["tree_sakura", "tree_seapine"]),
    "flower": dict(props=["planter", "wheelbarrow", "watering_can", "birdhouse", "bush_flower", "hay_bale", "scarecrow"],
                   garden=["tulip", "daisy", "rose", "sunflower", "flowers_blue", "hydrangea", "lavender"],
                   trees=["tree_poplar", "tree_oak"]),
    "snow": dict(props=["barrel", "crates", "stone_lantern", "snow_bush", "snowman", "sled", "snow_sign"],
                 garden=["snow_rock", "snow_bush", "snow_sapling"], trees=["tree_snowfir", "tree_bare"]),
}


def house_yard(sc, kind, cx, front, half, rng, village):
    """집 앞 살림 + 한쪽 꽃밭 + 가끔 울타리·정원수. 집마다 고르는 것이 달라 반복이 안 보이게."""
    y = YARD[village]
    shop = kind in SHOPS
    for side in (-1, 1):
        if rng.random() < 0.2:
            continue
        pick = rng.choice(["crates", "barrel", "planter"] if shop and village != "snow" else y["props"])
        x = cx + side * (half - rng.uniform(0.6, 1.4))
        z = front + rng.uniform(0.5, 1.2)
        if sc.free(x, z, 0.7):
            sc.add(pick, x, z, 0.7, v=rng.randrange(4))
    # 꽃밭: 집 옆 앞마당에 3~7포기 덩어리
    side = rng.choice((-1, 1))
    gx, gz = cx + side * (half + rng.uniform(1.0, 2.5)), front + rng.uniform(-1.5, 1.5)
    for _ in range(rng.randint(3, 7)):
        x, z = gx + rng.uniform(-1.6, 1.6), gz + rng.uniform(-1.0, 1.0)
        if sc.free(x, z, 0.35):
            sc.add(rng.choice(y["garden"]), x, z, 0.35, v=rng.randrange(4))
    # 울타리: 앞마당 가장자리에 3~5칸
    fence = "snow_fence" if village == "snow" else "fence"
    if rng.random() < 0.4 and village != "hanami":
        fx0 = cx - side * half - side * rng.uniform(0.5, 1.5)
        fz = front + rng.uniform(2.2, 3.0)
        for k in range(rng.randint(3, 5)):
            x = fx0 - side * k * 1.5
            if sc.free(x, fz, 0.6):
                sc.add(fence, x, fz, 0.6, v=rng.randrange(3))
    if rng.random() < 0.55:
        x = cx - side * (half + rng.uniform(1.5, 3.2))
        z = front - rng.uniform(0.5, 3.0)
        tree = rng.choice(y["trees"] + ["bush"])
        r = 2.2 if tree.startswith("tree") else 0.9
        if sc.free(x, z, r):
            sc.add(tree, x, z, r, v=rng.randrange(8))


def street_lamps(sc, roads, water, village_names):
    """마을 골목을 따라 가로등·석등을 엇갈려 세운다 (12~16m 마다)."""
    rng = random.Random(33)
    lamp_of = {"hanami": "stone_lantern", "snow": "street_lamp", "harumi": "street_lamp", "flower": "street_lamp"}
    for name, line, half, kind in roads:
        if kind != "street":
            continue
        village = next((v for v in village_names if name.startswith("s_" + {"harumi": "harumi", "hanami": "hanami",
                                                                              "flower": "flower", "snow": "snow"}[v])), None)
        if village is None:
            continue
        acc, side = rng.uniform(0, 6), 1
        for i in range(1, len(line)):
            acc += math.hypot(line[i][0] - line[i - 1][0], line[i][1] - line[i - 1][1])
            if acc < 13:
                continue
            dx, dz = line[i][0] - line[i - 1][0], line[i][1] - line[i - 1][1]
            ln = math.hypot(dx, dz) or 1
            x, z = line[i][0] - dz / ln * (half + 0.8) * side, line[i][1] + dx / ln * (half + 0.8) * side
            xi, zi = int(x), int(z)
            if 0 <= xi < W and 0 <= zi < H and water[zi, xi] < -999 and sc.free(x, z, 0.6):
                sc.add(lamp_of[village], x, z, 0.6)
                acc, side = rng.uniform(-3, 0), -side


def water_props(sc, hcell, wc, slope, lake_surf):
    for z in range(2, H - 2, 2):
        for x in range(2, W - 2, 2):
            px, pz = x + float(hash01(x, z, 5)), z + float(hash01(z, x, 6))
            xi, zi = int(px), int(pz)
            w = wc[zi, xi]
            if w > -999:
                lake_w = any(abs(w - s) < 0.01 for s in lake_surf.values())
                if lake_w and hcell[zi, xi] < w - 0.6 and hash01(x, z, 9) < 0.05 and sc.free(px, pz, 0.8):
                    sc.add("lily_pad", px, pz, 0.8, v=int(hash01(x, z, 10) * 2))
                continue
            if slope[zi, xi] > 1.5 and hash01(x, z, 11) < 0.05:
                # 절벽 밑동 바위: 남쪽(앞)으로 내려가 평평한 데 놓는다
                for dz in range(1, 6):
                    qz = zi + dz
                    if qz < H and slope[qz, xi] < 0.5 and wc[qz, xi] < -999:
                        if hash01(x, z, 15) < 0.3 and sc.free(px, qz + 1.0, 2.0):
                            sc.add("boulder", px, qz + 1.0, 2.0)
                        elif sc.free(px, qz + 0.5, 1.0):
                            sc.add("rock", px, qz + 0.5, 1.0, v=int(hash01(x, z, 12) * 8))
                        break
                continue
            if slope[zi, xi] > 0.9:
                continue
            near_lake = False
            for dx in (-2, 0, 2):
                for dz in (-2, 0, 2):
                    q = wc[min(H - 1, max(0, zi + dz)), min(W - 1, max(0, xi + dx))]
                    if q > 1.0 and any(abs(q - s) < 0.01 for s in lake_surf.values()):
                        near_lake = True
            if near_lake and hash01(x, z, 13) < 0.25 and sc.free(px, pz, 0.6):
                sc.add("reeds", px, pz, 0.6, v=int(hash01(x, z, 14) * 3))


def bridges(sc, roads, wc, hcell):
    def h_at(x, z):
        return float(hcell[min(H - 1, max(0, int(z))), min(W - 1, max(0, int(x)))])

    def wet(x, z):
        xi, zi = int(x), int(z)
        return 0 <= xi < W and 0 <= zi < H and wc[zi, xi] > -999

    count = 0
    for name, line, half, kind in roads:
        last_dry, inside = None, False
        for (x, z) in line:
            if wet(x, z) and not inside:
                inside = True
            elif not wet(x, z) and inside:
                inside = False
                if last_dry is not None:
                    a, b = last_dry, (x, z)
                    ln = math.hypot(b[0] - a[0], b[1] - a[1]) + 1.0
                    if ln <= 18.0:
                        deck = max(h_at(*a), h_at(*b)) + 0.05
                        sc.add("bridge", (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, ln * 0.5,
                               dir=round(math.atan2(b[1] - a[1], b[0] - a[0]), 3), len=round(ln, 2), deck=round(deck, 2))
                        count += 1
            if not wet(x, z):
                last_dry = (x, z)
    return count


# --- 실행 -----------------------------------------------------------------

def main():
    os.makedirs(OUT, exist_ok=True)
    river_lines = {r["name"]: poly_field(catmull(r["pts"], 2.0), 60.0, VSHAPE) for r in RIVERS}
    print("높이…")
    h, ta, tb = build_height(river_lines)
    h, lake_surf = carve_lakes(h, ta)
    roads = road_lines()
    h = flatten_roads(h, roads)
    print("강…")
    rivers = river_surfaces(h, lake_surf)
    falls = []
    for rv in rivers:
        falls += find_falls(rv)
    h, water = carve_rivers(h, rivers, ta, tb)
    water = build_water(h, water, lake_surf, ta)
    print("지면…")
    bw = biome_weights()
    ground, hcell, wcell, slope = build_ground(h, water, roads, bw, lake_surf)
    zones = build_zones(bw)

    print("소품…")
    global ROAD_D
    road_d = np.full(CSHAPE, 999.0)
    for name, line, half, kind in roads:
        d, _ = poly_field(line, half + 6.0, CSHAPE, 0.5)
        road_d = np.minimum(road_d, d - half)
    ROAD_D = road_d
    sc = Scatter()
    rng = random.Random(3)
    add_landmarks(sc, rng, hcell)
    place_villages(sc, roads, wcell, hcell, slope)
    street_lamps(sc, roads, wcell, list(VILLAGES.keys()))
    n_bridges = bridges(sc, roads, wcell, hcell)
    near_water = np.zeros(CSHAPE)
    for name in ("mirror", "misty"):
        ld = lake_d(CX, CZ, LAKES[name])
        near_water = np.maximum(near_water, smoothstep(1.6, 1.1, ld))
    pads_mask = np.zeros(CSHAPE, bool)
    for px, pz, pr, _ in PADS[:5]:
        pads_mask |= np.hypot(CX - px, CZ - pz) < pr * 0.8
    water_props(sc, hcell, wcell, slope, lake_surf)
    scatter_flora(sc, bw, hcell, wcell, slope, road_d, near_water, pads_mask)

    meta = {
        "width": W, "height": H, "sea_level": SEA,
        "spawn": [PLAZA[0], PLAZA[1] + 7],
        "lake_surface": round(lake_surf["mirror"], 2),
        "waterfalls": falls,
        "monsters": [
            {"id": "sakura_slime", "x": 500, "z": 700, "radius": 6, "count": 3, "respawn": 8},
            {"id": "sakura_slime", "x": 620, "z": 744, "radius": 6, "count": 3, "respawn": 8},
            {"id": "sakura_slime", "x": 380, "z": 680, "radius": 6, "count": 3, "respawn": 8},
            {"id": "sakura_slime", "x": 476, "z": 520, "radius": 6, "count": 3, "respawn": 8},
            {"id": "sakura_slime", "x": 262, "z": 664, "radius": 7, "count": 4, "respawn": 9},
            {"id": "forest_goblin", "x": 150, "z": 640, "radius": 6, "count": 3, "respawn": 14},
            {"id": "forest_goblin", "x": 282, "z": 500, "radius": 6, "count": 3, "respawn": 14},
            {"id": "sakura_slime", "x": 760, "z": 702, "radius": 7, "count": 4, "respawn": 9},
            {"id": "sakura_slime", "x": 884, "z": 620, "radius": 6, "count": 3, "respawn": 9},
            {"id": "forest_goblin", "x": 322, "z": 318, "radius": 7, "count": 4, "respawn": 14},
            {"id": "forest_goblin", "x": 232, "z": 384, "radius": 6, "count": 3, "respawn": 14},
            {"id": "forest_goblin", "x": 452, "z": 382, "radius": 7, "count": 4, "respawn": 14},
            {"id": "forest_goblin", "x": 540, "z": 276, "radius": 7, "count": 4, "respawn": 14},
            {"id": "forest_goblin", "x": 724, "z": 372, "radius": 7, "count": 4, "respawn": 14},
            {"id": "forest_goblin", "x": 452, "z": 140, "radius": 7, "count": 4, "respawn": 14},
            {"id": "forest_goblin", "x": 700, "z": 176, "radius": 7, "count": 4, "respawn": 14},
        ],
        # 전망 지점: 반지름 안에 서면 카메라가 고개를 든다 (평소 38°, 21m). look_ahead·look_up: 바라보는 점을 북쪽·위로 (m)
        "viewpoints": [
            {"name": "약속의 언덕", "x": 422, "z": 728, "radius": 8, "pitch": 14, "distance": 26, "look_ahead": 12, "look_up": 4},
            {"name": "거울 호수 나루", "x": 622, "z": 532, "radius": 7, "pitch": 12, "distance": 26, "look_ahead": 10, "look_up": 6},
            {"name": "등대 전망대", "x": 990, "z": 414, "radius": 7, "pitch": 12, "distance": 24, "look_ahead": 8, "look_up": 2},
            {"name": "풍차 언덕", "x": 880, "z": 566, "radius": 7, "pitch": 16, "distance": 24, "look_ahead": 4, "look_up": 4},
            {"name": "신사 언덕", "x": 114, "z": 500, "radius": 7, "pitch": 14, "distance": 24, "look_ahead": 8, "look_up": 3},
            {"name": "노을 절벽", "x": 100, "z": 372, "radius": 8, "pitch": 8, "distance": 26, "look_ahead": 10, "look_up": 2},
            {"name": "안개 호숫가", "x": 662, "z": 340, "radius": 7, "pitch": 12, "distance": 26, "look_ahead": 10, "look_up": 8},
            {"name": "눈꽃 마을 언덕", "x": 560, "z": 154, "radius": 7, "pitch": 10, "distance": 28, "look_ahead": 10, "look_up": 12},
        ],
        "statues": [{"id": sid, "name": name, "x": x, "z": z} for sid, name, x, z in STATUES],
        "zone_cell": ZONE_CELL,
        "zone_names": ZONE_NAMES,
    }

    def wf(name, arr):
        with open(os.path.join(OUT, name), "wb") as f:
            f.write(arr.astype("<f4").tobytes())

    wf("terrain.f32", h)
    wf("water.f32", water)
    with open(os.path.join(OUT, "ground.u8"), "wb") as f:
        f.write(ground.astype(np.uint8).tobytes())
    with open(os.path.join(OUT, "zones.u8"), "wb") as f:
        f.write(zones.astype(np.uint8).tobytes())
    with open(os.path.join(OUT, "props.json"), "w", encoding="utf-8") as f:
        json.dump(sc.items, f, ensure_ascii=False, separators=(",", ":"))
    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    preview(h, water, ground, sc.items, roads, falls)
    counts = {}
    for p in sc.items:
        counts[p["type"]] = counts.get(p["type"], 0) + 1
    print("소품", len(sc.items), dict(sorted(counts.items(), key=lambda kv: -kv[1])[:30]))
    print("다리", n_bridges, "폭포", [(f["x"], f["top_z"], f["top_y"], f["bottom_y"]) for f in falls])
    print("호수 수면", {k: round(v, 2) for k, v in lake_surf.items()})
    print("높이 %.1f .. %.1f" % (h.min(), h.max()))


PALETTE = {
    GRASS: (104, 168, 72), FOREST: (58, 110, 58), MEADOW: (140, 186, 84), DIRT: (176, 138, 92),
    COBBLE: (160, 156, 150), SAND: (232, 214, 160), SAKURA: (238, 176, 196), AUTUMN: (214, 120, 56),
    GRAVEL: (130, 124, 116), PEBBLE: (170, 162, 150), SNOW: (236, 240, 248), TULIP: (226, 88, 110),
    LAVENDER: (160, 120, 210), MOSS: (70, 120, 70), ROCKY: (128, 128, 136), STONE: (190, 186, 176),
    GINKGO: (236, 196, 64), WHEAT: (222, 196, 110), ICE: (186, 220, 240), FROSTGRASS: (206, 210, 196),
    GOLDGRASS: (176, 168, 70),
}


def preview(h, water, ground, props, roads, falls):
    hc = h[:-1, :-1]
    gx = np.gradient(hc, axis=1)
    gz = np.gradient(hc, axis=0)
    shade = np.clip(1.0 + (-gx * 0.6 - gz * 0.6) * 0.5, 0.45, 1.3)
    lut = np.zeros((256, 3))
    for k, c in PALETTE.items():
        lut[k] = c
    col = lut[ground]
    steep = np.maximum(np.abs(gx), np.abs(gz)) > 1.0
    col[steep] = (120, 110, 100)
    wc = water[:-1, :-1]
    wet = (wc > -999) & (wc > hc)
    depth = np.clip(wc - hc, 0, 6)
    wcol = np.stack([60 - depth * 8, 150 - depth * 12, 200 - depth * 6], -1)
    col = np.where(wet[..., None], wcol, col * shade[..., None])
    img = np.clip(col, 0, 255).astype(np.uint8)
    marks = {"tree": (30, 80, 30), "house": (200, 60, 50), "big": (255, 255, 255)}
    big = {"lighthouse", "windmill", "torii", "shrine", "pavilion", "fountain", "bridge", "dock", "temple", "pagoda",
           "castle_ruin", "tree_giant", "tree_world", "tree_lone", "tree_sakura_giant", "watchtower_ruin", "onsen", "tent"}
    for p in props:
        t = p["type"]
        x, z = int(p["x"]), int(p["z"])
        if not (1 <= x < W - 1 and 1 <= z < H - 1):
            continue
        if t in big:
            img[z - 2:z + 3, x - 2:x + 3] = marks["big"]
        elif "front_z" in p:
            img[z - 2:z + 3, x - 2:x + 3] = marks["house"]
        elif t.startswith("tree"):
            img[z, x] = marks["tree"]
    for f in falls:
        x, z = int(f["x"]), int(f["top_z"])
        img[max(0, z - 3):z + 4, max(0, x - 3):x + 4] = (120, 220, 255)
    dst = os.path.join(ROOT, "docs", "design")
    os.makedirs(dst, exist_ok=True)
    Image.fromarray(img).save(os.path.join(dst, "map_preview.png"))


if __name__ == "__main__":
    main()
