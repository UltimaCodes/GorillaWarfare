using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Every map in the game, in one place: a key, the name players see, and the scene it lives in.
///
/// Lives in `Resources/Maps.asset` so a map's name can be changed in the inspector, and adding a
/// map is one row here plus its scene in Build Settings. Everything that used to assume there was
/// exactly one map - build index 1, hard-coded - asks this instead: which map the room picked,
/// and whether the scene that's loaded is a map at all.
///
/// The room's pick is a room property, the same way the mode is (MatchState.ModeKey), so a late
/// joiner gets it from the server and the room browser can show it before anyone joins.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Map Registry")]
public class MapRegistry : ScriptableObject
{
    [System.Serializable]
    public class Map
    {
        [Tooltip("What the room property stores. Never shown, so it can stay put while the name changes.")]
        public string key;

        [Tooltip("What players see - the lobby's map picker and the room browser.")]
        public string displayName;

        [Tooltip("The scene's name, exactly as it appears in Build Settings.")]
        public string sceneName;
    }

    /// The room property holding the picked map's key.
    public const string RoomKey = "map";

    const string ResourcePath = "Maps";

    [SerializeField] Map[] maps = new Map[0];

    static MapRegistry loaded;
    static bool searched;

    // If the asset is ever missing, the game still has the map it always had rather than none.
    static readonly Map[] Fallback = { new Map { key = "jungle", displayName = "JUNGLE", sceneName = "Game" } };

    static MapRegistry Asset
    {
        get
        {
            if (!searched)
            {
                loaded = Resources.Load<MapRegistry>(ResourcePath);
                searched = true;

                if (loaded == null)
                    Debug.LogWarning("[maps] no Maps asset in Resources - only the jungle is playable. "
                                     + "Run Tools/Gorilla Warfare/Set up the maps");
            }

            return loaded;
        }
    }

    public static IReadOnlyList<Map> All
    {
        get
        {
            MapRegistry asset = Asset;
            return asset != null && asset.maps != null && asset.maps.Length > 0 ? asset.maps : Fallback;
        }
    }

    /// The first map - what a new room starts on, and where the sandbox goes.
    public static Map Default => All[0];

    public static Map Find(string key)
    {
        foreach (Map map in All)
        {
            if (map.key == key)
                return map;
        }

        return Default;
    }

    /// The map this room picked, or the default if it never picked one (a room from an older build).
    public static Map Current
    {
        get
        {
            if (PhotonNetwork.InRoom
                && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKey, out object value)
                && value is string key)
                return Find(key);

            return Default;
        }
    }

    /// Whether a scene is one of the maps - rather than the menu, or anything else.
    public static bool IsMapScene(Scene scene)
    {
        foreach (Map map in All)
        {
            if (map.sceneName == scene.name)
                return true;
        }

        return false;
    }

    /// Whether a map is loaded right now.
    public static bool InMap => IsMapScene(SceneManager.GetActiveScene());

    /// Where a key sits in the list, for cycling through it.
    public static int IndexOf(string key)
    {
        IReadOnlyList<Map> all = All;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].key == key)
                return i;
        }

        return 0;
    }
}
