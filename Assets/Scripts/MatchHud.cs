using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;

// The match layer of the HUD: clock, mode, kill feed, respawn timer, end of match scoreboard.
//
// Separate from CombatHud because they have different lifetimes. CombatHud belongs to your
// player and dies with it, which is exactly wrong for a respawn countdown - the whole point of
// that timer is that it's on screen while you have no player. This one lives on RoomManager and
// survives everything.
//
// IMGUI and loud on purpose. It gets replaced wholesale in M5; until then it should at least
// shout, because a deathmatch clock nobody notices might as well not exist.
public class MatchHud : MonoBehaviour
{
    const int gameSceneIndex = 1;
    const float feedEntrySeconds = 6f;

    static readonly Color Warmup = new Color(1f, 0.85f, 0.1f);
    static readonly Color Live = new Color(0.95f, 0.95f, 0.95f);
    static readonly Color Urgent = new Color(1f, 0.25f, 0.15f);
    static readonly Color Kill = new Color(1f, 0.4f, 0.1f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

    GUIStyle huge;
    GUIStyle label;
    GUIStyle small;
    Texture2D pixel;

    void EnsureStyles()
    {
        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        if (huge != null)
            return;

        huge = new GUIStyle(GUI.skin.label)
        {
            fontSize = 58,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter,
        };

        label = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter,
        };

        small = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight,
        };
    }

    bool ShouldDraw =>
        PhotonNetwork.InRoom && SceneManager.GetActiveScene().buildIndex == gameSceneIndex;

    void OnGUI()
    {
        if (!ShouldDraw)
            return;

        EnsureStyles();

        DrawClock();
        DrawFeed();
        DrawPhaseMessage();
        DrawLadderProgress();
    }

    void DrawClock()
    {
        float left = MatchState.TimeLeft;
        MatchPhase phase = MatchState.Phase;

        string clock = $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}";

        // Red for the last thirty seconds. A clock you have to read is a clock you ignore.
        GUI.color = phase == MatchPhase.Warmup ? Warmup
                  : left <= 30f ? Urgent
                  : Live;

        GUI.Label(new Rect(0f, 6f, Screen.width, 80f), clock, huge);

        GUI.color = Dim;
        GUI.Label(new Rect(0f, 68f, Screen.width, 30f),
                  MatchState.Mode == MatchMode.GunGame ? "GUN GAME" : "DEATHMATCH", label);

        GUI.color = Color.white;
    }

    // Newest at the top, oldest fading out. Walked backwards so the list order is the draw order.
    void DrawFeed()
    {
        float y = 12f;
        int drawn = 0;

        for (int i = MatchState.Feed.Count - 1; i >= 0 && drawn < 6; i--)
        {
            MatchState.KillEvent entry = MatchState.Feed[i];

            float age = Time.time - entry.at;
            if (age > feedEntrySeconds)
                continue;

            float fade = Mathf.Clamp01((feedEntrySeconds - age) / 1.5f);

            string line = string.IsNullOrEmpty(entry.killer)
                ? $"{entry.victim} died"
                : $"{entry.killer}  «{entry.weapon}»  {entry.victim}";

            if (entry.headshot)
                line += "  HEAD";

            GUI.color = new Color(Kill.r, Kill.g, Kill.b, fade);
            GUI.Label(new Rect(0f, y, Screen.width - 20f, 24f), line, small);

            y += 22f;
            drawn++;
        }

        GUI.color = Color.white;
    }

    void DrawPhaseMessage()
    {
        float centre = Screen.height * 0.5f;

        if (MatchState.Phase == MatchPhase.Over)
        {
            DrawResults(centre);
            return;
        }

        if (RoomManager.AwaitingRespawn)
        {
            float left = Mathf.Max(0f, RoomManager.RespawnAt - Time.time);

            GUI.color = Urgent;
            GUI.Label(new Rect(0f, centre - 90f, Screen.width, 80f), "YOU DIED", huge);

            GUI.color = Dim;
            GUI.Label(new Rect(0f, centre - 10f, Screen.width, 40f), $"respawning in {left:F1}", label);
            GUI.color = Color.white;
            return;
        }

        if (MatchState.Phase == MatchPhase.Warmup)
        {
            GUI.color = Warmup;
            GUI.Label(new Rect(0f, centre - 120f, Screen.width, 60f), "GET READY", label);

            string weapons = string.Join("   ", PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer));
            GUI.Label(new Rect(0f, centre - 90f, Screen.width, 40f), weapons, label);
            GUI.color = Color.white;
        }
    }

    void DrawResults(float centre)
    {
        Player winner = MatchState.Winner;

        GUI.color = Kill;
        GUI.Label(new Rect(0f, centre - 160f, Screen.width, 80f),
                  winner != null ? MatchState.NameOf(winner).ToUpper() : "NOBODY", huge);

        GUI.color = Dim;
        GUI.Label(new Rect(0f, centre - 90f, Screen.width, 30f), "WINS", label);

        // Full standings, since the round is over and there's nothing else to look at.
        float y = centre - 40f;
        GUIStyle row = new GUIStyle(label) { alignment = TextAnchor.UpperCenter, fontSize = 20 };

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            GUI.color = player == PhotonNetwork.LocalPlayer ? Live : Dim;
            GUI.Label(new Rect(0f, y, Screen.width, 26f),
                      $"{MatchState.NameOf(player)}   {RoomManager.GetStat(player, RoomManager.KillsKey)} / {RoomManager.GetStat(player, RoomManager.DeathsKey)}",
                      row);
            y += 24f;
        }

        GUI.color = Dim;
        GUI.Label(new Rect(0f, y + 16f, Screen.width, 26f), $"next match in {MatchState.TimeLeft:F0}", row);
        GUI.color = Color.white;
    }

    // Gun game only. Without this you have no idea how close you are to the next weapon, which
    // is the entire tension of the mode.
    void DrawLadderProgress()
    {
        if (MatchState.Mode != MatchMode.GunGame || MatchState.Phase == MatchPhase.Over)
            return;

        int rung = MatchState.LadderRung(PhotonNetwork.LocalPlayer);
        int done = MatchState.LadderKills(PhotonNetwork.LocalPlayer);
        int needed = MatchState.KillsToAdvance;
        int total = WeaponLoadout.GunGameLadder.Length;

        GUIStyle left = new GUIStyle(label) { alignment = TextAnchor.LowerLeft, fontSize = 18 };

        GUI.color = Dim;
        GUI.Label(new Rect(24f, Screen.height - 96f, 400f, 30f),
                  $"RUNG {rung + 1} / {total}", left);

        // Pips rather than a number, so it reads at a glance mid-fight.
        for (int i = 0; i < needed; i++)
        {
            GUI.color = i < done ? Kill : new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(new Rect(24f + i * 18f, Screen.height - 66f, 13f, 13f), pixel);
        }

        GUI.color = Color.white;
    }
}
