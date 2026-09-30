using TMPro;
using UnityEngine;

/// <summary>
/// A token balance that counts up or down to the real one instead of jumping - a payout you
/// watch arrive. Pops when it lands on a bigger number.
/// </summary>
public class TokenCounter : MonoBehaviour
{
    [SerializeField] TMP_Text text;
    [SerializeField] string suffix = " TOKENS";

    float shown = -1f;
    int target;
    float punch;

    void OnEnable()
    {
        PlayerWallet.Changed += Refresh;
        target = PlayerWallet.Tokens;
        if (shown < 0f)
            shown = target;
        Draw();
    }

    void OnDisable() => PlayerWallet.Changed -= Refresh;

    void Refresh()
    {
        int next = PlayerWallet.Tokens;
        if (next > target)
            punch = 1f;
        target = next;
    }

    /// Straight to the real number - when opening a screen, rather than counting from wherever.
    public void Snap()
    {
        target = PlayerWallet.Tokens;
        shown = target;
        Draw();
    }

    void Update()
    {
        if (Mathf.Abs(shown - target) > 0.01f)
        {
            float speed = Mathf.Max(8f, Mathf.Abs(target - shown) * 3f);
            shown = Mathf.MoveTowards(shown, target, speed * Time.unscaledDeltaTime);
            Draw();
        }

        if (punch > 0f)
        {
            punch = Mathf.Max(0f, punch - Time.unscaledDeltaTime * 3f);
            float s = 1f + 0.18f * Mathf.Sin(punch * Mathf.PI);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    void Draw()
    {
        if (text != null)
            text.text = Mathf.RoundToInt(shown).ToString("N0") + suffix;
    }
}
