using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Moves anything still sitting on TextMeshPro's default font onto one of the project's own.
//
// LiberationSans is what TMP assigns when nobody chooses - it's the giveaway that a piece of UI
// was made and never styled. Four prefabs were still on it: the scoreboard rows, the lobby
// player list, the room browser list, and the nametag on the player prefab. Everything around
// them had been restyled, so those four read as leftovers from an older build, which is exactly
// what they were.
//
// Done through the TMP API rather than by swapping guids in the YAML, because a TMP_Text points
// at both a font asset and a material built from that font's atlas. Change one and not the
// other and the glyphs are looked up in the wrong texture - you get the right shapes drawn from
// the wrong pixels, or nothing at all. Assigning `font` sets the matching material with it.
public static class FontRetarget
{
    // In-game reads at a glance while someone is shooting at you. The menus are Ryaan's, and
    // Chomsky is what he used for most of them.
    const string InGame = "Helvetica Punk";
    const string Menu = "Chomsky";

    static readonly string[] Prefabs =
    {
        "Assets/Prefabs/ScoreboardItem.prefab",
        "Assets/Resources/PhotonPrefabs/PlayerController.prefab",
        "Assets/Prefabs/PlayerListItem.prefab",
        "Assets/Prefabs/RoomListItem.prefab",
    };

    [MenuItem("Tools/Gorilla Warfare/Retarget default fonts")]
    public static void Run()
    {
        TMP_FontAsset inGame = Find(InGame);
        TMP_FontAsset menu = Find(Menu);

        if (inGame == null || menu == null)
        {
            Debug.LogError($"[font] missing a font asset - '{InGame}' or '{Menu}' is not in Assets/Fonts");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        int moved = 0;
        List<string> touched = new List<string>();

        foreach (string path in Prefabs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogWarning($"[font] no prefab at {path}");
                continue;
            }

            // The menu lists live in the menu and should match it; the scoreboard and the
            // nametag are in the game and should match the HUD.
            bool isMenuSide = path.Contains("PlayerListItem") || path.Contains("RoomListItem");
            TMP_FontAsset target = isMenuSide ? menu : inGame;

            int changed = Retarget(prefab, target);

            if (changed == 0)
                continue;

            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);

            moved += changed;
            touched.Add($"{System.IO.Path.GetFileName(path)} -> {target.name} ({changed})");
        }

        // The scenes too, in case anything was placed directly rather than instanced.
        foreach (string scenePath in new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Game.unity" })
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            TMP_FontAsset target = scenePath.Contains("Menu") ? menu : inGame;

            int changed = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
                changed += Retarget(root, target);

            if (changed == 0)
                continue;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            moved += changed;
            touched.Add($"{System.IO.Path.GetFileName(scenePath)} -> {target.name} ({changed})");
        }

        AssetDatabase.SaveAssets();

        foreach (string line in touched)
            Debug.Log($"[font] {line}");

        Debug.Log($"[font] {moved} labels moved off the default font");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static int Retarget(GameObject root, TMP_FontAsset target)
    {
        int changed = 0;

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            // Only the ones nobody chose. Anything already pointing at a deliberate font is
            // somebody's decision and none of this script's business.
            if (text.font == null || !text.font.name.Contains("LiberationSans"))
                continue;

            text.font = target;
            EditorUtility.SetDirty(text);
            changed++;
        }

        return changed;
    }

    static TMP_FontAsset Find(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains(name))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return null;
    }
}
