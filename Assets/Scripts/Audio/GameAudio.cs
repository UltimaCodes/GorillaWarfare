using System.Collections.Generic;
using UnityEngine;

// One-shot sound player. Everything loads from Resources/Audio/<folder> by name, so there's
// nothing to drag into the inspector and nothing that breaks if a prefab gets rebuilt.
//
// Drop more .ogg files into any of those folders and they get picked up automatically - the
// variants are chosen at random so repeated sounds don't machine-gun.
public static class GameAudio
{
    public const string Shoot = "Shoot";
    public const string Impact = "Impact";
    public const string Footstep = "Footstep";
    public const string Hurt = "Hurt";
    public const string Death = "Death";
    public const string UI = "UI";

    /// Your shot landed. The single most useful sound in a shooter - it's the difference
    /// between aiming and guessing - so it gets its own bank rather than borrowing the impact.
    public const string Hit = "Hit";

    /// Someone died and it was you. Deliberately a different shape to a hit, not a louder one.
    public const string Kill = "Kill";

    /// <summary>
    /// Your overshield just shattered.
    ///
    /// Its own bank because it is the one piece of information you cannot get any other way -
    /// health you can read off the bar, but the moment the shield goes is the moment the next
    /// shot starts hurting, and by the time you have looked down to check you are dead.
    ///
    /// Drop a glass break into Resources/Audio/Shield and it plays. With the folder empty it
    /// falls back to an impact pitched up, which reads as breakage rather than as a hit but is
    /// no substitute for the real thing.
    /// </summary>
    /// <summary>
    /// Boots scraping along the ground. Its own bank because a slide is not a footstep - it is
    /// one continuous noise rather than a series of taps, and borrowing the footstep bank made
    /// sliding sound like walking very quickly.
    ///
    /// Drop a scrape into Resources/Audio/Slide and it plays. Empty, it falls back to a footstep
    /// pitched down, which reads as a scuff and is obviously not the real thing.
    /// </summary>
    public const string Slide = "Slide";

    public const string Shield = "Shield";

    /// <summary>
    /// The vine firing out and catching. Its own bank rather than borrowing Slide - a thwip and
    /// a scrape are opposite shapes of sound, a snap against a sustain, and asking one bank to
    /// be both would have meant tuning it badly for one of the two.
    ///
    /// Drop something into Resources/Audio/Vine and it plays. Empty, it falls back to a shot
    /// pitched up, which is at least short and sharp rather than nothing.
    /// </summary>
    public const string Vine = "Vine";

    /// <summary>
    /// Wind under everything else while going fast - the sound half of the FOV kick and the
    /// wind lines, so speed reads as speed even with the sound off... except it isn't, that's
    /// the point. Wants to be a smooth bed with as few distinct events in it as possible - the
    /// ambient jungle track that got cut had wind chimes buried in it and measured at 0.49
    /// events a second, which is exactly the shape this must not be. AudioCheck's WindIsSmooth
    /// holds that down the same way it held the jungle bed to account, before it got cut.
    /// </summary>
    public const string Wind = "Wind";

    /// A pineapple going off. Its own bank rather than the generic impact, which is the sound a
    /// bullet makes hitting a wall - reusing it would make the loudest thing in the game sound
    /// like the smallest.
    public const string Explosion = "Explosion";

    /// Eating one banana and pulling out another. Used to borrow a random UI click.
    public const string Reload = "Reload";

    /// <summary>
    /// Under everything else once you're nearly dead. Added 2026-08-22 alongside the HUD punch
    /// pass - the edge already went red and beat in time with this in `GameHud.UpdateAdrenaline`,
    /// but nothing made a sound, so the one moment in the game most shooters reach for audio
    /// first had none at all.
    ///
    /// Its own bank for the same reason Shield and Slide have theirs - a heartbeat is a
    /// completely different shape of sound to Hurt's cry and deserves a low, physical thump.
    /// Drop a beat into Resources/Audio/Heartbeat and it plays at the rate the screen edge
    /// already pulses at. Empty, it falls back to Hurt pitched down almost an octave and a half,
    /// which reads as a dull thud rather than a cry - closer to the right shape than nothing.
    /// </summary>
    public const string Heartbeat = "Heartbeat";
    public const float HeartbeatVolume = 0.55f;

    // One place for how loud everything is, rather than a number at each call site.
    //
    // The ordering is what matters more than the values: hit and kill confirmation sit above
    // the guns, because they're the sounds you're actually listening for, and a gunshot you
    // fired yourself is information you already have.
    public const float ShotVolume = 0.55f;
    public const float ImpactVolume = 0.4f;
    public const float HitVolume = 0.8f;
    public const float KillVolume = 0.95f;
    public const float ReloadVolume = 0.6f;
    public const float HurtVolume = 0.7f;
    public const float DeathVolume = 0.85f;
    public const float FootstepVolume = 0.45f;
    public const float UiVolume = 0.7f;

