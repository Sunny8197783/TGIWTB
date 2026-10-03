"""하루미 섬 — 첫 지역 지도 생성기.

카메라는 북쪽(-z)을 본다. 그래서 '보여 줄 것'은 전부 북쪽에 둔다:
  마을(남) → 호수(중앙) → 폭포 절벽(호수 북단, 남향) → 벚꽃 고원(북서) → 바다 전망 절벽(북동)
남쪽에서 북쪽으로 걸을수록 풍경이 한 겹씩 열리는 구도.

출력 (data/world/):
  terrain.f32  꼭짓점 높이 (W+1)*(H+1) float32, 행 우선(z 바깥, x 안쪽)
  water.f32    꼭짓점 수면 높이, 물 없음 = -1000
  ground.u8    칸 지면 종류 W*H 바이트 (값 = 종류 번호)
  props.json   소품 배치
  meta.json    크기·시작점·전망 지점·폭포·조명
미리보기: docs/design/map_preview.png
"""
import json
import math
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
import png  # noqa: E402

W = H = 224
SEA = 0.0
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "data", "world")

# 지면 종류. 셰이더(terrain.gdshader)의 번호와 같아야 한다.
GRASS, FOREST, MEADOW, DIRT, COBBLE, SAND, SAKURA, AUTUMN, GRAVEL, PEBBLE = range(10)


# --- 잡음 ---------------------------------------------------------------

def _hash(ix, iz, seed):
    h = (ix * 374761393 + iz * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def value_noise(x, z, seed):
    ix, iz = math.floor(x), math.floor(z)
    fx, fz = x - ix, z - iz
    ux, uz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    a = _hash(ix, iz, seed)
    b = _hash(ix + 1, iz, seed)
    c = _hash(ix, iz + 1, seed)
    d = _hash(ix + 1, iz + 1, seed)
    return (a + (b - a) * ux) + ((c + (d - c) * ux) - (a + (b - a) * ux)) * uz


def fbm(x, z, seed, octaves=4, scale=1.0):
    total, amp, freq, norm = 0.0, 1.0, scale, 0.0
    for o in range(octaves):
        total += (value_noise(x * freq, z * freq, seed + o * 17) * 2 - 1) * amp
        norm += amp
        amp *= 0.5
        freq *= 2.0
    return total / norm


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


# --- 선(길·강) ----------------------------------------------------------

def seg_dist(px, pz, ax, az, bx, bz):
    """점과 선분의 거리, 그리고 선분 위 비율 t."""
    dx, dz = bx - ax, bz - az
    l2 = dx * dx + dz * dz
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / l2))
    qx, qz = ax + dx * t, az + dz * t
    return math.hypot(px - qx, pz - qz), t


def poly_dist(px, pz, pts):
    """폴리라인까지 거리와 시작점부터의 누적 비율(0..1)."""
    best, best_s = 1e9, 0.0
    lengths = [math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1)]
    total = sum(lengths)
    acc = 0.0
    for i in range(len(pts) - 1):
        d, t = seg_dist(px, pz, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1])
        if d < best:
            best, best_s = d, (acc + lengths[i] * t) / total
        acc += lengths[i]
    return best, best_s


# 고원 위 강: 북서 샘 → 벚꽃 숲 → 폭포 턱
RIVER = [(46, 20), (58, 27), (72, 30), (86, 40), (97, 50), (106, 60), (112, 72)]
# 호수에서 나와 마을을 지나 남동 해변으로
STREAM = [(137, 112), (145, 126), (149, 142), (157, 160), (166, 178), (174, 196), (178, 214)]
STREAM_BED = 1.6    # 이 안쪽은 물 바닥 (m)
STREAM_BANK = 3.0   # 여기서 둑이 끝나고 원래 땅
# 길
PATHS = [
    # 마을 동쪽 → 풍차 언덕 → 고원 오르막 → 등대
    [(148, 155), (164, 150), (176, 136), (182, 118), (178, 96), (174, 80), (170, 62), (166, 44), (170, 28), (174, 18)],
    # 마을 서쪽 → 숲 → 서쪽 골짜기 오르막 → 벚꽃 숲 신사
    [(78, 150), (64, 146), (52, 132), (46, 112), (44, 92), (46, 74), (52, 60), (62, 48), (70, 40)],
    # 호숫가 산책로 (남쪽 둑)
    [(84, 126), (100, 125), (112, 126), (126, 124), (138, 118)],
    # 벚꽃 숲 → 단풍 숲 → 등대 (고원 가로지르기)
    [(70, 40), (86, 34), (104, 36), (122, 42), (140, 36), (156, 26), (174, 18)],
    # 마을 남쪽 → 해변 부두
    [(112, 168), (114, 184), (118, 200)],
]
LAKE_C, LAKE_R = (112.0, 99.0), (31.0, 22.0)
LAKE_SURFACE = 1.8
ISLET_C, ISLET_R = (124.0, 101.0), 4.5

