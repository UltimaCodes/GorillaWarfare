"""Measures short WAV candidates for a snappy, thwip-shaped sound rather than picking by ear.

Usage: python tools/analyze_swishes.py <folder of .wav files>

Prints duration, crest factor (peak / RMS - how sharp the transient is against the average
level), how early the peak lands as a fraction of the clip's length, and an ASCII envelope.
A thwip wants to be short, with a high crest factor and an early peak - a crack that decays,
rather than a swell that builds. That's a different shape from a sustained scrape (see the
Slide bank, measured the same way) and is why the two are picked by different criteria.

Handles 8/16/24-bit PCM WAV without any third-party dependency - audioop, which used to do this,
was removed in Python 3.13.
"""
import wave, struct, glob, os, sys, math


def load(path):
    with wave.open(path, 'rb') as w:
        n = w.getnframes()
        sr = w.getframerate()
        sw = w.getsampwidth()
        ch = w.getnchannels()
        raw = w.readframes(n)

    if sw == 2:
        samples = struct.unpack('<%dh' % (len(raw) // 2), raw)
        maxval = 32768.0
    elif sw == 1:
        samples = [s - 128 for s in raw]
        maxval = 128.0
    elif sw == 3:
        count = len(raw) // 3
        samples = []
        for i in range(count):
            b = raw[i * 3:i * 3 + 3]
            v = b[0] | (b[1] << 8) | (b[2] << 16)
            if v & 0x800000:
                v -= 0x1000000
            samples.append(v)
        maxval = 8388608.0
    else:
        raise ValueError('unsupported sample width %d' % sw)

    if ch > 1:
        samples = samples[::ch]  # left channel only - good enough for shape, not for mixing

    return [s / maxval for s in samples], sr


def envelope(samples, buckets=40):
    n = len(samples)
    chunk = max(1, n // buckets)
    env = []
    for i in range(0, n, chunk):
        block = samples[i:i + chunk]
        if not block:
            continue
        env.append(math.sqrt(sum(x * x for x in block) / len(block)))
    return env


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else '.'
    paths = sorted(glob.glob(os.path.join(folder, '*.wav')))

    if not paths:
        print(f'no .wav files in {folder}')
        return

    results = []
    for path in paths:
        samples, sr = load(path)
        n = len(samples)
        duration = n / sr
        peak = max(abs(s) for s in samples)
        rms = math.sqrt(sum(s * s for s in samples) / n)
        crest = peak / rms if rms > 0 else 0
        peak_index = max(range(n), key=lambda i: abs(samples[i]))
        attack_frac = peak_index / n

        env = envelope(samples)
        peak_e = max(env) if env else 0
        bar = ''.join('#' if e > peak_e * 0.6 else ('.' if e > peak_e * 0.15 else ' ')
                      for e in env)

        results.append((os.path.basename(path), duration, crest, attack_frac, bar))

    print(f"{'file':<20}{'dur(s)':>8}{'crest':>8}{'attack%':>9}  envelope")
    for name, dur, crest, attack, bar in results:
        print(f"{name:<20}{dur:>8.3f}{crest:>8.2f}{attack * 100:>8.1f}%  {bar}")


if __name__ == '__main__':
    main()
