using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The map switch in the lobby - the same shape as ModeSelector beside it, and for the same
/// reasons: real objects in the scene, so it can be moved and restyled by hand, and this script
/// only decides what the label says and who may press it.
///
/// The host cycles through MapRegistry; everyone else sees the pick. It goes onto the room as a
/// property, so late joiners and the room browser both get it from the server.
/// </summary>
public class MapSelector : MonoBehaviour
{
    [Tooltip("Pressing this cycles the map. Hidden for anyone who isn't the host.")]
    [SerializeField] Button button;

    [Tooltip("Shows the map. Doubles as the button's own label.")]
    [SerializeField] TMP_Text label;

    [Tooltip("Shown instead of the button to anyone who isn't the host.")]
    [SerializeField] TMP_Text readout;

    string shown;
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

    void OnEnable()
    {
        shown = null;
    }

    void Update()
    {
        if (!PhotonNetwork.InRoom)
            return;

        MapRegistry.Map map = MapRegistry.Current;
        bool host = PhotonNetwork.IsMasterClient;

        if (map.key == shown && host == wasHost)
            return;

        shown = map.key;
        wasHost = host;

        if (label != null)
            label.text = map.displayName;

        // Anyone who isn't the host sees the pick rather than a button that refuses to work.
        if (button != null)
            button.gameObject.SetActive(host);

        if (readout != null)
        {
            readout.gameObject.SetActive(!host);
            readout.text = map.displayName;
        }
    }

    /// Wired to the button, and public so it can be re-wired anywhere.
    public void Cycle()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;

        IReadOnlyList<MapRegistry.Map> all = MapRegistry.All;
        MapRegistry.Map next = all[(MapRegistry.IndexOf(MapRegistry.Current.key) + 1) % all.Count];

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { MapRegistry.RoomKey, next.key } });

        GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);
    }
}
