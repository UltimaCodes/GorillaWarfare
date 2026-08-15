using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

// Mode picker, drawn over the lobby.
//
// It used to sit on the create-room screen, which meant choosing before anyone had turned up -
// you had to decide between deathmatch and gun game while looking at an empty text field. It
// belongs in the room, where you can see who's actually here and agree on what to play.
//
// Only the master client can change it, for the same reason only they can start: the mode is a
// room property, and two people setting it at once is a race with no winner. Everyone else sees
// what was picked, so nobody loads in expecting the wrong game.
//
// Still drawn rather than built, because the lobby is a prefab hierarchy in the menu scene and
// M5 is going to redesign all of it. Cycle() is the whole behaviour, so hanging it off a real
// button later is a one line change.
public class MatchSetupHud : MonoBehaviour
{
    const int menuSceneIndex = 0;

    GUIStyle button;
    GUIStyle caption;
    GUIStyle readout;

    void OnGUI()
    {
        // In the lobby only. Once the game scene loads the mode is fixed - it's what the match
        // was built from.
        if (SceneManager.GetActiveScene().buildIndex != menuSceneIndex || !PhotonNetwork.InRoom)
            return;

        if (button == null)
        {
            button = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            caption = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            readout = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        }

        MatchMode mode = MatchState.Mode;
        string name = mode == MatchMode.GunGame ? "GUN GAME" : "DEATHMATCH";

        float left = 24f;
        float top = Screen.height - 116f;

        GUI.color = new Color(1f, 1f, 1f, 0.7f);
        GUI.Label(new Rect(left, top, 320f, 24f), "PLAYING", caption);

        if (PhotonNetwork.IsMasterClient)
        {
            GUI.color = Color.white;
            if (GUI.Button(new Rect(left, top + 22f, 260f, 44f), name, button))
                Cycle();
        }
        else
        {
            // Not yours to change, so it reads as a fact rather than a dead button.
            GUI.color = new Color(1f, 0.85f, 0.2f, 1f);
            GUI.Label(new Rect(left, top + 22f, 320f, 44f), name, readout);
        }

        GUI.color = new Color(1f, 1f, 1f, 0.5f);
        GUI.Label(new Rect(left, top + 70f, 460f, 24f),
                  mode == MatchMode.GunGame
                      ? "climb the ladder, two kills a rung, win on the peel"
                      : "three random bananas, most kills on the clock",
                  caption);

        GUI.color = Color.white;
    }

    // Written straight to the room, so late joiners get it from the server and the browser
    // updates without anyone being told.
    public void Cycle()
    {
        MatchMode next = MatchState.Mode == MatchMode.Deathmatch ? MatchMode.GunGame : MatchMode.Deathmatch;

        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { MatchState.ModeKey, (int)next } });

        GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);
    }
}
