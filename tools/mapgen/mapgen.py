"""하루미 대륙 지도 생성기 (1024x1024m). 설계와 근거: docs/design/WORLD.md, docs/design/M10_kingdoms.md

M10 둥근 대륙: 하루미 마을이 한가운데 (512, 560), 둘레 120~150m 가 초보 사냥터(바람의 초원).
여섯 지역이 60°씩 마을을 둘러싸고(북서 고목·북 서리·북동 안개 / 남서 단풍·남 벚꽃·남동 꽃), 지역 끝에 왕국이 하나씩.
카메라는 북쪽(-z)을 본다. 절벽 A 가 마을 북쪽을 고리로 감싸고 서·동으로는 해안까지 뻗는다 —
북쪽 셋은 절벽 위(높고 어렵다), 남쪽 셋은 아래. 절벽은 늘 마을(남쪽)을 보고 서서 폭포가 화면에 보인다.
서리 고원은 절벽 B(마을 둘레 335m 고리, 북쪽 부채꼴에서만 솟는다) 위, 그 뒤가 설산.

출력 (data/world/):
  terrain.f32  꼭짓점 높이 (W+1)*(H+1) float32, 행 우선(z 바깥, x 안쪽)
  water.f32    꼭짓점 수면 높이, 물 없음 = -1000
  ground.u8    칸 지면 종류 W*H
  zones.u8     8m 칸 지역 번호 (이름은 meta.zone_names)
  props.json   소품 배치
  meta.json    크기·시작점·폭포·전망 지점·전송석·마을/왕국·지역
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
                   poly_field, polyline_length, ridged, smoothstep)

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


# 하루미 마을 = 대륙 한가운데. 지역은 여기서 본 방향(북 0°, 시계 방향)으로 나눈다
CENTER = (512.0, 560.0)


def polar(X, Z):
    """마을에서 거리(m)와 방향(°): 북 0, 동 +90, 남 ±180, 서 -90."""
    dx, dz = X - CENTER[0], Z - CENTER[1]
    return np.hypot(dx, dz), np.degrees(np.arctan2(dx, -dz))


# 서쪽 해안 (z, x): 북서는 노을 만이 깊이 들어온다(엘더 왕국 북쪽 노을 절벽에서 북북서 바다로 해가 진다),
# 남서 끝은 둥글게 돌아 남쪽 해안으로
WEST_PTS = [(0, 330), (60, 296), (120, 248), (180, 190), (230, 132), (270, 92), (320, 62), (380, 46), (440, 38),
            (500, 42), (560, 34), (620, 42), (680, 32), (720, 40), (760, 58), (800, 52), (840, 70), (880, 98),
            (920, 140), (960, 200), (1000, 262), (1024, 300)]
# 동쪽 해안 (z, x): 등대 곶(z≈610)이 튀어나오고, 남동 끝은 둥글게
EAST_PTS = [(0, 1000), (100, 1004), (200, 998), (260, 990), (320, 998), (380, 1004), (440, 992), (500, 988),
            (560, 996), (600, 1016), (630, 1012), (680, 994), (740, 990), (800, 982), (860, 962), (900, 934),
            (940, 892), (1000, 830), (1024, 800)]
_west, _east = spline1d(WEST_PTS), spline1d(EAST_PTS)


def west_coast(z):
    """서쪽 물가의 x."""
    return _west(z) + wiggle(z, 101, 0, 10, 3)


def east_coast(z):
    """동쪽 물가의 x. z≈610 에 등대 곶."""
    return _east(z) + wiggle(z, 106, 0, 10, 3) * (1 - np.exp(-((z - 610) / 30) ** 2))


def south_coast(x):
    """남쪽 물가의 z. 하나미 왕도 앞(x≈480)은 항구 자리로 붙든다."""
    base = lambda t: 962 + wiggle(t, 111, 60, 20, 3)  # noqa: E731
    return pinned(base, x, [(480, 972)], 60)


# 절벽 A: 서쪽 해안 → 마을 북쪽을 반지름 140m 고리로 감싸고 → 동쪽 해안. 선 북쪽이 위(고목·서리·안개)
CLIFF_A_PTS = [(-40, 612), (60, 606), (150, 600), (250, 594), (330, 586), (370, 556), (384, 512), (408, 470),
               (446, 436), (480, 418), (512, 413), (544, 418), (578, 436), (616, 470), (640, 512), (654, 556),
               (694, 586), (774, 594), (874, 600), (964, 606), (1064, 612)]
CLIFF_A = meander(catmull(CLIFF_A_PTS, 2.0), 9.0, 70.0, 121)
# 절벽 B: 마을 둘레 335m 고리인데 북쪽 부채꼴(서리 고원)에서만 솟는다 — 양옆은 비탈로 잦아든다
CLIFF_B_R = 335.0
CLIFF_B_LOBE = (26.0, 42.0)   # 이 각도 안은 온 높이, 바깥은 0


def cliff_sdf(line, shape, off):
    """절벽선까지 부호 거리 (+ = 아래·마을 쪽, - = 위)와 선을 따라 잰 거리 s(m).
    선에서 90m 넘게 먼 곳은 ±90 (절벽 단은 그 안에서만 쓴다). 위/아래는 선 + 지도 위쪽으로 닫은 다각형으로 가른다."""
    from PIL import Image, ImageDraw
    d, s = poly_field(line, 90.0, shape, off)
    d = np.where(np.isinf(d), 90.0, d)
    poly = [(x - off, z - off) for x, z in line] + [(line[-1][0] + 60, -200), (line[0][0] - 60, -200)]
    im = Image.new("L", (shape[1], shape[0]), 0)
    ImageDraw.Draw(im).polygon(poly, fill=1)
    upper = np.asarray(im, bool)
    return np.where(upper, -d, d), s * polyline_length(line)


def cliff_b(X, Z):
    """절벽 B: 부호 거리(+ = 안쪽·아래), 둘레를 따라 잰 거리, 부채꼴 무게."""
    r, th = polar(X, Z)
    rb = CLIFF_B_R + 14 * n1(th * 0.05, 131) + 6 * n1(th * 0.2, 132)
    lobe = smoothstep(CLIFF_B_LOBE[1], CLIFF_B_LOBE[0], np.abs(th))
    return rb - r, np.radians(th) * CLIFF_B_R, lobe


# 길이 절벽을 넘는 곳 (x, z, 반지름): 여기선 절벽 대신 긴 오르막
RAMPS_A = [(512, 412, 22), (388, 486, 22), (640, 490, 22), (150, 600, 26), (880, 602, 26)]
RAMPS_B = [(512, 226, 30), (322, 268, 26), (706, 262, 26)]


def ramp_mask(X, Z, ramps):
    m = np.zeros_like(X)
    for rx, rz, rw in ramps:
        m = np.maximum(m, np.exp(-((X - rx) ** 2 + (Z - rz) ** 2) / (rw * rw)))
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

PLAZA = CENTER

# 왕국: 지역 끝. 성은 북쪽 끝(앞이 남쪽 = 카메라), 그 앞에 광장, 남쪽으로 골목과 집.
# 성 그림은 keep_<id>·castle_front_<id>, houses = 이 왕국 집 종류
KINGDOMS = [
    dict(id="hanami", name="하나미 왕도", c=(470.0, 896.0), biome="sakura", level=10, yard="hanami", lamp="stone_lantern",
         houses=["jhouse", "jhouse", "jhouse", "teahouse", "hokora", "jhouse", "teahouse"]),
    dict(id="floren", name="플로렌 왕국", c=(840.0, 792.0), biome="flower", level=10, yard="flower", lamp="street_lamp",
         houses=["cottage", "cottage", "greenhouse", "house_yellow", "flower_shop", "bakery", "cottage"]),
    dict(id="hongyeop", name="홍엽 왕도", c=(170.0, 772.0), biome="autumn", level=16, yard="hongyeop", lamp="stone_lantern",
         houses=["hanok", "hanok", "hanok", "teahouse", "hanok"]),
    dict(id="elder", name="엘더 왕국", c=(186.0, 352.0), biome="ancient", level=18, yard="elder", lamp="stone_lantern",
         houses=["treehouse", "treehouse", "stump_house", "treehouse"]),
    dict(id="lumie", name="루미에 왕국", c=(842.0, 352.0), biome="misty", level=20, yard="lumie", lamp="street_lamp",
         houses=["lakehouse", "lakehouse", "house_blue", "house_fisher", "boathouse", "lakehouse"]),
    dict(id="nord", name="노르드 왕국", c=(512.0, 112.0), biome="frost", level=26, yard="snow", lamp="street_lamp",
         houses=["chalet", "chalet", "lodge", "chalet"]),
]
KINGDOM_R = 66.0
KINGDOM_OF = {k["id"]: k for k in KINGDOMS}

# 지역 입구 (큰길 위 갈림목): 전송석 + 이정표. (id, 이름, x, z, 요구 레벨)
HUBS = [("sakura", "벚꽃 골짜기 입구", 500, 712, 5), ("flower", "꽃 들판 입구", 650, 652, 6),
        ("autumn", "단풍 협곡 입구", 372, 652, 12), ("ancient", "고목의 숲 입구", 340, 452, 14),
        ("misty", "안개 호수 입구", 684, 452, 16), ("frost", "서리 고원 입구", 512, 352, 22)]

# 지역 우두머리 자리 (왕국 앞 길목에서 조금 비켜) — data/world/spawns.json 의 fixed 무리와 같아야 한다
BOSS_SPOTS = {"goblin_chief": (466, 818), "scarecrow_king": (750, 748), "tengu_captain": (262, 672),
              "treant_elder": (284, 376), "mist_queen": (792, 430), "yeti_king": (542, 198)}


def kingdom_streets(k):
    """왕국 골목: 성문에서 남쪽으로 한 줄, 광장을 지나는 동서 큰 골목, 남쪽 뒷골목."""
    cx, cz = k["c"]
    i = k["id"]
    return [(f"s_{i}_ns", [(cx, cz - 26), (cx, cz - 13), (cx, cz + 8), (cx + 2, cz + 30), (cx, cz + 58)], 1.6, "street"),
            (f"s_{i}_ew", [(cx - 56, cz + 10), (cx, cz + 8), (cx + 56, cz + 6)], 1.5, "street"),
            (f"s_{i}_s", [(cx - 42, cz + 36), (cx, cz + 34), (cx + 42, cz + 36)], 1.2, "street")]


# 평평하게 고르는 자리: (x, z, 반지름, 목표 높이 또는 None=가운데 높이, 덧올림)
PADS = ([(PLAZA[0], PLAZA[1], 64, None, 0.0)]
        + [(k["c"][0], k["c"][1], KINGDOM_R, None, 0.0) for k in KINGDOMS]
        + [(k["c"][0], k["c"][1] - 31, 13, None, 2.0) for k in KINGDOMS]           # 성 언덕
        + [(x, z, 14, None, 0.0) for _, _, x, z, _ in HUBS]
        + [(x, z, 12, None, 0.0) for x, z in BOSS_SPOTS.values()]
        + [(446, 600, 10, None, 0.0),    # 야영지
           (606, 600, 10, None, 0.0),    # 거석 원
           (318, 636, 18, None, 0.0),    # 단풍 산사
           (588, 764, 14, None, 0.0),    # 벚꽃 신사
           (600, 300, 14, None, 0.0),    # 온천
           (130, 268, 12, None, 0.0),    # 노을 절벽
           (646, 186, 40, None, 0.0)])   # 얼어붙은 호수
TOWN_PADS = 1 + len(KINGDOMS)   # PADS 앞쪽 = 마을·왕국 (큰 나무를 안 심는다)

# 언덕: (x, z, 반지름, 높이)
BUMPS = [
    (462, 472, 26, 6.5),    # 약속의 언덕
    (606, 600, 20, 3.0),    # 거석 언덕
    (430, 640, 20, 5.0),    # 망루 언덕
    (440, 780, 22, 4.0),    # 천년 벚나무
    (588, 764, 22, 7.0),    # 벚꽃 신사 언덕
    (628, 772, 18, 5.0), (780, 664, 18, 5.0), (934, 664, 16, 4.5),   # 풍차 언덕
    (1000, 612, 18, 5.0),   # 등대 곶
    (318, 636, 18, 4.0),    # 단풍 산사
    (430, 262, 20, 7.0),    # 별 관측소
    (130, 268, 16, 4.0),    # 노을 절벽
]

# 바위 언덕: (x, z, 반지름, 높이). 길·마을·강을 비켜서
TORS = [(436, 700, 10, 5.0), (582, 700, 9, 4.5), (360, 830, 10, 5.0), (660, 900, 9, 4.0), (900, 860, 10, 5.0),
        (80, 660, 9, 6.0), (90, 520, 9, 6.0), (250, 470, 10, 6.0), (360, 330, 10, 6.0), (600, 380, 9, 5.0),
        (790, 520, 10, 6.0), (940, 430, 8, 5.0), (700, 70, 12, 7.0), (420, 80, 10, 6.0), (880, 140, 12, 8.0),
        (220, 640, 9, 5.0), (700, 560, 8, 4.0)]

LAKES = {
    "mirror": dict(c=(572.0, 482.0), r=(38.0, 24.0), seed=31),  # 거울 호수: 절벽 고리에서 큰 폭포가 떨어진다
    "misty": dict(c=(712.0, 396.0), r=(54.0, 32.0), seed=37),   # 안개 호수: 북쪽 절벽 B 에서 폭포
    "pond": dict(c=(452.0, 612.0), r=(10.0, 7.0), seed=39),     # 야영지 연못
    # 들판 곳곳의 작은 못 — 갈대·수련, 버드나무 (넓은 풀밭이 끝없이 같아 보이지 않게)
    "pond_e": dict(c=(612.0, 548.0), r=(11.0, 7.0), seed=45),
    "pond_w": dict(c=(322.0, 506.0), r=(14.0, 9.0), seed=43),
    "pond_s": dict(c=(404.0, 822.0), r=(13.0, 8.0), seed=47),
    "pond_f": dict(c=(740.0, 806.0), r=(12.0, 8.0), seed=49),
    "pond_k": dict(c=(300.0, 820.0), r=(11.0, 7.0), seed=53),
    "tarn": dict(c=(430.0, 170.0), r=(20.0, 12.0), seed=51),    # 서리 고원 산정 호수
}
ISLET_C, ISLET_R = (582.0, 486.0), 4.0

# 강: 흐르는 방향으로. 수면은 지형을 따라 '내려가기만' 하게 계산하고, 절벽에서 뚝 떨어지는 곳이 폭포가 된다.
RIVERS = [
    dict(name="north", pts=[(596, 10), (600, 70), (606, 130), (614, 190), (624, 236), (640, 270), (664, 310),
                            (684, 350), (700, 378)], bed=1.8, bank=5.4, to="misty"),
    dict(name="falls", pts=[(676, 420), (652, 434), (630, 444), (610, 452), (594, 462), (582, 470)],
         bed=2.2, bank=5.0, frm="misty", to="mirror", meander=3.0),
    dict(name="south", pts=[(566, 506), (556, 526), (548, 548), (546, 575), (548, 610), (544, 660), (538, 720),
                            (534, 780), (536, 840), (542, 890), (544, 940), (540, 1012)],
         bed=2.6, bank=6.4, frm="mirror"),
    dict(name="west", pts=[(388, 30), (380, 90), (366, 150), (344, 210), (318, 270), (296, 330), (282, 390),
                           (280, 450), (284, 520), (288, 590), (280, 640), (262, 690), (258, 740), (256, 800),
                           (236, 850), (196, 890), (150, 925), (104, 966)],
         bed=2.4, bank=6.2),
    # 초원 북서쪽 개울: 절벽 고리에서 작은 폭포로 떨어져 마을 북쪽을 지나 큰 강에 든다
    dict(name="creek", pts=[(436, 392), (440, 420), (450, 450), (470, 488), (500, 514), (530, 526), (554, 530)],
         bed=1.0, bank=3.4, meander=10.0, wave=70.0, to="south"),
    # 꽃 들판 실개울: 동쪽 바다로
    dict(name="brook", pts=[(800, 612), (826, 660), (868, 700), (910, 720), (950, 740), (1000, 752)],
         bed=0.9, bank=3.0, meander=10.0, wave=70.0),
]
# 강을 곧게 펴 두는 곳 (x, z, 반지름): 마을·왕국 한가운데·폭포 앞 — 굽이가 집·광장·폭포 자리를 덮치지 않게
RIVER_CALM = [(PLAZA[0], PLAZA[1], 80), (540, 896, 60), (610, 452, 34), (444, 436, 30), (288, 594, 36),
              (624, 236, 34), (330, 240, 30), (250, 790, 50)]

# 길: (이름, 점들, 반폭, 종류). street = 마을 안 포석, trail = 좁은 오솔길
ROADS = [
    # 큰길 여섯: 마을 → 지역 입구(전송석) → 왕국
    ("r_frost", [(512, 548), (510, 512), (506, 470), (510, 432), (512, 412), (514, 380), (512, 352), (516, 310),
                 (514, 268), (512, 236), (512, 206), (512, 170)], 1.6, "road"),
    ("r_ancient", [(500, 548), (476, 534), (446, 516), (418, 500), (390, 486), (366, 470), (340, 452), (300, 428),
                   (266, 406), (250, 384), (242, 362)], 1.5, "road"),
    ("r_misty", [(524, 548), (552, 536), (584, 524), (614, 510), (640, 490), (662, 470), (684, 452), (720, 440),
                 (760, 418), (780, 390), (786, 362)], 1.5, "road"),
    ("r_flower", [(566, 568), (590, 590), (620, 620), (650, 652), (690, 676), (730, 700), (770, 730), (784, 760),
                  (784, 800)], 1.5, "road"),
    ("r_sakura", [(512, 568), (512, 600), (508, 640), (504, 680), (500, 712), (498, 750), (500, 790), (506, 830),
                  (516, 866), (526, 902)], 1.5, "road"),
    ("r_autumn", [(455, 572), (430, 595), (400, 625), (372, 652), (340, 672), (300, 690), (264, 712), (240, 740),
                  (226, 782)], 1.5, "road"),
    # 바깥 고리길: 왕국끼리 (막다른 길이 없다)
    ("ring_nw", [(242, 362), (262, 330), (296, 292), (322, 268), (360, 228), (400, 190), (440, 156), (456, 122)], 1.3, "road"),
    ("ring_ne", [(568, 118), (606, 132), (646, 156), (680, 200), (700, 246), (712, 280), (744, 312), (786, 360)], 1.3, "road"),
    ("ring_w", [(130, 362), (122, 400), (124, 460), (132, 520), (144, 570), (150, 600), (146, 650), (140, 700),
                (126, 740), (114, 782)], 1.3, "road"),
    ("ring_e", [(898, 358), (912, 400), (914, 460), (904, 520), (890, 570), (880, 602), (878, 650), (884, 700),
                (892, 740), (896, 798)], 1.3, "road"),
    ("ring_sw", [(170, 830), (220, 860), (290, 885), (360, 900), (414, 906)], 1.3, "road"),
    ("ring_se", [(512, 932), (560, 930), (620, 910), (690, 880), (750, 850), (798, 830)], 1.3, "road"),
    # 오솔길: 볼거리로
    ("t_tree", [(508, 500), (484, 486), (468, 478)], 0.9, "trail"),
    ("t_lake", [(584, 524), (582, 512)], 0.9, "trail"),
    ("t_stones", [(590, 590), (604, 598)], 0.9, "trail"),
    ("t_camp", [(430, 595), (446, 604)], 0.9, "trail"),
    ("t_tower", [(512, 600), (470, 620), (440, 636)], 0.9, "trail"),
    ("t_cape", [(890, 590), (940, 604), (990, 610)], 1.0, "trail"),
    ("t_shrine", [(500, 800), (540, 806), (576, 810), (588, 798), (588, 768)], 0.9, "trail"),
    ("t_temple", [(334, 676), (324, 664), (318, 650)], 0.9, "trail"),
    ("t_observatory", [(514, 290), (470, 276), (440, 268)], 0.9, "trail"),
    ("t_onsen", [(516, 310), (560, 304), (590, 300)], 0.9, "trail"),
    ("t_sunset", [(150, 330), (140, 300), (132, 276)], 0.9, "trail"),
    ("t_ruin", [(720, 440), (750, 462), (766, 474)], 0.9, "trail"),
    ("t_stump", [(300, 428), (318, 406), (328, 396)], 0.9, "trail"),
    # 하루미 마을 골목 (포석)
    ("s_harumi_w", [(506, 562), (480, 566), (455, 572)], 1.5, "street"),
    ("s_harumi_e", [(518, 562), (540, 564), (566, 568)], 1.5, "street"),
    ("s_harumi_n", [(512, 554), (511, 530), (512, 506)], 1.5, "street"),
    ("s_harumi_s", [(512, 566), (513, 590), (512, 612)], 1.4, "street"),
    ("s_harumi_back", [(474, 538), (496, 530), (512, 530)], 1.1, "street"),
] + [s for k in KINGDOMS for s in kingdom_streets(k)]

# 꽃밭·밀밭: (가운데 x, z, 반폭, 반깊이, 종류, 기울기). 둥근 네모를 잡음으로 흔든 모양 — 자로 그은 네모 밭은 인공적이었다
FIELDS = [
    (700, 742, 30, 22, TULIP, 0.2), (650, 822, 34, 20, TULIP, -0.15), (712, 920, 34, 18, TULIP, 0.1),
    (722, 608, 24, 16, LAVENDER, -0.25), (940, 790, 18, 28, LAVENDER, 0.3),
    (460, 664, 28, 16, WHEAT, 0.15), (600, 866, 30, 18, WHEAT, -0.1), (380, 866, 26, 16, WHEAT, 0.1),
]


def field_mask(X, Z, f, pad=0.0):
    """밭 하나의 모양: 둥근 네모(초타원)를 비스듬히 돌리고 가장자리를 잡음으로 흔든다."""
    cx, cz, rx, rz, kind, rot = f
    c, sn = math.cos(rot), math.sin(rot)
    dx, dz = X - cx, Z - cz
    u, v = (dx * c + dz * sn) / (rx + pad), (-dx * sn + dz * c) / (rz + pad)
    r = (np.abs(u) ** 3 + np.abs(v) ** 3) ** (1 / 3)
    return r + 0.12 * fbm(X * 0.05, Z * 0.05, int(cx) + int(cz), 3) < 1.0


ZONE_NAMES = ["", "바람의 초원", "하루미 마을", "벚꽃 골짜기", "꽃 들판", "등대 곶", "단풍 협곡", "고목의 숲",
              "안개 호수", "서리 고원", "거울 호수"] + [k["name"] for k in KINGDOMS]

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
    # 구릉: 넓은 기복 + 작은 언덕(BotW 의 작은 삼각형 — 걸음의 박자를 바꾼다). 바다 쪽(남)으로 조금씩 낮아진다
    h = (4.0 + 3.0 * fbm(X * 0.004, Z * 0.004, 5) + 2.5 * fbm(X * 0.015, Z * 0.015, 6)
         + 3.0 * smoothstep(960, 600, Z))
    for bx, bz, br, bh in BUMPS:
        h += bh * gauss(X - bx, Z - bz, br)
    rng = random.Random(9)
    # 구릉은 크기를 섞는다: 큰 언덕 몇 + 작은 둔덕 여럿 (한 크기의 혹만 뿌리면 고른 곰보 땅이 됐다)
    for _ in range(18):
        bx, bz = rng.uniform(60, 960), rng.uniform(200, 940)
        h += rng.uniform(2.5, 6.0) * gauss(X - bx, Z - bz, rng.uniform(30, 60))
    for _ in range(60):
        bx, bz = rng.uniform(60, 960), rng.uniform(200, 940)
        h += rng.uniform(0.8, 2.6) * gauss(X - bx, Z - bz, rng.uniform(5, 16))
    # 바위 언덕(작은 메사): 비탈이 가팔라 바위가 드러나고 위는 풀밭 — 넓은 들판의 길잡이
    for tx, tz, tr, th in TORS:
        d = np.hypot(X - tx, Z - tz) + 3.5 * fbm(X * 0.08, Z * 0.08, int(tx), 2)
        h += th * smoothstep(tr, tr * 0.55, d)
    # 서쪽 강을 따라 골이 진다 (단풍 협곡)
    dw = river_lines["west"][0]
    dwf = np.where(np.isinf(dw), 999.0, dw)
    h -= 3.5 * np.exp(-(dwf / 45.0) ** 2)

    # 절벽 두 단: A 는 마을 둘레 고리 + 서·동 해안까지, B 는 서리 고원 부채꼴만
    da, sa = cliff_sdf(CLIFF_A, VSHAPE, 0.0)
    db, sb, lobe = cliff_b(X, Z)
    ta = tier_step(da, ramp_mask(X, Z, RAMPS_A), sa, 113)
    tb = tier_step(db, ramp_mask(X, Z, RAMPS_B), sb, 123) * lobe
    rise_a = 16 + 4 * n1(sa * 0.01, 41)
    rise_b = 22 + 6 * n1(sb * 0.008, 42)
    t2 = 5 * fbm(X * 0.01, Z * 0.01, 31) - 5 * np.exp(-(dwf / 18.0) ** 2)   # 강이 골을 판다
    # 설산: 능선 잡음의 잔 옥타브가 뾰족한 가시를 세웠다 — 옥타브를 줄이고 좌표를 비틀어 둥글고 긴 산줄기로
    wx = X + 30 * fbm(X * 0.006, Z * 0.006, 65, 2)
    wz = Z + 30 * fbm(X * 0.006, Z * 0.006, 66, 2)
    rid = ridged(wx * 0.009, wz * 0.009, 61, octaves=2) ** 1.4
    north = smoothstep(185, 15, Z)
    t3 = 5 * fbm(X * 0.015, Z * 0.015, 62, 3) * smoothstep(-20, 60, -db) + north * (14 + 40 * rid)
    h = h + ta * (rise_a + t2 + t3) + tb * (rise_b - t2 * 0.5)

    # 평평한 자리 (차례대로: 왕국을 고른 뒤 그 위에 성 언덕)
    for px, pz, pr, target, lift in PADS:
        d = np.hypot(X - px, Z - pz)
        m = smoothstep(pr, pr * 0.6, d)
        if target is None:
            target = float(h[int(pz), int(px)])
        tilt = (PLAZA[1] + 60 - Z) * 0.012 if (px, pz) == PLAZA else 0.0
        h = lerp(h, target + lift + tilt, m)

    # 바다: 남쪽·남서는 모래 해변, 나머지는 바위 절벽
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

_CELL_CLIFFS = None


def cell_cliffs():
    """칸 격자의 절벽 A 부호 거리, 절벽 B 부호 거리 (한 번만 계산)."""
    global _CELL_CLIFFS
    if _CELL_CLIFFS is None:
        da, _ = cliff_sdf(CLIFF_A, CSHAPE, 0.5)
        db, _, _ = cliff_b(CX, CZ)
        _CELL_CLIFFS = (da, db)
    return _CELL_CLIFFS


def biome_weights():
    """지역 무게: 절벽 A 위/아래 + 마을에서 본 방향. 경계는 잡음으로 흔들어 섞는다.
    아래: 마을 둘레(남쪽 120m, 절벽 고리 안쪽 전부)는 초원, 그 밖은 남서 단풍 · 남 벚꽃 · 남동 꽃(동쪽 곶은 바닷가).
    위: 북서 고목 · 북 서리 · 북동 안개."""
    da, _ = cell_cliffs()
    r, th = polar(CX, CZ)
    thw = th + 16 * fbm(CX * 0.004, CZ * 0.004, 71) + 5 * fbm(CX * 0.02, CZ * 0.02, 72)
    rw = r + 18 * fbm(CX * 0.01, CZ * 0.01, 73)
    up = smoothstep(3, -3, da)
    low = 1 - up
    rbeg = 122 + 34 * smoothstep(110, 60, np.abs(thw))
    beg = low * smoothstep(rbeg + 12, rbeg - 12, rw)
    out = low * (1 - beg)
    sak = smoothstep(138, 162, np.abs(thw))
    west = (thw < 0).astype(np.float64)
    fro = smoothstep(42, 18, np.abs(thw))
    ec = east_coast(CZ)
    cape = smoothstep(ec - 75, ec - 45, CX) * smoothstep(500, 540, CZ) * smoothstep(720, 680, CZ)
    return {
        "grass": beg,
        "sakura": out * sak,
        "autumn": out * (1 - sak) * west,
        "flower": out * (1 - sak) * (1 - west) * (1 - cape),
        "coast": out * (1 - sak) * (1 - west) * cape,
        "ancient": up * (1 - fro) * west,
        "frost": up * fro,
        "misty": up * (1 - fro) * (1 - west),
    }


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
    # (절벽 A·B 사이 서리 부채꼴은 산기슭이라 거의 마른 풀)
    fringe = smoothstep(-40, 10, cell_cliffs()[1])
    g[is_["frost"] & (fbm(CX * 0.02, CZ * 0.02, 53, 3) + fringe * 0.7 > 0.42)] = FROSTGRASS
    # 절벽 B 아래 산기슭: 마른 풀 사이로 푸른 풀밭·눈 얼룩 (한 색 판으로 넓게 깔리면 밋밋했다)
    foot = is_["frost"] & (cell_cliffs()[1] > 4)
    fn = fbm(CX * 0.025, CZ * 0.025, 55, 3)
    g[foot & (fn < -0.18)] = GRASS
    g[foot & (fn < -0.32)] = MEADOW
    g[foot & (fn > 0.3)] = SNOW
    g[is_["frost"] & ((slope > 1.3) | (hc > 92) | ((fbm(CX * 0.035, CZ * 0.035, 54, 3) > 0.48) & (slope > 0.45)))] = ROCKY
    for f in FIELDS:
        g[field_mask(CX, CZ, f)] = f[4]
    # 얼어붙은 호수
    g[np.hypot((CX - 646) / 34, (CZ - 186) / 20) + 0.1 * fbm(CX * 0.06, CZ * 0.06, 63, 2) < 1.0] = ICE
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
        else:
            # 서리 고원의 길은 다져진 돌길 — 흙길이 눈밭에서 갈색 띠로 튀었다
            frost = m & (bw["frost"] > 0.5)
            g[frost] = STONE
            g[m & ~frost & (slope > 0.32)] = STONE
            g[m & ~frost & (slope <= 0.32)] = DIRT
    g[np.hypot(CX - PLAZA[0], CZ - PLAZA[1]) < 12] = COBBLE
    for k in KINGDOMS:
        g[np.hypot(CX - k["c"][0], CZ - (k["c"][1] + 8)) < 10] = COBBLE
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
    zone_of = {"grass": 1, "sakura": 3, "flower": 4, "coast": 5, "autumn": 6, "ancient": 7, "misty": 8, "frost": 9}
    stack = np.stack([bw[k][iz, ix] for k in order])
    pick = np.argmax(stack, axis=0)
    z = np.zeros(zx.shape, np.uint8)
    for i, k in enumerate(order):
        z[pick == i] = zone_of[k]
    z[lake_d(zx, zz, LAKES["mirror"]) < 1.5] = 10
    z[lake_d(zx, zz, LAKES["misty"]) < 1.5] = 8
    z[np.hypot(zx - PLAZA[0], zz - PLAZA[1]) < 66] = 2
    for i, k in enumerate(KINGDOMS):
        z[np.hypot(zx - k["c"][0], zz - k["c"][1]) < KINGDOM_R] = 11 + i
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

    def reserve(self, x, z, r):
        """자리만 비워 둔다 (교관 자리·성 마당). free 는 옆 칸까지만 보니 큰 원은 작은 원으로 채운다."""
        n = int(r // 5)
        for i in range(-n, n + 1):
            for j in range(-n, n + 1):
                if (i * 5) ** 2 + (j * 5) ** 2 <= r * r:
                    self.grid.setdefault((int((x + i * 5) // 8), int((z + j * 5) // 8)), []).append((x + i * 5, z + j * 5, min(r, 3.6)))

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


def scatter_flora(sc, bw, hc, wc, slope, road_d, near_water, pads_mask, bare, seed=7):
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
    ok = dry & (slope < 0.9) & ~bare   # 광장 돌바닥엔 풀꽃도 안 난다
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


# 하루미 마을 광장 둘레 교관·안내인 자리 (data/npcs/*.json 과 같다) — 집·소품이 덮지 않게 비워 둔다
NPC_SPOTS = [(504, 573), (497, 560), (527, 560), (502, 549), (522, 549)]


def waystones():
    """전송석 (id, 이름, x, z, 요구 레벨): 마을 1 + 지역 입구 6 + 왕국 6. 게임은 meta.waystones 를 읽는다 (World/Waystones)."""
    out = [("harumi", "하루미 마을", 520.0, 574.0, 1)]
    out += [(hid, name, x + 6.0, float(z), lv) for hid, name, x, z, lv in HUBS]
    out += [("k_" + k["id"], k["name"], k["c"][0] - 9, k["c"][1] + 16, k["level"]) for k in KINGDOMS]
    return out


def add_castle(sc, k):
    """성: 북쪽 끝 언덕 위 본성 + 그 앞 성문 한 장(성문·양옆 성벽·양끝 탑을 한 그림으로 — 색이 맞고 판 그림이라
    동서로 뻗은 성벽만 그릴 수 있다). 성 마당은 비워 둔다 (집이 들어오지 않게)."""
    cx, cz = k["c"]
    s = k["id"]
    sc.add(f"keep_{s}", cx, cz - 31, 4.0, front_z=cz - 28)
    sc.add(f"castle_front_{s}", cx, cz - 18, 4.0)
    if s == "elder":
        sc.add("tree_world", cx + 2, cz - 50, 9.0)   # 세계수가 나무 궁전 뒤로 솟는다
    # 성 양옆·뒤로 이 지역 나무 — 성이 빈 들판에 홀로 서 있지 않게
    trees = YARD[k["yard"]]["trees"]
    for i, (dx, dz) in enumerate(((-22, -26), (22, -26), (-27, -40), (27, -40), (-14, -46), (14, -46), (-34, -20), (34, -20))):
        sc.add(trees[i % len(trees)], cx + dx, cz + dz, 2.4, v=i)
    sc.reserve(cx, cz - 28, 16)
    sc.add("goddess", cx + 11, cz + 1, 2.2, front_z=cz + 2)


def add_landmarks(sc, rng, hcell):
    # 하루미 마을 광장: 분수·시계탑·우물, 교관 자리
    sc.add("fountain", PLAZA[0], PLAZA[1], 3.2)
    sc.add("clocktower", 534, 545, 4.0, front_z=548)
    sc.add("well", 492, 579, 1.6)
    for x, z in NPC_SPOTS:
        sc.reserve(x, z, 1.4)
    for x, z in ((486, 549), (538, 576)):
        sc.add("market_stall", x, z, 1.8)
    for wid, name, x, z, lv in waystones():
        sc.add("waystone", x, z, 1.2)
        sc.reserve(x, z + 4, 5)   # 앞(남쪽)을 비운다 — 집이 서면 돌을 가린다
    # 지역 입구: 이정표·천막
    for hid, name, x, z, lv in HUBS:
        sc.add("signpost", x - 5, z + 2, 0.8)
        if sc.free(x + 12, z - 6, 2.6):
            sc.add("tent", x + 12, z - 6, 2.6)
    for k in KINGDOMS:
        add_castle(sc, k)

    # --- 바람의 초원 (초보 사냥터) ---
    sc.add("tree_lone", 462, 470, 4.5)
    sc.add("bench", 466, 482, 1.2, look="north")
    for i in range(8):     # 거석 원: 일곱 개를 둥글게 (하나는 쓰러져 비었다)
        if i == 5:
            continue
        a = i / 8 * math.tau + 0.3
        sc.add("standing_stone", 606 + math.cos(a) * 7, 600 + math.sin(a) * 5.5, 1.0, v=i % 4)
    sc.add("watchtower_ruin", 432, 640, 4.0)
    sc.add("tent", 446, 600, 2.6)   # 천막 그림에 모닥불·통나무 의자가 들어 있다
    sc.add("pavilion", ISLET_C[0], ISLET_C[1], 3.5)
    sc.add("dock", 582, 508, 2.5)
    sc.add("boat", 570, 510, 2.0)
    sc.add("gazebo", 620, 496, 3.0, front_z=499)
    sc.add("shepherd_hut", 632, 580, 3.5, front_z=583)
    for k in range(6):
        sc.add("fence", 626 + k * 1.5, 587, 0.6)
    for x, z in ((640, 586), (643, 589)):
        sc.add("hay_bale", x, z, 1.0)

    # --- 벚꽃 골짜기 (남) ---
    sc.add("tree_sakura_giant", 440, 780, 5.5)
    sc.add("shrine", 588, 758, 3.5)
    for z in (778, 790, 802):
        sc.add("torii", 588, z, 2.2)
    for x, z in ((450, 700), (622, 712), (560, 840)):
        sc.add("hokora", x, z, 2.0, front_z=z + 1)
    sc.add("castle_ruin", 466, 804, 6.0)   # 고블린이 차지한 옛 성채 — 고블린 대장이 그 앞에

    # --- 꽃 들판 (남동) · 등대 곶 ---
    for i, (x, z) in enumerate(((628, 772), (780, 664), (934, 664))):
        sc.add("windmill", x, z, 5.0, v=i)
    sc.add("lighthouse", 1004, 612, 5.0)
    sc.add("bench", 992, 604, 1.5, look="north")
    sc.add("barn", 770, 880, 4.0, front_z=883)
    for x, z in ((760, 888), (777, 890), (784, 884)):
        sc.add("hay_bale", x, z, 1.0)
    sc.add("cart", 752, 884, 1.4)
    for ox, oz in ((636, 700),):          # 과수원: 사과나무 줄
        for i in range(3):
            for j in range(3):
                x, z = ox + i * 7 + rng.uniform(-1, 1), oz + j * 7 + rng.uniform(-1, 1)
                if sc.free(x, z, 2.4):
                    sc.add("tree_apple", x, z, 2.4)

    # --- 단풍 협곡 (남서) ---
    sc.add("temple", 318, 630, 4.5, front_z=633)
    sc.add("pagoda", 298, 628, 3.5)
    sc.add("torii", 318, 654, 2.2)
    for i in range(3):
        for side in (-1, 1):
            sc.add("jlantern", 318 + side * 2.6, 640 + i * 5.0, 0.5)
    for x, z in ((306, 640), (330, 641), (336, 650)):
        sc.add(rng.choice(["pumpkins", "persimmon_basket"]), x, z, 0.6, v=rng.randrange(3))
    sc.add("tree_cedar", 296, 616, 3.0)
    sc.add("tree_cedar", 340, 618, 3.0)
    sc.add("moon_deck", 300, 806, 3.5, front_z=809)     # 단풍 연못 누대
    sc.add("hokora", 230, 700, 2.0, front_z=701)

    # --- 고목의 숲 (북서) ---
    sc.add("stump_house", 330, 392, 3.0, front_z=395)
    sc.add("fairy_ring", 384, 420, 2.5)
    sc.add("fairy_ring", 252, 520, 2.5)
    sc.add("tree_giant", 362, 540, 6.0)
    sc.add("bench", 130, 266, 1.5, look="north")   # 노을 절벽: 북북서가 탁 트인 노을 만 (해가 지는 쪽)
    sc.add("moon_deck", 148, 284, 3.5, front_z=287)

    # --- 안개 호수 (북동) ---
    sc.add("gazebo", 700, 358, 3.0, front_z=361)   # 호수 북쪽 물가 — 남쪽에서 호수 너머로 보인다
    sc.add("castle_ruin", 770, 466, 6.0)
    for i in range(6):
        sc.add("stone_lantern", 676 + i * 12, 434, 0.6)

    # --- 서리 고원 (북) ---
    sc.add("observatory", 430, 258, 3.5, front_z=261)
    sc.add("onsen", 600, 300, 4.0)
    sc.add("ice_hut", 658, 192, 2.5, front_z=194)
    sc.add("ice_shrine", 690, 174, 3.5, front_z=177)
    sc.add("pass_gate", 512, 222, 2.0)            # 서리 고원으로 오르는 고개 돌문

    # 작은 폐허 터: 길을 걷다 '저게 뭐지' 하고 들르는 곳 (BotW 의 작은 볼거리)
    for (rx, rz, n) in ((432, 640, 5), (300, 300, 6), (770, 466, 5), (466, 804, 5), (120, 640, 4), (880, 880, 4),
                        (620, 380, 4)):
        for k in range(n):
            a = rng.uniform(0, math.tau)
            d = rng.uniform(5, 11)
            x, z = rx + math.cos(a) * d, rz + math.sin(a) * d
            kind = rng.choice(["ruin_pillar", "ruin_pillar", "ruin_wall", "standing_stone", "rock"])
            if sc.free(x, z, 1.2):
                sc.add(kind, x, z, 1.2, v=rng.randrange(4))
        if n >= 5 and sc.free(rx, rz + 14, 2.0):
            sc.add("ruin_arch", rx + rng.uniform(-3, 3), rz + 14, 2.0)
    for x, z in ((506, 616), (444, 568), (580, 532), (902, 592)):
        sc.add("signpost", x, z, 0.8)


VILLAGES = {
    "harumi": dict(streets=["s_harumi_w", "s_harumi_e", "s_harumi_n", "s_harumi_s", "s_harumi_back"],
                   kinds=["house_red", "house_blue", "house_green", "house_yellow", "bakery", "flower_shop", "inn",
                          "blacksmith", "general_store", "tavern", "house_stone", "house_tower"],
                   center=PLAZA, radius=62, yard="harumi", lamp="street_lamp"),
}
for _k in KINGDOMS:
    VILLAGES[_k["id"]] = dict(streets=[s[0] for s in kingdom_streets(_k)], kinds=_k["houses"], center=_k["c"],
                              radius=KINGDOM_R - 2, yard=_k["yard"], lamp=_k["lamp"])
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
                    house_yard(sc, kind, hx, front, hw, rng, v["yard"])
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
    "hongyeop": dict(props=["jlantern", "persimmon_basket", "pumpkins", "barrel", "twig_pot", "bench"],
                     garden=["maple_sapling", "flowers_yellow", "mushrooms", "fern_small"], trees=["tree_maple", "tree_ginkgo"]),
    "elder": dict(props=["mushroom_big", "mushroom_glow", "log", "stump", "fern", "barrel"],
                  garden=["fern_small", "mushrooms", "fiddlehead", "flowers_blue"], trees=["tree_ancient", "tree_birch"]),
    "lumie": dict(props=["planter", "barrel", "crates", "bench", "rope", "watering_can"],
                  garden=["flowers_blue", "flowers_white", "hydrangea", "lavender"], trees=["tree_willow", "tree_birch"]),
    "snow": dict(props=["barrel", "crates", "stone_lantern", "snow_bush", "snowman", "sled", "snow_sign"],
                 garden=["snow_rock", "snow_bush", "snow_sapling"], trees=["tree_snowfir", "tree_bare"]),
}
NO_FENCE = {"hanami", "hongyeop", "elder"}


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
    if rng.random() < 0.4 and village not in NO_FENCE:
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


def street_lamps(sc, roads, water):
    """마을·왕국 골목을 따라 가로등·석등을 엇갈려 세운다 (12~16m 마다)."""
    rng = random.Random(33)
    for name, line, half, kind in roads:
        if kind != "street":
            continue
        village = next((v for v in VILLAGES if name.startswith(f"s_{v}_")), None)
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
                sc.add(VILLAGES[village]["lamp"], x, z, 0.6)
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
    street_lamps(sc, roads, wcell)
    n_bridges = bridges(sc, roads, wcell, hcell)
    near_water = np.zeros(CSHAPE)
    for name in ("mirror", "misty"):
        ld = lake_d(CX, CZ, LAKES[name])
        near_water = np.maximum(near_water, smoothstep(1.6, 1.1, ld))
    pads_mask = np.zeros(CSHAPE, bool)
    for px, pz, pr, _, _ in PADS[:TOWN_PADS]:
        pads_mask |= np.hypot(CX - px, CZ - pz) < pr * 0.8
    water_props(sc, hcell, wcell, slope, lake_surf)
    scatter_flora(sc, bw, hcell, wcell, slope, road_d, near_water, pads_mask, ground == COBBLE)

    def view(name, x, z, radius=7, pitch=12, distance=26, ahead=10, up=6):
        return {"name": name, "x": x, "z": z, "radius": radius, "pitch": pitch, "distance": distance,
                "look_ahead": ahead, "look_up": up}

    meta = {
        "width": W, "height": H, "sea_level": SEA,
        "center": list(CENTER),
        "spawn": [PLAZA[0], PLAZA[1] + 22],
        "lake_surface": round(lake_surf["mirror"], 2),
        "waterfalls": falls,
        # 전망 지점: 반지름 안에 서면 카메라가 고개를 든다 (평소 38°, 21m). look_ahead·look_up: 바라보는 점을 북쪽·위로 (m)
        "viewpoints": [
            view("약속의 언덕", 466, 478, 8, 14, 26, 12, 6),
            view("거울 호수 나루", 582, 512, 7, 12, 26, 10, 8),
            view("등대 전망대", 992, 604, 7, 12, 24, 8, 2),
            view("풍차 언덕", 780, 674, 7, 16, 24, 4, 4),
            view("벚꽃 신사", 588, 776, 6, 14, 24, 8, 4),
            view("노을 절벽", 130, 270, 8, 8, 26, 10, 2),
            view("안개 호숫가", 700, 436, 7, 12, 26, 10, 8),
            view("별 관측소", 430, 268, 7, 10, 28, 10, 12),
        ] + [view(k["name"], k["c"][0], k["c"][1] + 24, 6, 16, 26, 14, 6) for k in KINGDOMS],
        # 전송석: 레벨이 되면 어느 전송석에서든 간다 (World/Waystones)
        "waystones": [{"id": i, "name": n, "x": round(x, 1), "z": round(z, 1), "level": lv} for i, n, x, z, lv in waystones()],
        # 마을·왕국: 몬스터 무리를 두지 않는 곳 (tools/place_camps.py)
        "towns": [{"id": "harumi", "name": "하루미 마을", "x": PLAZA[0], "z": PLAZA[1], "r": 64}]
                 + [{"id": k["id"], "name": k["name"], "x": k["c"][0], "z": k["c"][1], "r": KINGDOM_R} for k in KINGDOMS],
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
    for k in KINGDOMS:
        cx, cz = k["c"]
        print(f"  {k['name']}: 높이 {h[int(cz), int(cx)]:.1f}, 성 {h[int(cz - 31), int(cx)]:.1f}")


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
        if t == "waystone":
            img[z - 3:z + 4, x - 3:x + 4] = (80, 200, 255)
        elif t in big or t.startswith("keep_"):
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
