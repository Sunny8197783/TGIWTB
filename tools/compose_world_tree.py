"""세계수 합성: PixelLab 한 장은 256px 까지라 줄기 한 장 + 수관 두 장(art_src/world_tree)을 겹쳐 큰 나무 한 장으로.
축소·확대 없이 그대로 겹친다 (1텍셀 = 1픽셀 규칙). 결과: art/env/tree_world_0.png
  /c/Users/gram/AppData/Local/Programs/Python/Python313/python.exe tools/compose_world_tree.py
"""
import os

from PIL import Image, ImageOps

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SRC = os.path.join(ROOT, "art_src", "world_tree")


def load(name):
    im = Image.open(os.path.join(SRC, name + ".png")).convert("RGBA")
    return im.crop(im.getbbox())


def main():
    trunk, a, b = load("trunk"), load("crown_a"), load("crown_b")
    W, H = 420, 424
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    # 뒤 → 앞: 줄기, 뒤쪽 높은 수관(거울), 왼쪽·오른쪽 수관, 가운데 앞 수관(거울).
    # 수관 아랫자락이 줄기 윗동을 덮는다 — 줄기 그림 윗변은 잘린 단면이라 보이면 안 된다
    out.alpha_composite(trunk, ((W - trunk.width) // 2, H - trunk.height))
    back = ImageOps.mirror(b)
    out.alpha_composite(back, ((W - back.width) // 2, 0))
    out.alpha_composite(a, (8, 62))
    out.alpha_composite(b, (W - b.width - 8, 70))
    front = ImageOps.mirror(a)
    out.alpha_composite(front, ((W - front.width) // 2 + 4, 112))
    out = out.crop(out.getbbox())
    out.save(os.path.join(ROOT, "art", "env", "tree_world_0.png"))
    print("tree_world_0", out.size)


if __name__ == "__main__":
    main()
