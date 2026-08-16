using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// A room with no opponents, no clock and every weapon, for finding out whether something works.
///
/// Reached from the settings screen in the main menu and nowhere else. It is not a game mode -
/// there is no winner, nothing is scored, and it deliberately cannot be started from inside a
/// match, because half its point is that nobody else is affected by what you do in it.
///
/// Runs in Photon's offline mode, which is the same trick the play mode probe uses: PUN behaves
/// exactly as it does online, callbacks and room properties and all, but there is no server and
/// nobody else can wander in. Everything downstream - spawning, loadouts, the HUD - carries on
/// without knowing the difference.
/// </summary>
public static class Sandbox
{
    /// Whether this session is a sandbox. Read by the match rules, which stop applying.
    public static bool Active { get; private set; }

    public const string RoomName = "~sandbox";

    /// <summary>
    /// Leaves whatever we are connected to and comes back up offline.
    ///
    /// The disconnect is not optional. Offline mode refuses to engage while a connection exists,
    /// and the menu connects to Photon on startup - so entering the sandbox from a live menu
    /// without dropping first silently does nothing at all.
    /// </summary>
    public static void Enter(MonoBehaviour host)
    {
        if (host == null || Active)
            return;

        host.StartCoroutine(Open());
    }

    static IEnumerator Open()
    {
        Active = true;

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();

            float waited = 0f;

            while (PhotonNetwork.IsConnected && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        PhotonNetwork.OfflineMode = true;

        // Named so it is obvious in a log which kind of room this is. Nobody can see it - there
        // is no server for it to be listed on.
        PhotonNetwork.CreateRoom(RoomName, new Photon.Realtime.RoomOptions { MaxPlayers = 1 });
    }

    /// <summary>
    /// Back to the menu, and back onto the network.
    ///
    /// Offline mode has to be cleared explicitly or the next attempt to play with anybody stays
    /// stubbornly local - which would look exactly like the game failing to connect.
    /// </summary>
    public static void Leave()
    {
        Active = false;

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();

        PhotonNetwork.OfflineMode = false;
    }
}
