jungle.wav - forest ambience from OpenGameArt's CC0 Background Ambience collection, which
is CC0. No attribution required; the credits should say so anyway.

The source was a 45s stereo MP3 measuring peak 0.04 and RMS 0.003 - roughly 28dB below full
scale, which is inaudible at any sensible ambience level. Turning the volume field up would
not have fixed it, because AudioSource.volume stops at 1 and there would be no headroom
left for anyone who wanted more.

So Tools/Gorilla Warfare/Normalise the ambience rebuilt it: mixed to mono, resampled to
22kHz, normalised to peak 0.7, and the tail crossfaded onto the head so the loop seam is
guaranteed rather than hoped for. Mono and 22kHz because ambience has no stereo image worth
keeping in a game where the camera spins constantly and nothing in a forest lives above
11kHz - that turned a 17MB uncompressed stereo file into 2MB.

Drop any other clip in here and run that tool again; it works on whatever is not already a
wav and leaves the result behind.