# 마을 줄: 모든 집은 남쪽(카메라)을 본다. (앞면 z, x 범위)
VILLAGE_ROWS = [
    (136, 80, 146),
    (151, 78, 148),
    (165, 82, 144),
]
STREETS = [(139, 76, 150), (155, 74, 152), (169, 80, 146)]  # (z 중심, x0, x1)
PLAZA_C, PLAZA_R = (112.0, 155.0), 7.0


def lake_d(x, z):
    """호수 중심에서의 정규화 거리(1 = 물가). 타원 둘을 섞고 잡음으로 흔들어 인공적인 원을 피한다."""
    lx = (x - LAKE_C[0]) / LAKE_R[0]
    lz = (z - LAKE_C[1]) / LAKE_R[1]
    main = math.sqrt(lx * lx + lz * lz)
    bx = (x - (LAKE_C[0] - 20)) / 14.0      # 서쪽 만
    bz = (z - (LAKE_C[1] + 9)) / 10.0
    bay = math.sqrt(bx * bx + bz * bz)
    blend = min(main, bay) - 0.08 * math.exp(-((main - bay) ** 2) * 8)
    return blend + 0.14 * fbm(x * 0.045, z * 0.045, 31, 3)


def river_surface(s):
    """고원 강의 수면. 고원 바닥(11.5 안팎)보다 1m 가량 낮게 흐르다 폭포 턱에서 떨어진다."""
    return lerp(11.0, 10.6, s)


def plateau_edge(x):
    """고원 남쪽 절벽선의 z. 호수 북단에서 폭포가 떨어지도록 가운데가 남쪽으로 나온다."""
    base = 70.0 + 5.0 * fbm(x * 0.03, 3.0, 11, 3)
    bulge = 4.0 * math.exp(-((x - 112) / 22.0) ** 2)
    return base + bulge


# 등대 곶: 섬 중심에서 본 등대(-58°, 120m)·전망 벤치(-67°, 113m)가 바다에 빠지지 않게 북동으로 내민 땅
CAPE_ANG, CAPE_WIDTH, CAPE_LEN = math.radians(-62.0), math.radians(11.0), 26.0


def island_radius(ang):
    off = math.atan2(math.sin(ang - CAPE_ANG), math.cos(ang - CAPE_ANG))
    cape = CAPE_LEN * math.exp(-(off / CAPE_WIDTH) ** 2)
    return 100.0 + 9.0 * fbm(math.cos(ang) * 2.0 + 5, math.sin(ang) * 2.0 + 5, 3, 3) + cape


# --- 높이 --------------------------------------------------------------

