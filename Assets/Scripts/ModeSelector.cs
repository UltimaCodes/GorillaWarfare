using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The deathmatch / gun game switch in the lobby.
///
/// Real objects in the scene rather than something drawn at runtime. The previous version was
/// IMGUI, which meant it existed only while the game was running - so its position, font and
/// size were numbers in a source file and there was no way to drag it anywhere. Everything here
/// is a normal Button and TMP_Text sitting under RoomMenu, so it can be moved, restyled and
/// re-fonted like anything else on the screen.
///
/// This script only decides what the label says and who is allowed to press it. Where it sits
/// and what it looks like is the scene's business.
/// </summary>
public class ModeSelector : MonoBehaviour
{
    [Tooltip("Pressing this cycles the mode. Hidden for anyone who isn't the host.")]
    [SerializeField] Button button;

    [Tooltip("Shows the mode. Doubles as the button's own label.")]
    [SerializeField] TMP_Text label;

    [Tooltip("One line explaining what the mode does.")]
    [SerializeField] TMP_Text description;

    [Tooltip("Shown instead of the button to anyone who isn't the host.")]
    [SerializeField] TMP_Text readout;

    MatchMode shown = (MatchMode)(-1);
    bool wasHost;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Cycle);
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Cycle);
    }

    void Update()
    {
        if (!PhotonNetwork.InRoom)
            return;

        MatchMode mode = MatchState.Mode;
        bool host = PhotonNetwork.IsMasterClient;

        // Only touches the UI when something actually changed, so this isn't rewriting text
        // meshes every frame for no reason.
        if (mode == shown && host == wasHost)
            return;

        shown = mode;
        wasHost = host;

        Apply(mode, host);
    }

    void Apply(MatchMode mode, bool host)
    {
        string name = mode == MatchMode.GunGame ? "GUN GAME" : "DEATHMATCH";

        if (label != null)
            label.text = name;

        if (description != null)
        {
            description.text = mode == MatchMode.GunGame
                ? "climb the ladder, two kills a rung, win on the peel"
                : "a random banana every life, most kills on the clock";
        }

        // Anyone who isn't the host sees what was picked rather than a button that refuses to
        // do anything - a dead control is worse than no control.
        if (button != null)
            button.gameObject.SetActive(host);

        if (readout != null)
        {
            readout.gameObject.SetActive(!host);
            readout.text = name;
        }
    }

    /// Wired to the button in the scene, and public so it can be re-wired anywhere.
    public void Cycle()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;

        MatchMode next = MatchState.Mode == MatchMode.Deathmatch ? MatchMode.GunGame : MatchMode.Deathmatch;

        // Straight onto the room, so late joiners get it from the server and the browser
        // updates without anyone being told.
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { MatchState.ModeKey, (int)next } });

        GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);
    }

    /// Repaint when the room property comes back, rather than waiting for the next Update to
    /// notice - the press and the label changing should look like one thing.
    void OnEnable()
    {
        shown = (MatchMode)(-1);
    }
}
