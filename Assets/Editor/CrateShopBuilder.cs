using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the crate shop as a prefab, once - same reasoning SettingsMenuBuilder already
/// established: this has to be reachable from wherever a "open crates" button ends up living,
/// and building it as scene content would tie it to one specific scene for no reason. Drag the
/// result into whatever scene needs it, wire a button to SetActive(true) on it.
///
/// Re-runnable and idempotent in the same shape every other builder in this project uses -
/// deletes and rebuilds the prefab from scratch. Nothing here has been hand-edited yet the way
/// the in-match HUD has, so a destructive rebuild is still safe - if that changes, this needs the
/// same Run()/Repair() split HudBuilder.cs uses.
/// </summary>
public static class CrateShopBuilder
{
    const string Folder = "Assets/Resources";
    const string Path = Folder + "/CrateShop.prefab";
    const string BananaTexturePath = "Assets/Textures/UI/BananaHealth.png";

    static readonly Color Ink = new Color(0.95f, 0.95f, 0.92f);
    static readonly Color Dim = new Color(0.95f, 0.95f, 0.92f, 0.6f);
    // Fully opaque, not 0.95 - a real screenshot showed the game world clearly through what was
    // meant to read as a solid takeover, well past what 5% see-through should produce. Rather
    // than chase why (no debugger on a batch-mode render), a modal shop screen doesn't actually
    // need to show the game behind it at all, so opaque sidesteps the question entirely.
    static readonly Color Backdrop = new Color(0.03f, 0.03f, 0.04f, 1f);
    static readonly Color PanelFace = new Color(0.1f, 0.1f, 0.12f, 1f);
    static readonly Color CardFace = new Color(0.16f, 0.16f, 0.19f, 1f);

    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

    [MenuItem("Tools/Gorilla Warfare/Build the crate shop")]
    public static void Run()
    {
        TMP_FontAsset font = FindFont();

        if (!Directory.Exists(Folder))
            Directory.CreateDirectory(Folder);

        GameObject root = new GameObject("CrateShop",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 550; // above the settings menu (500), below nothing

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CrateOpeningScreen screen = root.AddComponent<CrateOpeningScreen>();
        SerializedObject so = new SerializedObject(screen);

        // Everything actually visible lives under this, not directly under root - root has to
        // stay active permanently so Awake (and Instance) actually fires; Unity never calls
        // Awake on a GameObject instantiated already-inactive, which an earlier version of this
        // learned the hard way (every button click logged "RoomManager never instantiated the
        // screen" despite it having done exactly that - Instance was simply never set). Starts
        // hidden; Open()/Close() toggle this, matching SettingsMenu's own `panel` shape exactly.
        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        Stretch((RectTransform)panel.transform);
        panel.SetActive(false);
        Wire(so, "panel", panel);

        Panel(panel.transform, "Backdrop", Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, Backdrop);

        // ---------------------------------------------------------------- select page
        GameObject selectPage = new GameObject("SelectPage", typeof(RectTransform));
        selectPage.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)selectPage.transform);

        TMP_Text title = Text(selectPage.transform, "Title", font, 72f, TextAlignmentOptions.Center,
                              TopCenter, new Vector2(0f, -90f), new Vector2(1200f, 100f), Ink);
        title.text = "OPEN A CRATE";

        TMP_Text balance = Text(selectPage.transform, "Balance", font, 40f, TextAlignmentOptions.Center,
                                TopCenter, new Vector2(0f, -170f), new Vector2(800f, 60f),
                                new Color(1f, 0.86f, 0.2f));
        balance.text = "0 TOKENS";
        Wire(so, "tokenBalanceText", balance);

        GameObject rotten = BuildCrateButton(selectPage.transform, "RottenCrateButton", -520f, font,
                                             new Color(0.55f, 0.42f, 0.22f));
        GameObject ripe = BuildCrateButton(selectPage.transform, "RipeCrateButton", 0f, font,
                                           new Color(0.96f, 0.82f, 0.16f));
        GameObject holy = BuildCrateButton(selectPage.transform, "HolyCrateButton", 520f, font,
                                           new Color(0.7f, 0.9f, 1f));

        Wire(so, "rottenButton", rotten.GetComponent<CrateButton>());
        Wire(so, "ripeButton", ripe.GetComponent<CrateButton>());
        Wire(so, "holyButton", holy.GetComponent<CrateButton>());