def height_at(x, z):
    cx, cz = W / 2.0, H / 2.0 + 4
    ang = math.atan2(z - cz, x - cx)
    dist = math.hypot(x - cx, z - cz)
    rad = island_radius(ang)

    edge = plateau_edge(x)
    on_plateau = z < edge

    # 기본 땅: 완만한 기복
    h = 2.3 + 0.7 * fbm(x * 0.02, z * 0.02, 5, 4)

    # 서쪽 숲은 조금 높고 울퉁불퉁하다
    h += 1.6 * smoothstep(70, 45, x) * smoothstep(75, 95, z) * (0.6 + 0.4 * fbm(x * 0.05, z * 0.05, 8))

    # 동쪽 풀밭 언덕 (풍차 언덕이 가장 높다)
    for hx, hz, hr, hh in ((182, 114, 16, 3.2), (166, 152, 12, 1.4), (196, 140, 14, 1.8), (150, 96, 10, 1.0)):
        h += hh * math.exp(-((x - hx) ** 2 + (z - hz) ** 2) / (hr * hr))

    # 고원: 가파른 절벽 한 줄로 올라선다
    top = 11.5 + 0.7 * fbm(x * 0.03, z * 0.03, 21, 3) + 1.0 * smoothstep(40, 10, z)
    cliff = smoothstep(edge + 1.4, edge - 0.6, z)
    # 오르막 두 곳: 동쪽 길과 서쪽 골짜기는 절벽 대신 긴 비탈
    ramp_e = smoothstep(14, 4, abs(x - 172))
    ramp_w = smoothstep(11, 3, abs(x - 47))
    ramp = max(ramp_e, ramp_w)
    slope = smoothstep(edge + 22, edge - 16, z)
    blend = lerp(cliff, slope, ramp)
    h = lerp(h, top, blend)

    # 호수 바닥
    ld = lake_d(x, z)
    if ld < 1.15 and z > edge - 1:
        shore = smoothstep(1.15, 0.92, ld)
        deep = smoothstep(0.95, 0.3, ld + 0.15 * fbm(x * 0.1, z * 0.1, 33, 2))
        bed = LAKE_SURFACE - 0.5 - 2.6 * deep
        h = lerp(h, bed, shore)
    # 호수 섬
    idist = math.hypot(x - ISLET_C[0], z - ISLET_C[1])
    if idist < ISLET_R + 2:
        h = lerp(h, 2.5, smoothstep(ISLET_R + 2, ISLET_R - 1, idist))

    # 고원 강: 샘에서 폭포 턱까지 수면이 천천히 내려간다
    d, s = poly_dist(x, z, RIVER)
    if d < 5.0:
        surface = river_surface(s)
        bed = surface - 0.9
        h = lerp(h, min(h, bed), smoothstep(5.0, 2.2, d))
        # 둑은 수면보다 확실히 위 — 안 그러면 강이 고원을 덮는다
        if d > 2.6:
            h = max(h, surface + 0.5 * smoothstep(2.6, 4.0, d))

    # 호수 → 바다 개울: 좁고 둑이 가파른 물길. 넓고 완만하면 얕은 물이 비탈을 덮어 유리판처럼 보인다.
    d, s = poly_dist(x, z, STREAM)
    if d < STREAM_BANK:
        surface = lerp(LAKE_SURFACE, 0.05, s)
        bed = surface - 0.7
        h = lerp(h, min(h, bed), smoothstep(STREAM_BANK, STREAM_BED, d))

    # 마을: 평평하게 고른다 (북쪽이 아주 약간 높다)
    vil = smoothstep(12, 4, max(76 - x, x - 150, 0) + max(128 - z, z - 172, 0))
    if vil > 0 and poly_dist(x, z, STREAM)[0] > STREAM_BANK:
        h = lerp(h, 2.5 + (172 - z) * 0.012, vil)

    # 섬 바깥은 바다. 고원 쪽 해안은 절벽, 남쪽은 모래 해변.
    beachy = smoothstep(edge + 10, edge + 40, z)
    coast_w = lerp(2.5, 16.0, beachy)
    land = smoothstep(rad + 1.0, rad - coast_w, dist)
    sea_floor = -4.0 + 1.5 * fbm(x * 0.03, z * 0.03, 41, 2)
    beach_h = lerp(0.3, h, smoothstep(rad - 2, rad - coast_w, dist))
    h = lerp(sea_floor, lerp(h, beach_h, beachy), land)
    return h


def water_at(x, z, h):
    """수면 높이. 물이 없으면 None. 둑 아래로 조금 파고들게(+0.35) 잡아 해안선이 끊기지 않게 한다."""
    # 물은 '물 밑 바닥'이 있는 곳에만. 절벽 면처럼 한참 아래로 떨어지는 곳까지 칠하면
    # 수면 칸이 공중에 계단처럼 뜬다.
    if lake_d(x, z) < 1.18 and LAKE_SURFACE - 4.5 < h < LAKE_SURFACE + 0.35 and z > plateau_edge(x) - 1:
        return LAKE_SURFACE
    d, s = poly_dist(x, z, RIVER)
    if d < 3.4:
        surf = river_surface(s)
        if surf - 1.6 < h < surf + 0.35:
            return surf
    d, s = poly_dist(x, z, STREAM)
    if d < STREAM_BED + 0.8:
        surf = lerp(LAKE_SURFACE, 0.05, s)
        if surf - 1.2 < h < surf + 0.1:
            return surf
    # 바다는 맨 마지막. 호수 바닥도 해수면보다 낮아서, 먼저 물으면 호수 가운데가 '바다'가 된다.
    if h < SEA + 0.35:
        return SEA
    return None


# --- 지면 종류 -----------------------------------------------------------