    // Under the hit and kill sounds on purpose. It is meant to be noticed, not to interrupt -
    // a shield break that drowns out the gunfire is worse than one you miss.
    public const float ShieldVolume = 0.55f;

    // Under the guns and over the footsteps. You should hear your own slide clearly and somebody
    // else's only as a hint that they are moving fast nearby.
    public const float SlideVolume = 0.5f;

    // Above everything. It is the loudest thing that happens and it should be.
    // Was 1.0, which is full scale - the loudest thing the game can produce, louder than the
    // kill sound, and with nothing above it to make it feel big by comparison. Loud is not the
    // same as heavy: weight comes from the low end, the shake and the tail, not from the meter
    // pinning. Pulled down so there is somewhere for it to be big.
    public const float ExplosionVolume = 0.62f;

    /// The low thump under the crack, played a beat later and further out. Two layers is what
    /// makes an explosion feel like it displaced air rather than like a sample.
    public const float ExplosionBodyVolume = 0.5f;

    const int poolSize = 16;

    static readonly Dictionary<string, AudioClip[]> banks = new Dictionary<string, AudioClip[]>();
    static AudioSource[] pool;
    static int next;

    static AudioClip Pick(string bank)
    {
        if (!banks.TryGetValue(bank, out AudioClip[] clips))
        {
            clips = Resources.LoadAll<AudioClip>($"Audio/{bank}");

            // Fall back to the parent folder, so a weapon asking for "Shoot/Blowgun" with no
            // clips of its own still makes a noise.
            int slash = bank.LastIndexOf('/');
            if (clips.Length == 0 && slash > 0)
                clips = Resources.LoadAll<AudioClip>($"Audio/{bank.Substring(0, slash)}");

            banks[bank] = clips;

            if (clips.Length == 0)
                Debug.LogWarning($"No clips in Resources/Audio/{bank}");
        }

        return clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }

    // Named clip, for when it matters which one you get (UI mostly).
    static AudioClip Pick(string bank, string clipName)
    {
        AudioClip clip = Resources.Load<AudioClip>($"Audio/{bank}/{clipName}");
        if (clip == null)
            Debug.LogWarning($"No clip Audio/{bank}/{clipName}");

        return clip;
    }

    static AudioSource NextSource()
    {
        if (pool == null)
        {
            // Round-robin pool instead of AudioSource.PlayClipAtPoint, which creates and
            // destroys a GameObject for every single sound.
            GameObject host = new GameObject("~AudioPool");
            Object.DontDestroyOnLoad(host);

            pool = new AudioSource[poolSize];

            for (int i = 0; i < poolSize; i++)
            {
                // One GameObject each, and this is the whole reason positional audio did not
                // work. Every source used to be a component on this same host, so moving one to
                // where an explosion happened moved all sixteen - and every 3D sound in the game
                // played from wherever the most recent one had been placed. An explosion thirty
                // metres behind a wall arrived directly in front of you, because by then the
                // shared transform had been dragged to your feet by a footstep.
                GameObject seat = new GameObject($"~Audio{i}");
                seat.transform.SetParent(host.transform, false);

                AudioSource src = seat.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;

                // Full volume out to a couple of metres, then falling away to nothing at fifty
                // five. Linear rather than logarithmic because it is predictable: a sound at
                // half the max distance is at half volume, which is easy to reason about when
                // balancing a mix by ear.
                src.minDistance = 2f;
                src.maxDistance = 55f;

                // No doppler. These sources teleport across the map between one sound and the
                // next, and Unity reads that jump as enormous velocity - which pitch shifts the
                // clip by a random amount every time. Nothing in this game moves fast enough for
                // real doppler to be worth that.
                src.dopplerLevel = 0f;

                pool[i] = src;
            }
        }

        // Prefer one that is not busy. Round robin alone will happily hand back a source that
        // is still playing an explosion and then change its spatial blend out from under it,
        // which turns a 3D sound 2D halfway through.
        for (int i = 0; i < poolSize; i++)
        {
            AudioSource candidate = pool[next];
            next = (next + 1) % poolSize;

            if (!candidate.isPlaying)
                return candidate;
        }

        // Everything is busy, so something has to be interrupted. The one that has been going
        // longest is the least missed.
        AudioSource chosen = pool[next];
        next = (next + 1) % poolSize;

        return chosen;
    }

