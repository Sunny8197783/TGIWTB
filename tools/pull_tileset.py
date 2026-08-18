"""
PixelLab Wang 타일셋(메타데이터 JSON + 스프라이트시트 PNG)을 내려받는다.

두 파일이 한 쌍이다:
  /mcp/tilesets/{id}/metadata → {이름}_metadata.json
  /mcp/tilesets/{id}/image    → {이름}_image.png

메타데이터의 tiles[i].corners(NW/NE/SW/SE = lower|upper)와 bounding_box 로
Godot 의 코너 지형(terrain peering bit)을 구성한다. original_position 은
생성 당시 배치라 이 시트와 어긋날 수 있으니 쓰지 않는다.

인증은 pull_sprites 와 같은 경로로 읽고 **절대 출력하지 않는다**.

사용: python tools/pull_tileset.py <출력폴더> <이름>=<tileset_id> [...]
      예) python tools/pull_tileset.py art/tiles grass_dirt=2a133152-... dirt_stone=09bc7fb5-...
"""
import sys, os, json, urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pull_sprites import auth_header, ssl_context

API = "https://api.pixellab.ai/mcp/tilesets/{}/{}"

# /image 는 backblaze 로 리다이렉트되는데, urllib 이 Authorization 헤더를 그대로
# 따라 보내면 서명 URL 이 이를 거부해 403 이 난다. inline=true 는 API 호스트가
# PNG 를 직접 내려주므로 리다이렉트가 없다.
KIND_QUERY = {"image": "?inline=true"}


def fetch(tileset_id, kind):
    url = API.format(tileset_id, kind) + KIND_QUERY.get(kind, "")
    req = urllib.request.Request(url, headers={"Authorization": auth_header()})
    try:
        return urllib.request.urlopen(req, timeout=180, context=ssl_context()).read()
    except urllib.error.HTTPError as e:
        raise SystemExit(f"{kind} 다운로드 실패 HTTP {e.code}")


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    for arg in sys.argv[2:]:
        name, tid = arg.split("=", 1)

        meta_bytes = fetch(tid, "metadata")
        meta_path = os.path.join(out, f"{name}_metadata.json")
        open(meta_path, "wb").write(meta_bytes)

        png_path = os.path.join(out, f"{name}_image.png")
        open(png_path, "wb").write(fetch(tid, "image"))

        meta = json.loads(meta_bytes)
        data = meta["tileset_data"]
        size = data["tile_size"]
        prompts = meta["metadata"]["terrain_prompts"]
        print(f"{name}: {len(data['tiles'])}타일 {size['width']}x{size['height']}")
        print(f"  lower = {prompts['lower'][:52]}")
        print(f"  upper = {prompts['upper'][:52]}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