def ground_at(x, z, h, wet):
    edge = plateau_edge(x)
    n = fbm(x * 0.07, z * 0.07, 51, 3)
    if wet is not None and h < wet - 0.05:
        return GRAVEL if (wet > 5 or poly_dist(x, z, STREAM)[0] < 5) else PEBBLE
    for pts in PATHS:
        d, _ = poly_dist(x, z, pts)
        if d < 1.4 + 0.4 * n:
            return DIRT
    # 마을 거리와 광장
    if math.hypot(x - PLAZA_C[0], z - PLAZA_C[1]) < PLAZA_R:
        return COBBLE
    for sz, x0, x1 in STREETS:
        if x0 <= x <= x1 and abs(z - sz) < (2.2 if sz == 155 else 1.6):
            return COBBLE if sz == 155 else DIRT
    # 해변
    if h < 0.9 and z > edge + 10:
        return SAND
    # 호숫가 자갈
    if 1.0 < lake_d(x, z) < 1.08 + 0.05 * n and h < LAKE_SURFACE + 0.6:
        return PEBBLE
    if z < edge + 1:
        # 고원
        if x < 104 + 8 * n and z > 12:
            return SAKURA
        if math.hypot(x - 128, z - 46) < 17 + 5 * n:
            return AUTUMN
        if n > 0.35:
            return MEADOW
        return GRASS
    if x < 68 + 6 * n and z > 76:
        return FOREST
    if x > 152 and n > 0.05:
        return MEADOW
    if n > 0.55:
        return MEADOW
    return GRASS


# --- 소품 배치 -----------------------------------------------------------

