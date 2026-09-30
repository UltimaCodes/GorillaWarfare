using System.Collections;
using UnityEngine;

/// <summary>
/// Pops in when switched on - after `delay`, from small and clear to full size - so a grid of
/// cards arrives in a ripple rather than all at once.
/// </summary>
public class PopIn : MonoBehaviour
{
    public float delay;
    public float seconds = 0.28f;

    CanvasGroup group;
    Vector3 rest;
    bool captured;

    void OnEnable()
    {
        if (!captured)
        {
            rest = transform.localScale;
            group = GetComponent<CanvasGroup>();
            if (group == null)
                group = gameObject.AddComponent<CanvasGroup>();
            captured = true;
        }

        StopAllCoroutines();
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        group.alpha = 0f;
        transform.localScale = rest * 0.8f;
        yield return UiMotion.Wait(delay);
        yield return UiMotion.Tween(seconds, UiMotion.OutBack, k =>
        {
            transform.localScale = rest * Mathf.LerpUnclamped(0.8f, 1f, k);
            group.alpha = Mathf.Clamp01(k * 1.5f);
        });
    }
}
