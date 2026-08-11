"""
art/player 클립의 8방향 일관성을 객관적으로 검사한다.

검사 항목:

  1) 프레임 수    — 방향마다 같은가
  2) 이펙트 오염  — 한 방향에만 밝고 채도 높은 덩어리가 붙었는가
  3) 캐릭터 크기  — 방향별 불투명 픽셀 수가 지나치게 차이 나는가

한계: 색 팔레트 전체를 비교하지는 않는다. 정면은 얼굴, 뒷면은 배낭이 보이므로
방향마다 색 구성이 다른 것이 정상이라, 팔레트 비교는 오탐만 낸다.
'방향별 동작이 같은가'를 보장하는 진짜 수단은 검사가 아니라 생성 방식이다 —
템플릿 모드는 8방향이 하나의 스켈레톤에서 나오므로 구조적으로 일관된다.
v3 커스텀 모드는 방향마다 독립 생성되어 제각각이 된다.

사용: python tools/check_sprite_consistency.py [클립이름 ...]
      인자가 없으면 art/player 아래 모든 클립을 검사한다.
종료 코드 0 = 전부 일관, 1 = 이상 있음.
"""
import sys, os, glob, zlib, struct
from collections import Counter

ROOT = os.path.join("art", "player")
DIRS = ["south", "south-east", "east", "north-east",
        "north", "north-west", "west", "south-west"]

# 이펙트 판정: 다른 방향 평균의 이 배수를 넘고 GLOW_FLOOR 개도 넘으면 이상으로 본다.
GLOW_TOLERANCE = 2.5
GLOW_FLOOR = 25

# 이 비율 이상 차이 나면 크기 이상으로 본다.
SIZE_TOLERANCE = 0.45


def read_png_rgba(path):
    """의존성 없이 PNG 를 RGBA 픽셀 리스트로 읽는다 (8bit RGBA/RGB 만)."""
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("PNG 아님")
    pos, idat, w, h, depth, ctype = 8, b"", 0, 0, 8, 6
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        typ = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + ln]
        if typ == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", chunk[:10])
        elif typ == b"IDAT":
            idat += chunk
        elif typ == b"IEND":
            break
        pos += 12 + ln
    if depth != 8 or ctype not in (2, 6):
        raise ValueError(f"지원하지 않는 포맷 depth={depth} ctype={ctype}")

    ch = 4 if ctype == 6 else 3
    raw = zlib.decompress(idat)
    stride = w * ch
    out, prev, p = [], bytearray(stride), 0
    for _ in range(h):
        f = raw[p]; p += 1
        line = bytearray(raw[p:p + stride]); p += stride
        for i in range(stride):
            a = line[i - ch] if i >= ch else 0
            b = prev[i]
            c = prev[i - ch] if i >= ch else 0
            if f == 1:   line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 255
        for i in range(0, stride, ch):
            if ch == 4:
                out.append((line[i], line[i + 1], line[i + 2], line[i + 3]))
            else:
                out.append((line[i], line[i + 1], line[i + 2], 255))
        prev = line
    return w, h, out


def clip_report(clip):
    base = os.path.join(ROOT, clip)
    if not os.path.isdir(base):
        return [f"{clip}: 폴더 없음"]

    problems, counts, glows, sizes = [], {}, {}, {}

    for d in DIRS:
        files = sorted(glob.glob(os.path.join(base, d, "frame_*.png")))
        if not files:
            problems.append(f"{clip}/{d}: 프레임 없음")
            continue
        counts[d] = len(files)

        opaque, glow = 0, 0
        for f in files:
            _, _, px = read_png_rgba(f)
            for r, g, b, a in px:
                if a <= 32:
                    continue
                opaque += 1
                mx, mn = max(r, g, b), min(r, g, b)
                # 밝고(>200) 채도가 낮지 않은 픽셀 = 발광 이펙트에 가깝다.
                # 캐릭터의 흰 티셔츠는 밝지만 채도가 거의 0 이라 걸러진다.
                if mx > 200 and (mx - mn) > 40:
                    glow += 1
        glows[d] = glow / max(1, len(files))
        sizes[d] = opaque / max(1, len(files))

    if not counts:
        return problems

    # 1) 프레임 수
    if len(set(counts.values())) > 1:
        problems.append(f"{clip}: 프레임 수 불일치 {counts}")

    # 2) 이펙트 오염 — 한 방향에만 '밝고 채도 높은 덩어리'가 붙는 경우.
    #
    #    팔레트를 통째로 비교하면 안 된다: 정면은 얼굴, 뒷면은 배낭이 보이므로
    #    방향마다 색 구성이 다른 것이 정상이다. 그래서 캐릭터 고유색이 아닌
    #    '이펙트처럼 튀는 픽셀'의 양만 방향끼리 비교한다.
    if glows:
        avg = sum(glows.values()) / len(glows)
        for d, g in glows.items():
            if g > max(GLOW_FLOOR, avg * GLOW_TOLERANCE):
                problems.append(f"{clip}/{d}: 이펙트로 보이는 밝은 픽셀 {g:.0f}개/프레임 "
                                f"(다른 방향 평균 {avg:.0f})")

    # 3) 크기
    if sizes:
        avg = sum(sizes.values()) / len(sizes)
        for d, s in sizes.items():
            if avg > 0 and abs(s - avg) / avg > SIZE_TOLERANCE:
                problems.append(f"{clip}/{d}: 크기 이상 (평균 {avg:.0f}px, 이 방향 {s:.0f}px)")

    return problems


def main():
    clips = sys.argv[1:]
    if not clips:
        clips = sorted(d for d in os.listdir(ROOT)
                       if os.path.isdir(os.path.join(ROOT, d)) and d != "idle")

    total = []
    for c in clips:
        probs = clip_report(c)
        total += probs
        print(f"[{'FAIL' if probs else ' OK '}] {c}")
        for p in probs:
            print(f"        {p}")

    print()
    print(f"이상 {len(total)}건" if total else "전부 일관")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
