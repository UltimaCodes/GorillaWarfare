using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// The sounds the game asks for, and whether they're the sounds it got.
//
// Two failures worth catching automatically. The first is silent: GameAudio resolves banks by
// folder name at runtime, so a missing folder is a warning in a log nobody reads and an event
// that just makes no noise. The second is the one that shipped - the pistol clip contained five
// shots rather than one, so a single trigger pull sounded like a burst. That was found by
// listening, which does not scale.
public static class AudioCheck
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    static void Check(bool ok, string label, string detail)
    {
        Log.AppendLine($"  {(ok ? "ok  " : "FAIL")}  {label,-46} {detail}");
        if (!ok)
            failures++;
    }

    /// <summary>
    /// The shield break, checked for what Ryaan actually asked for.
    ///
    /// He was specific: one break, not loud, not a pile of them. Length is the part worth
    /// asserting - a clip over a second and a half is a cupboard falling over rather than a pane
    /// going, and no amount of volume tuning fixes that. The transient count and the envelope
    /// live in Tools/Gorilla Warfare/Measure the shield sounds, which prints them rather than
    /// asserting, because picking a sound is a judgement and this is only a guard against the
    /// obviously wrong one.
    /// </summary>
    static void ShieldIsTheRightShape()
    {
        AudioClip[] shield = Resources.LoadAll<AudioClip>("Audio/Shield");

        Check(shield.Length > 0, "the shield bank has something in it",
              $"{shield.Length} clips");

        foreach (AudioClip clip in shield)
        {
            Check(clip.length < 1.5f, $"{clip.name} is a break rather than a collapse",
                  $"{clip.length:F2}s");
        }

    }

    /// <summary>
    /// The slide scrape has to be a scrape rather than an impact.
    ///
    /// Length is the checkable part: a slide loops for as long as you are sliding, so anything
    /// under half a second turns into a stutter no matter how good the recording is. Whether it
    /// is continuous is judged from the envelope, which the measuring tool prints.
    /// </summary>
    static void SlideIsAScrape()
    {
        AudioClip[] slide = Resources.LoadAll<AudioClip>("Audio/Slide");

        Check(slide.Length > 0, "the slide bank has something in it", $"{slide.Length} clips");

        foreach (AudioClip clip in slide)
        {
            Check(clip.length > 0.5f, $"{clip.name} is long enough to loop without stuttering",
                  $"{clip.length:F2}s");
        }
    }

    /// <summary>
    /// The vine's thwip has to be a snap rather than a whoosh - the opposite shape from the
    /// slide's scrape, which is exactly why it isn't the same bank. Length is the checkable
    /// part, same as the other two shape checks; how sharp the attack is was measured with
    /// tools/analyze_swishes.py when the clips were picked and is recorded in the bank's own
    /// README rather than re-derived here every run.
    /// </summary>
    static void VineIsAThwip()
    {
        AudioClip[] vine = Resources.LoadAll<AudioClip>("Audio/Vine");

        Check(vine.Length > 0, "the vine bank has something in it", $"{vine.Length} clips");

        foreach (AudioClip clip in vine)
        {
            Check(clip.length < 0.3f, $"{clip.name} is a snap rather than a whoosh",
                  $"{clip.length:F2}s");
        }
    }

    public static void Run()
    {
        failures = 0;
        Log.Clear();

        BanksExist();
        NamedClipsExist();
        WeaponsHaveTheirOwnVoice();
        SingleShotsAreSingleShots();
        NothingIsSilentOrClipped();
        MusicLoopsCleanly();
        ShieldIsTheRightShape();
        SlideIsAScrape();
        VineIsAThwip();

        Debug.Log("[audio] banks\n" + Log);
        Debug.Log(failures == 0 ? "[audio] ===== ALL PASS =====" : $"[audio] {failures} FAILURES");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static AudioClip[] Bank(string name) => Resources.LoadAll<AudioClip>("Audio/" + name);

    // Every bank the code names has to have something in it.
    static void BanksExist()
    {
        string[] banks =
        {
            GameAudio.Shoot, GameAudio.Impact, GameAudio.Footstep, GameAudio.Hurt,
            GameAudio.Death, GameAudio.UI, GameAudio.Hit, GameAudio.Kill, GameAudio.Reload,
        };

        foreach (string bank in banks)
        {
            AudioClip[] clips = Bank(bank);
            Check(clips.Length > 0, $"{bank} has clips", $"{clips.Length}");
        }
    }

    // These are asked for by name, so a rename is a silent miss rather than a compile error.
    static void NamedClipsExist()
    {
        (string bank, string clip)[] named =
        {
            ("UI", "click_001"),
            ("UI", "error_001"),
            ("UI", "confirm"),
            ("UI", "back"),
            ("Hit", "hit"),
            ("Hit", "headshot"),
        };

        foreach ((string bank, string clip) in named)
        {
            AudioClip found = Resources.Load<AudioClip>($"Audio/{bank}/{clip}");
            Check(found != null, $"{bank}/{clip} exists", found != null ? $"{found.length * 1000f:F0}ms" : "missing");
        }
    }

    static void WeaponsHaveTheirOwnVoice()
    {
        foreach (string weapon in WeaponLoadout.GunGameLadder)
        {
            AudioClip[] clips = Bank($"Shoot/{weapon}");
            Check(clips.Length > 0, $"{weapon} has a firing sound", $"{clips.Length} clip(s)");
        }
    }

    /// <summary>
    /// One trigger pull, one bang.
    ///
    /// Counts onsets: windowed RMS, and a jump from below the floor to above the threshold is a
    /// new hit. The pistol clip that shipped had five of them, so tapping fire sounded like
    /// holding it down. Verified by ear at the time, which is exactly the check that doesn't
    /// survive somebody dropping in a new file.
    /// </summary>
    static void SingleShotsAreSingleShots()
    {
        foreach (string weapon in WeaponLoadout.GunGameLadder)
        {
            foreach (AudioClip clip in Bank($"Shoot/{weapon}"))
            {
                int onsets = CountOnsets(clip, out float peak);

                Check(onsets == 1, $"{weapon} fires once per clip",
                      $"{onsets} onset(s) in {clip.length * 1000f:F0}ms, peak {peak:F2}");

                // A shot that runs on is a shot you can't fire quickly.
                Check(clip.length < 1.2f, $"{weapon} clip is short enough to repeat",
                      $"{clip.length * 1000f:F0}ms");
            }
        }
    }

    static void NothingIsSilentOrClipped()
    {
        List<string> quiet = new List<string>();
        List<string> hot = new List<string>();

        foreach (string bank in new[] { "Hit", "Kill", "Reload", "Death", "UI", "Hurt", "Impact", "Footstep" })
        {
            foreach (AudioClip clip in Bank(bank))
            {
                CountOnsets(clip, out float peak);

                if (peak < 0.08f)
                    quiet.Add($"{bank}/{clip.name} at {peak:F2}");

                // Peaking at full scale is not clipping - it's what normalised audio does, and
                // nearly every sample in the Kenney packs touches 1.0 exactly once. Clipping is
                // the waveform going flat at the top, so what matters is how long it stays
                // there. The first version of this check failed seven perfectly good files.
                int flat = LongestFullScaleRun(clip);
                if (flat > 24)
                    hot.Add($"{bank}/{clip.name} flat for {flat} samples");
            }
        }

        Check(quiet.Count == 0, "nothing is inaudible",
              quiet.Count == 0 ? "all above the floor" : string.Join(", ", quiet));

        Check(hot.Count == 0, "nothing is clipping",
              hot.Count == 0 ? "all below full scale" : string.Join(", ", hot));
    }

    /// <summary>
    /// Music has to meet itself at the seam.
    ///
    /// A loop whose last sample doesn't line up with its first clicks audibly every single time
    /// it repeats, which on a 16 second track is four times a minute forever. The generator
    /// rounds every component to a whole number of cycles across the loop specifically to avoid
    /// this; the check is here because it's the sort of thing that gets broken by changing one
    /// frequency and never noticed until someone is sat in the menu.
    /// </summary>
    static void MusicLoopsCleanly()
    {
        foreach (string name in new[] { "menu", "lobby", "warmup", "combat", "over" })
        {
            AudioClip clip = Resources.Load<AudioClip>($"Audio/Music/{name}");

            if (clip == null)
            {
                // Optional by design. One theme covers the whole game - MusicPlayer falls back
                // to whichever track it has - and with neither it disables itself.
                Log.AppendLine($"  ..    music '{name}' absent, the other one covers for it");
                continue;
            }

            float[] data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0) || data.Length < 2)
                continue;

            float seam = Mathf.Abs(data[0] - data[data.Length - 1]);

            // A jump between two samples only clicks if it's big compared to how loud the
            // track is at that moment. Comparing the raw gap to a fixed number said a real
            // track was broken while it was fine - 0.07 is nothing in a passage running at
            // half scale, and it's a bang in a passage that's nearly silent. What matters is
            // the discontinuity against the local level.
            int window = Mathf.Min(clip.frequency / 10, data.Length / 4);   // 100ms
            float level = Mathf.Max(EdgeLevel(data, 0, window), EdgeLevel(data, data.Length - window, window));
            float relative = seam / Mathf.Max(level, 0.002f);

            Check(relative < 1.5f, $"music '{name}' loops without a click",
                  $"seam gap {seam:F4} against a level of {level:F4} = {relative:F2}x");

            // The warmup and scoreboard slots are stingers, so only the long ones get held
            // to a length.
            bool stinger = name == "warmup" || name == "over";

            Check(stinger || clip.length > 8f, $"music '{name}' is long enough not to nag",
                  $"{clip.length:F1}s");
        }
    }

    /// RMS of a window, used to judge how loud a track is right at its edges.
    static float EdgeLevel(float[] data, int start, int count)
    {
        double sum = 0.0;
        int end = Mathf.Min(start + count, data.Length);

        for (int i = Mathf.Max(0, start); i < end; i++)
            sum += data[i] * data[i];

        return count > 0 ? Mathf.Sqrt((float)(sum / count)) : 0f;
    }

    /// The longest run of consecutive samples pinned at full scale, which is what a clipped
    /// waveform actually looks like.
    static int LongestFullScaleRun(AudioClip clip)
    {
        float[] data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0))
            return 0;

        int longest = 0;
        int run = 0;

        foreach (float v in data)
        {
            if (Mathf.Abs(v) >= 0.9995f)
            {
                run++;
                if (run > longest)
                    longest = run;
            }
            else
            {
                run = 0;
            }
        }

        return longest;
    }

    static int CountOnsets(AudioClip clip, out float peak)
    {
        peak = 0f;

        float[] data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0))
            return -1;

        int window = Mathf.Max(1, clip.frequency / 200);   // 5ms
        int onsets = 0;
        bool above = false;

        const float rise = 0.18f;
        const float fall = 0.06f;

        for (int start = 0; start + window <= data.Length; start += window)
        {
            double sum = 0.0;
            for (int i = start; i < start + window; i++)
            {
                float v = Mathf.Abs(data[i]);
                sum += data[i] * data[i];

                if (v > peak)
                    peak = v;
            }

            float rms = Mathf.Sqrt((float)(sum / window));

            if (!above && rms > rise)
            {
                onsets++;
                above = true;
            }
            else if (above && rms < fall)
            {
                above = false;
            }
        }

        return onsets;
    }
}
