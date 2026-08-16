using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes whatever button it's on open the settings screen.
///
/// A component rather than a persistent onClick entry wired in the inspector, because the
/// settings screen doesn't exist until the game is running - it's instantiated onto RoomManager,
/// which survives the trip between the menu and the game. There is nothing in the scene for an
/// inspector reference to point at.
/// </summary>
[RequireComponent(typeof(Button))]
public class OpenSettingsButton : MonoBehaviour
{
    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
        {
            GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);

            if (SettingsMenu.Instance != null)
                SettingsMenu.Instance.Open();
            else
                Debug.LogWarning("[settings] nothing to open - RoomManager never instantiated the screen");
        });
    }
}
