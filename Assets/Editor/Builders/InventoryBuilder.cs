using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the inventory screen (InventoryScreen) as a prefab, Resources/Inventory.prefab, which
/// RoomManager instantiates the way it does the crate shop and the settings menu. In the main
/// menu's style (MenuStyle): weapons down the left, the one you've picked turning in the middle
/// with its finish's name, rarity and line under it, your finishes in a grid on the right.
///
/// Re-runnable - it replaces the prefab whole - for as long as nobody's edited the prefab by hand.
/// </summary>
public static class InventoryBuilder
{
    const string Path = "Assets/Resources/Inventory.prefab";
    const float Margin = 100f;

    [MenuItem("Tools/Gorilla Warfare/Build the inventory")]
    public static void Run()
    {
        MenuStyle.Load();

        GameObject root = MenuStyle.CanvasRoot("Inventory", 540);
        InventoryScreen screen = root.AddComponent<InventoryScreen>();

        RectTransform panel = MenuStyle.Stretch(root.transform, "Panel");
        CanvasGroup fade = panel.gameObject.AddComponent<CanvasGroup>();
        MenuStyle.Panel(panel, "Backdrop", Vector2.zero, Vector2.one, MenuStyle.Middle, Vector2.zero, Vector2.zero, MenuStyle.Backing, raycast: true);
        RectTransform body = MenuStyle.Stretch(panel, "Body");

        // ---- top ----
        MenuStyle.Label(body, "Title", "INVENTORY", true, 96f, Color.white, TextAlignmentOptions.TopLeft,
                        MenuStyle.TopLeft, new Vector2(Margin, -50f), new Vector2(900f, 120f));
        TMP_Text collected = MenuStyle.Label(body, "Collected", "0 / 30 FINISHES", false, 34f, MenuStyle.Muted, TextAlignmentOptions.TopLeft,
                                             MenuStyle.TopLeft, new Vector2(Margin + 4f, -166f), new Vector2(700f, 44f));
        collected.characterSpacing = 4f;

        TMP_Text tokenLabel = MenuStyle.Label(body, "Tokens", "0 TOKENS", false, 44f, MenuStyle.Banana, TextAlignmentOptions.TopRight,
                                              MenuStyle.TopRight, new Vector2(-Margin, -60f), new Vector2(600f, 56f));
        tokenLabel.rectTransform.pivot = MenuStyle.TopRight;
        TokenCounter counter = tokenLabel.gameObject.AddComponent<TokenCounter>();
        MenuStyle.Wire(counter, "text", tokenLabel);

        TMP_Text payout = MenuStyle.Label(body, "Payout", "+0 TOKENS", true, 40f, MenuStyle.Banana, TextAlignmentOptions.TopRight,
                                          MenuStyle.TopRight, new Vector2(-Margin, -118f), new Vector2(600f, 56f));
        payout.rectTransform.pivot = MenuStyle.TopRight;

        Button crates = MenuStyle.GhostButton(body, "OpenCrates", "OPEN CRATES", MenuStyle.TopRight, new Vector2(-Margin - 440f, -62f), new Vector2(280f, 58f));

        Button close = MenuStyle.TextButton(body, "Back", "BACK", 52f, MenuStyle.BottomLeft, new Vector2(Margin + 20f, 60f), new Vector2(300f, 70f));

        // ---- weapons ----
        MenuStyle.Label(body, "WeaponsEyebrow", "WEAPONS", false, 28f, MenuStyle.Muted, TextAlignmentOptions.TopLeft,
                        MenuStyle.TopLeft, new Vector2(Margin, -250f), new Vector2(400f, 36f)).characterSpacing = 6f;

        RectTransform list = MenuStyle.Rect(body, "WeaponList", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft,
                                            new Vector2(Margin, -292f), new Vector2(420f, 660f));
        VerticalLayoutGroup stack = list.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 6f;
        stack.childControlWidth = true;
        stack.childControlHeight = false;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        InventoryWeaponRow row = BuildRow(list);

        // ---- the preview ----
        float cx = 560f;
        TMP_Text weaponName = MenuStyle.Label(body, "WeaponName", "THE BUNCH", false, 30f, MenuStyle.Muted, TextAlignmentOptions.TopLeft,
                                              MenuStyle.TopLeft, new Vector2(cx, -250f), new Vector2(720f, 40f));
        weaponName.characterSpacing = 6f;

        Image frame = MenuStyle.Panel(body, "PreviewFrame", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft,
                                      new Vector2(cx - 3f, -289f), new Vector2(678f, 426f), MenuStyle.Rule);
        RectTransform previewRect = MenuStyle.Rect(body, "Preview", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft,
                                                   new Vector2(cx, -292f), new Vector2(672f, 420f));
        RawImage preview = previewRect.gameObject.AddComponent<RawImage>();
        preview.color = Color.white;
        MenuStyle.Label(previewRect, "DragHint", "DRAG TO TURN", false, 20f, new Color(1f, 1f, 1f, 0.3f), TextAlignmentOptions.BottomRight,
                        MenuStyle.BottomRight, new Vector2(-12f, 8f), new Vector2(300f, 30f)).rectTransform.pivot = MenuStyle.BottomRight;

        TMP_Text finishName = MenuStyle.Label(body, "FinishName", "STOCK", true, 64f, Color.white, TextAlignmentOptions.TopLeft,
                                              MenuStyle.TopLeft, new Vector2(cx, -760f), new Vector2(720f, 80f));
        finishName.rectTransform.pivot = new Vector2(0f, 0.5f);
        finishName.rectTransform.anchoredPosition = new Vector2(cx, -765f);

        Image pill = MenuStyle.Panel(body, "RarityPill", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft,
                                     new Vector2(cx, -812f), new Vector2(10f, 34f), Color.white);
        TMP_Text rarity = MenuStyle.Label(body, "Rarity", "DEFAULT", false, 32f, Color.white, TextAlignmentOptions.TopLeft,
                                          MenuStyle.TopLeft, new Vector2(cx + 22f, -810f), new Vector2(300f, 40f));
        rarity.characterSpacing = 6f;
        TMP_Text owned = MenuStyle.Label(body, "Owned", "", false, 26f, MenuStyle.Muted, TextAlignmentOptions.TopRight,
                                         MenuStyle.TopLeft, new Vector2(cx + 672f, -814f), new Vector2(460f, 36f));
        owned.rectTransform.pivot = MenuStyle.TopRight;
        TMP_Text flavour = MenuStyle.Paragraph(body, "Flavour", "", 28f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.TopLeft,
                                               MenuStyle.TopLeft, new Vector2(cx, -858f), new Vector2(672f, 80f));

        Button equip = MenuStyle.PrimaryButton(body, "Equip", "EQUIP", MenuStyle.TopLeft, new Vector2(cx, -950f), new Vector2(220f, 66f));
        Button equipAll = MenuStyle.GhostButton(body, "EquipAll", "EQUIP ON ALL", MenuStyle.TopLeft, new Vector2(cx + 232f, -950f), new Vector2(230f, 66f));
        Button scrap = MenuStyle.GhostButton(body, "Scrap", "SCRAP", MenuStyle.TopLeft, new Vector2(cx + 474f, -950f), new Vector2(198f, 66f),
                                             new Color(1f, 0.1f, 0.25f, 0.6f));
        Button spares = MenuStyle.TextButton(body, "ScrapSpares", "SCRAP SPARES", 30f, MenuStyle.TopRight, new Vector2(-Margin - 260f, -968f), new Vector2(300f, 50f));

        // ---- finishes ----
        MenuStyle.Label(body, "FinishesEyebrow", "YOUR FINISHES", false, 28f, MenuStyle.Muted, TextAlignmentOptions.TopLeft,
                        MenuStyle.TopRight, new Vector2(-Margin, -250f), new Vector2(560f, 36f)).characterSpacing = 6f;
        RectTransform grid = MenuStyle.ScrollGrid(body, "FinishGrid", MenuStyle.TopRight, new Vector2(-Margin, -292f),
                                                  new Vector2(560f, 650f), new Vector2(166f, 150f), new Vector2(12f, 12f), 3);
        FinishCard card = BuildCard(grid, "CardTemplate", new Vector2(166f, 150f));

        TMP_Text empty = MenuStyle.Paragraph(body, "Empty", "NOTHING HERE YET\nOPEN A CRATE", 36f, MenuStyle.Muted, TextAlignmentOptions.Center,
                                             MenuStyle.TopRight, new Vector2(-Margin, -560f), new Vector2(560f, 120f));

        // Wiring.
        MenuStyle.Wire(screen, "panel", panel.gameObject);
        MenuStyle.Wire(screen, "fade", fade);
        MenuStyle.Wire(screen, "body", body);
        MenuStyle.Wire(screen, "collectedText", collected);
        MenuStyle.Wire(screen, "tokens", counter);
        MenuStyle.Wire(screen, "closeButton", close);
        MenuStyle.Wire(screen, "cratesButton", crates);
        MenuStyle.Wire(screen, "weaponList", list);
        MenuStyle.Wire(screen, "weaponRowTemplate", row);
        MenuStyle.Wire(screen, "preview", preview);
        MenuStyle.Wire(screen, "weaponName", weaponName);
        MenuStyle.Wire(screen, "finishName", finishName);
        MenuStyle.Wire(screen, "rarityText", rarity);
        MenuStyle.Wire(screen, "rarityPill", pill);
        MenuStyle.Wire(screen, "flavourText", flavour);
        MenuStyle.Wire(screen, "ownedText", owned);
        MenuStyle.Wire(screen, "equipButton", equip);
        MenuStyle.Wire(screen, "equipLabel", equip.GetComponentInChildren<TMP_Text>());
        MenuStyle.Wire(screen, "equipAllButton", equipAll);
        MenuStyle.Wire(screen, "scrapButton", scrap);
        MenuStyle.Wire(screen, "scrapLabel", scrap.GetComponentInChildren<TMP_Text>());
        MenuStyle.Wire(screen, "scrapSparesButton", spares);
        MenuStyle.Wire(screen, "scrapSparesLabel", spares.GetComponentInChildren<TMP_Text>());
        MenuStyle.Wire(screen, "payout", payout);
        MenuStyle.Wire(screen, "finishGrid", grid);
        MenuStyle.Wire(screen, "finishCardTemplate", card);
        MenuStyle.Wire(screen, "emptyNote", empty.gameObject);

        panel.gameObject.SetActive(false);
        Save(root, Path);

        Debug.Log($"[skins] built {Path}");
        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static InventoryWeaponRow BuildRow(RectTransform list)
    {
        RectTransform rect = MenuStyle.Rect(list, "RowTemplate", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft, Vector2.zero, new Vector2(420f, 74f));
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 74f;

        Image face = rect.gameObject.AddComponent<Image>();
        face.color = new Color(1f, 1f, 1f, 0.02f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = face;

        Image accent = MenuStyle.Bar(rect, new Color(1f, 0.82f, 0.12f, 0f), true, 7f);

        TMP_Text name = MenuStyle.Label(rect, "Name", "THE BUNCH", true, 34f, Color.white, TextAlignmentOptions.MidlineLeft,
                                        MenuStyle.MiddleLeft, new Vector2(24f, 12f), new Vector2(390f, 44f));
        name.rectTransform.pivot = MenuStyle.MiddleLeft;
        TMP_Text finish = MenuStyle.Label(rect, "Finish", "STOCK", false, 24f, MenuStyle.Muted, TextAlignmentOptions.MidlineLeft,
                                          MenuStyle.MiddleLeft, new Vector2(24f, -20f), new Vector2(390f, 30f));
        finish.rectTransform.pivot = MenuStyle.MiddleLeft;

        rect.gameObject.AddComponent<HoverLift>().hoverScale = 1.03f;
        rect.gameObject.AddComponent<PopIn>();

        InventoryWeaponRow row = rect.gameObject.AddComponent<InventoryWeaponRow>();
        MenuStyle.Wire(row, "button", button);
        MenuStyle.Wire(row, "nameText", name);
        MenuStyle.Wire(row, "finishText", finish);
        MenuStyle.Wire(row, "accent", accent);
        MenuStyle.Wire(row, "face", face);
        return row;
    }

    /// <summary>
    /// A finish card - shared with the crate reel (CrateShopBuilder builds its reel's cards with
    /// this). A banana frame behind that shows when it's selected, the card's face over it.
    /// </summary>
    public static FinishCard BuildCard(Transform parent, string name, Vector2 size)
    {
        RectTransform rect = MenuStyle.Rect(parent, name, MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft, Vector2.zero, size);

        Image selection = rect.gameObject.AddComponent<Image>();
        selection.color = MenuStyle.Banana;

        Image face = MenuStyle.Panel(rect, "Face", Vector2.zero, Vector2.one, MenuStyle.Middle, Vector2.zero, new Vector2(-6f, -6f),
                                     new Color(0.06f, 0.07f, 0.09f, 1f), raycast: true);

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = face;

        float pictureHeight = size.y - 46f;
        RectTransform pictureRect = MenuStyle.Rect(rect, "Picture", MenuStyle.TopMiddle, MenuStyle.TopMiddle, MenuStyle.TopMiddle,
                                                   new Vector2(0f, -5f), new Vector2(size.x - 10f, pictureHeight));
        RawImage picture = pictureRect.gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;

        Image wash = MenuStyle.Panel(rect, "RarityWash", new Vector2(0f, 0f), new Vector2(1f, 0f), MenuStyle.BottomMiddle,
                                     new Vector2(0f, 3f), new Vector2(-6f, 40f), new Color(1f, 1f, 1f, 0.2f));
        Image strip = MenuStyle.Panel(rect, "RarityStrip", new Vector2(0f, 0f), new Vector2(1f, 0f), MenuStyle.BottomMiddle,
                                      new Vector2(0f, 3f), new Vector2(-6f, 5f), Color.white);

        TMP_Text label = MenuStyle.Label(rect, "Name", "FINISH", true, size.x > 180f ? 22f : 18f, Color.white, TextAlignmentOptions.Center,
                                         MenuStyle.BottomMiddle, new Vector2(0f, 12f), new Vector2(size.x - 12f, 28f));
        label.rectTransform.pivot = MenuStyle.BottomMiddle;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = size.x > 180f ? 22f : 18f;

        TMP_Text count = MenuStyle.Label(rect, "Count", "x2", false, 24f, MenuStyle.Banana, TextAlignmentOptions.TopRight,
                                         MenuStyle.TopRight, new Vector2(-8f, -6f), new Vector2(60f, 28f));
        count.rectTransform.pivot = MenuStyle.TopRight;

        RectTransform mark = MenuStyle.Rect(rect, "Equipped", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft, new Vector2(6f, -6f), new Vector2(46f, 24f));
        Image markFace = mark.gameObject.AddComponent<Image>();
        markFace.color = MenuStyle.Banana;
        markFace.raycastTarget = false;
        TMP_Text markLabel = MenuStyle.Label(mark, "Label", "ON", true, 18f, MenuStyle.Ink, TextAlignmentOptions.Center, MenuStyle.Middle, Vector2.zero, new Vector2(46f, 24f));
        markLabel.fontSharedMaterial = MenuStyle.DisplayFlat;

        rect.gameObject.AddComponent<HoverLift>();
        rect.gameObject.AddComponent<PopIn>();

        FinishCard card = rect.gameObject.AddComponent<FinishCard>();
        MenuStyle.Wire(card, "button", button);
        MenuStyle.Wire(card, "picture", picture);
        MenuStyle.Wire(card, "nameText", label);
        MenuStyle.Wire(card, "countText", count);
        MenuStyle.Wire(card, "rarityStrip", strip);
        MenuStyle.Wire(card, "rarityWash", wash);
        MenuStyle.Wire(card, "selection", selection);
        MenuStyle.Wire(card, "equippedMark", mark.gameObject);
        return card;
    }

    public static void Save(GameObject root, string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
    }
}
