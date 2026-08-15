using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

// Mode picker for the menu, drawn over the existing UI.
//
// The mode has to be chosen before the room is created, which means it belongs on the create
// screen - and that screen is a prefab hierarchy in the menu scene. Rather than do scene surgery
// for a control that M5 is going to redesign anyway, this draws itself. Launcher.CycleMode is
// public, so wiring it to a real button later is one drag.
public class MatchSetupHud : MonoBehaviour
{
    const int menuSceneIndex = 0;

    GUIStyle button;
    GUIStyle caption;

    void OnGUI()
    {
        // Only on the create/browse screens, and never once a room exists - the mode is fixed
        // at creation because it's a room property the server hands to late joiners.
        if (SceneManager.GetActiveScene().buildIndex != menuSceneIndex || PhotonNetwork.InRoom)
            return;

        if (Launcher.Instance == null || !PhotonNetwork.IsConnectedAndReady)
            return;

        if (button == null)
        {
            button = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            caption = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        }

        MatchMode mode = Launcher.Instance.SelectedMode;

        GUI.color = new Color(1f, 1f, 1f, 0.7f);
        GUI.Label(new Rect(24f, Screen.height - 108f, 320f, 24f), "NEW ROOM RUNS", caption);

        GUI.color = Color.white;
        if (GUI.Button(new Rect(24f, Screen.height - 84f, 260f, 44f),
                       mode == MatchMode.GunGame ? "GUN GAME" : "DEATHMATCH", button))
        {
            Launcher.Instance.CycleMode();
        }

        GUI.color = new Color(1f, 1f, 1f, 0.5f);
        GUI.Label(new Rect(24f, Screen.height - 38f, 420f, 24f),
                  mode == MatchMode.GunGame
                      ? "climb the ladder, two kills a rung, win on the peel"
                      : "three random bananas, most kills on the clock",
                  caption);

        GUI.color = Color.white;
    }
}
