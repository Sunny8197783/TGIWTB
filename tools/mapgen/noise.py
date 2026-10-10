"""지도 생성기용 numpy 잡음·선 도구. 값은 전부 결정적(같은 seed → 같은 지도)."""
import math

import numpy as np


def _hash(ix, iz, seed):
    h = (ix * 374761393 + iz * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def hash01(ix, iz, seed):
    """정수 좌표 → 0..1 (스칼라·배열 둘 다)."""
    return _hash(np.asarray(ix, np.int64), np.asarray(iz, np.int64), seed)


def vnoise(x, z, seed):
    x = np.asarray(x, np.float64)
    z = np.asarray(z, np.float64)
    ix = np.floor(x).astype(np.int64)
    iz = np.floor(z).astype(np.int64)
    fx, fz = x - ix, z - iz
    ux, uz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    a = _hash(ix, iz, seed)
    b = _hash(ix + 1, iz, seed)
    c = _hash(ix, iz + 1, seed)
    d = _hash(ix + 1, iz + 1, seed)
    return a + (b - a) * ux + (c - a) * uz + (a - b - c + d) * ux * uz


def fbm(x, z, seed, octaves=4):
    """-1..1 (대략)."""
    total, amp, freq, norm = 0.0, 1.0, 1.0, 0.0
    for o in range(octaves):
        total = total + (vnoise(x * freq, z * freq, seed + o * 17) * 2 - 1) * amp
        norm += amp
        amp *= 0.5
        freq *= 2.0
    return total / norm


def n1(x, seed, octaves=3):
    """한 축 잡음 (절벽선·해안선)."""
    return fbm(x, np.full_like(np.asarray(x, np.float64), 0.37), seed, octaves)


def ridged(x, z, seed, octaves=4):
    """0..1 능선 잡음 — 산줄기."""
    total, amp, freq, norm = 0.0, 1.0, 1.0, 0.0
    for o in range(octaves):
        v = 1.0 - np.abs(vnoise(x * freq, z * freq, seed + o * 31) * 2 - 1)
        total = total + v * v * amp
        norm += amp
        amp *= 0.5
        freq *= 2.1
    return total / norm


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, np.float64) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def gauss(dx, dz, r):
    return np.exp(-(dx * dx + dz * dz) / (r * r))


# --- 선 ------------------------------------------------------------------

def catmull(pts, step=2.0):
    """점들을 지나는 매끈한 곡선을 step m 간격 점으로."""
    if len(pts) < 3:
        return densify(pts, step)
    p = [pts[0]] + list(pts) + [pts[-1]]
    out = []
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        n = max(1, int(math.hypot(p2[0] - p1[0], p2[1] - p1[1]) / step))
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in (0, 1)))
    out.append(tuple(pts[-1]))
    return out


def densify(pts, step=2.0):
    out = []
    for a, b in zip(pts[:-1], pts[1:]):
        n = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / step))
        for k in range(n):
            out.append((a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n))
    out.append(tuple(pts[-1]))
    return out


def meander(pts, amp, wave, seed, keep_ends=12.0):
    """선을 옆으로 살살 흔든다 (길이 자로 그은 것처럼 보이지 않게). 양 끝은 고정."""
    if amp <= 0 or len(pts) < 3:
        return pts
    acc, cum = 0.0, [0.0]
    for a, b in zip(pts[:-1], pts[1:]):
        acc += math.hypot(b[0] - a[0], b[1] - a[1])
        cum.append(acc)
    total = acc
    out = []
    for i, (x, z) in enumerate(pts):
        a, b = pts[max(0, i - 1)], pts[min(len(pts) - 1, i + 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dz) or 1.0
        nx, nz = -dz / ln, dx / ln
        end = min(cum[i], total - cum[i])
        k = min(1.0, end / keep_ends)
        off = amp * k * float(fbm(np.array(cum[i] / wave), np.array(0.5), seed, 2))
        out.append((x + nx * off, z + nz * off))
    return out


def poly_field(pts, R, shape, off=0.0):
    """격자의 각 점에서 폴리라인까지 거리(R 밖은 inf)와 시작점부터의 비율 s(0..1).
    선분마다 둘레 상자만 계산한다 (지도 전체를 매번 훑으면 느리다)."""
    nz, nx = shape
    d = np.full(shape, np.inf)
    s = np.zeros(shape)
    lens = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts[:-1], pts[1:])]
    total = sum(lens) or 1.0
    acc = 0.0
    for (a, b), L in zip(zip(pts[:-1], pts[1:]), lens):
        x0 = max(0, int(math.floor(min(a[0], b[0]) - R - off)))
        x1 = min(nx, int(math.ceil(max(a[0], b[0]) + R - off)) + 1)
        z0 = max(0, int(math.floor(min(a[1], b[1]) - R - off)))
        z1 = min(nz, int(math.ceil(max(a[1], b[1]) + R - off)) + 1)
        if x0 < x1 and z0 < z1:
            XX, ZZ = np.meshgrid(np.arange(x0, x1) + off, np.arange(z0, z1) + off)
            dx, dz = b[0] - a[0], b[1] - a[1]
            l2 = dx * dx + dz * dz
            t = np.clip(((XX - a[0]) * dx + (ZZ - a[1]) * dz) / l2, 0, 1) if l2 > 0 else np.zeros_like(XX)
            dist = np.hypot(XX - (a[0] + dx * t), ZZ - (a[1] + dz * t))
            sd, ss = d[z0:z1, x0:x1], s[z0:z1, x0:x1]
            m = (dist < sd) & (dist <= R)
            sd[m] = dist[m]
            ss[m] = (acc + L * t[m]) / total
        acc += L
    return d, s


def polyline_length(pts):
    return sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts[:-1], pts[1:]))


def point_dist(px, pz, pts):
    """한 점에서 폴리라인까지 거리 (스칼라)."""
    best = 1e9
    for a, b in zip(pts[:-1], pts[1:]):
        dx, dz = b[0] - a[0], b[1] - a[1]
        l2 = dx * dx + dz * dz
        t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((px - a[0]) * dx + (pz - a[1]) * dz) / l2))
        best = min(best, math.hypot(px - a[0] - dx * t, pz - a[1] - dz * t))
    return best


if __name__ == "__main__":
    # 자기 점검: 같은 입력 → 같은 값, 범위, 선 거리
    a = fbm(np.array([1.3, 50.2]), np.array([7.7, 9.1]), 5)
    assert np.allclose(a, fbm(np.array([1.3, 50.2]), np.array([7.7, 9.1]), 5))
    assert np.all(np.abs(a) <= 1.0)
    d, s = poly_field([(0, 0), (10, 0)], 3, (5, 12))
    assert abs(d[2, 5] - 2.0) < 1e-9 and abs(s[0, 5] - 0.5) < 1e-9 and np.isinf(d[4, 11])
    assert abs(point_dist(5, 2, [(0, 0), (10, 0)]) - 2.0) < 1e-9
    c = catmull([(0, 0), (10, 0), (20, 10)], 1.0)
    assert c[0] == (0, 0) and c[-1] == (20, 10)
    print("noise ok")