    /// Positional. Use for anything happening in the world.
    public static void PlayAt(string bank, Vector3 position, float volume = 1f, float pitchJitter = 0.08f)
    {
        AudioClip clip = Pick(bank);

        // An empty bank borrows from one that has something in it. The slide folder is waiting
        // for a real scrape; until then a footstep does the job badly rather than silently,
        // which is the difference between "not finished" and "broken".
        if (clip == null && bank == Slide)
            clip = Pick(Footstep);

        // Same idea as the Slide fallback above, for the same reason - a bank that hasn't been
        // filled yet should sound wrong rather than say nothing.
        bool borrowedShot = false;
        if (clip == null && bank == Vine)
        {
            clip = Pick(Shoot);
            borrowedShot = true;
        }

        if (clip == null)
            return;

        AudioSource src = NextSource();
        src.transform.position = position;
        src.spatialBlend = 1f;
        src.minDistance = 2f;
        src.maxDistance = 55f;
        src.volume = volume * GameSettings.SfxVolume;
        src.pitch = (borrowedShot ? 1.6f : 1f) + Random.Range(-pitchJitter, pitchJitter);
        src.PlayOneShot(clip);
    }

    /// Non-positional. Menus, and anything that happened to you rather than near you.
    /// <summary>
    /// The same as PlayAt, a moment later and at a different pitch.
    ///
    /// Used to put a low body under an explosion's crack. Scheduled on the source rather than
    /// run from a coroutine, because there is nothing here that owns a MonoBehaviour and
    /// PlayScheduled is sample accurate where a coroutine is frame accurate.
    /// </summary>
    public static void PlayAtDelayed(string bank, Vector3 position, float volume, float pitch,
                                     float delay)
    {
        AudioClip clip = Pick(bank);

        // A bank that has nothing in it yet borrows from one that does. Pitching a real
        // recording is arranging, not synthesising, and it means a missing clip is a slightly
        // wrong sound rather than silence.
        if (clip == null && bank == Slide)
        {
            clip = Pick(Footstep);
            pitch *= 0.55f;
        }

        if (clip == null)
            return;

        AudioSource src = NextSource();
        src.transform.position = position;
        src.spatialBlend = 1f;
        src.minDistance = 2f;
        src.maxDistance = 55f;
        src.clip = clip;
        src.volume = volume * GameSettings.SfxVolume;

        // Pitched down rather than a second recording. Dropping the same bang an octave or so
        // is what a big version of it sounds like, and it costs nothing.
        src.pitch = pitch;
        src.PlayDelayed(delay);
    }

    public static void PlayHeartbeat(float strength)
    {
        AudioClip clip = Pick(Heartbeat);
        float pitch = 1f;

        if (clip == null)
        {
            clip = Pick(Hurt);
            pitch = 0.55f;
        }

        if (clip == null)
            return;

        AudioSource src = NextSource();
        src.spatialBlend = 0f;
        src.volume = HeartbeatVolume * Mathf.Clamp01(strength) * GameSettings.SfxVolume;
        src.pitch = pitch;
        src.PlayOneShot(clip);
    }

    public static void Play2D(string bank, float volume = 1f, float pitchJitter = 0f)
    {
        AudioClip clip = Pick(bank);
        if (clip == null)
            return;

        PlayFlat(clip, volume, pitchJitter);
    }

    public static void Play2D(string bank, string clipName, float volume = 1f)
    {
        AudioClip clip = Pick(bank, clipName);
        if (clip == null)
            return;

        PlayFlat(clip, volume, 0f);
    }

    /// <summary>
    /// Same, but at a deliberate pitch rather than a random wobble.
    ///
    /// This is the one that carries the combo. A sound that climbs in pitch as you keep
    /// connecting is the oldest trick there is - Peggle's ending, a coin streak in Mario, the
    /// ranking meter in ULTRAKILL - and it works because it turns a series of separate events
    /// into one rising line. Nothing else here costs so little and does so much.
    /// </summary>
    public static void PlayPitched(string bank, string clipName, float volume, float pitch)
    {
        AudioClip clip = Pick(bank, clipName);
        if (clip == null)
            return;

        AudioSource src = NextSource();
        src.spatialBlend = 0f;
        src.volume = volume * GameSettings.SfxVolume;
        src.pitch = pitch;
        src.PlayOneShot(clip);
    }

    static void PlayFlat(AudioClip clip, float volume, float pitchJitter)
    {
        AudioSource src = NextSource();
        src.spatialBlend = 0f;
        src.volume = volume * GameSettings.SfxVolume;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.PlayOneShot(clip);
    }
}
