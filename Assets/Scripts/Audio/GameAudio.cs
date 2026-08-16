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
    public const string Shield = "Shield";

    /// A pineapple going off. Its own bank rather than the generic impact, which is the sound a
    /// bullet makes hitting a wall - reusing it would make the loudest thing in the game sound
    /// like the smallest.
    public const string Explosion = "Explosion";

    /// Eating one banana and pulling out another. Used to borrow a random UI click.
    public const string Reload = "Reload";

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

    // Above everything. It is the loudest thing that happens and it should be.
    public const float ExplosionVolume = 1f;

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
                AudioSource src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 60f;
                pool[i] = src;
            }
        }

        AudioSource chosen = pool[next];
        next = (next + 1) % poolSize;
        return chosen;
    }

    /// Positional. Use for anything happening in the world.
    public static void PlayAt(string bank, Vector3 position, float volume = 1f, float pitchJitter = 0.08f)
    {
        AudioClip clip = Pick(bank);
        if (clip == null)
            return;

        AudioSource src = NextSource();
        src.transform.position = position;
        src.spatialBlend = 1f;
        src.volume = volume * GameSettings.SfxVolume;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.PlayOneShot(clip);
    }

    /// Non-positional. Menus, and anything that happened to you rather than near you.
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
