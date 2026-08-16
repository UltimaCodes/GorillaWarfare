using System.IO;
using UnityEditor;
using UnityEngine;

// Turns a quiet stereo recording into an ambience bed the mix can actually use.
//
// The forest loop that came off OpenGameArt measures peak 0.04 and RMS 0.003 - about 28dB below
// full scale. Played at any sensible ambience level it is silence with extra steps, and the fix
// is not to turn the volume field up, because AudioSource.volume stops at 1 and there would be
// no headroom left for anyone who wanted more.
//
// So it gets normalised on the way in. Mono and 22kHz while we are here: ambience has no stereo
// image worth keeping in a game where the camera spins constantly, and nothing in a forest bed
// lives above 11kHz. That turns a 17MB uncompressed stereo file into about 2MB, which is the
// difference between committing it and not.
//
// Re-runnable. It works from whatever non-WAV files are sitting in the folder and leaves the
// result behind as a WAV.
public static class AmbienceImport
{
    const string Folder = "Assets/Resources/Audio/Ambience";

    // Loud enough to be present under gunfire, quiet enough not to be a character in the scene.
    // The component scales this again by the effects slider.
    const float TargetPeak = 0.7f;

    const int TargetRate = 22050;

    [MenuItem("Tools/Gorilla Warfare/Normalise the ambience")]
    public static void Run()
    {
        int done = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Already processed. Without this a second run normalises its own output, which
            // does nothing the first time and quietly clips it if the target ever changes.
            if (path.EndsWith(".wav"))
                continue;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            if (clip == null)
                continue;

            if (Convert(clip, path))
                done++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[ambience] normalised {done} clip(s)");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static bool Convert(AudioClip clip, string path)
    {
        float[] raw = new float[clip.samples * clip.channels];

        if (!clip.GetData(raw, 0))
        {
            Debug.LogError($"[ambience] could not read {clip.name}");
            return false;
        }

        // Down to mono first, so the peak that gets normalised is the peak of what will actually
        // be played rather than of one channel of something else.
        int frames = clip.samples;
        float[] mono = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            float sum = 0f;

            for (int c = 0; c < clip.channels; c++)
                sum += raw[i * clip.channels + c];

            mono[i] = sum / clip.channels;
        }

        // Linear resample. Fine for a bed of insects and wind; it would not be fine for music.
        int outFrames = Mathf.Max(1, Mathf.RoundToInt(frames * (float)TargetRate / clip.frequency));
        float[] resampled = new float[outFrames];

        for (int i = 0; i < outFrames; i++)
        {
            float at = (float)i * (frames - 1) / Mathf.Max(1, outFrames - 1);
            int low = Mathf.Clamp(Mathf.FloorToInt(at), 0, frames - 1);
            int high = Mathf.Min(low + 1, frames - 1);

            resampled[i] = Mathf.Lerp(mono[low], mono[high], at - low);
        }

        float peak = 0f;

        foreach (float sample in resampled)
            peak = Mathf.Max(peak, Mathf.Abs(sample));

        if (peak < 0.0001f)
        {
            Debug.LogError($"[ambience] {clip.name} is silent");
            return false;
        }

        float gain = TargetPeak / peak;

        for (int i = 0; i < resampled.Length; i++)
            resampled[i] = Mathf.Clamp(resampled[i] * gain, -1f, 1f);

        // The seam matters more than anything else here, because this thing loops for the whole
        // match. A short crossfade of the tail onto the head guarantees it, rather than hoping
        // the recording happened to end where it started.
        int blend = Mathf.Min(TargetRate / 4, resampled.Length / 8);

        for (int i = 0; i < blend; i++)
        {
            float t = (float)i / blend;
            int tail = resampled.Length - blend + i;

            resampled[i] = Mathf.Lerp(resampled[tail], resampled[i], t);
        }

        float[] looped = new float[resampled.Length - blend];
        System.Array.Copy(resampled, looped, looped.Length);

        string target = Path.Combine(Folder, Path.GetFileNameWithoutExtension(path) + ".wav")
                            .Replace('\\', '/');

        WriteWav(target, looped, TargetRate);

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.ImportAsset(target);

        Debug.Log($"[ambience] {clip.name}: peak {peak:F3} -> {TargetPeak:F2} "
                  + $"({gain:F1}x), {clip.channels}ch {clip.frequency}Hz -> mono {TargetRate}Hz, "
                  + $"{looped.Length / (float)TargetRate:F1}s");

        return true;
    }

    /// A 16 bit mono PCM wav. Written by hand because Unity has no audio writer of any kind.
    static void WriteWav(string path, float[] samples, int rate)
    {
        using (FileStream file = new FileStream(path, FileMode.Create))
        using (BinaryWriter write = new BinaryWriter(file))
        {
            int dataBytes = samples.Length * 2;

            write.Write(new[] { 'R', 'I', 'F', 'F' });
            write.Write(36 + dataBytes);
            write.Write(new[] { 'W', 'A', 'V', 'E' });

            write.Write(new[] { 'f', 'm', 't', ' ' });
            write.Write(16);
            write.Write((short)1);          // PCM
            write.Write((short)1);          // mono
            write.Write(rate);
            write.Write(rate * 2);          // byte rate
            write.Write((short)2);          // block align
            write.Write((short)16);         // bits

            write.Write(new[] { 'd', 'a', 't', 'a' });
            write.Write(dataBytes);

            foreach (float sample in samples)
                write.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
        }
    }
}
