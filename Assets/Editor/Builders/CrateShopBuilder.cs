using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the crate screen (CrateOpeningScreen) as a prefab, Resources/CrateShop.prefab, which
/// RoomManager instantiates. Rebuilt 2026-09-30 in the main menu's own style (MenuStyle) - "revamp
/// the entire crates system to match the rest of the UI" - with the three chests (Kenney, CC0,
/// Assets/Art/Crates) on a stage behind the cards, each in its own look (FinishLibraryBuilder's
/// crate looks), and a reel of real finish cards.
///
/// Re-runnable and destructive - it replaces the prefab whole. Nothing in it has been edited by
/// hand; if that changes, this wants the Run/Repair split HudBuilder has.
/// </summary>
public static class CrateShopBuilder
{
    const string Path = "Assets/Resources/CrateShop.prefab";
    const string Sprites = "Assets/Resources/Particles/Finish";
    const float Margin = 100f;

    static readonly string[] Chests = { "DungeonChest", "PirateChest", "PlatformerChest" };
    static readonly string[] Looks = { "crate-rotten", "crate-ripe", "crate-holy" };

    [MenuItem("Tools/Gorilla Warfare/Build the crate shop")]
    public static void Run()
    {
        MenuStyle.Load();

        GameObject root = MenuStyle.CanvasRoot("CrateShop", 550);
        CrateOpeningScreen screen = root.AddComponent<CrateOpeningScreen>();

        RectTransform panel = MenuStyle.Stretch(root.transform, "Panel");
        CanvasGroup fade = panel.gameObject.AddComponent<CanvasGroup>();
        MenuStyle.Panel(panel, "Backdrop", Vector2.zero, Vector2.one, MenuStyle.Middle, Vector2.zero, Vector2.zero, MenuStyle.Backing, raycast: true);

        RectTransform stageRect = MenuStyle.Stretch(panel, "Stage");
        RawImage stageView = stageRect.gameObject.AddComponent<RawImage>();
        stageView.raycastTarget = false;

        TMP_Text tokenLabel = MenuStyle.Label(panel, "Tokens", "0 TOKENS", false, 44f, MenuStyle.Banana, TextAlignmentOptions.TopRight,
                                              MenuStyle.TopRight, new Vector2(-Margin, -60f), new Vector2(600f, 56f));
        TokenCounter counter = tokenLabel.gameObject.AddComponent<TokenCounter>();
        MenuStyle.Wire(counter, "text", tokenLabel);

        // ---------------------------------------------------------------- pick a crate
        RectTransform select = MenuStyle.Stretch(panel, "SelectPage");
        MenuStyle.Label(select, "Title", "CRATES", true, 96f, Color.white, TextAlignmentOptions.TopLeft,
                        MenuStyle.TopLeft, new Vector2(Margin, -50f), new Vector2(900f, 120f));
        MenuStyle.Label(select, "Subtitle", "EVERY FINISH WORKS ON EVERY WEAPON", false, 30f, MenuStyle.Muted, TextAlignmentOptions.Bottom,
                        MenuStyle.BottomMiddle, new Vector2(0f, 76f), new Vector2(900f, 40f)).characterSpacing = 4f;
        Button inventory = MenuStyle.GhostButton(select, "Inventory", "INVENTORY", MenuStyle.TopRight, new Vector2(-Margin - 440f, -62f), new Vector2(260f, 58f));
        Button close = MenuStyle.TextButton(select, "Back", "BACK", 52f, MenuStyle.BottomLeft, new Vector2(Margin + 20f, 60f), new Vector2(300f, 70f));

        CrateCard[] cards = new CrateCard[3];
        RectTransform[] spots = new RectTransform[3];
        for (int i = 0; i < 3; i++)
            cards[i] = BuildCrateCard(select, i, new Vector2((i - 1) * 540f, -130f), out spots[i]);

        // ---------------------------------------------------------------- opening
        RectTransform opening = MenuStyle.Stretch(panel, "OpeningPage");
        Image dim = MenuStyle.Panel(opening, "Dim", Vector2.zero, Vector2.one, MenuStyle.Middle, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0f));
        TMP_Text openingTitle = MenuStyle.Label(opening, "CrateName", "RIPE CRATE", true, 72f, MenuStyle.Banana, TextAlignmentOptions.Top,
                                                MenuStyle.TopMiddle, new Vector2(0f, -60f), new Vector2(1200f, 90f));

