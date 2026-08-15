"""Builds the game's feedback sounds from scratch.

    python tools/sound_generator.py

Synthesised rather than sourced, for three reasons. The pack sounds that were standing in are
wrong in a specific way - the death sound is a sci-fi explosion and the reload is a UI click -
and finding replacements means trawling for something that happens to fit. These are written to
fit: a hit tick that cuts through gunfire without being a beep, a kill that is unmistakably a
different event, a reload that sounds like fruit. They're also CC0 by construction, and tunable
by editing a number rather than by going looking again.

Everything is 44.1kHz mono 16-bit, which is what AssetFixups imports them as anyway.

The house style is ULTRAKILL and Cruelty Squad: harsh, saturated, slightly too loud. Nothing
here is clean, and the soft clipping is deliberate.
"""

import math
import os
import struct
import wave

import numpy as np

RATE = 44100
OUT = "Assets/Resources/Audio"


# ----------------------------------------------------------------- building blocks

def samples(seconds):
    return int(RATE * seconds)


def t(seconds):
    """Time axis, in seconds."""
    return np.linspace(0.0, seconds, samples(seconds), endpoint=False)


def env(length, attack=0.002, decay=None, curve=2.5):
    """Percussive envelope: near instant attack, exponential fall."""
    n = length if isinstance(length, int) else samples(length)
    decay = decay if decay is not None else n / RATE

    a = min(samples(attack), max(1, n // 8))
    out = np.ones(n)

    out[:a] = np.linspace(0.0, 1.0, a)
    fall = np.linspace(0.0, 1.0, n - a)
    out[a:] = np.exp(-curve * fall * (n / RATE) / decay)

    return out


def sweep(f0, f1, seconds, shape="exp"):
    """A tone whose pitch slides. Phase is integrated, or it clicks at the joins."""
    axis = t(seconds)

    if shape == "exp":
        freq = f0 * (f1 / f0) ** (axis / max(seconds, 1e-6))
    else:
        freq = np.linspace(f0, f1, len(axis))

    phase = 2 * np.pi * np.cumsum(freq) / RATE
    return np.sin(phase)


def tone(freq, seconds):
    return np.sin(2 * np.pi * freq * t(seconds))


def noise(seconds):
    return np.random.uniform(-1.0, 1.0, samples(seconds))


def lowpass(x, cutoff):
    """One pole filter. Crude, and crude is the point - it takes the fizz off without
    making anything sound polished."""
    a = math.exp(-2 * math.pi * cutoff / RATE)
    out = np.empty_like(x)
    last = 0.0

    for i, v in enumerate(x):
        last = (1 - a) * v + a * last
        out[i] = last

    return out


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def saturate(x, amount=2.0):
    """Soft clip. Adds grit and, more usefully, makes a quiet sound feel loud without
    actually being louder."""
    return np.tanh(x * amount) / np.tanh(amount)


def normalise(x, peak=0.89):
    top = np.max(np.abs(x))
    return x * (peak / top) if top > 1e-9 else x


def pad(x, seconds=0.01):
    """A moment of silence on the end, so nothing clicks as it stops."""
    return np.concatenate([x, np.zeros(samples(seconds))])


def write(bank, name, data):
    folder = os.path.join(OUT, bank)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, name + ".wav")

    data = normalise(pad(data))
    pcm = (np.clip(data, -1.0, 1.0) * 32767).astype("<i2")

    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(pcm.tobytes())

    print(f"[sound] {bank}/{name}.wav  {len(data) / RATE * 1000:.0f}ms")


# ----------------------------------------------------------------- the sounds

def hit():
    """Landing a shot. Short, bright, dry.

    This is the single most useful sound in a shooter - it's the difference between aiming and
    guessing. It has to cut through a rifle firing ten times a second, which means high and
    percussive rather than loud, or it just adds to the mush.
    """
    length = 0.055
    body = tone(1500, length) * 0.7 + tone(2260, length) * 0.3
    tick = highpass(noise(length), 3000) * 0.35

    return saturate((body + tick) * env(length, decay=0.02, curve=4.0), 1.6)


def headshot():
    """Same job, but you want to know it was a head without reading anything."""
    length = 0.085
    body = sweep(1800, 3000, length) * 0.6 + tone(2400, length) * 0.4
    tick = highpass(noise(length), 4000) * 0.4

    return saturate((body + tick) * env(length, decay=0.035, curve=3.2), 1.9)


def kill():
    """Someone died and it was you. Has to be unmistakably a different event from a hit, so it
    goes downward where the hit goes up, and it's allowed to take its time."""
    length = 0.34

    fall = sweep(660, 220, length) * 0.5
    thump = sweep(150, 55, length) * 0.6
    grit = lowpass(noise(length), 1800) * 0.18

    shaped = (fall + thump + grit) * env(length, decay=0.16, curve=2.6)
    return saturate(shaped, 2.4)


def reload_():
    """You eat the old banana and pull a fresh one out.

    Wet rather than mechanical. A rifle click would be the obvious sound and the wrong one -
    the weapon is fruit, and the reload is the joke.
    """
    length = 0.44
    out = np.zeros(samples(length))

    # The peel: filtered noise sliding down.
    peel = lowpass(noise(0.16), 2600) * env(0.16, decay=0.09, curve=2.0)
    peel *= np.linspace(1.0, 0.35, len(peel))
    out[:len(peel)] += peel * 0.8

    # The bite: a short low thud.
    bite_at = samples(0.17)
    bite = (sweep(320, 90, 0.09) * 0.8 + lowpass(noise(0.09), 900) * 0.4) * env(0.09, decay=0.05)
    out[bite_at:bite_at + len(bite)] += bite

    # A fresh one out of the bunch: a soft pop.
    pop_at = samples(0.30)
    pop = sweep(180, 700, 0.05) * env(0.05, decay=0.03, curve=3.5)
    out[pop_at:pop_at + len(pop)] += pop * 0.7

    return saturate(out, 1.7)


def death():
    """Yours, or someone else's, nearby. Heavy and organic - the placeholder was a sci-fi
    explosion, which is the one thing a gorilla dying should not sound like."""
    length = 0.85

    body = sweep(190, 42, length) * 0.75
    guts = lowpass(noise(length), 700) * 0.5
    crack = highpass(noise(0.05), 2000) * 0.5

    out = (body + guts) * env(length, decay=0.42, curve=2.2)
    out[:len(crack)] += crack * env(0.05, decay=0.03)

    return saturate(out, 2.8)


def swing():
    """The peel, swung. Air, not impact."""
    length = 0.26

    air = lowpass(highpass(noise(length), 400), 5000)
    shape = np.concatenate([
        np.linspace(0.0, 1.0, samples(0.09)),
        np.linspace(1.0, 0.0, samples(length) - samples(0.09)),
    ])

    return saturate(air * shape ** 2 * 0.9, 1.4)


def confirm():
    """Menu confirm. Punchy and rude, not a spaceship door."""
    length = 0.13
    two = np.concatenate([tone(430, 0.05), tone(680, 0.08)])
    return saturate(two * env(length, decay=0.07, curve=2.0), 3.0)


def back():
    """Menu back. The same idea, downward."""
    length = 0.12
    two = np.concatenate([tone(620, 0.05), tone(360, 0.07)])
    return saturate(two * env(length, decay=0.06, curve=2.0), 3.0)


def main():
    np.random.seed(7)   # so a rerun produces the same files

    write("Hit", "hit", hit())
    write("Hit", "headshot", headshot())
    write("Kill", "kill", kill())
    write("Reload", "reload", reload_())
    write("Death", "death", death())
    write("Shoot/Peel", "swing", swing())
    write("UI", "confirm", confirm())
    write("UI", "back", back())


if __name__ == "__main__":
    main()
