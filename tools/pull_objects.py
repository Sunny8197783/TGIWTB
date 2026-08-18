"""
PixelLab 맵 오브젝트(투명 배경 PNG)를 내려받는다.

맵 오브젝트는 **8시간 뒤 자동 삭제**되므로 생성 직후 바로 받아 둬야 한다.

인증은 pull_sprites 와 같은 경로로 읽고 **절대 출력하지 않는다**.

사용: python tools/pull_objects.py <출력폴더> <파일이름>=<object_id> [...]
      예) python tools/pull_objects.py art/objects house_a=784945f9-... well=c8b10649-...
"""
import sys, os, urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pull_sprites import auth_header, ssl_context

API = "https://api.pixellab.ai/mcp/map-objects/{}/download"


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    for arg in sys.argv[2:]:
        name, oid = arg.split("=", 1)
        req = urllib.request.Request(API.format(oid),
                                     headers={"Authorization": auth_header()})
        try:
            body = urllib.request.urlopen(req, timeout=180, context=ssl_context()).read()
        except urllib.error.HTTPError as e:
            print(f"{name}: 실패 HTTP {e.code}")
            continue

        path = os.path.join(out, f"{name}.png")
        open(path, "wb").write(body)
        print(f"{name}.png  {len(body)}바이트")

    return 0


if __name__ == "__main__":
    sys.exit(main())
