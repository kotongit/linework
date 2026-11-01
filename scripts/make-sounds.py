#!/usr/bin/env python3
"""Writes the Windows app's short original sound cues. Standard library only."""
import math, struct, wave
from pathlib import Path

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "windows/Linework.App/Resources/Sounds"


def tone(freq, seconds, decay, level=1.0, delay=0.0):
    total = int(RATE * (seconds + delay))
    samples = [0.0] * total
    for i in range(int(RATE * delay), total):
        t = i / RATE - delay
        attack = min(t / 0.003, 1)
        samples[i] = level * attack * math.exp(-t * decay) * math.sin(2 * math.pi * freq * t)
    return samples


def mix(*parts):
    length = max(len(p) for p in parts)
    return [sum(p[i] for p in parts if i < len(p)) for i in range(length)]


def write(name, samples, peak):
    top = max(abs(s) for s in samples) or 1
    # Fades the end, so nothing clicks.
    fade = int(RATE * 0.01)
    for i in range(fade):
        samples[-1 - i] *= i / fade
    with wave.open(str(OUT / name), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(b"".join(struct.pack("<h", int(s / top * peak * 32767)) for s in samples))


OUT.mkdir(parents=True, exist_ok=True)
write("tick.wav", tone(1760, 0.06, 70), 0.45)
write("chime.wav", mix(tone(880, 0.6, 7), tone(1320, 0.6, 9, 0.6), tone(1760, 0.4, 14, 0.25)), 0.7)
write("note.wav", mix(tone(1175, 0.35, 11), tone(2350, 0.25, 20, 0.2)), 0.35)
write("celebrate.wav", mix(*(tone(f, 0.9, 5, 1 - i * 0.1, i * 0.11) for i, f in enumerate([523.25, 659.25, 783.99, 1046.5]))), 0.7)
