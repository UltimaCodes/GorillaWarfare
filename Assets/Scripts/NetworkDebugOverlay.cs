using System.Text;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// On-screen network state readout for diagnosing the late-join visibility bug.
///
/// Installs itself at runtime, so it needs no scene or prefab changes -- which also means
/// nothing here can be clobbered by the Unity Editor re-serializing an asset.
///
/// The question it answers: when a late joiner is invisible to the player who created the room,
/// does that player's client have a PlayerController for them AT ALL? "No entry" and
/// "entry in the wrong place" and "entry that isn't drawing" are three different bugs.
///
/// Toggle with F3. Remove this file once the bug is fixed.
/// </summary>
public class NetworkDebugOverlay : MonoBehaviour
{
    const float refreshInterval = 0.5f;

    static NetworkDebugOverlay instance;

    bool visible = true;
    float nextRefresh;
    string cached = "gathering...";
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null)
            return;

        GameObject host = new GameObject("~NetworkDebugOverlay");
        instance = host.AddComponent<NetworkDebugOverlay>();
        DontDestroyOnLoad(host);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3))
            visible = !visible;

        if (Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        cached = Gather();
    }

    static string Gather()
    {
        StringBuilder sb = new StringBuilder(1024);

        sb.AppendLine("=== NETWORK DEBUG (F3 to hide) ===");
        sb.AppendLine($"state={PhotonNetwork.NetworkClientState} region={PhotonNetwork.CloudRegion ?? "?"}");

        if (!PhotonNetwork.InRoom)
        {
            sb.AppendLine("NOT IN ROOM");
            return sb.ToString();
        }

        Player me = PhotonNetwork.LocalPlayer;
        sb.AppendLine($"me: actor={me.ActorNumber} nick='{me.NickName}' master={PhotonNetwork.IsMasterClient}");
        sb.AppendLine($"room='{PhotonNetwork.CurrentRoom.Name}' players={PhotonNetwork.CurrentRoom.PlayerCount}");

        sb.Append("actors in room:");
        foreach (Player p in PhotonNetwork.PlayerList)
            sb.Append($" {p.ActorNumber}{(p.IsMasterClient ? "*" : "")}");
        sb.AppendLine();

        PlayerManager[] managers = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
        sb.AppendLine($"--- PlayerManagers: {managers.Length} (expect one per player)");
        foreach (PlayerManager pm in managers)
        {
            PhotonView pv = pm.GetComponent<PhotonView>();
            sb.AppendLine(pv == null
                ? "  [PM] <no PhotonView>"
                : $"  [PM] view={pv.ViewID} owner={Describe(pv.Owner)} mine={pv.IsMine}");
        }

        PlayerController[] controllers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        sb.AppendLine($"--- PlayerControllers: {controllers.Length} (expect one per player)");
        foreach (PlayerController pc in controllers)
        {
            PhotonView pv = pc.GetComponent<PhotonView>();
            Renderer[] rends = pc.GetComponentsInChildren<Renderer>(true);
            int drawing = 0;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i].enabled && rends[i].gameObject.activeInHierarchy)
                    drawing++;
            }

            Vector3 p = pc.transform.position;
            sb.AppendLine(pv == null
                ? "  [PC] <no PhotonView>"
                : $"  [PC] view={pv.ViewID} owner={Describe(pv.Owner)} mine={pv.IsMine} " +
                  $"pos=({p.x:F1},{p.y:F1},{p.z:F1}) draw={drawing}/{rends.Length}");
        }

        if (controllers.Length < PhotonNetwork.CurrentRoom.PlayerCount)
            sb.AppendLine(">>> MISSING a PlayerController: their spawn event never arrived here.");

        return sb.ToString();
    }

    static string Describe(Player p)
    {
        return p == null ? "<none>" : $"{p.ActorNumber}('{p.NickName}')";
    }

    void OnGUI()
    {
        if (!visible)
            return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 14;
            style.alignment = TextAnchor.UpperLeft;
            style.normal.textColor = Color.white;
        }

        const float w = 560f;
        const float h = 380f;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(8, 8, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, w - 16, h - 8), cached, style);
    }
}
