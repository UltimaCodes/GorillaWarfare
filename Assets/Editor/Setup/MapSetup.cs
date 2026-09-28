using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The map system's one-time setup: the map list (`Resources/Maps.asset`), every map scene in
/// Build Settings with the menu first, and a map card in the lobby.
///
/// Each part only fills in what's missing, so this is safe to run again - an existing list keeps
/// its names, an existing map card keeps wherever it was moved to.
///
/// The map card is a copy of the lobby's own game mode card, so it arrives in the same style,
/// and it goes directly above that card - empty space in the lobby's right column - so nothing
/// already there moves.
/// </summary>
public static class MapSetup
{
    const string RegistryPath = "Assets/Resources/Maps.asset";
    const string MenuScenePath = "Assets/Scenes/Menu.unity";
    const float CardHeight = 175f;
    const float CardGap = 30f;

    [MenuItem("Tools/Gorilla Warfare/Set up the maps")]
    public static void Run()
    {
        MapRegistry registry = EnsureRegistry();
        EnsureBuildSettings(registry);
        EnsureLobbyCard();

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static MapRegistry EnsureRegistry()
    {
        MapRegistry registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(RegistryPath);

        if (registry != null)
        {
            Debug.Log("[maps] map list already exists - left alone");
            return registry;
        }

        registry = ScriptableObject.CreateInstance<MapRegistry>();
        SerializedObject so = new SerializedObject(registry);
        SerializedProperty maps = so.FindProperty("maps");

        (string key, string name, string scene)[] rows =
        {
            ("jungle", "JUNGLE", "Game"),
            ("zoo", "THE ZOO", "Zoo"),
        };

        maps.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            SerializedProperty row = maps.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("key").stringValue = rows[i].key;
            row.FindPropertyRelative("displayName").stringValue = rows[i].name;
            row.FindPropertyRelative("sceneName").stringValue = rows[i].scene;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[maps] created {RegistryPath} with {rows.Length} maps");
        return registry;
    }

    /// The menu first (RoomManager and the launcher treat index 0 as "not a map"), then every
    /// registered map whose scene exists.
    static void EnsureBuildSettings(MapRegistry registry)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int added = 0;

        SerializedProperty maps = new SerializedObject(registry).FindProperty("maps");

        for (int i = 0; i < maps.arraySize; i++)
        {
            string sceneName = maps.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue;
            string path = $"Assets/Scenes/{sceneName}.unity";

            if (!System.IO.File.Exists(path) || scenes.Exists(s => s.path == path))
                continue;

            scenes.Add(new EditorBuildSettingsScene(path, true));
            added++;
        }

        int menu = scenes.FindIndex(s => s.path == MenuScenePath);
        if (menu > 0)
        {
            EditorBuildSettingsScene entry = scenes[menu];
            scenes.RemoveAt(menu);
            scenes.Insert(0, entry);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[maps] build settings: {added} map(s) added, "
                  + string.Join(", ", scenes.ConvertAll(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));
    }

    /// A copy of the game mode card, made into a map picker, placed above it.
    static void EnsureLobbyCard()
    {
        Scene menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        if (Object.FindFirstObjectByType<MapSelector>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[maps] the lobby already has a map picker - left alone");
            return;
        }

        ModeSelector mode = Object.FindFirstObjectByType<ModeSelector>(FindObjectsInactive.Include);
        RectTransform modeCard = mode != null ? mode.transform.parent as RectTransform : null;

        if (modeCard == null)
        {
            Debug.LogError("[maps] no game mode card in the lobby to copy - no map picker added");
            return;
        }

        GameObject cardObject = Object.Instantiate(modeCard.gameObject, modeCard.parent);
        cardObject.name = "MapCard";
        cardObject.transform.SetSiblingIndex(modeCard.GetSiblingIndex());
        RectTransform card = (RectTransform)cardObject.transform;

        // The copy's selector, and what it points at - already remapped onto the copy's own children.
        ModeSelector copied = cardObject.GetComponentInChildren<ModeSelector>(true);
        SerializedObject from = new SerializedObject(copied);
        Button button = from.FindProperty("button").objectReferenceValue as Button;
        TMP_Text label = from.FindProperty("label").objectReferenceValue as TMP_Text;
        TMP_Text readout = from.FindProperty("readout").objectReferenceValue as TMP_Text;
        TMP_Text description = from.FindProperty("description").objectReferenceValue as TMP_Text;

        GameObject selectorObject = copied.gameObject;
        selectorObject.name = "MapSelector";
        Object.DestroyImmediate(copied);

        // A map needs a name, not a paragraph - and the card is shorter so it fits above the mode.
        if (description != null)
            Object.DestroyImmediate(description.gameObject);

        MapSelector selector = selectorObject.AddComponent<MapSelector>();
        SerializedObject to = new SerializedObject(selector);
        to.FindProperty("button").objectReferenceValue = button;
        to.FindProperty("label").objectReferenceValue = label;
        to.FindProperty("readout").objectReferenceValue = readout;
        to.ApplyModifiedPropertiesWithoutUndo();

        foreach (TMP_Text text in cardObject.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "Eyebrow")
                text.text = "MAP";
        }

        string first = MapRegistry.Default.displayName;
        if (label != null) label.text = first;
        if (readout != null) readout.text = first;

        card.sizeDelta = new Vector2(card.sizeDelta.x, CardHeight);
        RectTransform selectorRect = (RectTransform)selectorObject.transform;
        selectorRect.sizeDelta = new Vector2(selectorRect.sizeDelta.x, CardHeight);

        // Bottom edge a gap above the mode card's top edge, worked out in the parent's own space so
        // it holds wherever the mode card has been moved to by hand.
        float modeTop = modeCard.localPosition.y + modeCard.rect.yMax;
        float cardBottom = card.localPosition.y + card.rect.yMin;
        card.anchoredPosition += new Vector2(0f, modeTop + CardGap - cardBottom);

        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);

        Debug.Log($"[maps] added a map picker to the lobby, above the game mode card at {card.anchoredPosition}");
    }
}