        // The reel.
        RectTransform reel = MenuStyle.Rect(opening, "Reel", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, new Vector2(0f, -20f), new Vector2(1640f, 300f));
        MenuStyle.Panel(reel, "Backing", Vector2.zero, Vector2.one, MenuStyle.Middle, Vector2.zero, Vector2.zero, new Color(0.02f, 0.025f, 0.03f, 0.92f));
        MenuStyle.Panel(reel, "EdgeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), MenuStyle.TopMiddle, Vector2.zero, new Vector2(0f, 3f), MenuStyle.Rule);
        MenuStyle.Panel(reel, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), MenuStyle.BottomMiddle, Vector2.zero, new Vector2(0f, 3f), MenuStyle.Rule);
        RectTransform viewport = MenuStyle.Rect(reel, "Viewport", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, Vector2.zero, new Vector2(1600f, 270f));
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform strip = MenuStyle.Rect(viewport, "Strip", MenuStyle.MiddleLeft, MenuStyle.MiddleLeft, MenuStyle.MiddleLeft, Vector2.zero, new Vector2(1f, 240f));
        FinishCard reelCard = InventoryBuilder.BuildCard(strip, "ReelCard", new Vector2(200f, 240f));
        RectTransform reelCardRect = (RectTransform)reelCard.transform;
        reelCardRect.anchorMin = reelCardRect.anchorMax = MenuStyle.MiddleLeft;
        reelCardRect.pivot = MenuStyle.Middle;
        Object.DestroyImmediate(reelCard.GetComponent<HoverLift>());
        Object.DestroyImmediate(reelCard.GetComponent<PopIn>());
        reelCard.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);   // no selection frame on the reel
        Image pointer = MenuStyle.Panel(reel, "Pointer", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, Vector2.zero, new Vector2(6f, 320f), MenuStyle.Banana);
        MenuStyle.Panel(pointer.rectTransform, "Top", MenuStyle.TopMiddle, MenuStyle.TopMiddle, MenuStyle.BottomMiddle, Vector2.zero, new Vector2(34f, 14f), MenuStyle.Banana);
        MenuStyle.Panel(pointer.rectTransform, "Bottom", MenuStyle.BottomMiddle, MenuStyle.BottomMiddle, MenuStyle.TopMiddle, Vector2.zero, new Vector2(34f, 14f), MenuStyle.Banana);

        TMP_Text skip = MenuStyle.Label(opening, "SkipHint", "CLICK TO SKIP", false, 28f, MenuStyle.Muted, TextAlignmentOptions.Bottom,
                                        MenuStyle.BottomMiddle, new Vector2(0f, 110f), new Vector2(600f, 40f));
        skip.characterSpacing = 6f;

        // The reveal.
        RectTransform reveal = MenuStyle.Stretch(opening, "Reveal");
        Image raysA = RayImage(reveal, "RaysA", 1500f);
        Image raysB = RayImage(reveal, "RaysB", 1100f);
        Image frame = MenuStyle.Panel(reveal, "WeaponFrame", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, new Vector2(0f, 172f), new Vector2(748f, 584f), Color.white);
        RectTransform revealRect = MenuStyle.Rect(reveal, "Weapon", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, new Vector2(0f, 172f), new Vector2(740f, 576f));
        RawImage revealView = revealRect.gameObject.AddComponent<RawImage>();
        revealView.raycastTarget = false;

        TMP_Text badge = MenuStyle.Label(reveal, "New", "NEW!", true, 64f, MenuStyle.Banana, TextAlignmentOptions.Center,
                                         MenuStyle.Middle, new Vector2(330f, 420f), new Vector2(260f, 90f));
        TMP_Text rarity = MenuStyle.Label(reveal, "Rarity", "APEX", false, 40f, Color.white, TextAlignmentOptions.Center,
                                          MenuStyle.Middle, new Vector2(0f, -145f), new Vector2(800f, 50f));
        rarity.characterSpacing = 10f;
        Image rarityBar = MenuStyle.Panel(reveal, "RarityBar", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, new Vector2(0f, -176f), new Vector2(260f, 6f), Color.white);
        TMP_Text name = MenuStyle.Label(reveal, "Name", "PRISMATIC", true, 104f, Color.white, TextAlignmentOptions.Center,
                                        MenuStyle.Middle, new Vector2(0f, -240f), new Vector2(1600f, 120f));
        TMP_Text flavour = MenuStyle.Label(reveal, "Flavour", "", false, 32f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Center,
                                           MenuStyle.Middle, new Vector2(0f, -306f), new Vector2(1400f, 44f));
        TMP_Text count = MenuStyle.Label(reveal, "Count", "", false, 28f, MenuStyle.Banana, TextAlignmentOptions.Center,
                                         MenuStyle.Middle, new Vector2(0f, -346f), new Vector2(1000f, 40f));
        count.characterSpacing = 4f;
        TMP_Text collection = MenuStyle.Label(reveal, "Collection", "", false, 28f, MenuStyle.Muted, TextAlignmentOptions.TopLeft,
                                              MenuStyle.TopLeft, new Vector2(Margin, -60f), new Vector2(600f, 40f));
        collection.characterSpacing = 4f;

        RectTransform actionsRect = MenuStyle.Rect(reveal, "Actions", MenuStyle.BottomMiddle, MenuStyle.BottomMiddle, MenuStyle.BottomMiddle,
                                                   new Vector2(0f, 60f), new Vector2(1240f, 76f));
        CanvasGroup actions = actionsRect.gameObject.AddComponent<CanvasGroup>();
        Button equipAll = MenuStyle.PrimaryButton(actionsRect, "EquipAll", "EQUIP ON ALL", MenuStyle.MiddleLeft, new Vector2(0f, 0f), new Vector2(300f, 72f));
        Button toInventory = MenuStyle.GhostButton(actionsRect, "ToInventory", "INVENTORY", MenuStyle.MiddleLeft, new Vector2(316f, 0f), new Vector2(250f, 72f));
        Button scrap = MenuStyle.GhostButton(actionsRect, "Scrap", "SCRAP", MenuStyle.MiddleLeft, new Vector2(582f, 0f), new Vector2(250f, 72f), new Color(1f, 0.1f, 0.25f, 0.6f));
        Button another = MenuStyle.GhostButton(actionsRect, "OpenAnother", "OPEN ANOTHER", MenuStyle.MiddleLeft, new Vector2(848f, 0f), new Vector2(392f, 72f), new Color(1f, 0.82f, 0.12f, 0.6f));
        Button back = MenuStyle.TextButton(reveal, "Back", "BACK", 52f, MenuStyle.BottomLeft, new Vector2(Margin + 20f, 60f), new Vector2(300f, 70f));

        Image flash = MenuStyle.Panel(panel, "Flash", Vector2.zero, Vector2.one, MenuStyle.Middle, Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0f));

        // ---------------------------------------------------------------- wiring
        MenuStyle.Wire(screen, "panel", panel.gameObject);
        MenuStyle.Wire(screen, "fade", fade);
        MenuStyle.Wire(screen, "stageView", stageView);
        MenuStyle.Wire(screen, "tokens", counter);
        MenuStyle.Wire(screen, "selectPage", select.gameObject);
        MenuStyle.Wire(screen, "cards", cards);
        MenuStyle.Wire(screen, "chestSpots", spots);
        MenuStyle.Wire(screen, "closeButton", close);
        MenuStyle.Wire(screen, "inventoryButton", inventory);

        GameObject[] chests = new GameObject[3];
        WeaponFinish[] looks = new WeaponFinish[3];
        for (int i = 0; i < 3; i++)
        {
            chests[i] = Load<GameObject>($"Assets/Art/Crates/{Chests[i]}/chest.fbx");
            looks[i] = Load<WeaponFinish>($"{FinishLibraryBuilder.LooksFolder}/{Looks[i]}.asset");
        }
        MenuStyle.Wire(screen, "chestModels", chests);
        MenuStyle.Wire(screen, "chestLooks", looks);
        MenuStyle.Wire(screen, "coinModel", Load<GameObject>("Assets/Art/Crates/PlatformerChest/coin-gold.fbx"));
        MenuStyle.Wire(screen, "glowSprite", Load<Texture2D>($"{Sprites}/light_01.png"));
        MenuStyle.Wire(screen, "starSprite", Load<Texture2D>($"{Sprites}/star_06.png"));
        MenuStyle.Wire(screen, "sparkSprite", Load<Texture2D>($"{Sprites}/star_04.png"));
        MenuStyle.Wire(screen, "smokeSprite", Load<Texture2D>($"{Sprites}/smoke_05.png"));
        MenuStyle.Wire(screen, "confettiSprite", Load<Texture2D>($"{Sprites}/star_07.png"));
        MenuStyle.Wire(screen, "raySprite", Load<Texture2D>($"{Sprites}/star_08.png"));

        MenuStyle.Wire(screen, "openingPage", opening.gameObject);
        MenuStyle.Wire(screen, "openingTitle", openingTitle);
        MenuStyle.Wire(screen, "flash", flash);
        MenuStyle.Wire(screen, "dim", dim);
        MenuStyle.Wire(screen, "skipHint", skip);

        MenuStyle.Wire(screen, "reel", reel);
        MenuStyle.Wire(screen, "reelViewport", viewport);
        MenuStyle.Wire(screen, "reelStrip", strip);
        MenuStyle.Wire(screen, "reelCardTemplate", reelCard);
        MenuStyle.Wire(screen, "pointer", pointer);

        MenuStyle.Wire(screen, "revealGroup", reveal.gameObject);
        MenuStyle.Wire(screen, "revealView", revealView);
        MenuStyle.Wire(screen, "revealFrame", frame);
        MenuStyle.Wire(screen, "raysA", raysA);
        MenuStyle.Wire(screen, "raysB", raysB);
        MenuStyle.Wire(screen, "revealName", name);
        MenuStyle.Wire(screen, "revealRarity", rarity);
        MenuStyle.Wire(screen, "revealRarityBar", rarityBar);
        MenuStyle.Wire(screen, "revealFlavour", flavour);
        MenuStyle.Wire(screen, "newBadge", badge);
        MenuStyle.Wire(screen, "countText", count);
        MenuStyle.Wire(screen, "collectionText", collection);
        MenuStyle.Wire(screen, "actions", actions);
        MenuStyle.Wire(screen, "equipAllButton", equipAll);
        MenuStyle.Wire(screen, "equipAllLabel", equipAll.GetComponentInChildren<TMP_Text>());
        MenuStyle.Wire(screen, "inventoryFromResultButton", toInventory);
        MenuStyle.Wire(screen, "scrapButton", scrap);
        MenuStyle.Wire(screen, "scrapLabel", scrap.GetComponentInChildren<TMP_Text>());
        MenuStyle.Wire(screen, "openAnotherButton", another);
        MenuStyle.Wire(screen, "openAnotherLabel", another.GetComponentInChildren<TMP_Text>());
        MenuStyle.Wire(screen, "backButton", back);

        opening.gameObject.SetActive(false);
        reveal.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);

        if (System.IO.File.Exists(Path))
            AssetDatabase.DeleteAsset(Path);
        InventoryBuilder.Save(root, Path);

        Debug.Log($"[crates] built {Path} - RoomManager instantiates it; a button anywhere calls CrateOpeningScreen.Instance.Open()");
        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// A crate's card: its colour along the top, name, price, line, a bar per rarity with its real
    /// odds, and OPEN. `spot` comes back as the point over the card where its chest stands.
    /// </summary>
    static CrateCard BuildCrateCard(Transform page, int index, Vector2 centre, out RectTransform spot)
    {
        CrateInfo crate = CrateInfo.All[index];
        Vector2 size = new Vector2(470f, 520f);

        RectTransform rect = MenuStyle.Rect(page, $"{crate.Key}Card", MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, centre, size);
        Image face = rect.gameObject.AddComponent<Image>();
        face.color = MenuStyle.Card;
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        rect.gameObject.AddComponent<PopIn>().delay = 0.08f * index;

        Image accent = MenuStyle.Panel(rect, "Accent", new Vector2(0f, 1f), new Vector2(1f, 1f), MenuStyle.TopMiddle, Vector2.zero, new Vector2(0f, 8f), crate.Colour);

        TMP_Text name = MenuStyle.Label(rect, "Name", crate.Name.ToUpperInvariant(), true, 52f, Color.white, TextAlignmentOptions.Top,
                                        MenuStyle.TopMiddle, new Vector2(0f, -26f), new Vector2(460f, 64f));
        TMP_Text price = MenuStyle.Label(rect, "Price", $"{crate.Cost} TOKENS", false, 40f, MenuStyle.Banana, TextAlignmentOptions.Top,
                                         MenuStyle.TopMiddle, new Vector2(0f, -92f), new Vector2(460f, 48f));
        TMP_Text tagline = MenuStyle.Label(rect, "Tagline", crate.Tagline, false, 28f, MenuStyle.Muted, TextAlignmentOptions.Top,
                                           MenuStyle.TopMiddle, new Vector2(0f, -140f), new Vector2(460f, 36f));

        Image[] fills = new Image[5];
        TMP_Text[] labels = new TMP_Text[5];
        for (int i = 0; i < 5; i++)
        {
            float y = -196f - i * 40f;
            labels[i] = MenuStyle.Label(rect, $"Odds{i}", "RARITY 0%", false, 24f, Color.white, TextAlignmentOptions.MidlineLeft,
                                        MenuStyle.TopLeft, new Vector2(32f, y), new Vector2(210f, 30f));
            MenuStyle.Panel(rect, $"OddsTrack{i}", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft, new Vector2(240f, y - 9f), new Vector2(208f, 12f), MenuStyle.Rule);
            Image fill = MenuStyle.Panel(rect, $"OddsFill{i}", MenuStyle.TopLeft, MenuStyle.TopLeft, MenuStyle.TopLeft, new Vector2(240f, y - 9f), new Vector2(208f, 12f), Color.white);
            fills[i] = fill;
        }

        TMP_Text need = MenuStyle.Label(rect, "Need", "NEED 0 MORE", false, 28f, MenuStyle.Danger, TextAlignmentOptions.Bottom,
                                        MenuStyle.BottomMiddle, new Vector2(0f, 112f), new Vector2(460f, 36f));

        Button open = MenuStyle.PrimaryButton(rect, "Open", "OPEN", MenuStyle.BottomMiddle, new Vector2(0f, 26f), new Vector2(400f, 76f));

        spot = MenuStyle.Rect(rect, "ChestSpot", MenuStyle.TopMiddle, MenuStyle.TopMiddle, MenuStyle.Middle, new Vector2(0f, 20f), new Vector2(10f, 10f));

        CrateCard card = rect.gameObject.AddComponent<CrateCard>();
        MenuStyle.Wire(card, "openButton", open);
        MenuStyle.Wire(card, "nameText", name);
        MenuStyle.Wire(card, "priceText", price);
        MenuStyle.Wire(card, "taglineText", tagline);
        MenuStyle.Wire(card, "needText", need);
        MenuStyle.Wire(card, "accent", accent);
        MenuStyle.Wire(card, "oddsFills", fills);
        MenuStyle.Wire(card, "oddsLabels", labels);
        MenuStyle.Wire(card, "group", group);
        return card;
    }

    /// Rays behind the reveal - the particle pack's star, huge and faint, turned by the screen.
    static Image RayImage(Transform parent, string name, float size)
    {
        Image rays = MenuStyle.Panel(parent, name, MenuStyle.Middle, MenuStyle.Middle, MenuStyle.Middle, new Vector2(0f, 150f), new Vector2(size, size), Color.white);
        rays.sprite = MenuStyle.Sprite($"{Sprites}/star_08.png");
        rays.preserveAspect = true;
        return rays;
    }

    static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            Debug.LogError($"[crates] nothing at {path}");
        return asset;
    }
}
