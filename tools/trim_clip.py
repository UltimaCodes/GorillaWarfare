"""Trims a sourced WAV to a length a weapon can actually fire at, and shrinks it on the way.

The explosion recordings from the Sonniss bundle arrive as 24 bit 96kHz stereo, which is a
mastering format. For a gunshot that is three kinds of overkill at once: nothing in the game
listens above 20kHz, nothing needs 24 bits of dynamic range through a Vorbis encoder, and a
weapon report has no stereo image because it comes from wherever the shooter is standing.

The length matters more than any of that. A shot sound longer than the interval between shots
overlaps itself, and a weapon whose own sound is machine-gunning underneath it sounds broken -
which is exactly what AudioCheck flagged on the pineapple launcher: a 1304ms clip on a weapon
that fires every 1111ms.

Usage:  python tools/trim_clip.py <in.wav> <out.wav> <milliseconds>
"""

import struct
import sys


def read_wav(path):
    data = open(path, "rb").read()

    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise SystemExit(f"{path} is not a RIFF WAVE")

    at = 12
    fmt = None
    samples = None

    # Walk the chunks rather than assuming fmt then data. Recordings from real libraries carry
    # LIST, bext and other metadata chunks in between, and a reader that assumes the layout
    # reads the metadata as audio.
    while at + 8 <= len(data):
        name = data[at:at + 4]
        size = struct.unpack("<I", data[at + 4:at + 8])[0]
        body = data[at + 8:at + 8 + size]

        if name == b"fmt ":
            tag, channels, rate, _, _, bits = struct.unpack("<HHIIHH", body[:16])
            fmt = (tag, channels, rate, bits)
        elif name == b"data":
            samples = body

        at += 8 + size + (size & 1)

    if fmt is None or samples is None:
        raise SystemExit(f"{path} has no fmt or data chunk")

    return fmt, samples


def to_float_mono(fmt, raw):
    tag, channels, rate, bits = fmt
    width = bits // 8
    frames = len(raw) // (width * channels)
    out = []

    for f in range(frames):
        total = 0.0

        for c in range(channels):
            at = (f * channels + c) * width
            chunk = raw[at:at + width]

            if bits == 16:
                value = struct.unpack("<h", chunk)[0] / 32768.0
            elif bits == 24:
                # Little endian, sign extended off the top byte.
                v = chunk[0] | (chunk[1] << 8) | (chunk[2] << 16)
                if v & 0x800000:
                    v -= 0x1000000
                value = v / 8388608.0
            elif bits == 32 and tag == 3:
                value = struct.unpack("<f", chunk)[0]
            elif bits == 32:
                value = struct.unpack("<i", chunk)[0] / 2147483648.0
            else:
                raise SystemExit(f"unsupported: {bits} bit, tag {tag}")

            total += value

        out.append(total / channels)

    return out, rate


def resample(samples, source_rate, target_rate):
    if source_rate == target_rate:
        return samples

    count = max(1, int(len(samples) * target_rate / source_rate))
    out = []

    for i in range(count):
        at = i * (len(samples) - 1) / max(1, count - 1)
        low = int(at)
        high = min(low + 1, len(samples) - 1)
        out.append(samples[low] + (samples[high] - samples[low]) * (at - low))

    return out


def write_wav(path, samples, rate):
    body = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples)

    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(body)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16))
        f.write(b"data" + struct.pack("<I", len(body)) + body)


def main():
    source, target, millis = sys.argv[1], sys.argv[2], float(sys.argv[3])

    fmt, raw = read_wav(source)
    mono, rate = to_float_mono(fmt, raw)

    keep = int(rate * millis / 1000.0)
    cut = mono[:keep]

    # A fade over the last 15% of what is kept. Cutting a decaying explosion dead leaves a step
    # in the waveform, and a step is a click - which would be a new transient in a clip whose
    # whole job is to be one.
    fade = max(1, int(len(cut) * 0.15))

    for i in range(fade):
        cut[len(cut) - fade + i] *= 1.0 - i / fade

    out = resample(cut, rate, 44100)

    write_wav(target, out, 44100)

    peak = max(abs(s) for s in out) if out else 0.0
    print(f"{source} -> {target}")
    print(f"  {fmt[3]} bit {fmt[2]}Hz {fmt[1]}ch {len(mono) / rate * 1000:.0f}ms")
    print(f"  16 bit 44100Hz mono {len(out) / 44100 * 1000:.0f}ms  peak {peak:.2f}")


if __name__ == "__main__":
    main()
