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

    const int poolSize = 16;

    static readonly Dictionary<string, AudioClip[]> banks = new Dictionary<string, AudioClip[]>();
    static AudioSource[] pool;
    static int next;

    static AudioClip Pick(string bank)
    {
        if (!banks.TryGetValue(bank, out AudioClip[] clips))
        {
            clips = Resources.LoadAll<AudioClip>($"Audio/{bank}");
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
        src.volume = volume;
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

    static void PlayFlat(AudioClip clip, float volume, float pitchJitter)
    {
        AudioSource src = NextSource();
        src.spatialBlend = 0f;
        src.volume = volume;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.PlayOneShot(clip);
    }
}
