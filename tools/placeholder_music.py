"""Stand-in loops for the music slots that don't have a real track yet.

    python tools/placeholder_music.py

These exist so the game isn't silent in a slot while the real thing is being written, and so
the crossfade between slots can be heard working. They are not meant to be good, and they are
meant to be obviously temporary - a real track should never be mistaken for one of these.

Deliberately plain: a pulse and a drone, nothing that could be confused for a finished idea.
Delete the file and MusicPlayer falls back to whichever slot does have something.
"""

import math
import os
import wave

import numpy as np

RATE = 44100
OUT = "Assets/Resources/Audio/Music"


def log(message):
    print(f"[placeholder] {message}")


def axis(seconds):
    return np.linspace(0.0, seconds, int(RATE * seconds), endpoint=False)


def cycles(freq, seconds):
    """Nearest frequency completing whole cycles in the loop, so the seam doesn't click."""
    return max(1, round(freq * seconds)) / seconds


def tone(freq, seconds, harmonics=(1.0, 0.4, 0.15)):
    a = axis(seconds)
    out = np.zeros(len(a))

    for i, level in enumerate(harmonics):
        out += np.sin(2 * np.pi * cycles(freq * (i + 1), seconds) * a) * level

    return out / max(1e-9, np.max(np.abs(out)))


def lowpass(x, cutoff):
    a = math.exp(-2 * math.pi * cutoff / RATE)
    out = np.empty_like(x)
    last = 0.0

    for i, v in enumerate(x):
        last = (1 - a) * v + a * last
        out[i] = last

    return out


def beat(seconds, bpm, width=0.1, curve=7.0):
    a = axis(seconds)
    beats = max(1, round(seconds * bpm / 60.0))
    period = seconds / beats
    phase = np.mod(a, period) / period

    return np.exp(-curve * phase / max(width, 1e-6)) * (phase < width * 3)


def write(name, data, peak=0.5):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")

    top = np.max(np.abs(data))
    data = data * (peak / top) if top > 1e-9 else data
    pcm = (np.clip(data, -1.0, 1.0) * 32767).astype("<i2")

    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(pcm.tobytes())

    seam = abs(float(data[0]) - float(data[-1]))
    log(f"{name}.wav  {len(data) / RATE:.1f}s, seam {seam:.4f}")


def combat():
    """Something with a pulse, so the match isn't silent and the tempo is roughly right."""
    seconds = 16.0
    a = axis(seconds)

    bass = tone(43.7, seconds) * 0.85
    kick = np.sin(2 * np.pi * cycles(50.0, seconds) * a) * beat(seconds, bpm=165) * 0.9
    hat = lowpass(np.random.uniform(-1, 1, len(a)), 9000) * beat(seconds, bpm=330, width=0.03) * 0.16
    air = lowpass(np.random.uniform(-1, 1, len(a)), 200) * 0.3

    return bass * 0.5 + kick * 0.75 + hat + air


def over():
    """Deflated. Someone won, everyone else is reading a list."""
    seconds = 20.0
    a = axis(seconds)

    low = tone(55.0, seconds, (1.0, 0.3)) * 0.7
    fifth = tone(73.4, seconds, (1.0, 0.2)) * 0.35

    # One slow swell across the whole loop, so it meets itself.
    swell = 0.45 + 0.4 * np.sin(2 * np.pi * cycles(1.0 / seconds, seconds) * a)
    air = lowpass(np.random.uniform(-1, 1, len(a)), 150) * 0.35

    return (low + fifth) * swell + air


def main():
    np.random.seed(3)

    write("combat", combat())
    write("over", over())


if __name__ == "__main__":
    main()
