"""몬스터 무리 자리를 지역마다 고른다: data/world/spawns.json 의 "zones"(계획) → "camps"(자리).

지역 안에서 평평하고, 물·길·마을·왕국·건물·전송석에서 떨어지고, 무리끼리 간격(gap)을 둔 자리를 고른다.
"dist" 가 있으면 하루미 마을(meta.center)에서 그 거리대 안에서만 — 같은 지역도 멀수록 센 무리를 둔다.
"prefer" 가 있으면 그 지면(밭·물가 등) 위 자리를 먼저 쓴다. 고정 자리("fixed")는 그대로 둔다 (우두머리).
지도를 다시 만들면(mapgen) 이것도 다시 돌린다.  $PY tools/place_camps.py [--preview]
"""
import json
import os
import re
import sys

import numpy as np

ROOT = os.path.join(os.path.dirname(__file__), "..")
WORLD = os.path.join(ROOT, "data", "world")
SPAWNS = os.path.join(WORLD, "spawns.json")

(GRASS, FOREST, MEADOW, DIRT, COBBLE, SAND, SAKURA, AUTUMN, GRAVEL, PEBBLE,
 SNOW, TULIP, LAVENDER, MOSS, ROCKY, STONE, GINKGO, WHEAT, ICE, FROSTGRASS, GOLDGRASS) = range(21)
PATH = {DIRT, COBBLE, STONE, GRAVEL}           # 길·광장
NO = {ICE}
GROUND_NAMES = {"wheat": WHEAT, "tulip": TULIP, "lavender": LAVENDER, "sand": SAND, "moss": MOSS, "snow": SNOW}
BUILDING = re.compile(r"house|shop|inn|warehouse|shrine|torii|lighthouse|clocktower|greenhouse|teahouse|tower|boathouse|castle")

MIN_GAP = 30.0       # 무리끼리 (m)
ROAD_GAP = 5.0       # 길에서
BUILDING_GAP = 14.0
STATUE_GAP = 16.0    # 전송석에서
TOWN_GAP = 20.0      # 마을·왕국 가장자리에서
SLOPE = 0.35         # 무리 반지름 안 높이 차 / 반지름


def strip_comments(text):
    return re.sub(r"^\s*//.*$", "", text, flags=re.M)


def main():
    meta = json.load(open(os.path.join(WORLD, "meta.json"), encoding="utf-8"))
    W, H, ZC = meta["width"], meta["height"], meta["zone_cell"]
    h = np.fromfile(os.path.join(WORLD, "terrain.f32"), "<f4").reshape(H + 1, W + 1)
    water = np.fromfile(os.path.join(WORLD, "water.f32"), "<f4").reshape(H + 1, W + 1)
    ground = np.fromfile(os.path.join(WORLD, "ground.u8"), np.uint8).reshape(H, W)
    zones = np.fromfile(os.path.join(WORLD, "zones.u8"), np.uint8).reshape(H // ZC, W // ZC)
    names = meta["zone_names"]
    props = json.load(open(os.path.join(WORLD, "props.json"), encoding="utf-8"))
    buildings = np.array([(p["x"], p["z"]) for p in props if BUILDING.search(p["type"])] or [(-999, -999)])
    statues = np.array([(s["x"], s["z"]) for s in meta["waystones"]])
    towns = [(t["x"], t["z"], t["r"]) for t in meta.get("towns", [])]
    cx0, cz0 = meta.get("center", [W / 2, H / 2])

    # 길까지 거리: 길 칸을 ROAD_GAP 만큼 부풀린 가면
    path = np.isin(ground, list(PATH))
    near_path = path.copy()
    r = int(ROAD_GAP)
    for dz in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dz * dz <= r * r:
                near_path |= np.roll(np.roll(path, dz, 0), dx, 1)
    wet = water[:-1, :-1] > h[:-1, :-1] - 0.05

    plan = json.loads(strip_comments(open(SPAWNS, encoding="utf-8").read()))
    camps = [c for c in plan.get("camps", []) if c.get("fixed")]
    taken = [(c["x"], c["z"]) for c in camps]
    rng = np.random.default_rng(7)

    def ok(x, z, radius, prefer=None, gap=MIN_GAP, dist=None):
        ix, iz = int(x), int(z)
        rr = int(radius)
        if ix - rr < 1 or iz - rr < 1 or ix + rr >= W - 1 or iz + rr >= H - 1:
            return False
        box = (slice(iz - rr, iz + rr + 1), slice(ix - rr, ix + rr + 1))
        if near_path[box].any() or wet[box].any() or np.isin(ground[box], list(NO)).any():
            return False
        if h[box].max() - h[box].min() > SLOPE * radius * 2:
            return False
        if prefer is not None and ground[iz, ix] not in prefer:
            return False
        if np.hypot(buildings[:, 0] - x, buildings[:, 1] - z).min() < BUILDING_GAP + radius:
            return False
        if np.hypot(statues[:, 0] - x, statues[:, 1] - z).min() < STATUE_GAP + radius:
            return False
        if any(np.hypot(tx - x, tz - z) < tr + TOWN_GAP for tx, tz, tr in towns):
            return False
        if dist is not None and not dist[0] <= np.hypot(x - cx0, z - cz0) <= dist[1]:
            return False
        return all(np.hypot(tx - x, tz - z) >= gap for tx, tz in taken)

    for zone, zp in plan["zones"].items():
        zid = names.index(zone)
        cells = np.argwhere(zones == zid)
        rng.shuffle(cells)
        for group in zp.get("groups", []):
            prefer = {GROUND_NAMES[g] for g in group["prefer"]} if "prefer" in group else None
            placed = 0
            for pass_prefer in ([prefer, None] if prefer else [None]):
                for cz, cx in cells:
                    if placed >= group["camps"]:
                        break
                    x = cx * ZC + rng.uniform(1, ZC - 1)
                    z = cz * ZC + rng.uniform(1, ZC - 1)
                    if not ok(x, z, group["radius"], pass_prefer, group.get("gap", MIN_GAP), group.get("dist")):
                        continue
                    taken.append((x, z))
                    camps.append({"ids": group["ids"], "x": round(float(x), 1), "z": round(float(z), 1),
                                  "radius": group["radius"], "respawn": group["respawn"]})
                    placed += 1
            print(f"{zone}: {'/'.join(sorted(set(group['ids'])))} {placed}/{group['camps']}")

    # "camps" 만 다시 쓴다 (계획·주석은 손으로 쓴 그대로)
    text = open(SPAWNS, encoding="utf-8").read()
    body = ",\n".join("    " + json.dumps(c, ensure_ascii=False) for c in camps)
    text = re.sub(r'"camps"\s*:\s*\[.*?\n  \]', '"camps": [\n' + body + "\n  ]", text, flags=re.S)
    open(SPAWNS, "w", encoding="utf-8").write(text)
    print(len(camps), "camps")

    if "--preview" in sys.argv:
        from PIL import Image, ImageDraw
        im = Image.open(os.path.join(ROOT, "docs", "design", "map_preview.png")).convert("RGB")
        s = im.width / W
        d = ImageDraw.Draw(im)
        for c in camps:
            col = (255, 40, 40) if c.get("fixed") else (255, 230, 40)
            x, z, r = c["x"] * s, c["z"] * s, max(3, c["radius"] * s)
            d.ellipse([x - r, z - r, x + r, z + r], outline=col, width=2)
        im.save(os.path.join(ROOT, "docs", "design", "camps_preview.png"))


if __name__ == "__main__":
    main()
