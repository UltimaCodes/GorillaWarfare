using System.Text;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

// Throwaway debug HUD for the late-join visibility bug.
//
// Symptom: whoever made the room never sees anyone who joins after them, can't shoot them,
// and never gets them on the scoreboard - but can move around fine and IS seen by them.
// Scoreboard rows come from OnPlayerEnteredRoom, which has nothing to do with spawning, so
// the inbound side is what's broken, not Instantiate.
//
// Everything a late joiner knows could have come from its join snapshot (buffered spawns +
// PlayerList), so they might be just as deaf and only look fine. Hence the callback counters.
//
// Installs itself, so no scene or prefab wiring to lose. F3 toggles.
// TODO: delete this once the bug is closed.
public class NetworkDebugOverlay : MonoBehaviourPunCallbacks
{
    const float refreshInterval = 0.5f;

    static NetworkDebugOverlay instance;

    int enteredCount;
    int leftCount;
    int propsCount;
    float lastInboundTime = -1f;
    string lastInbound = "<nothing yet>";

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

    void Note(string what)
    {
        lastInbound = what;
        lastInboundTime = Time.unscaledTime;
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        enteredCount++;
        Note($"PlayerEntered actor={newPlayer.ActorNumber}");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        leftCount++;
        Note($"PlayerLeft actor={otherPlayer.ActorNumber}");
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        propsCount++;
        Note($"PropsUpdate actor={targetPlayer.ActorNumber}");
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

    string Gather()
    {
        StringBuilder sb = new StringBuilder(1024);

        sb.AppendLine("=== NETWORK DEBUG (F3 to hide) ===");

        // PhotonHandler gates sending, serializing and dispatching on this one flag.
        sb.AppendLine($"MESSAGE QUEUE RUNNING: {PhotonNetwork.IsMessageQueueRunning}   <<< must be True");
        sb.AppendLine($"state={PhotonNetwork.NetworkClientState} region={PhotonNetwork.CloudRegion ?? "?"} ping={PhotonNetwork.GetPing()}ms");

        if (!PhotonNetwork.InRoom)
        {
            sb.AppendLine("NOT IN ROOM");
            return sb.ToString();
        }

        Player me = PhotonNetwork.LocalPlayer;
        sb.AppendLine($"me: actor={me.ActorNumber} master={PhotonNetwork.IsMasterClient}");
        sb.AppendLine($"room='{PhotonNetwork.CurrentRoom.Name}' PlayerCount={PhotonNetwork.CurrentRoom.PlayerCount} PlayerList={PhotonNetwork.PlayerList.Length}");

        sb.Append("actors:");
        foreach (Player p in PhotonNetwork.PlayerList)
            sb.Append($" {p.ActorNumber}{(p.IsMasterClient ? "*" : "")}");
        sb.AppendLine();

        // Still 0 while someone joins = nothing is reaching us.
        string since = lastInboundTime < 0f ? "never" : $"{Time.unscaledTime - lastInboundTime:F1}s ago";
        sb.AppendLine($"callbacks: entered={enteredCount} left={leftCount} props={propsCount}");
        sb.AppendLine($"last inbound: {lastInbound} ({since})");

        PlayerController[] controllers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        sb.AppendLine($"--- PlayerControllers: {controllers.Length} (expect {PhotonNetwork.CurrentRoom.PlayerCount})");
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
                : $"  [PC] view={pv.ViewID} owner={(pv.Owner == null ? "<none>" : pv.Owner.ActorNumber.ToString())} " +
                  $"mine={pv.IsMine} pos=({p.x:F1},{p.y:F1},{p.z:F1}) draw={drawing}/{rends.Length}");
        }

        if (controllers.Length < PhotonNetwork.CurrentRoom.PlayerCount)
            sb.AppendLine(">>> MISSING a PlayerController: their spawn never arrived here.");

        return sb.ToString();
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

        const float w = 600f;
        const float h = 420f;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(8, 8, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, w - 16, h - 8), cached, style);
    }
}
