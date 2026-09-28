"""표준 라이브러리만으로 PNG 를 읽고 쓴다 (이 환경엔 PIL 이 없다)."""
import struct
import zlib


def write_rgba(path, width, height, pixels):
    """pixels: 길이 w*h 의 (r,g,b,a) 튜플 목록, 행 우선."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        row = pixels[y * width:(y + 1) * width]
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))
    _write(path, width, height, 6, raw)


def write_gray(path, width, height, values):
    """values: 길이 w*h 의 0..255 정수."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw += bytes(values[y * width:(y + 1) * width])
    _write(path, width, height, 0, raw)


def _write(path, width, height, color_type, raw):
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", ihdr))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))


def read_rgba(path):
    """8비트 RGBA/RGB/회색 PNG 를 (w, h, [(r,g,b,a)...]) 로. 인터레이스 미지원."""
    with open(path, "rb") as f:
        data = f.read()
    pos = 8
    idat = bytearray()
    palette = None
    trns = None
    while pos < len(data):
        length, tag = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if tag == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
        elif tag == b"PLTE":
            palette = [tuple(body[i:i + 3]) for i in range(0, len(body), 3)]
        elif tag == b"tRNS":
            trns = body
        elif tag == b"IDAT":
            idat += body
        pos += 12 + length
    assert depth == 8, "8비트만 지원"
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw = zlib.decompress(bytes(idat))
    stride = w * channels
    out = []
    prev = bytearray(stride)
    i = 0
    for _ in range(h):
        ft = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b = prev[x]
            c = prev[x - channels] if x >= channels else 0
            if ft == 1:
                line[x] = (line[x] + a) & 255
            elif ft == 2:
                line[x] = (line[x] + b) & 255
            elif ft == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif ft == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        for x in range(w):
            px = line[x * channels:(x + 1) * channels]
            if ctype == 6:
                out.append(tuple(px))
            elif ctype == 2:
                out.append((px[0], px[1], px[2], 255))
            elif ctype == 0:
                out.append((px[0], px[0], px[0], 255))
            elif ctype == 4:
                out.append((px[0], px[0], px[0], px[1]))
            elif ctype == 3:
                r, g, b = palette[px[0]]
                a = trns[px[0]] if trns and px[0] < len(trns) else 255
                out.append((r, g, b, a))
        prev = line
    return w, h, out
