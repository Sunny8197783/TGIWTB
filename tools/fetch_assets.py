"""PixelLab 결과물을 내려받아 art/ 에 놓는다. 목록은 tools/assets.json.

항목 종류:
  {"dest": "art/env/tree_oak_0.png", "object": "<object id>"}          단방향 오브젝트
  {"dest": "art/characters/hero/run/{dir}/{i}.png", "anim": "<url 목록 파일 아님>" ...}
캐릭터 애니메이션은 fetch_anim() 이 get_character 가 준 URL 목록(assets.json 의 "frames")으로 받는다.

오브젝트는 투명 여백을 잘라 낸다. 단 아래쪽 기준(발/밑동)과 좌우 가운데는 지킨다 —
판을 그림의 아랫변 가운데에 세우기 때문에, 한쪽만 자르면 소품이 옆으로 밀리거나 공중에 뜬다.
"""
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "mapgen"))
import png  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OBJ = "https://backblaze.pixellab.ai/file/pixellab-characters/objects/b2f2eb25-bc38-463e-a6e5-95708f359131/{id}/rotations/unknown.png"
CHAR = "https://backblaze.pixellab.ai/file/pixellab-characters/b2f2eb25-bc38-463e-a6e5-95708f359131/{char}"
DIRS = ["south", "south-east", "east", "north-east", "north", "north-west", "west", "south-west"]


def download(url, dest):
    # 이 환경의 파이썬(msys)에는 인증서 묶음이 없어 urllib 이 TLS 검증에 실패한다. curl 은 된다.
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    subprocess.run(["curl", "-sfL", "--max-time", "60", "-o", dest, url], check=True)


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
    for item in manifest:
        dest = os.path.join(ROOT, item["dest"])
        if "object" in item:
            if os.path.exists(dest) and not force:
                continue
            try:
                download(OBJ.format(id=item["object"]), dest)
            except subprocess.CalledProcessError:
                print("pending", item["dest"])  # 아직 생성 중
                continue
            size = trim(dest) if item.get("trim", True) else None
            print("object", item["dest"], size)
        elif "anim" in item:
            # 캐릭터 애니메이션: 방향마다 애니메이션 id 가 따로 있다
            got = 0
            for d, anim in item["anim"].items():
                for i in range(item["frames"]):
                    fdest = dest.format(dir=d, i=i)
                    if os.path.exists(fdest) and not force:
                        continue
                    try:
                        download(f"{CHAR.format(char=item['char'])}/animations/{anim}/{d}/{i}.png", fdest)
                        got += 1
                    except subprocess.CalledProcessError:
                        print("pending", fdest)
            print("anim", item["dest"], got, "frames")
        elif item.get("rotations"):
            for d in DIRS:
                fdest = dest.format(dir=d)
                if not os.path.exists(fdest) or force:
                    download(f"{CHAR.format(char=item['char'])}/rotations/{d}.png", fdest)


if __name__ == "__main__":
    main()
