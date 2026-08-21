swish-10 and swish-13 from the "Swishes Sound Pack" by artisticdude, CC0 - no attribution
required, though the credits should say so anyway.

https://opengameart.org/content/swishes-sound-pack

13 clips in the pack, 4 described as lighter and 9 as heavier. Picked by measuring rather than
by ear, same method as the Slide bank: duration, crest factor (peak over RMS), and how early the
peak lands as a fraction of the clip's own length. A thwip wants to be short with a sharp, early
peak - a crack that decays, not a swell that builds - which is a different shape from a sustained
scrape and the reason this is its own bank rather than a reuse of Slide.

swish-10: 0.125s, crest 6.08 (highest of the 13), peak at 17.7% into the clip (earliest of the
13). The sharpest, earliest-peaking clip in the pack by both measures.

swish-13: 0.071s, crest 4.75, peak at 32.5%. Shortest overall, a second flavour for variety so
firing the vine repeatedly doesn't sound identical every time - GameAudio.PlayAt picks randomly
between whatever's in a bank.

Run `python tools/analyze_swishes.py <folder>` to see the numbers and an ASCII envelope for a
folder of candidates - handles 8/16/24-bit WAV with no dependency beyond the standard library,
since audioop (what the Slide measurement originally used) was removed in Python 3.13.
