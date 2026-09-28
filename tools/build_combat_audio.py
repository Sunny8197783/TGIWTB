"""Reproducible original synthesized combat effects. No external samples or dependencies."""
import math, random, struct, wave
from pathlib import Path

RATE = 44100
ROOT = Path(__file__).resolve().parents[1] / 'art' / 'audio'
ROOT.mkdir(parents=True, exist_ok=True)
for name, duration, bass, noise, ring in [
    ('swing', .15, 0, .42, 0), ('hit', .16, 110, .32, 0),
    ('heavy', .27, 65, .42, 0), ('hurt', .22, 135, .28, 0),
    ('block', .19, 170, .25, 1800), ('parry', .35, 0, .1, 2400),
    ('kill', .32, 75, .42, 700),
]:
    rng = random.Random(name)
    values = []
    smooth = 0.0
    for i in range(int(RATE * duration)):
        t = i / RATE
        x = t / duration
        smooth = .72 * smooth + .28 * rng.uniform(-1, 1)
        envelope = min(1, t / .003) * math.exp(-7 * x) * (1 - x)
        if name == 'swing': envelope = math.sin(math.pi * x) ** 2 * (1 - x)
        sample = noise * smooth * 2
        if bass: sample += .48 * math.sin(2 * math.pi * (bass * t - bass * t*t / (duration*3)))
        if ring: sample += .2 * (math.sin(2*math.pi*ring*t) + .4*math.sin(2*math.pi*ring*1.47*t))
        values.append(max(-.85, min(.85, sample * envelope)))
    with wave.open(str(ROOT / (name + '.wav')), 'wb') as out:
        out.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        out.writeframes(b''.join(struct.pack('<h', round(v * 32767)) for v in values))
    print(name, len(values), 'samples', 'peak', round(max(map(abs, values)), 3))
