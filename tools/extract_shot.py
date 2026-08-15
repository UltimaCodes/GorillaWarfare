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

    if width == 2:
        data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    elif width == 3:
        # 24 bit has no numpy dtype. Pad each 3 byte sample up to 4 and read as int32, putting
        # the original bytes in the high three so the sign carries.
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3)
        packed = np.zeros((len(b), 4), dtype=np.uint8)
        packed[:, 1:] = b
        data = packed.view("<i4").ravel().astype(np.float32) / 2147483648.0
    elif width == 4:
        data = np.frombuffer(raw, dtype="<i4").astype(np.float32) / 2147483648.0
    else:
        raise SystemExit(f"[shot] {path} is {width * 8} bit, which isn't handled")

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


def transients(data, rate, bucket_ms=5.0, floor=0.18, apart_ms=90.0):
    """Every distinct bang in the recording, as sample indices.

    Peak per bucket, not RMS, and local maxima rather than a threshold crossing.

    The threshold approach this replaces could not see rapid fire at all. It called a shot
    "started" when the level rose past a fraction of the peak and "ended" when it fell back
    below a lower one - but between two shots 80ms apart the level never falls that far, so an
    entire magazine registered as a single onset and the extractor happily cut all of it. The
    clip measured as one shot by the same broken logic that produced it, which is why it passed.
    """
    bucket = max(1, int(rate * bucket_ms / 1000.0))
    count = len(data) // bucket

    env = np.abs(data[:count * bucket].reshape(count, bucket)).max(axis=1)
    top = env.max()

    if top <= 0:
        return [], env, bucket

    env = env / top
    apart = max(1, int(apart_ms / bucket_ms))

    # A bang is a sudden rise, not a loud moment.
    #
    # These recordings were made with a device that could not handle the level, so its automatic
    # gain drags the reverb tail up to 80-90% of the shot itself. Every attempt to find shots by
    # loudness failed on that: the tail is exactly as loud as the thing that caused it, so a
    # level threshold either finds a "shot" every 50ms or finds nothing.
    #
    # What a real shot has and a tail does not is the attack - the level jumping several times
    # over in five milliseconds from whatever preceded it. That is what this looks for, and it
    # can be sanity checked against physics: a bolt action cannot cycle in 55ms, so if the
    # detector claims it did, the detector is wrong.
    look_back = max(2, int(30.0 / bucket_ms))

    peaks = []
    for i in range(look_back, len(env) - 1):
        if env[i] < floor:
            continue

        before = env[i - look_back:i].mean()
        if env[i] < before * 2.5:
            continue

        if env[i] < env[i + 1]:
            continue

        if peaks and i - peaks[-1] < apart:
            if env[i] > env[peaks[-1]]:
                peaks[-1] = i
            continue

        peaks.append(i)

    return peaks, env, bucket


def extract(data, rate, lead_ms=10.0, tail_floor=0.004, min_seconds=0.22, max_seconds=1.4):
    peaks, env, bucket = transients(data, rate)

    if not peaks:
        raise SystemExit("[shot] no transients found - is the file silent?")

    log(f"{len(peaks)} distinct bang(s) in the recording")

    # The most isolated one, not the loudest.
    #
    # Loudest sounds right and isn't: in these sessions the loudest shot is often the middle of
    # a fast pair, so its tail gets truncated by the next one 90ms later and the clip comes out
    # stunted. What matters for a usable clip is room after the bang, so this scores each
    # candidate by the space following it and takes the roomiest of the loud ones.
    loud_enough = [i for i in peaks if env[i] >= env[max(peaks, key=lambda j: env[j])] * 0.55]

    def room_after(i):
        later = [j for j in peaks if j > i]
        return (later[0] - i) if later else len(env) - i

    best = max(loud_enough, key=room_after)

    log(f"taking bang {peaks.index(best) + 1} of {len(peaks)}, "
        f"{room_after(best) * bucket / rate:.2f}s of room after it")

    start = max(0, best * bucket - int(rate * lead_ms / 1000.0))

    # Hard stop before the next bang, so a follow-up shot can never be glued on the end. This
    # is the part that was missing: without it, "cut until it goes quiet" runs through the whole
    # magazine, because it never goes quiet.
    limit = start + int(rate * max_seconds)

    # Only a bang comparable in loudness to this one ends the clip.
    #
    # A shot outdoors is followed by its own echo off whatever is nearby, and an echo is a
    # local maximum like any other - which is how the Mosin recording produced 62 "bangs" from
    # a bolt action that physically cannot fire four times a second. Treating those as the next
    # shot cut every clip down to 40ms, killing the tail that makes a gun sound like it was
    # fired somewhere rather than in a padded box.
    #
    # An echo comes back quieter. A real follow-up shot does not.
    loud = env[best] * 0.55
    later = [i for i in peaks if i > best and env[i] >= loud]

    if later:
        limit = min(limit, later[0] * bucket - int(rate * 0.015))

    # Otherwise run on while it decays, so the tail is kept.
    #
    # The floor has to be genuinely low. At 5% of peak these clean studio recordings were
    # "finished" 40ms after the bang, which is not a gunshot - it's a click. What makes a shot
    # sound like a gun rather than a snap is the decay, and that lives a long way down.
    floor = env[best] * tail_floor
    end = limit

    # And never shorter than this, whatever the floor says.
    earliest = start + int(rate * min_seconds)

    for i in range(best, min(len(env), limit // bucket)):
        if env[i] < floor and i * bucket > earliest:
            end = i * bucket
            break

    clip = np.copy(data[start:min(end, len(data))])

    # Put the bang at the front.
    #
    # The detector fires on the rising edge, and outdoors that edge can begin in wind and
    # handling noise well before the shot. A clip with a lead-in plays the bang that long after
    # the trigger, which reads as input lag rather than as a quiet start.
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
