using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes whatever button it's on open the crate shop. Same shape as OpenSettingsButton, same
/// reason - the crate shop doesn't exist until RoomManager has instantiated it, so there's
/// nothing in the scene for an inspector reference to point at.
/// </summary>
[RequireComponent(typeof(Button))]
public class OpenCrateShopButton : MonoBehaviour
{
    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
        {
            GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);

            if (CrateOpeningScreen.Instance != null)
                CrateOpeningScreen.Instance.Open();
            else
                Debug.LogWarning("[crates] nothing to open - RoomManager never instantiated the screen");
        });
    }
}
