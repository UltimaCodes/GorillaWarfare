using TMPro;
using UnityEngine;

/// <summary>
/// Shows your token balance - the crate currency - on whatever label it's on, and keeps it current
/// when a crate is bought or a match pays out. The main menu's player card uses it.
/// </summary>
public class TokenReadout : MonoBehaviour
{
    [SerializeField] TMP_Text text;
    [SerializeField] string suffix = " TOKENS";

    void OnEnable()
    {
        PlayerWallet.Changed += Draw;
        Draw();
    }

    void OnDisable() => PlayerWallet.Changed -= Draw;

    void Draw()
    {
        if (text != null)
            text.text = PlayerWallet.Tokens.ToString("N0") + suffix;
    }
}
