using Photon.Pun;
using Photon.Realtime;
using UnityEditor;
using UnityEngine;

// Asks Photon which regions this AppId can actually use, and how far away each one is.
//
// The region token is a short string like "eu" or "in", and getting it wrong does not produce a
// helpful error - it produces a client that cannot find any rooms, which looks exactly like the
// game being broken. The documentation is behind a bot check from here, and the tokens differ
// from the names shown in the dashboard, so guessing between "mea" and "uae" was not good enough.
//
// This connects, lets PUN ping every region, prints the list with round trip times, and
// disconnects. Nothing is changed by running it. The ping figures are from whoever runs it, so
// they answer the useful question directly: which single region is least unfair to everybody.
[InitializeOnLoad]
public static class RegionCheck
{
    const string Flag = "gw_regioncheck";

    static RegionCheck()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Gorilla Warfare/List Photon regions")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);

        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        PhotonNetwork.NetworkingClient.AppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime;

        // Best region on purpose - that is the mode that makes PUN ping all of them and build
        // the summary this is here to read.
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = string.Empty;
        PhotonNetwork.ConnectUsingSettings();

        Debug.Log("[region] connecting to ping every region, this takes a few seconds");
    }

    static double started;

    static void Tick()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        if (started <= 0d)
            started = EditorApplication.timeSinceStartup;

        PhotonNetwork.NetworkingClient?.Service();

        RegionHandler handler = PhotonNetwork.NetworkingClient?.RegionHandler;

        if (handler != null && handler.EnabledRegions != null && handler.EnabledRegions.Count > 0)
        {
            Debug.Log($"[region] {handler.EnabledRegions.Count} regions enabled on this AppId:");

            foreach (Region region in handler.EnabledRegions)
                Debug.Log($"[region]   token '{region.Code}'  {region.Ping} ms  ({region.HostAndPort})");

            Region best = handler.BestRegion;

            if (best != null)
                Debug.Log($"[region] nearest from here is '{best.Code}' at {best.Ping} ms");

            Finish();
            return;
        }

        // Something is wrong - no internet, a bad AppId, a firewall. Say so rather than hanging.
        if (EditorApplication.timeSinceStartup - started > 30d)
        {
            Debug.LogError("[region] no region list after 30 seconds - check the AppId and the connection");
            Finish();
        }
    }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        started = 0d;

        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
