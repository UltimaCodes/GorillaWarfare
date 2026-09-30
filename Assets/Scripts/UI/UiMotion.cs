using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The menus' movement: tweens on unscaled time (a menu opened mid-match still moves while the
/// game's time is frozen by a hit-stop), and the easing curves they use.
/// </summary>
public static class UiMotion
{
    /// Runs `step` from 0 to 1 over `seconds`, eased.
    public static IEnumerator Tween(float seconds, Func<float, float> ease, Action<float> step)
    {
        float t = 0f;
        while (t < seconds)
        {
            step(ease(Mathf.Clamp01(t / Mathf.Max(seconds, 1e-4f))));
            yield return null;
            t += Time.unscaledDeltaTime;
        }

        step(ease(1f));
    }

    public static IEnumerator Wait(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
            yield return null;
    }

    public static float Linear(float k) => k;
    public static float OutQuad(float k) => 1f - (1f - k) * (1f - k);
    public static float OutCubic(float k) => 1f - Mathf.Pow(1f - k, 3f);
    public static float OutQuint(float k) => 1f - Mathf.Pow(1f - k, 5f);
    public static float InQuad(float k) => k * k;
    public static float InOutCubic(float k) => k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;

    /// Past the end and back - a pop.
    public static float OutBack(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float k1 = k - 1f;
        return 1f + c3 * k1 * k1 * k1 + c1 * k1 * k1;
    }

    /// A spring settling.
    public static float OutElastic(float k)
    {
        if (k <= 0f) return 0f;
        if (k >= 1f) return 1f;
        return Mathf.Pow(2f, -10f * k) * Mathf.Sin((k * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
    }
}
