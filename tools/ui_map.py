"""지도 미리보기(docs/design/map_preview.png, 1m = 1px)에서 화면에 쓰는 지도 두 장을 만든다 (mapgen 뒤에 돌린다).
  art/ui/map_mini.png   512x512 (2m = 1px) — HUD 미니맵이 1:1 로 잘라 쓴다
  art/ui/map_world.png  336x336 (약 3m = 1px) — 지도 화면 (M)
색 수를 줄여 픽셀 그림처럼 또렷하게 한다.
"""
import os
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
src = Image.open(os.path.join(ROOT, "docs", "design", "map_preview.png")).convert("RGB")
os.makedirs(os.path.join(ROOT, "art", "ui"), exist_ok=True)
for name, size, colors in (("map_mini", 512, 48), ("map_world", 336, 40)):
    img = src.resize((size, size), Image.BOX).quantize(colors, method=Image.MEDIANCUT, dither=Image.NONE).convert("RGB")
    img.save(os.path.join(ROOT, "art", "ui", name + ".png"))
    print(name, img.size)