        GameObject closeGo = BuildTextButton(selectPage.transform, "CloseButton", "CLOSE", font,
                                             new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                                             new Vector2(300f, 70f));
        Wire(so, "closeButton", closeGo.GetComponent<Button>());

        Wire(so, "selectPage", selectPage);

        // ---------------------------------------------------------------- opening page
        GameObject openingPage = new GameObject("OpeningPage", typeof(RectTransform));
        openingPage.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)openingPage.transform);
        openingPage.SetActive(false);

        // The carousel: a masked viewport with a wide strip inside it that slides left. Cards
        // are cloned from cardTemplate at runtime (CrateOpeningScreen.BuildCarousel) - nothing
        // here is hand-placed per card.
        GameObject viewportGo = Panel(openingPage.transform, "CarouselViewport", Center, Center, Center,
                                      Vector2.zero, new Vector2(1400f, 260f), new Color(0f, 0f, 0f, 0.4f));
        viewportGo.AddComponent<RectMask2D>();
        RectTransform viewport = (RectTransform)viewportGo.transform;
        Wire(so, "carouselViewport", viewport);

        GameObject stripGo = new GameObject("CarouselStrip", typeof(RectTransform));
        stripGo.transform.SetParent(viewportGo.transform, false);
        RectTransform strip = (RectTransform)stripGo.transform;
        strip.anchorMin = strip.anchorMax = new Vector2(0f, 0.5f);
        strip.pivot = new Vector2(0f, 0.5f);
        strip.anchoredPosition = Vector2.zero;
        strip.sizeDelta = new Vector2(1f, 260f); // width is meaningless - children position themselves
        Wire(so, "carouselStrip", strip);

        RectTransform card = BuildCard(stripGo.transform, "CardTemplate", font);
        card.gameObject.SetActive(false);
        Wire(so, "cardTemplate", card);

        // A fixed line at the viewport's own centre, on top of the strip - where the result
        // actually lands, same convention every real case-opening reel uses.
        GameObject pointer = Panel(viewportGo.transform, "LandingPointer", Center, Center, Center,
                                   Vector2.zero, new Vector2(6f, 280f), new Color(1f, 1f, 1f, 0.9f));
        Wire(so, "landingPointer", (RectTransform)pointer.transform);

        // ---------------------------------------------------------------- result panel
        GameObject resultPanel = new GameObject("ResultPanel", typeof(RectTransform));
        resultPanel.transform.SetParent(openingPage.transform, false);
        Stretch((RectTransform)resultPanel.transform);
        resultPanel.SetActive(false);

        Image glow = Image(resultPanel.transform, "Glow", Vector2.zero, Vector2.one, Center,
                           Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.3f));
        Wire(so, "resultGlow", glow);

        Image resultCard = Image(resultPanel.transform, "ResultCard", Center, Center, Center,
                                 new Vector2(0f, 60f), new Vector2(260f, 340f), Color.white);
        resultCard.sprite = BananaSprite();
        Wire(so, "resultCardImage", resultCard);

        TMP_Text resultText = Text(resultPanel.transform, "ResultRarity", font, 84f, TextAlignmentOptions.Center,
                                   Center, new Vector2(0f, -140f), new Vector2(900f, 100f), Ink);
        resultText.text = "APEX";
        Wire(so, "resultRarityText", resultText);

        GameObject openAnother = BuildTextButton(resultPanel.transform, "OpenAnotherButton", "OPEN ANOTHER",
                                                 font, new Vector2(0.5f, 0f), new Vector2(-180f, 90f),
                                                 new Vector2(340f, 70f));
        Wire(so, "openAnotherButton", openAnother.GetComponent<Button>());

        GameObject closeFromResult = BuildTextButton(resultPanel.transform, "CloseFromResultButton", "CLOSE",
                                                     font, new Vector2(0.5f, 0f), new Vector2(180f, 90f),
                                                     new Vector2(340f, 70f));
        // Reusing the same closeButton field the select page's own close button is wired to
        // isn't possible (one field, one target) - this calls screen.Close directly instead,
        // same effect (toggles `panel`, not root - see CrateOpeningScreen.Close's own comment).
        closeFromResult.GetComponent<Button>().onClick.AddListener(screen.Close);

        Wire(so, "resultPanel", resultPanel);

        Wire(so, "openingPage", openingPage);

        so.ApplyModifiedPropertiesWithoutUndo();

        if (File.Exists(Path))
            AssetDatabase.DeleteAsset(Path);

        PrefabUtility.SaveAsPrefabAsset(root, Path);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();

        Debug.Log($"[crates] built at {Path} - RoomManager instantiates it automatically, "
                  + "a button anywhere just calls CrateOpeningScreen.Instance.Open()");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static GameObject BuildCrateButton(Transform parent, string name, float x, TMP_FontAsset font, Color accent)
    {
        GameObject go = Panel(parent, name, Center, Center, Center,
                              new Vector2(x, -20f), new Vector2(440f, 560f), CardFace);

        CanvasGroup group = go.AddComponent<CanvasGroup>();
        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();

        Image accentStripe = Image(go.transform, "Accent", new Vector2(0f, 1f), new Vector2(1f, 1f),
                                   new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 14f), accent);

        Image icon = Image(go.transform, "Icon", Center, Center, Center,
                           new Vector2(0f, 60f), new Vector2(220f, 260f), Color.white);
        icon.color = accent;
        icon.sprite = BananaSprite();

        TMP_Text name1 = Text(go.transform, "Name", font, 40f, TextAlignmentOptions.Center,
                              Center, new Vector2(0f, -140f), new Vector2(400f, 60f), Ink);

        TMP_Text cost = Text(go.transform, "Cost", font, 32f, TextAlignmentOptions.Center,
                             Center, new Vector2(0f, -195f), new Vector2(400f, 50f),
                             new Color(1f, 0.86f, 0.2f));

        CrateButton crateButton = go.AddComponent<CrateButton>();
        SerializedObject cbSo = new SerializedObject(crateButton);
        Wire(cbSo, "button", button);
        Wire(cbSo, "nameText", name1);
        Wire(cbSo, "costText", cost);
        Wire(cbSo, "group", group);
        cbSo.ApplyModifiedPropertiesWithoutUndo();

        return go;
    }

    static RectTransform BuildCard(Transform parent, string name, TMP_FontAsset font)
    {
        GameObject go = Panel(parent, name, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                              new Vector2(0f, 0.5f), Vector2.zero, new Vector2(190f, 220f), CardFace);

        Image icon = Image(go.transform, "Icon", Center, Center, Center,
                           new Vector2(0f, 20f), new Vector2(120f, 140f), Color.white);
        icon.sprite = BananaSprite();

        Text(go.transform, "Label", font, 24f, TextAlignmentOptions.Center,
            Center, new Vector2(0f, -80f), new Vector2(180f, 40f), Ink);

        return (RectTransform)go.transform;
    }

    static GameObject BuildTextButton(Transform parent, string name, string label, TMP_FontAsset font,
                                      Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject go = Panel(parent, name, anchor, anchor, anchor, position, size, PanelFace);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();

        // The bug that shipped first: `label` sat right there in the parameter list and never
        // actually reached the Text() call below it, which only ever received the GameObject's
        // own internal name ("Label") - so every button this built rendered as an empty dark
        // rectangle. Confirmed on a real screenshot (both crate-shop-check.png and
        // crate-reveal-check.png showed it) before being caught here.
        TMP_Text labelText = Text(go.transform, "Label", font, 32f, TextAlignmentOptions.Center,
                                  Center, Vector2.zero, size, Ink);
        labelText.text = label;

        return go;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static GameObject Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                            Vector2 pivot, Vector2 position, Vector2 size, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.AddComponent<Image>();
        image.color = colour;

        return go;
    }

    static Image Image(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                       Vector2 pivot, Vector2 position, Vector2 size, Color colour)
    {
        GameObject go = Panel(parent, name, anchorMin, anchorMax, pivot, position, size, colour);
        return go.GetComponent<Image>();
    }

    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float size,
                         TextAlignmentOptions alignment, Vector2 anchor, Vector2 position,
                         Vector2 dimensions, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = colour;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;

        if (font != null)
            text.font = font;

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        return text;
    }

    static void Wire(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);

        if (property == null)
        {
            Debug.LogError($"[crates] {so.targetObject.GetType().Name} has no field called '{field}'");
            return;
        }

        property.objectReferenceValue = value;
    }

    static Sprite bananaSprite;

    static Sprite BananaSprite()
    {
        if (bananaSprite != null)
            return bananaSprite;

        bananaSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BananaTexturePath);

        if (bananaSprite == null)
            Debug.LogError($"[crates] no banana sprite at {BananaTexturePath}");

        return bananaSprite;
    }

    static TMP_FontAsset FindFont()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset Jersey10");

        if (guids.Length == 0)
            guids = AssetDatabase.FindAssets("t:TMP_FontAsset");

        if (guids.Length == 0)
            return null;

        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
