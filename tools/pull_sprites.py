"""
PixelLab 캐릭터의 완성된 애니메이션 zip 을 내려받아 art/player 규칙으로 펼친다.

zip 안 구조가 우리 규칙(`{클립}/{방향}/frame_NNN.png`)과 같아서 그대로 옮기면 된다.
전 작업이 끝나기 전에는 서버가 HTTP 423 을 주므로, 그때는 그냥 실패로 끝낸다(재시도는 호출자 몫).

API 키는 환경변수 PIXELLAB_API_KEY 또는 MCP 설정 파일에서 읽는다.
**절대 출력하지 않는다.**

사용: python tools/pull_sprites.py <character_id> <풀어놓을 폴더> [클립이름=폴더명 ...]
      예) python tools/pull_sprites.py fe30... build/pull  Sprint=run Punch=punch
      매핑을 주면 그 애니메이션만, 안 주면 전부 원래 이름 그대로 푼다.
"""
import sys, os, io, json, ssl, zipfile, glob, urllib.request

API = "https://api.pixellab.ai/mcp/characters/{}/download"


def auth_header():
    """Authorization 헤더 값을 만든다. 값은 어디에도 출력하지 않는다."""
    k = os.environ.get("PIXELLAB_API_KEY")
    if k:
        return k if k.lower().startswith("bearer ") else f"Bearer {k}"

    home = os.path.expanduser("~")
    for path in (os.path.join(home, ".claude.json"),
                 os.path.join(os.getcwd(), ".mcp.json")):
        if not os.path.isfile(path):
            continue
        try:
            blob = json.load(open(path, encoding="utf-8"))
        except Exception:
            continue
        found = []

        # MCP 서버 항목 중 pixellab 을 가리키고 Authorization 헤더를 가진 것을 찾는다.
        def walk(node):
            if isinstance(node, dict):
                url = node.get("url")
                hdr = node.get("headers")
                if isinstance(url, str) and "pixellab" in url and isinstance(hdr, dict):
                    for key, val in hdr.items():
                        if key.lower() == "authorization" and isinstance(val, str):
                            found.append(val)
                for v in node.values():
                    walk(v)
            elif isinstance(node, list):
                for v in node:
                    walk(v)

        walk(blob)
        if found:
            return found[0]

    raise SystemExit("PixelLab 인증 정보를 찾지 못했다. PIXELLAB_API_KEY 환경변수로 넣어라.")


def ssl_context():
    """
    MSYS2 파이썬은 기본 신뢰 저장소가 비어 있어 검증에 실패한다.
    인증 토큰을 실어 보내는 요청이므로 검증을 끄지 않고, CA 번들을 찾아 붙인다.
    """
    ctx = ssl.create_default_context()
    if ctx.cert_store_stats().get("x509_ca", 0) > 0:
        return ctx
    for path in ("/usr/ssl/certs/ca-bundle.crt",
                 "C:/msys64/usr/ssl/certs/ca-bundle.crt",
                 "/etc/ssl/certs/ca-certificates.crt"):
        if os.path.isfile(path):
            ctx.load_verify_locations(path)
            return ctx
    raise SystemExit("CA 번들을 찾지 못했다. SSL_CERT_FILE 을 지정해라.")


def slug(name):
    return "".join(c if c.isalnum() else "-" for c in name.lower()).strip("-")


def main():
    char_id, out = sys.argv[1], sys.argv[2]
    mapping = {}
    for arg in sys.argv[3:]:
        src, dst = arg.split("=", 1)
        mapping[slug(src)] = dst

    req = urllib.request.Request(API.format(char_id),
                                 headers={"Authorization": auth_header()})
    try:
        body = urllib.request.urlopen(req, timeout=300, context=ssl_context()).read()
    except urllib.error.HTTPError as e:
        # 423 = 아직 생성 중. 키는 절대 찍지 않는다.
        raise SystemExit(f"다운로드 실패 HTTP {e.code}")

    os.makedirs(out, exist_ok=True)
    written = {}
    with zipfile.ZipFile(io.BytesIO(body)) as z:
        for info in z.infolist():
            if info.is_dir() or not info.filename.lower().endswith(".png"):
                continue
            parts = info.filename.replace("\\", "/").split("/")
            if len(parts) < 3:
                continue
            clip, direction, fname = parts[-3], parts[-2], parts[-1]
            key = slug(clip)
            if mapping and key not in mapping:
                continue
            dst_clip = mapping.get(key, key)
            d = os.path.join(out, dst_clip, direction)
            os.makedirs(d, exist_ok=True)
            open(os.path.join(d, fname), "wb").write(z.read(info))
            written.setdefault(dst_clip, set()).add(direction)

    for clip in sorted(written):
        n = len(glob.glob(os.path.join(out, clip, sorted(written[clip])[0], "frame_*.png")))
        print(f"{clip}: {len(written[clip])}방향 x {n}프레임")
    if not written:
        print("받은 것 없음 — 애니메이션 이름 매핑을 확인해라.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
