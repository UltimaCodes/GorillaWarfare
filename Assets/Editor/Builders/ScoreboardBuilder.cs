using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the tab scoreboard's visuals - a single pooled row template inside a vertical layout,
/// which Scoreboard.cs stamps out and fills at runtime the same way GameHud.UpdateStandings
/// already builds its own rows. The scoreboard never had a builder or a styling pass before this;
/// unlike HudBuilder, there is nothing hand-edited here to protect, so Run() rebuilds it outright
/// every time rather than needing a separate, narrower Repair().
/// </summary>
public static class ScoreboardBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";

    static readonly Color OutlineColour = new Color(0.07f, 0.08f, 0.1f);
    const float OutlineWidth = 0.55f;

    [MenuItem("Tools/Gorilla Warfare/Build the tab scoreboard")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Scoreboard scoreboard = Object.FindFirstObjectByType<Scoreboard>(FindObjectsInactive.Include);

        if (scoreboard == null)
        {
            Debug.LogError("[scoreboard] no Scoreboard component in the Game scene - nothing to build onto");
            Finish(1);
            return;
        }

        GameObject root = scoreboard.gameObject;

        // Rebuilt outright - anything already under here is the old per-player prefab wiring
        // (or a previous run of this same tool), never something hand-styled to preserve.
        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        TMP_FontAsset font = FindFontNamed("Jersey10");
        TMP_FontAsset rankFont = FindFontNamed("Anton");

        // Reported directly: "not really a visible scoreboard... no canvas or something behind
        // it." A CS2-style scoreboard is a real bounded card, not a wash over the whole screen -
        // and this project has already been burned once by a translucent backdrop reading as
        // "almost fully see-through" at 95% opacity (CrateShop's, see bug-log.md's twenty-sixth
        // pass), which is why this started fully opaque rather than re-discovering that the slow
        // way a second time. Reported back once the layout itself was fixed: wants some
        // transparency after all, just not that much - 0.9 rather than a fresh guess back toward
        // the range that already burned this project twice.
        //
        // Height sized for the real worst case, not just however many rows happened to be
        // tested - a full 8-player Team Deathmatch lobby is the column header plus two team
        // headers plus eight player rows, 11 rows total, which the original 620 never actually
        // had room for even before the childForceExpandHeight bug (see BuildRow) made every row
        // balloon to fill whatever space there was regardless.
        Vector2 cardSize = new Vector2(1000f, 900f);

        GameObject backdrop = Panel(root.transform, "Backdrop", Center, Center, Center,
                                    new Vector2(0f, 20f), cardSize + new Vector2(40f, 40f),
                                    new Color(0.04f, 0.04f, 0.05f, 0.9f));

        // A lighter top bar, its own element rather than part of the same flat panel, so the
        // card reads as having a header the way every reference (CS2, Valorant, Overwatch) does
        // rather than one undifferentiated slab.
        GameObject headerBar = Panel(backdrop.transform, "HeaderBar", TopCenter, TopCenter, TopCenter,
                                     Vector2.zero, new Vector2(cardSize.x + 40f, 64f),
                                     new Color(0.09f, 0.09f, 0.11f, 0.9f));

        TMP_Text title = Text(headerBar.transform, "Title", rankFont, 34f, TextAlignmentOptions.Center,
                              new Vector2(cardSize.x, 56f));
        title.text = "SCOREBOARD";
        title.color = new Color(1f, 0.82f, 0.1f);
        title.gameObject.SetActive(true);

        // A CHILD of the backdrop, stretch-anchored to fill it (minus margins for the header and
        // some padding), rather than a sibling positioned with its own independently-computed
        // offset. The first version did the latter - two absolute positions that were supposed to
        // land inside one another by arithmetic alone - and a sign/reference-point error nobody
        // caught by reading the numbers put the whole row column entirely *below* the backdrop
        // instead of inside it (caught for real this time by PlayModeProbe.CheckScoreboardLayout,
        // reading actual laid-out world corners rather than trusting the arithmetic again).
        // Nesting like this makes "inside the backdrop" true by construction - there's no second
        // set of numbers that has to agree with the first.
        GameObject rowsHost = new GameObject("Rows", typeof(RectTransform));
        rowsHost.transform.SetParent(backdrop.transform, false);

        RectTransform column = (RectTransform)rowsHost.transform;
        column.anchorMin = Vector2.zero;
        column.anchorMax = Vector2.one;
        column.pivot = Center;
        column.offsetMin = new Vector2(20f, 20f);
        column.offsetMax = new Vector2(-20f, -(64f + 16f));

        // Row gap widened 6->14 and the column gap inside each row 10->18 (see BuildRow) -
        // reported back as "the spacing is a bit off" once the layout and opacity were sorted.
        // Rows were sitting close enough to blur into each other, and the rank/name/stat columns
        // close enough to read as crowded rather than tabular.
        VerticalLayoutGroup rowsLayout = rowsHost.AddComponent<VerticalLayoutGroup>();
        rowsLayout.childAlignment = TextAnchor.UpperCenter;
        rowsLayout.spacing = 14f;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;

        // A real table, not one rich-text line pretending to be one - a CS2-style scoreboard's
        // whole readability comes from every row's stats landing in the same place regardless of
        // name length. Fields are this game's own, not CS2's: no money or MVP stars, but a rank,
        // a name, the stat that actually decides the mode (style score or ladder rung), K/D
        // always shown since it still means something in every mode, and a streak flourish.
        RectTransform rowTemplate = BuildRow(column, "RowTemplate", font);
        rowTemplate.gameObject.SetActive(false);

        SerializedObject so = new SerializedObject(scoreboard);
        Wire(so, "container", column);
        Wire(so, "backdrop", (RectTransform)backdrop.transform);
        Wire(so, "rowTemplate", rowTemplate);
        Wire(so, "canvasGroup", group);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[scoreboard] rebuilt - one row template, pooled and filled by Scoreboard.cs at runtime");
        Finish(0);
    }

    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

    // Rank / Name / Primary (style score or ladder rung, whichever the mode actually uses) /
    // Secondary (K/D) / Streak. Columns plus the four 18px gaps between them (see BuildRow) sum
    // to 922, clear of the ~960 the card's own padding leaves available.
    const float RankWidth = 70f;
    const float NameWidth = 280f;
    const float PrimaryWidth = 260f;
    const float SecondaryWidth = 130f;
    const float StreakWidth = 110f;
    const float RowHeight = 58f;

    /// <summary>
    /// One row of the scoreboard table - a horizontal strip of five named, fixed-width columns
    /// rather than one long rich-text line. Fixed widths are what make every row's stats line up
    /// regardless of how long a name is; Scoreboard.cs finds each column by name the same way
    /// SettingsMenu.Part already finds a row's Label/Value by name, rather than by child index.
    /// </summary>
    static RectTransform BuildRow(Transform parent, string name, TMP_FontAsset font)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 18f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        // The actual bug behind "the spacing is off" (screenshotted: header row at the top,
        // first player row far below it, most of the card empty in between). This row's own
        // HorizontalLayoutGroup, forcing its columns to expand vertically, was also reporting
        // upward that the *row itself* wants to expand - so the outer vertical list handed each
        // row a share of all the leftover space instead of packing rows together, and the row's
        // own text just centred inside its own now-oversized rect. False here, plus an explicit
        // zero below rather than the unset default, removes that upward signal entirely - the
        // row's height is fully decided by its own LayoutElement, nothing it contains.
        layout.childForceExpandHeight = false;

        LayoutElement rowElement = row.AddComponent<LayoutElement>();
        rowElement.preferredHeight = RowHeight;
        rowElement.flexibleHeight = 0f;

        Text(row.transform, "Rank", font, 40f, TextAlignmentOptions.Center, new Vector2(RankWidth, RowHeight));
        TMP_Text nameText = Text(row.transform, "Name", font, 40f, TextAlignmentOptions.Left, new Vector2(NameWidth, RowHeight));

        // Player-typed, so never parsed as rich text, and cut off with an ellipsis rather than
        // running into the Score column. PlayerNames.Clean caps names too - this is the backstop.
        nameText.richText = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        Text(row.transform, "Primary", font, 40f, TextAlignmentOptions.Left, new Vector2(PrimaryWidth, RowHeight));
        Text(row.transform, "Secondary", font, 40f, TextAlignmentOptions.Center, new Vector2(SecondaryWidth, RowHeight));
        Text(row.transform, "Streak", font, 34f, TextAlignmentOptions.Center, new Vector2(StreakWidth, RowHeight));

        return (RectTransform)row.transform;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static GameObject Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                            Vector2 pivot, Vector2 position, Vector2 size, Color? colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        if (colour.HasValue)
        {
            Image image = go.AddComponent<Image>();
            image.color = colour.Value;
            image.raycastTarget = false;
        }

        return go;
    }


    // Same outline-plus-underlay treatment HudBuilder.Text gives every HUD label, so the
    // scoreboard reads as drawn in the same ink as the rest of the HUD instead of the plain,
    // unstyled rows it shipped with - the thing that was reported as "not updated with the game."
    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float size,
                         TextAlignmentOptions alignment, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true;
        text.color = Color.white;

        if (font != null)
            text.font = font;

        Material material = text.fontMaterial;
        material.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColour);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
        text.fontMaterial = material;

        RectTransform rect = (RectTransform)go.transform;
        rect.sizeDelta = dimensions;

        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredHeight = dimensions.y;
        element.preferredWidth = dimensions.x;

        return text;
    }

    static TMP_FontAsset FindFontNamed(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains(name))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return TMP_Settings.defaultFontAsset;
    }

    static void Wire(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);

        if (property == null)
        {
            Debug.LogError($"[scoreboard] Scoreboard has no field called '{field}'");
            return;
        }

        property.objectReferenceValue = value;
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
