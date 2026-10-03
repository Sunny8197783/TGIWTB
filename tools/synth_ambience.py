"""환경음 고리(loop)를 만든다 → audio/ambience/*.wav (M4)

Kenney 팩에는 자연 환경음이 없어서 synth_sfx.py 처럼 직접 합성한다. numpy 없이 순수 파이썬.
  wind      바람: 잡음을 저역통과, 차단 주파수와 세기를 느리게 흔든다 (돌풍)
  birds     새: 대부분 조용하고, 가끔 짧은 지저귐 마디 (두 종류)
  crickets  귀뚜라미(밤): 4kHz 언저리 짧은 펄스 묶음, 세 마리가 엇갈린다
  waves     파도: 6초쯤마다 밀려와 부서지고 쓸려 나간다
  lake      호숫가 찰랑임: 낮은 잡음이 1~2초마다 작게 출렁
  waterfall 폭포: 굵은 잡음 + 낮은 울림, 아주 느리게 일렁

이음매: 끝에 덤으로 만든 구간을 앞머리에 겹쳐 섞어(crossfade) 반복해도 툭 끊기지 않게 한다.
python tools/synth_ambience.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "audio", "ambience")
FADE = 1.0  # 이음매 겹침 (초)


class SVF:
    """상태변수 필터. 계수를 매 샘플 바꿔도 안정적이다."""

    def __init__(self, q=0.7):
        self.low = self.band = 0.0
        self.q = q

    def step(self, x, f):
        fc = 2 * math.sin(math.pi * min(f, RATE / 6) / RATE)
        self.low += fc * self.band
        high = x - self.low - self.band / self.q
        self.band += fc * high
        return self.low, self.band, high


def lfo(rng, n, rate_hz, smooth=0.9995):
    """천천히 떠도는 0..1 값 (무작위 걸음을 매끄럽게)"""
    v, target, out = 0.5, 0.5, []
    step_every = max(1, int(RATE / max(rate_hz, 1e-3)))
    for i in range(n):
        if i % step_every == 0:
            target = rng.random()
        v = v * smooth + target * (1 - smooth)
        out.append(v)
    return out


def wind(n, rng):
    f, out = SVF(0.6), []
    cut, gust = lfo(rng, n, 0.15, 0.99995), lfo(rng, n, 0.25, 0.99993)
    for i in range(n):
        lo, _, _ = f.step(rng.uniform(-1, 1), 180 + 700 * cut[i])
        out.append(lo * (0.25 + 0.75 * gust[i] ** 1.5))
    return out


def chirp(buf, start, dur, f0, f1, amp, vib=0.0):
    n, ph = int(dur * RATE), 0.0
    for i in range(n):
        if start + i >= len(buf):
            break
        t = i / n
        env = math.sin(math.pi * t) ** 2
        f = f0 + (f1 - f0) * t + vib * math.sin(2 * math.pi * 30 * i / RATE)
        ph += 2 * math.pi * f / RATE
        buf[start + i] += amp * env * math.sin(ph)


def birds(n, rng):
    buf = [0.0] * n
    t = rng.uniform(0.3, 1.5)
    while t < n / RATE:
        start = int(t * RATE)
        if rng.random() < 0.6:
            # 종달새 같은 빠른 지저귐: 높게 오르내리는 짧은 음 4~8개
            base = rng.uniform(3200, 4200)
            for k in range(rng.randint(4, 8)):
                f0 = base * rng.uniform(0.9, 1.15)
                chirp(buf, start + int(k * 0.075 * RATE), 0.05, f0, f0 * rng.uniform(1.1, 1.35), rng.uniform(0.3, 0.5), 60)
        else:
            # 휘파람새 같은 두 음 "삐-요": 길게 하나, 내려가며 하나
            base = rng.uniform(2200, 2800)
            chirp(buf, start, 0.32, base, base * 1.04, 0.45, 25)
            chirp(buf, start + int(0.4 * RATE), 0.22, base * 1.3, base * 0.95, 0.4, 25)
        t += rng.uniform(1.6, 4.5)
    # 숲에 퍼지는 느낌: 짧은 메아리 두 겹
    for delay, g in ((0.11, 0.25), (0.23, 0.12)):
        d = int(delay * RATE)
        for i in range(n - 1, d - 1, -1):
            buf[i] += buf[i - d] * g
    return buf


def crickets(n, rng):
    buf = [0.0] * n
    for freq, period, amp in ((4300, 0.46, 0.5), (4650, 0.52, 0.35), (3950, 0.61, 0.3)):
        t = rng.uniform(0, period)
        while t < n / RATE:
            for p in range(rng.choice((3, 3, 4))):
                s = int((t + p * 0.026) * RATE)
                m = int(0.016 * RATE)
                for i in range(m):
                    if s + i < n:
                        env = math.sin(math.pi * i / m)
                        buf[s + i] += amp * env * math.sin(2 * math.pi * freq * (s + i) / RATE)
            t += period * rng.uniform(0.93, 1.07)
    return buf


def waves(n, rng):
    lo_f, hi_f, out = SVF(0.5), SVF(0.7), []
    brown = 0.0
    swell, t = [0.0] * n, 0.0
    while t < n / RATE:
        # 밀려오기 1.6초 → 부서짐 → 4초 동안 쓸려 나감
        rise, fall = rng.uniform(1.3, 1.9), rng.uniform(3.2, 4.6)
        peak = rng.uniform(0.7, 1.0)
        for i in range(int((rise + fall) * RATE)):
            j = int(t * RATE) + i
            if j >= n:
                break
            s = i / RATE
            v = peak * (s / rise) ** 2 if s < rise else peak * math.exp(-(s - rise) * 1.1)
            swell[j] = max(swell[j], v)
        t += rise + fall * rng.uniform(0.55, 0.8)
    for i in range(n):
        w = rng.uniform(-1, 1)
        brown = brown * 0.995 + w * 0.05
        lo, _, _ = lo_f.step(brown * 6, 250 + 900 * swell[i])
        _, _, hi = hi_f.step(w, 3000)
        out.append(lo * (0.2 + 0.8 * swell[i]) + hi * 0.08 * swell[i] ** 3)
    return out


def lake(n, rng):
    f, out = SVF(1.2), []
    lap, t = [0.0] * n, 0.0
    while t < n / RATE:
        dur, amp = rng.uniform(0.35, 0.6), rng.uniform(0.5, 1.0)
        for i in range(int(dur * RATE)):
            j = int(t * RATE) + i
            if j < n:
                lap[j] += amp * math.sin(math.pi * i / (dur * RATE)) ** 2
        t += rng.uniform(0.9, 2.2)
    for i in range(n):
        _, band, _ = f.step(rng.uniform(-1, 1), 320 + 260 * lap[i])
        out.append(band * (0.08 + 0.9 * lap[i]))
    return out


def waterfall(n, rng):
    lo_f, mid_f, out = SVF(0.6), SVF(0.9), []
    sway = lfo(rng, n, 0.4, 0.9998)
    for i in range(n):
        w = rng.uniform(-1, 1)
        lo, _, _ = lo_f.step(w, 160)
        _, mid, _ = mid_f.step(w, 900 + 300 * sway[i])
        out.append(lo * 1.6 + mid * 0.7 + w * 0.12)
    return out


def loop(gen, seconds, seed):
    rng = random.Random(seed)
    n, x = int(seconds * RATE), int(FADE * RATE)
    s = gen(n + x, rng)
    body = s[:n]
    for i in range(x):
        k = i / x
        body[i] = body[i] * k + s[n + i] * (1 - k)  # 끝이 앞머리로 녹아든다
    peak = max(abs(v) for v in body) or 1.0
    return [v / peak * 0.7 for v in body]


def write(name, samples):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, v)) * 32767)) for v in samples))
    print(path, f"{len(samples) / RATE:.1f}s")


if __name__ == "__main__":
    write("wind", loop(wind, 20, 1))
    write("birds", loop(birds, 24, 2))
    write("crickets", loop(crickets, 12, 3))
    write("waves", loop(waves, 24, 4))
    write("lake", loop(lake, 12, 5))
    write("waterfall", loop(waterfall, 10, 6))
