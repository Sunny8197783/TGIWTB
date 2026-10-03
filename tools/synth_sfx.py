"""Kenney 팩에 없는 '바람 가르는 소리'를 만든다 → audio/sfx/*.wav

휘두름(swing): 짧고 높게 쓸고 지나가는 바람. 공격마다 한 층으로 깐다.
돌진(dash): 길고 낮게 밀려가는 바람.
만드는 법: 흰 잡음을 공진 대역통과 필터에 통과시키면서 중심 주파수를 올렸다 내리고, 음량 곡선을 입힌다.
python tools/synth_sfx.py
"""
import math
import os
import random
import struct
import wave

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "audio", "sfx")


def whoosh(seconds, f_lo, f_peak, f_end, attack, q, gain, seed):
    rng = random.Random(seed)
    n = int(RATE * seconds)
    out = []
    # 상태변수 필터(SVF) — 계수를 매 샘플 바꿔도 안정적이다
    low = band = 0.0
    peak_at = attack / seconds
    for i in range(n):
        t = i / n
        if t < peak_at:
            k = t / peak_at
            f = f_lo + (f_peak - f_lo) * k * k
            env = math.sin(k * math.pi * 0.5)
        else:
            k = (t - peak_at) / (1 - peak_at)
            f = f_peak + (f_end - f_peak) * k
            env = (1 - k) ** 2.2
        fc = 2 * math.sin(math.pi * min(f, RATE / 6) / RATE)
        x = rng.uniform(-1, 1)
        low += fc * band
        high = x - low - band / q
        band += fc * high
        out.append(band * env * gain)
    peak = max(abs(v) for v in out) or 1
    return [v / peak * 0.85 for v in out]


def save(name, samples):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, v)) * 32767)) for v in samples))
    print(path)


def main():
    for i in range(4):
        save(f"swing_{i}", whoosh(0.22 + i * 0.02, 500, 2600 + i * 300, 900, 0.05, 1.6, 1, seed=10 + i))
    for i in range(2):
        save(f"swing_heavy_{i}", whoosh(0.34, 250, 1500 + i * 200, 400, 0.12, 1.3, 1, seed=20 + i))
    for i in range(3):
        save(f"dash_{i}", whoosh(0.30, 300, 1300 + i * 150, 500, 0.06, 0.9, 1, seed=30 + i))


if __name__ == "__main__":
    main()
