using UnityEditor;
using UnityEngine;

// Measures the candidate shield break clips so one can be picked on evidence.
//
// Ryaan's brief was specific: one break, not a loud one, not a pile of them. Those are three
// measurable things - how many transients are in it, how loud its peak is, and how long it runs -
// and picking by filename would be guessing at all three. "impactGlass_heavy" might be a single
// heavy pane or it might be a cupboard falling over.
//
// Unity is doing the decoding here because there is no ffmpeg on this machine and the clips are
// Vorbis. That is the whole reason this is an editor script rather than a Python one.
public static class ShieldSoundPick
{
    [MenuItem("Tools/Gorilla Warfare/Measure the shield sounds")]
    public static void Run()
    {
        // Every bank that has been picked by measurement rather than by ear. Reading them all
        // in one run means the numbers sit next to each other, which is how you notice that the
        // "light" glass break was the loudest of its set.
        foreach (string bank in new[] { "Shield", "Slide" })
        {
            Debug.Log($"[shield] ---- {bank} ----");

            foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio/" + bank))
                Report(clip);
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Report(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];

        if (!clip.GetData(samples, 0))
        {
            Debug.LogError($"[shield] could not read {clip.name} - is it set to Decompress On Load?");
            return;
        }

        float peak = 0f;
        double sum = 0d;

        foreach (float sample in samples)
        {
            float size = Mathf.Abs(sample);
            peak = Mathf.Max(peak, size);
            sum += size * size;
        }

        float rms = Mathf.Sqrt((float)(sum / Mathf.Max(1, samples.Length)));

        // Transients, counted off a smoothed envelope rather than off raw samples. Glass is
        // broadband and crosses zero constantly, so counting peaks directly reports hundreds of
        // "breaks" in a single one.
        int window = Mathf.Max(1, clip.frequency / 200);   // 5ms
        int windows = samples.Length / clip.channels / window;
        float[] envelope = new float[Mathf.Max(1, windows)];

        for (int w = 0; w < windows; w++)
        {
            float loudest = 0f;

            for (int i = 0; i < window; i++)
            {
                int at = (w * window + i) * clip.channels;
                if (at < samples.Length)
                    loudest = Mathf.Max(loudest, Mathf.Abs(samples[at]));
            }

            envelope[w] = loudest;
        }

        // A transient is a rise past a fraction of the peak from something much quieter. The
        // gate afterwards is what stops one break's own decay being counted as several.
        int onsets = 0;
        bool armed = true;
        int quiet = 0;

        for (int w = 0; w < envelope.Length; w++)
        {
            if (envelope[w] > peak * 0.35f && armed)
            {
                onsets++;
                armed = false;
                quiet = 0;
            }
            else if (envelope[w] < peak * 0.12f)
            {
                // 60ms below the floor before another rise counts as a separate event. Shorter
                // than that and the ring-out of a single pane re-triggers it.
                if (++quiet > 12)
                    armed = true;
            }
        }

        // A crude picture of the shape, because a number that looks fine and a shape that looks
        // wrong is exactly the failure this project has hit before.
        System.Text.StringBuilder shape = new System.Text.StringBuilder();
        int columns = Mathf.Min(48, envelope.Length);

        for (int c = 0; c < columns; c++)
        {
            float loudest = 0f;
            int from = c * envelope.Length / columns;
            int to = (c + 1) * envelope.Length / columns;

            for (int w = from; w < to && w < envelope.Length; w++)
                loudest = Mathf.Max(loudest, envelope[w]);

            shape.Append(loudest > peak * 0.6f ? '#' : loudest > peak * 0.25f ? '+' :
                         loudest > peak * 0.05f ? '.' : ' ');
        }

        // How badly the end meets the beginning, relative to how loud the clip is there. A loop
        // clicks when the jump across the seam is large next to the local level, not when it is
        // large in absolute terms - that distinction has already cost this project a check that
        // failed a perfectly good track.
        int channels = clip.channels;
        int last = samples.Length - channels;
        float seam = last > 0 ? Mathf.Abs(samples[last] - samples[0]) : 0f;
        float local = Mathf.Max(0.0001f, (Mathf.Abs(samples[0]) + Mathf.Abs(samples[last])) * 0.5f + rms);

        // Crest factor is what separates a bed from a bed with things in it. A dense wall of
        // insects sits close to its own average, so peak over RMS is small. A wind chime, a
        // single bird, a car door - anything you would notice - spikes far above the bed and
        // pushes it up. That number, and how many transients there are per second, is the whole
        // difference between ambience you stop hearing and ambience that becomes a metronome.
        float crest = rms > 0.0001f ? peak / rms : 0f;
        float perSecond = onsets / Mathf.Max(0.01f, clip.length);

        Debug.Log($"[shield] {clip.name,-26} {clip.length:F2}s  peak {peak:F2}  rms {rms:F3}  "
                  + $"crest {crest:F1}  events/s {perSecond:F2}  seam {seam / local:F2}  |{shape}|");
    }
}