class Scatter:
    """겹치지 않게 뿌린다. 격자 버킷으로 가까운 것만 비교."""

    def __init__(self):
        self.items = []
        self.grid = {}

    def free(self, x, z, r):
        gx, gz = int(x // 8), int(z // 8)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for ox, oz, orr in self.grid.get((gx + dx, gz + dz), ()):
                    if math.hypot(ox - x, oz - z) < r + orr:
                        return False
        return True

    def add(self, kind, x, z, r, **extra):
        self.grid.setdefault((int(x // 8), int(z // 8)), []).append((x, z, r))
        item = {"type": kind, "x": round(x, 2), "z": round(z, 2)}
        item.update(extra)
        self.items.append(item)


def main():
    os.makedirs(OUT, exist_ok=True)
    VW, VH = W + 1, H + 1
    heights = [0.0] * (VW * VH)
    for z in range(VH):
        for x in range(VW):
            heights[z * VW + x] = height_at(float(x), float(z))

    water = [-1000.0] * (VW * VH)
    for z in range(VH):
        for x in range(VW):
            w = water_at(float(x), float(z), heights[z * VW + x])
            if w is not None:
                water[z * VW + x] = w

    def cell_h(x, z):
        return (heights[z * VW + x] + heights[z * VW + x + 1] + heights[(z + 1) * VW + x] + heights[(z + 1) * VW + x + 1]) * 0.25

    def cell_w(x, z):
        vals = [water[(z + dz) * VW + x + dx] for dz in (0, 1) for dx in (0, 1)]
        vals = [v for v in vals if v > -999]
        return max(vals) if vals else None

    ground = [0] * (W * H)
    for z in range(H):
        for x in range(W):
            ground[z * W + x] = ground_at(x + 0.5, z + 0.5, cell_h(x, z), cell_w(x, z))

    def slope(x, z):
        x, z = max(0, min(W - 1, int(x))), max(0, min(H - 1, int(z)))
        a = heights[z * VW + x]
        return max(abs(heights[z * VW + x + 1] - a), abs(heights[(z + 1) * VW + x] - a))

    def h_at(x, z):
        x, z = max(0, min(W - 1, int(x))), max(0, min(H - 1, int(z)))
        return cell_h(x, z)

    def dry(x, z):
        xi, zi = max(0, min(W - 1, int(x))), max(0, min(H - 1, int(z)))
        return cell_w(xi, zi) is None and h_at(x, z) > SEA + 0.4

    def gtype(x, z):
        return ground[max(0, min(H - 1, int(z))) * W + max(0, min(W - 1, int(x)))]

    def near_path(x, z, r):
        return any(poly_dist(x, z, p)[0] < r for p in PATHS)

    sc = Scatter()

    # 1) 랜드마크 (먼저 놓아야 주변 소품이 비켜 간다)
    sc.add("lighthouse", 176, 14, 5.0)
    sc.add("bench", 156, 12, 1.5, look="north")
    sc.add("windmill", 184, 111, 5.0)
    sc.add("torii", 70, 36, 2.5)
    sc.add("shrine", 70, 30, 3.5)
    sc.add("pavilion", ISLET_C[0], ISLET_C[1], 3.5)
    sc.add("dock", 112, 123.5, 2.5)
    sc.add("boat", 104, 121, 2.0)
    sc.add("fountain", PLAZA_C[0], PLAZA_C[1], 3.2)
    sc.add("dock_sea", 118, 205, 3.0)
    sc.add("fish_crates", 115, 202, 1.2)
    sc.add("buoy", 124, 210, 1.0)
    sc.add("buoy", 111, 212, 1.0)

    # 다리: 길이 물을 건너는 곳. 길을 촘촘히 따라가다 물에 들어간 곳과 나온 곳을 찾아
    # 양쪽 마른 둑을 잇는다. 게임은 이 직사각형 위를 걸을 수 있고(깊은 물 막힘 해제), 발 높이는 deck.
    def add_bridge(a, b):
        ln = math.hypot(b[0] - a[0], b[1] - a[1]) + 1.0
        if ln > 16.0:
            return  # 너무 길면 다리가 아니라 둑길 — 놓지 않는다
        deck = max(h_at(*a), h_at(*b)) + 0.05
        cx, cz = (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5
        sc.add("bridge", cx, cz, ln * 0.5, dir=round(math.atan2(b[1] - a[1], b[0] - a[0]), 3),
               len=round(ln, 2), deck=round(deck, 2))

    def bridge_along(pts, step=0.25):
        last_dry, inside = None, False
        for k in range(len(pts) - 1):
            (x0, z0), (x1, z1) = pts[k], pts[k + 1]
            n = max(1, int(math.hypot(x1 - x0, z1 - z0) / step))
            for i in range(n + 1):
                px, pz = lerp(x0, x1, i / n), lerp(z0, z1, i / n)
                xi, zi = int(px), int(pz)
                wet = 0 <= xi < W and 0 <= zi < H and cell_w(xi, zi) is not None
                if wet and not inside:
                    inside = True
                elif not wet and inside:
                    inside = False
                    if last_dry is not None:
                        add_bridge(last_dry, (px, pz))
                if not wet:
                    last_dry = (px, pz)

    for pts in PATHS:
        bridge_along(pts)
    for sz, x0, x1 in STREETS:
        bridge_along([(x0, sz), (x1, sz)])

    # 2) 집: 남쪽을 보고 줄지어 선다. 개울과 광장은 비운다.
    kinds = ["house_red", "house_blue", "house_green", "bakery", "house_yellow", "flower_shop",
             "inn", "house_red", "blacksmith", "house_blue", "general_store", "house_green",
             "tavern", "house_yellow", "house_red", "house_blue"]
    ki = 0
    for front_z, x0, x1 in VILLAGE_ROWS:
        x = x0 + 4
        while x < x1 - 3:
            kind = kinds[ki % len(kinds)]
            half = 4.5 if kind in ("inn", "tavern") else 3.5
            cx = x + half
            blocked = poly_dist(cx, front_z - 3, STREAM)[0] < half + 3.0 or \
                math.hypot(cx - PLAZA_C[0], front_z - 3 - PLAZA_C[1]) < PLAZA_R + half
            if not blocked and sc.free(cx, front_z - 3, half):
                sc.add(kind, cx, front_z - 3, half, front_z=front_z)
                # 집 앞 살림살이: 가게는 상자, 살림집은 화분. 문(가운데)은 비운다.
                shop = kind in ("bakery", "general_store", "blacksmith", "flower_shop", "inn", "tavern")
                if kind == "tavern":
                    sc.add("barrel", cx + half + 0.6, front_z + 0.4, 0.9)
                for side in (-1, 1):
                    dx = side * (half - 1.1)
                    deco = "crates" if shop and side == (1 if _hash(int(cx), int(front_z), 31) < 0.5 else -1) else "planter"
                    sc.add(deco, cx + dx, front_z + 0.7, 0.8)
                ki += 1
                x += half * 2 + 3.0
            else:
                x += 2.0
    # 광장: 가운데 분수, 둘레에 벤치 넷, 동쪽에 우물
    for bx, bz in ((-5.5, 3.5), (5.5, 3.5), (-5.5, -4.5), (5.5, -4.5)):
        px, pz = PLAZA_C[0] + bx, PLAZA_C[1] + bz
        if sc.free(px, pz, 1.2):
            sc.add("bench", px, pz, 1.2)
    wx, wz = PLAZA_C[0] + 10.5, PLAZA_C[1] - 1.0
    if sc.free(wx, wz, 1.6):
        sc.add("well", wx, wz, 1.6)
    # 광장 남쪽 과일 가판대
    stalls = 0
    for dx, dz in ((-8.5, 6.5), (8.5, 6.5), (-9.5, 3.0), (9.5, 3.0), (-7.0, -7.5), (7.0, -7.5), (-10.5, -3.0)):
        px, pz = PLAZA_C[0] + dx, PLAZA_C[1] + dz
        if stalls < 2 and sc.free(px, pz, 1.4):
            sc.add("market_stall", px, pz, 1.4)
            stalls += 1
    # 풍차 농장: 건초 더미, 손수레, 남쪽 울타리 한 줄
    for hx, hz in ((178, 122), (181, 125), (190, 123), (193, 120)):
        if dry(hx, hz) and sc.free(hx, hz, 1.2):
            sc.add("hay_bale", hx, hz, 1.2)
    if sc.free(191, 116, 1.6):
        sc.add("cart", 191, 116, 1.6)
    for fx in range(172, 199, 2):
        if dry(fx + 0.5, 128.5) and sc.free(fx + 0.5, 128.5, 0.7):
            sc.add("fence", fx + 0.5, 128.5, 0.7)
    # 마을 들머리 이정표
    for sz, x0, x1 in STREETS:
        for px in (x0 + 1.5, x1 - 1.5):
            pz = sz - 2.4
            if dry(px, pz) and sc.free(px, pz, 0.8):
                sc.add("signpost", px, pz, 0.8)
    # 가로등: 거리 남쪽 가장자리를 따라
    for sz, x0, x1 in STREETS:
        for x in range(x0 + 2, x1, 9):
            px, pz = x + 0.5, sz + 2.6
            if dry(px, pz) and sc.free(px, pz, 0.6):
                sc.add("street_lamp", px, pz, 0.6)
    # 호숫가 석등
    for i in range(22):
        a = i / 22 * math.tau
        # 중심에서 바깥으로 걸어 나가 물가(lake_d 1.14)에 닿는 자리
        px, pz = LAKE_C
        for step in range(80):
            px = LAKE_C[0] + math.cos(a) * step * 0.6
            pz = LAKE_C[1] + math.sin(a) * step * 0.6
            if lake_d(px, pz) > 1.14:
                break
        if pz > plateau_edge(px) + 3 and dry(px, pz) and slope(px, pz) < 0.6 and sc.free(px, pz, 1.0):
            sc.add("stone_lantern", px, pz, 1.0)

    # 3) 나무와 풀꽃: 지역마다 종류와 밀도가 다르다
    # (종류, 차지 반경, 칸당 확률). 작은 장식(반경 < 0.6)은 가까이서 봐야 보이는 것들 — 카메라가 가까워서 촘촘해야 한다.
    rules = {
        SAKURA: [("tree_sakura", 3.2, 0.10), ("bush_pink", 1.0, 0.05), ("flowers_white", 0.6, 0.05),
                 ("petals", 0.45, 0.12), ("flowers_pink", 0.4, 0.04), ("grass_tuft", 0.4, 0.03)],
        AUTUMN: [("tree_maple", 3.0, 0.10), ("bush_orange", 1.0, 0.05), ("mushrooms", 0.4, 0.05),
                 ("fern_small", 0.4, 0.04), ("stone", 0.5, 0.02)],
        FOREST: [("tree_oak", 3.0, 0.08), ("tree_pine", 2.4, 0.06), ("bush", 1.0, 0.06), ("fern", 0.9, 0.05),
                 ("fern_small", 0.4, 0.07), ("mushrooms", 0.4, 0.06), ("stone", 0.5, 0.02), ("sapling", 0.5, 0.02),
                 ("grass_tuft", 0.4, 0.04)],
        MEADOW: [("flowers_mix", 0.8, 0.08), ("flowers_yellow", 0.4, 0.06), ("flowers_blue", 0.4, 0.05),
                 ("flowers_pink", 0.4, 0.04), ("lavender", 0.5, 0.03), ("dandelion", 0.4, 0.04), ("flowers_white", 0.4, 0.04),
                 ("tree_oak", 3.0, 0.006), ("bush", 1.0, 0.01)],
        GRASS: [("tree_oak", 3.0, 0.012), ("tree_pine", 2.4, 0.006), ("bush", 1.0, 0.02), ("flowers_mix", 0.8, 0.015),
                ("rock", 0.8, 0.006), ("grass_tuft", 0.4, 0.05), ("flowers_yellow", 0.4, 0.02), ("dandelion", 0.4, 0.015),
                ("pebbles", 0.4, 0.008), ("stone", 0.5, 0.006)],
    }
    rng_seed = 7
    # 큰 나무를 먼저 전부 심고 그다음 풀꽃. 한 번에 돌면 먼저 놓인 덤불이
    # 나무 자리를 다 막아서 숲에 나무가 몇 그루 안 남는다.
    for big_pass in (True, False):
      for z in range(2, H - 2):
        for x in range(2, W - 2):
            g = gtype(x, z)
            if g not in rules:
                continue
            for i, (kind, r, dens) in enumerate(rules[g]):
                if (r > 2) != big_pass:
                    continue
                roll = _hash(x, z, rng_seed + i * 101)
                if roll > dens:
                    continue
                px = x + _hash(x, z, 900 + i)
                pz = z + _hash(z, x, 901 + i)
                if not dry(px, pz) or slope(px, pz) > 0.9:
                    continue
                big = r > 2
                if big and near_path(px, pz, r + 1.2):
                    continue
                if not big and near_path(px, pz, 1.6):
                    continue
                if sc.free(px, pz, r):
                    sc.add(kind, px, pz, r, v=int(_hash(x, z, 77 + i) * 4))
    # 절벽 밑과 물가 바위, 호숫가 갈대, 수련
    for z in range(2, H - 2, 2):
        for x in range(2, W - 2, 2):
            px, pz = x + _hash(x, z, 5), z + _hash(z, x, 6)
            if not dry(px, pz):
                xi, zi = int(px), int(pz)
                w = cell_w(xi, zi)
                if w == LAKE_SURFACE and h_at(px, pz) < LAKE_SURFACE - 0.6 and _hash(x, z, 9) < 0.05 and sc.free(px, pz, 0.8):
                    sc.add("lily_pad", px, pz, 0.8, v=int(_hash(x, z, 10) * 2))
                continue
            if slope(px, pz) > 1.5 and _hash(x, z, 11) < 0.08 and sc.free(px, pz, 1.0):
                sc.add("rock", px, pz, 1.0, v=int(_hash(x, z, 12) * 4))
            wet_near = any(cell_w(max(0, min(W - 1, x + dx)), max(0, min(H - 1, z + dz))) == LAKE_SURFACE
                           for dx in (-2, 0, 2) for dz in (-2, 0, 2))
            if wet_near and _hash(x, z, 13) < 0.25 and sc.free(px, pz, 0.6):
                sc.add("reeds", px, pz, 0.6, v=int(_hash(x, z, 14) * 3))

    props = sc.items

    # 4) 메타
    meta = {
        "width": W, "height": H, "sea_level": SEA,
        "spawn": [112, 160],
        "lake_surface": LAKE_SURFACE,
        "waterfalls": [{"x": 112.0, "top_z": plateau_edge(112) - 1.0, "top_y": river_surface(1.0),
                        "bottom_y": LAKE_SURFACE, "width": 4.5}],
        # 몬스터 무리: 중심·반경 안에 count 마리, 죽으면 respawn 초 뒤 다시
        "monsters": [
            {"id": "sakura_slime", "x": 160, "z": 152, "radius": 5, "count": 3, "respawn": 8},
            {"id": "sakura_slime", "x": 66, "z": 44, "radius": 6, "count": 4, "respawn": 10},
            {"id": "forest_goblin", "x": 52, "z": 120, "radius": 6, "count": 3, "respawn": 14},
        ],
        # 전망 지점: 반지름 안에 서면 카메라가 고개를 든다 (pitch°, distance m — 평소 38°, 21m).
        # look_ahead·look_up: 바라보는 점을 북쪽·위로 옮긴다 (m) — 키 큰 풍차가 잘리지 않게, 바다가 넓게
        # 카메라가 늘 북쪽을 보므로 북쪽이 트인 곳만 된다 (폭포 위는 절벽 면만 보여서 뺐다)
        "viewpoints": [
            {"name": "등대 전망대", "x": 156, "z": 12, "radius": 7, "pitch": 18, "distance": 22, "look_ahead": 6, "look_up": 0},
            {"name": "풍차 언덕", "x": 184, "z": 120, "radius": 7, "pitch": 18, "distance": 24, "look_ahead": 4, "look_up": 4},
        ],
        "zones": [
            {"name": "하루미 마을", "x0": 74, "z0": 126, "x1": 152, "z1": 174},
            {"name": "거울 호수", "x0": 80, "z0": 76, "x1": 144, "z1": 124},
            {"name": "벚꽃 고원", "x0": 20, "z0": 8, "x1": 104, "z1": 70},
            {"name": "단풍 언덕", "x0": 106, "z0": 26, "x1": 150, "z1": 66},
            {"name": "등대 곶", "x0": 150, "z0": 0, "x1": 210, "z1": 40},
            {"name": "풍차 들판", "x0": 152, "z0": 80, "x1": 214, "z1": 180},
            {"name": "속삭이는 숲", "x0": 14, "z0": 76, "x1": 72, "z1": 200},
            {"name": "남쪽 해변", "x0": 90, "z0": 180, "x1": 190, "z1": 224},
        ],
    }

    with open(os.path.join(OUT, "terrain.f32"), "wb") as f:
        f.write(struct.pack("<%df" % len(heights), *heights))
    with open(os.path.join(OUT, "water.f32"), "wb") as f:
        f.write(struct.pack("<%df" % len(water), *water))
    with open(os.path.join(OUT, "ground.u8"), "wb") as f:
        f.write(bytes(ground))
    with open(os.path.join(OUT, "props.json"), "w", encoding="utf-8") as f:
        json.dump(props, f, ensure_ascii=False, indent=0)
    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    preview(heights, water, ground, props, VW)
    counts = {}
    for p in props:
        counts[p["type"]] = counts.get(p["type"], 0) + 1
    print("props", len(props), dict(sorted(counts.items(), key=lambda kv: -kv[1])))
    print("height range %.1f .. %.1f" % (min(heights), max(heights)))


PALETTE = {
    GRASS: (104, 168, 72), FOREST: (58, 110, 58), MEADOW: (140, 186, 84), DIRT: (176, 138, 92),
    COBBLE: (160, 156, 150), SAND: (232, 214, 160), SAKURA: (238, 176, 196), AUTUMN: (214, 120, 56),
    GRAVEL: (130, 124, 116), PEBBLE: (170, 162, 150),
}
PROP_COLORS = {
    "tree_sakura": (255, 120, 170), "tree_oak": (30, 90, 30), "tree_pine": (20, 70, 50),
    "tree_maple": (200, 50, 20), "street_lamp": (255, 230, 120), "stone_lantern": (255, 230, 120),
}


def preview(heights, water, ground, props, VW):
    S = 3
    out = [(0, 0, 0, 255)] * (W * S * H * S)
    for z in range(H):
        for x in range(W):
            h = heights[z * VW + x]
            hx = heights[z * VW + x + 1] - h
            hz = heights[(z + 1) * VW + x] - h
            shade = max(0.45, min(1.3, 1.0 + (-hx * 0.6 - hz * 0.6) * 0.5))
            steep = max(abs(hx), abs(hz)) > 1.0
            w = water[z * VW + x]
            if w > -999 and w > h:
                depth = w - h
                c = (int(60 - depth * 8), int(150 - depth * 12), int(200 - depth * 6))
            elif steep:
                c = (120, 110, 100)
            else:
                c = PALETTE[ground[z * W + x]]
            c = tuple(max(0, min(255, int(ch * shade))) for ch in c)
            for sy in range(S):
                for sx in range(S):
                    out[(z * S + sy) * W * S + x * S + sx] = (c[0], c[1], c[2], 255)
    for p in props:
        col = PROP_COLORS.get(p["type"])
        if p["type"].startswith("house") or p["type"] in ("inn", "tavern", "bakery", "blacksmith", "flower_shop", "general_store"):
            col = (200, 70, 60)
        elif p["type"] in ("lighthouse", "windmill", "torii", "shrine", "pavilion", "fountain", "bridge", "dock", "bench"):
            col = (255, 255, 255)
        if not col:
            continue
        cx, cz = int(p["x"] * S), int(p["z"] * S)
        r = 3 if col == (255, 255, 255) or col == (200, 70, 60) else 1
        for dz in range(-r, r + 1):
            for dx in range(-r, r + 1):
                px, pz = cx + dx, cz + dz
                if 0 <= px < W * S and 0 <= pz < H * S:
                    out[pz * W * S + px] = (col[0], col[1], col[2], 255)
    dst = os.path.join(ROOT, "docs", "design")
    os.makedirs(dst, exist_ok=True)
    png.write_rgba(os.path.join(dst, "map_preview.png"), W * S, H * S, out)


if __name__ == "__main__":
    main()
