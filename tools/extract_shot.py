"""Cuts a single clean shot out of a longer recording.

    python tools/extract_shot.py source.wav Assets/Resources/Audio/Shoot/Pistol/pistol.wav

Range recordings are almost never one shot. The pack these came from has fifteen seconds of
someone emptying an SKS, and dropping that in whole is exactly the bug that shipped the first
time round - one trigger pull sounding like a burst, which took listening to catch.

So this finds the onsets, picks the best one, and cuts from just before it to where it has
decayed back into the noise floor. Picking the *best* rather than the first matters: the first
shot in a recording is often the one where the recorder was still adjusting, and the last is
often clipped by the file ending.

Also folds to mono and resamples to 44.1kHz, which is what everything else here is.
"""

import argparse
import os
import sys
import wave

import numpy as np

TARGET_RATE = 44100


def log(message):
    print(f"[shot] {message}")


def read(path):
    with wave.open(path, "rb") as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)

    if width != 2:
        raise SystemExit(f"[shot] {path} is {width * 8} bit; only 16 bit is handled")

    data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0

    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)

    return data, rate


def resample(data, rate, target=TARGET_RATE):
    if rate == target:
        return data

    # Linear is plenty for a gunshot - the content is broadband noise, and nobody is going to
    # hear interpolation error underneath a bang.
    count = int(len(data) * target / rate)
    return np.interp(
        np.linspace(0.0, len(data) - 1, count),
        np.arange(len(data)),
        data,
    )


def envelope(data, rate, window_ms=3.0):
    """Windowed RMS, which is what onsets are actually visible in."""
    window = max(1, int(rate * window_ms / 1000.0))
    count = len(data) // window

    trimmed = data[:count * window].reshape(count, window)
    return np.sqrt((trimmed ** 2).mean(axis=1)), window


def find_onsets(rms, rise=0.12, fall=0.04):
    onsets = []
    above = False

    peak = rms.max()
    if peak <= 0:
        return onsets

    for i, level in enumerate(rms / peak):
        if not above and level > rise:
            onsets.append(i)
            above = True
        elif above and level < fall:
            above = False

    return onsets


def extract(data, rate, lead_ms=8.0, tail_floor=0.02, max_seconds=1.4):
    rms, window = envelope(data, rate)
    onsets = find_onsets(rms)

    if not onsets:
        raise SystemExit("[shot] no onsets found - is the file silent?")

    log(f"{len(onsets)} shot(s) in the recording")

    # The loudest onset, because the quiet ones are usually the recorder still settling.
    def loudness(index):
        end = min(len(rms), index + int(rate * 0.15 / window))
        return rms[index:end].max()

    best = max(onsets, key=loudness)
    log(f"taking shot {onsets.index(best) + 1} of {len(onsets)}, the loudest")

    start = max(0, best * window - int(rate * lead_ms / 1000.0))

    # Run on until it has fallen back into the floor, or the next shot begins - whichever
    # comes first, so a fast follow-up doesn't get glued onto the end of this one.
    floor = rms.max() * tail_floor
    limit = start + int(rate * max_seconds)

    nxt = [o for o in onsets if o > best]
    if nxt:
        limit = min(limit, nxt[0] * window - int(rate * 0.01))

    end = limit
    for i in range(best, min(len(rms), limit // window)):
        if rms[i] < floor:
            end = i * window
            break

    clip = data[start:min(end, len(data))]

    # Put the bang at the front.
    #
    # The RMS onset fires on the rising edge, and on an outdoor recording that edge can start
    # in wind and handling noise well before the shot - one of these came out with 140ms of
    # nothing in front of the bang. That is not a cosmetic problem: a gunshot clip with a lead
    # in plays the bang that long after the trigger, and it reads as input lag rather than as
    # a quiet start.
    lead = int(rate * 0.012)
    peak_at = int(np.argmax(np.abs(clip)))

    if peak_at > lead:
        clip = clip[peak_at - lead:]

    # A short fade either end, or the cut itself clicks.
    fade = min(int(rate * 0.004), len(clip) // 8)
    if fade > 0:
        clip[:fade] *= np.linspace(0.0, 1.0, fade)
        clip[-fade:] *= np.linspace(1.0, 0.0, fade)

    return clip


def write(path, data, rate=TARGET_RATE, peak=0.92):
    os.makedirs(os.path.dirname(path), exist_ok=True)

    top = np.max(np.abs(data))
    data = data * (peak / top) if top > 1e-9 else data

    pcm = (np.clip(data, -1.0, 1.0) * 32767).astype("<i2")

    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes(pcm.tobytes())

    log(f"wrote {path}  {len(data) / rate * 1000:.0f}ms")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("destination")
    parser.add_argument("--max", type=float, default=1.4, help="longest clip to keep, seconds")
    args = parser.parse_args()

    data, rate = read(args.source)
    log(f"{os.path.basename(args.source)}: {len(data) / rate:.1f}s at {rate}Hz")

    clip = extract(data, rate, max_seconds=args.max)
    write(args.destination, resample(clip, rate))


if __name__ == "__main__":
    main()
