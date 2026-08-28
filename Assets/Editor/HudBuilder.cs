using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the in-game HUD into the game scene as real objects, once.
//
// Same deal as ModeSelectorBuilder: this is a one-shot that creates the hierarchy, wires the
// references and saves the scene. After that the scene owns it - move it, recolour it, change
// the font, delete the bits you don't want. Running it again replaces what it made and nothing
// else, so a botched experiment can be undone without hand-editing scene YAML.
//
// The numbers below are a starting layout, not a design. Everything here was previously a
// constant inside an OnGUI call; the point of the exercise is that it's now scene data.
public static class HudBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";
    const string RootName = "GameHud";

    // 1080 tall, matched on height. A HUD anchored to the corners of a 16:9 reference would
    // creep inwards on an ultrawide; matching height keeps everything the same physical size
    // and just gives you more world between the corners.
    static readonly Vector2 Reference = new Vector2(1920f, 1080f);

    // Ladder pips are a fixed set that gets shown or hidden. Eight is well past killsPerRung,
    // which is two, and costs nothing to carry.
    const int PipCount = 8;

    [MenuItem("Tools/Gorilla Warfare/Build the in-game HUD")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != RootName)
                continue;

            Debug.Log("[hud] replacing the previous HUD");
            Object.DestroyImmediate(root);
            break;
        }

        TMP_FontAsset font = FindFont();

        // ---------------------------------------------------------------- canvas
        GameObject rootObject = new GameObject(RootName,
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        Canvas canvas = rootObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Below the Tab scoreboard, which is its own canvas at zero. Holding the scoreboard
        // should cover the HUD rather than fight it for the same pixels.
        canvas.sortingOrder = -1;

        CanvasScaler scaler = rootObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = Reference;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        // No GraphicRaycaster on purpose. Nothing here is clickable, and a raycaster would sit
        // between the player and the game swallowing input.

        GameHud hud = rootObject.AddComponent<GameHud>();

        // ---------------------------------------------------------------- scope
        // First child, so everything else draws on top of it. Sniper aiming blacks out the
        // screen; the ammo count and the clock should survive that.
        GameObject scope = Panel(rootObject.transform, "Scope", Vector2.zero, Vector2.one,
                                 new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null);
        scope.SetActive(false);

        Image glass = Image(scope.transform, "Glass", Center, Center, Center,
                            Vector2.zero, new Vector2(1080f, 1080f), Color.white);

        // Sides. The circle is as tall as the window, so on anything wider than square there is
        // screen left over on either side that has to be blacked out too.
        RectTransform scopeLeft = Image(scope.transform, "PanelLeft",
                                        new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                                        Vector2.zero, new Vector2(420f, 0f), Color.black).rectTransform;

        RectTransform scopeRight = Image(scope.transform, "PanelRight",
                                         new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                                         Vector2.zero, new Vector2(420f, 0f), Color.black).rectTransform;

        // Hairlines, with a gap in the middle so they don't cover what you're shooting at.
        Color hair = new Color(0f, 0f, 0f, 0.85f);
        Image(scope.transform, "HairLeft", Center, Center, new Vector2(1f, 0.5f),
              new Vector2(-12f, 0f), new Vector2(2000f, 1.5f), hair);
        Image(scope.transform, "HairRight", Center, Center, new Vector2(0f, 0.5f),
              new Vector2(12f, 0f), new Vector2(2000f, 1.5f), hair);
        Image(scope.transform, "HairUp", Center, Center, new Vector2(0.5f, 0f),
              new Vector2(0f, 12f), new Vector2(1.5f, 2000f), hair);
        Image(scope.transform, "HairDown", Center, Center, new Vector2(0.5f, 1f),
              new Vector2(0f, -12f), new Vector2(1.5f, 2000f), hair);

        // ---------------------------------------------------------------- crosshair
        GameObject crosshair = Panel(rootObject.transform, "Crosshair", Center, Center, Center,
                                     Vector2.zero, Vector2.zero, null);

        Image up = Tick(crosshair.transform, "Up", new Vector2(3f, 12f), new Vector2(0f, 14f));
        Image down = Tick(crosshair.transform, "Down", new Vector2(3f, 12f), new Vector2(0f, -14f));
        Image leftTick = Tick(crosshair.transform, "Left", new Vector2(12f, 3f), new Vector2(-14f, 0f));
        Image rightTick = Tick(crosshair.transform, "Right", new Vector2(12f, 3f), new Vector2(14f, 0f));

        // A diamond rather than a dot. Turned forty five degrees it reads as a mark that
        // arrived rather than as part of the crosshair that changed colour.
        // Off by default; the settings screen turns it on. Authored here so a freshly built
        // HUD has one, and GameHud makes its own if it finds the slot empty - which is what
        // happens to a HUD built before the dot existed.
        Image dot = Image(crosshair.transform, "Dot", Center, Center, Center,
                          Vector2.zero, new Vector2(3f, 3f), Color.white);
        dot.gameObject.SetActive(false);

        Image marker = Image(crosshair.transform, "HitMarker", Center, Center, Center,
                             Vector2.zero, new Vector2(18f, 18f), Color.white);
        marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        marker.gameObject.SetActive(false);

        // ---------------------------------------------------------------- health, bottom left
        GameObject health = Panel(rootObject.transform, "Health", BottomLeft, BottomLeft, BottomLeft,
                                  new Vector2(48f, 48f), Vector2.zero, null);

        // The bananameter - literally that, not a tinted rectangle. Direct correction after the
        // first attempt: "what you did is not what I meant by bananameter." A pixel-art banana
        // (`BananaSprite`, drawn procedurally the same way every other texture on this HUD
        // already is - no sourced or hand-painted asset) in three stacked layers: an always-
        // visible dim husk showing the full shape, a pale trail that lags behind on damage and
        // catches up (the "inertia" asked for), and the live reading on top, tinted through the
        // same ripeness palette the weapon's own magazine already uses.
        //
        // Leans INTO the corner rather than away from it - reported that the old rectangular
        // bar's lean pointed the wrong way. Negative rather than the positive angle that bar
        // used, and there's no separate border frame to carry as a rigid body any more: the
        // outline is baked into the sprite itself, so the container holding the three layers can
        // rotate directly.
        const float barLean = -8f;
        Vector2 meterSize = new Vector2(300f, 88f);

        RectTransform meter = Panel(health.transform, "BananaMeter", BottomLeft, BottomLeft, BottomLeft,
                                    Vector2.zero, meterSize, null).GetComponent<RectTransform>();
        meter.localRotation = Quaternion.Euler(0f, 0f, barLean);

        BananaLayer(meter, "Husk", new Color(0.22f, 0.15f, 0.08f, 0.85f), false);
        Image trail = BananaLayer(meter, "Trail", new Color(1f, 0.96f, 0.75f, 0.55f), true);
        Image fill = BananaLayer(meter, "Fill", new Color(0.6f, 0.92f, 0.14f, 1f), true);

        Image shield = Image(meter, "Shield", BottomLeft, BottomLeft, BottomLeft,
                             new Vector2(meterSize.x, 0f), new Vector2(0f, meterSize.y),
                             new Color(0.35f, 0.8f, 1f));
        shield.gameObject.SetActive(false);

        TMP_Text healthNumber = Text(health.transform, "Number", font, 76f,
                                     TextAlignmentOptions.BottomLeft, BottomLeft,
                                     new Vector2(0f, meterSize.y + 14f), new Vector2(300f, 90f));
        healthNumber.text = "140";
        healthNumber.characterSpacing = 3f;

        TMP_Text streak = Text(health.transform, "Streak", font, 22f,
                               TextAlignmentOptions.BottomLeft, BottomLeft,
                               new Vector2(0f, -30f), new Vector2(400f, 28f));
        streak.text = "3 IN A ROW";
        streak.color = new Color(1f, 0.55f, 0.1f);

        TMP_Text heal = Text(health.transform, "Heal", font, 34f,
                             TextAlignmentOptions.BottomLeft, BottomLeft,
                             new Vector2(310f, meterSize.y + 14f), new Vector2(200f, 50f));
        heal.text = "+35";

        // ---------------------------------------------------------------- ladder, above health
        GameObject ladder = Panel(rootObject.transform, "Ladder", BottomLeft, BottomLeft, BottomLeft,
                                  new Vector2(48f, 190f), Vector2.zero, null);

        TMP_Text ladderLabel = Text(ladder.transform, "Label", font, 22f,
                                    TextAlignmentOptions.BottomLeft, BottomLeft,
                                    new Vector2(0f, 26f), new Vector2(400f, 28f));
        ladderLabel.text = "RUNG 1 / 5";
        ladderLabel.color = new Color(1f, 1f, 1f, 0.55f);

        Image[] pips = new Image[PipCount];
        for (int i = 0; i < PipCount; i++)
        {
            pips[i] = Image(ladder.transform, $"Pip{i}", BottomLeft, BottomLeft, BottomLeft,
                            new Vector2(i * 22f, 0f), new Vector2(15f, 15f),
                            new Color(1f, 1f, 1f, 0.2f));
        }

        // ---------------------------------------------------------------- ammo, bottom right
        GameObject ammo = Panel(rootObject.transform, "Ammo", BottomRight, BottomRight, BottomRight,
                                new Vector2(-48f, 48f), Vector2.zero, null);

        // The magazine bar (mirroring health's, added the seventeenth pass) is gone - reported
        // directly as looking wrong, and a bare number reads faster mid-fight than a bar you'd
        // have to glance twice at anyway on a 5-30 round magazine. Positions below are back to
        // the pre-bar layout.
        //
        // A bit more presence than a flat 0.75 alpha - reported as wanting more depth and
        // visibility on the weapon name specifically. `nameOutline` is thicker than the HUD's
        // shared default (see `Text`'s own `outlineWidth` parameter), which is what actually
        // reads as "more depth" here - alpha alone was already close to opaque.
        const float nameOutline = 0.5f;

        TMP_Text weaponName = Text(ammo.transform, "Name", font, 30f,
                                   TextAlignmentOptions.BottomRight, BottomRight,
                                   new Vector2(0f, 150f), new Vector2(560f, 36f), nameOutline);
        weaponName.text = "RIFLE";
        weaponName.color = Color.white;

        // The round count is the number you actually read mid-fight, so it gets the size.
        TMP_Text ammoNumber = Text(ammo.transform, "Rounds", font, 120f,
                                   TextAlignmentOptions.BottomRight, BottomRight,
                                   new Vector2(-70f, 0f), new Vector2(400f, 140f));
        ammoNumber.text = "30";
        ammoNumber.characterSpacing = 3f;

        // Spares tucked under its right shoulder, bare - "5", not "x5".
        TMP_Text spare = Text(ammo.transform, "Spare", font, 42f,
                              TextAlignmentOptions.BottomRight, BottomRight,
                              new Vector2(0f, 14f), new Vector2(120f, 56f));
        spare.text = "5";
        spare.color = new Color(1f, 1f, 1f, 0.55f);

        // ---------------------------------------------------------------- clock, top centre
        GameObject top = Panel(rootObject.transform, "Match", TopCenter, TopCenter, TopCenter,
                               new Vector2(0f, -40f), Vector2.zero, null);

        TMP_Text clock = Text(top.transform, "Clock", font, 56f,
                              TextAlignmentOptions.Top, TopCenter,
                              Vector2.zero, new Vector2(400f, 68f));
        clock.text = "5:00";

        TMP_Text modeLabel = Text(top.transform, "Mode", font, 24f,
                                  TextAlignmentOptions.Top, TopCenter,
                                  new Vector2(0f, -62f), new Vector2(500f, 32f));
        modeLabel.text = "DEATHMATCH";
        modeLabel.color = new Color(1f, 1f, 1f, 0.55f);

        // ---------------------------------------------------------------- centre messages
        GameObject centre = Panel(rootObject.transform, "Centre", Center, Center, Center,
                                  Vector2.zero, Vector2.zero, null);

        // Full screen, first in the group so everything else in the middle draws over it. Only
        // up when the round is over.
        GameObject results = Panel(centre.transform, "ResultsBackdrop", Vector2.zero, Vector2.one,
                                   Center, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.72f));
        results.SetActive(false);

        // Big. This is the winner's name at the end of a match and the multikill callout during
        // one, and at 84 it read as a caption rather than as the game shouting at you.
        TMP_Text title = Text(centre.transform, "Title", font, 130f,
                              TextAlignmentOptions.Center, Center,
                              new Vector2(0f, 230f), new Vector2(1600f, 150f));
        title.text = "DOUBLE";
        title.color = new Color(1f, 0.1f, 0.58f);

        TMP_Text subtitle = Text(centre.transform, "Subtitle", font, 42f,
                                 TextAlignmentOptions.Center, Center,
                                 new Vector2(0f, 148f), new Vector2(1600f, 56f));
        subtitle.text = "3 IN A ROW";
        subtitle.color = new Color(1f, 1f, 1f, 0.55f);

        // Under the crosshair and centred, which is the only place on the screen you are
        // reliably looking. It used to sit off to the right at 46 point, on the theory that
        // directly under the crosshair would cover whoever you were shooting at - but well
        // below the aim point covers nothing, and a counter nobody notices is a counter that
        // may as well not exist.
        TMP_Text combo = Text(centre.transform, "Combo", font, 76f,
                              TextAlignmentOptions.Center, Center,
                              new Vector2(0f, -170f), new Vector2(400f, 96f));
        combo.text = "x4";
        combo.color = new Color(1f, 0.95f, 0.25f);

        // Standings, listed under the winner when the round is over.
        // Low enough to clear the subtitle above it. The first row starts at the top of this
        // box, so anchoring it any higher puts the standings through the winner's name.
        RectTransform standings = Column(centre.transform, "Standings", Center, Center,
                                         new Vector2(0f, -110f), new Vector2(700f, 400f),
                                         TextAnchor.UpperCenter);

        TMP_Text standingRow = Text(standings, "RowTemplate", font, 28f,
                                    TextAlignmentOptions.Center, Center,
                                    Vector2.zero, new Vector2(700f, 34f));
        standingRow.text = "someone   0 / 0";

        // ---------------------------------------------------------------- kill feed, top right
        // Below the clock rather than level with it. At 16:9 a 900 wide box anchored to the
        // top right reaches back to x 972 and the clock runs out to 1160, so they overlap.
        RectTransform feed = Column(rootObject.transform, "Feed", TopRight, TopRight,
                                    new Vector2(-48f, -150f), new Vector2(760f, 320f),
                                    TextAnchor.UpperRight);

        TMP_Text feedRow = Text(feed, "RowTemplate", font, 28f,
                                TextAlignmentOptions.Right, TopRight,
                                Vector2.zero, new Vector2(760f, 34f));
        feedRow.text = "someone got peeled by someone else";

        // ---------------------------------------------------------------- damage bearings
        // A ring of marks around the crosshair saying which way you are being shot from. Its own
        // group rather than living with the crosshair, because the radius is the thing you would
        // want to change and it should not drag the reticle with it.
        GameObject bearings = Panel(rootObject.transform, "DamageBearings", Center, Center, Center,
                                    Vector2.zero, Vector2.zero, null);

        // A tangential bar, not an arrow. At this size an arrowhead is four pixels of detail
        // nobody can read while being shot; a thick arc segment reads instantly.
        Image arrow = Image(bearings.transform, "Template", Center, Center, Center,
                            Vector2.zero, new Vector2(110f, 14f), new Color(1f, 0.1f, 0.25f));
        arrow.gameObject.SetActive(false);

        // ---------------------------------------------------------------- damage numbers
        // Full screen, because these are positioned by projecting a world point and can land
        // anywhere. Last child so they read over the top of everything else.
        GameObject damage = Panel(rootObject.transform, "DamageNumbers", Vector2.zero, Vector2.one,
                                  Center, Vector2.zero, Vector2.zero, null);

        TMP_Text damageRow = Text(damage.transform, "Template", font, 46f,
                                  TextAlignmentOptions.Center, Center,
                                  Vector2.zero, new Vector2(220f, 60f));
        damageRow.text = "24";

        // ---------------------------------------------------------------- wiring
        SerializedObject so = new SerializedObject(hud);

        Wire(so, "healthTrack", meter);
        Wire(so, "healthFill", fill);
        Wire(so, "healthTrail", trail);
        Wire(so, "healthShield", shield);
        Wire(so, "healthNumber", healthNumber);
        Wire(so, "streakText", streak);
        Wire(so, "healText", heal);

        Wire(so, "weaponName", weaponName);
        Wire(so, "ammoNumber", ammoNumber);
        Wire(so, "spareNumber", spare);

        Wire(so, "crosshairUp", up.rectTransform);
        Wire(so, "crosshairDown", down.rectTransform);
        Wire(so, "crosshairLeft", leftTick.rectTransform);
        Wire(so, "crosshairRight", rightTick.rectTransform);
        Wire(so, "hitMarker", marker);
        Wire(so, "crosshairDot", dot);

        Wire(so, "scope", scope);
        Wire(so, "scopeGlass", glass);
        Wire(so, "scopeLeft", scopeLeft);
        Wire(so, "scopeRight", scopeRight);

        Wire(so, "clock", clock);
        Wire(so, "modeLabel", modeLabel);
        Wire(so, "centreTitle", title);
        Wire(so, "centreSubtitle", subtitle);
        Wire(so, "comboText", combo);

        // The slide chain, roughly level with the hit combo but hugging the right edge of the
        // screen rather than the centre. Deliberately NOT a child of `centre` - that panel is
        // built with zero size (it only ever needed to anchor centred children at its own
        // origin), so a "right" anchor on a child of a zero-*width* parent collapses to the same
        // point a centred one would and this sat only 70 points left of true centre - invisible
        // as a bug until Jersey 10's wider glyphs on the kill/ladder title actually reached far
        // enough left to overlap it. Parented on the root canvas instead, where the anchor means
        // what it says.
        TMP_Text slideRank = Text(rootObject.transform, "SlideCombo", font, 54f,
                                  TextAlignmentOptions.Right, new Vector2(1f, 0.5f),
                                  new Vector2(-60f, -170f), new Vector2(620f, 80f));
        slideRank.text = "SLIDE";
        slideRank.gameObject.SetActive(false);
        Wire(so, "slideCombo", slideRank);

        // The rank text alone was just a word changing, four times - asked directly for an
        // ULTRAKILL/DMC style meter under it instead, the same idea as the style bar under
        // "CHAOTIC" or a stale-combo readout: a number draining, not only a name. Reads the
        // live chain window (`PlayerMovement.ChainWindowFraction`) rather than counting hits -
        // full the instant you land a slide, empty by the time the chain would expire, so the
        // bar itself is the "hurry up and slide again" cue the rank name alone never gave.
        // A child of `slideRank`'s own GameObject, not a sibling - that box has real width and
        // height (620x80), unlike `Centre`, so anchoring a child to its corner actually means
        // something, and it shows/hides for free with the text's own SetActive.
        GameObject slideMeterTrack = Panel(slideRank.transform, "MeterTrack",
                                           new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f),
                                           new Vector2(0f, -6f), new Vector2(300f, 12f),
                                           new Color(0f, 0f, 0f, 0.55f));

        Image slideMeterFill = Image(slideMeterTrack.transform, "Fill", Vector2.zero, Vector2.one, Center,
                                     Vector2.zero, Vector2.zero, new Color(1f, 0.15f, 0.6f, 0.95f));
        // Fully qualified: this class's own `Image(...)` helper shadows the `UnityEngine.UI.Image`
        // type name, so `Image.Type`/`Image.FillMethod` resolve to the method group instead of the
        // type without the namespace spelled out.
        slideMeterFill.type = UnityEngine.UI.Image.Type.Filled;
        slideMeterFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        slideMeterFill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Right;
        slideMeterFill.fillAmount = 1f;
        Wire(so, "slideMeterFill", slideMeterFill);
        Wire(so, "resultsBackdrop", results);

        // Full screen, behind the rest of the HUD. Red and pulsing when you are nearly dead.
        GameObject edge = Panel(rootObject.transform, "AdrenalineEdge", Vector2.zero, Vector2.one,
                                Center, Vector2.zero, Vector2.zero,
                                new Color(0.75f, 0.03f, 0.06f, 0f));
        edge.transform.SetAsFirstSibling();
        edge.SetActive(false);
        Wire(so, "adrenalineEdge", edge.GetComponent<Image>());

        Wire(so, "ladder", ladder);
        Wire(so, "ladderLabel", ladderLabel);

        SerializedProperty pipArray = so.FindProperty("ladderPips");
        pipArray.arraySize = PipCount;
        for (int i = 0; i < PipCount; i++)
            pipArray.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];

        Wire(so, "feedContainer", feed);
        Wire(so, "feedTemplate", feedRow);
        Wire(so, "standingsContainer", standings);
        Wire(so, "standingsTemplate", standingRow);
        Wire(so, "damageContainer", (RectTransform)damage.transform);
        Wire(so, "damageTemplate", damageRow);
        Wire(so, "arrowContainer", (RectTransform)bearings.transform);
        Wire(so, "arrowTemplate", arrow);

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[hud] built with font '{(font != null ? font.name : "none")}' - "
                  + "everything in it is a scene object now, move and restyle it from here");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// Fills in pieces a HUD built by an older version of this script doesn't have.
    ///
    /// Run is destructive by design - it replaces the whole GameHud root, which is right when
    /// you want to start over and wrong once anyone has moved anything. But the HUD keeps
    /// gaining parts, and telling Ryaan to throw away his layout every time one arrives is not
    /// a trade worth making. This adds what's missing and touches nothing else.
    /// </summary>
    [MenuItem("Tools/Gorilla Warfare/Repair the in-game HUD")]
    public static void Repair()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameHud hud = Object.FindFirstObjectByType<GameHud>();

        if (hud == null)
        {
            Debug.LogError("[hud] no GameHud in the scene - build it first");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        SerializedObject so = new SerializedObject(hud);
        int added = 0;

        // The centre dot, which arrived with the crosshair settings.
        SerializedProperty dotSlot = so.FindProperty("crosshairDot");

        if (dotSlot != null && dotSlot.objectReferenceValue == null)
        {
            SerializedProperty up = so.FindProperty("crosshairUp");
            RectTransform tick = up != null ? up.objectReferenceValue as RectTransform : null;

            if (tick != null && tick.parent != null)
            {
                Image dot = Image(tick.parent, "Dot", Center, Center, Center,
                                  Vector2.zero, new Vector2(3f, 3f), Color.white);
                dot.gameObject.SetActive(false);

                dotSlot.objectReferenceValue = dot;
                added++;

                Debug.Log("[hud] added the crosshair dot");
            }
        }

        // The results curtain, which arrived with the win screen rework.
        SerializedProperty curtain = so.FindProperty("resultsBackdrop");

        if (curtain != null && curtain.objectReferenceValue == null)
        {
            SerializedProperty titleSlot = so.FindProperty("centreTitle");
            TMP_Text title = titleSlot != null ? titleSlot.objectReferenceValue as TMP_Text : null;

            if (title != null && title.transform.parent != null)
            {
                GameObject made = Panel(title.transform.parent, "ResultsBackdrop",
                                        Vector2.zero, Vector2.one, Center, Vector2.zero,
                                        Vector2.zero, new Color(0f, 0f, 0f, 0.72f));

                // Behind its siblings, or it covers the winner's name it is meant to sit under.
                made.transform.SetAsFirstSibling();
                made.SetActive(false);

                curtain.objectReferenceValue = made;
                added++;

                Debug.Log("[hud] added the results backdrop");
            }
        }

        // Damage bearings, which arrived with the direction indicator.
        SerializedProperty arrowSlot = so.FindProperty("arrowContainer");

        if (arrowSlot != null && arrowSlot.objectReferenceValue == null)
        {
            GameHud existing = hud;

            GameObject group = new GameObject("DamageBearings", typeof(RectTransform));
            group.transform.SetParent(existing.transform, false);

            RectTransform groupRect = (RectTransform)group.transform;
            groupRect.anchorMin = groupRect.anchorMax = groupRect.pivot = Center;
            groupRect.anchoredPosition = Vector2.zero;
            groupRect.sizeDelta = Vector2.zero;

            Image made = Image(group.transform, "Template", Center, Center, Center,
                               Vector2.zero, new Vector2(110f, 14f), new Color(1f, 0.1f, 0.25f));
            made.gameObject.SetActive(false);

            arrowSlot.objectReferenceValue = groupRect;

            SerializedProperty templateSlot = so.FindProperty("arrowTemplate");
            if (templateSlot != null)
                templateSlot.objectReferenceValue = made;

            added++;
            Debug.Log("[hud] added the damage bearing ring");
        }

        // The adrenaline edge, which arrived with the near-death speed boost. GameHud builds
        // its own if the slot is empty, but SceneCheck wants every reference filled - and an
        // authored one can be restyled, which a runtime one cannot.
        SerializedProperty edgeSlot = so.FindProperty("adrenalineEdge");

        if (edgeSlot != null && edgeSlot.objectReferenceValue == null)
        {
            GameObject made = Panel(hud.transform, "AdrenalineEdge", Vector2.zero, Vector2.one,
                                    Center, Vector2.zero, Vector2.zero,
                                    new Color(0.75f, 0.03f, 0.06f, 0f));

            // Behind everything else in the HUD. It is a warning at the edge of vision, not a
            // thing to read.
            made.transform.SetAsFirstSibling();
            made.SetActive(false);

            Image image = made.GetComponent<Image>();
            image.raycastTarget = false;

            edgeSlot.objectReferenceValue = image;
            added++;

            Debug.Log("[hud] added the adrenaline edge");
        }

        // The slide rank, which arrived with the chain. Parented on the root canvas rather than
        // alongside `comboText` - that sibling's own parent (`Centre`) is built with zero size,
        // so a "right" anchor on a child of it collapses to the same point a centred one would.
        // See the matching fix in `Run()`.
        SerializedProperty comboSlot = so.FindProperty("slideCombo");

        if (comboSlot != null && comboSlot.objectReferenceValue == null)
        {
            SerializedProperty hitSlot = so.FindProperty("comboText");
            TMP_Text sibling = hitSlot != null ? hitSlot.objectReferenceValue as TMP_Text : null;

            if (sibling != null)
            {
                TMP_Text made = Text(hud.transform, "SlideCombo", sibling.font, 80f,
                                     TextAlignmentOptions.Right, new Vector2(1f, 0.5f),
                                     new Vector2(-60f, -170f), new Vector2(680f, 100f));
                made.text = "SLIDE";
                made.gameObject.SetActive(false);

                comboSlot.objectReferenceValue = made;
                added++;

                Debug.Log("[hud] added the slide rank");
            }
        }

        // Sizes and positions that changed after the HUD was first built. Applied by name so a
        // scene built by an older version catches up without being replaced - the alternative
        // is telling Ryaan to rebuild and lose whatever he has moved.
        added += Retune(so, "centreTitle", 130f, new Vector2(0f, 230f), new Vector2(1600f, 150f));
        added += Retune(so, "centreSubtitle", 42f, new Vector2(0f, 148f), new Vector2(1600f, 56f));
        added += Retune(so, "comboText", 76f, new Vector2(0f, -170f), new Vector2(400f, 96f));

        // Reported too small on 2026-08-17 and again on 2026-08-21. 80 matches and slightly
        // beats the kill-streak combo text (76, above) - the two are the same idea, a hot streak
        // told to you as text - and the wider box is so BANANAS!!! at the new size doesn't wrap.
        // Alignment passed explicitly: Retune defaults to Center, and this one was built Right
        // to hug the edge of its group rather than sit centred in its box like the other three.
        // Position moved 2026-08-29 along with a reparent onto the root canvas - see Repair's own
        // note above; an older scene's slideCombo is still a child of the zero-width Centre panel
        // and Retune only moves the RectTransform, so this alone won't fix that scene's parent,
        // only its position once it has (or gets) the right one.
        added += Retune(so, "slideCombo", 80f, new Vector2(-60f, -170f), new Vector2(680f, 100f),
                        TextAlignmentOptions.Right);

        if (added == 0)
        {
            Debug.Log("[hud] nothing missing");
        }
        else
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[hud] repaired {added} missing piece(s), everything else left alone");
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// Resizes and repositions one referenced label, if it isn't already where it should be.
    ///
    /// Returns whether anything changed, so a repair run that finds everything correct can say
    /// so rather than reporting work it didn't do.
    /// </summary>
    static int Retune(SerializedObject so, string field, float size, Vector2 position,
                      Vector2 dimensions, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        SerializedProperty slot = so.FindProperty(field);
        TMP_Text text = slot != null ? slot.objectReferenceValue as TMP_Text : null;

        if (text == null)
            return 0;

        RectTransform rect = (RectTransform)text.transform;

        if (Mathf.Approximately(text.fontSize, size)
            && rect.anchoredPosition == position
            && rect.sizeDelta == dimensions
            && text.alignment == alignment)
            return 0;

        text.fontSize = size;
        text.alignment = alignment;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        EditorUtility.SetDirty(text);
        Debug.Log($"[hud] resized {field} to {size:F0} point at {position}");

        return 1;
    }

    static void Wire(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);

        // A renamed field would otherwise wire nothing and fail silently at runtime, which is
        // exactly the sort of thing that gets blamed on the layout.
        if (property == null)
        {
            Debug.LogError($"[hud] GameHud has no field called '{field}'");
            return;
        }

        property.objectReferenceValue = value;
    }

    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    static readonly Vector2 BottomRight = new Vector2(1f, 0f);
    static readonly Vector2 TopRight = new Vector2(1f, 1f);
    static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

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

    static Image Image(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                       Vector2 pivot, Vector2 position, Vector2 size, Color colour)
    {
        return Panel(parent, name, anchorMin, anchorMax, pivot, position, size, colour)
               .GetComponent<Image>();
    }

    static Image Tick(Transform parent, string name, Vector2 size, Vector2 position)
    {
        return Image(parent, name, Center, Center, Center, position, size, Color.white);
    }

    static Sprite bananaSprite;

    /// <summary>
    /// A pixel-art banana, drawn by the same maths every procedural texture in this HUD already
    /// uses rather than a sourced or hand-painted asset - direct request, "find a pixel art
    /// banana that fits our vibe," and this project's own vibe already has a house style for
    /// exactly that (`GameHud.BuildScopeMask`, the skybox's pixelation): quantised shapes, drawn
    /// with real maths, not a texture pulled from somewhere else.
    ///
    /// A single silhouette, shaded in greyscale rather than baked-in colour, so tinting it with
    /// an `Image.color` (the ripeness palette) recolours the whole banana while keeping its own
    /// light-to-dark curve - the same reason the bar's old Shine/Shadow overlays were separate
    /// images, just baked into the sprite this time because `Image.Type.Filled` clips the image
    /// it's on, not children sitting on top of it, so an overlay child would spill past wherever
    /// the fill happens to cut off.
    /// </summary>
    static Sprite BananaSprite()
    {
        if (bananaSprite != null)
            return bananaSprite;

        // Chunkier than a first pass at 64x14/maxThickness 9 - that came out 7:1 length to
        // thickness, closer to a blade than a banana at a glance. This is closer to 4:1.
        const int width = 52;
        const int height = 18;
        const float amplitude = 3f;       // how much the spine arcs, in texels
        const float maxThickness = 13f;   // the banana's fattest point, in texels

        bool[,] on = new bool[width, height];

        float CenterY(float t) => height * 0.5f + amplitude * Mathf.Sin(Mathf.PI * t);
        float HalfThickness(float t) => 0.5f * maxThickness * Mathf.Sin(Mathf.PI * t);

        for (int x = 0; x < width; x++)
        {
            float t = (x + 0.5f) / width;
            float centre = CenterY(t);
            float half = HalfThickness(t);

            for (int y = 0; y < height; y++)
                on[x, y] = Mathf.Abs(y + 0.5f - centre) <= half;
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "~banana" };
        Color[] pixels = new Color[width * height];

        for (int x = 0; x < width; x++)
        {
            float t = (x + 0.5f) / width;
            float centre = CenterY(t);
            float half = HalfThickness(t);

            for (int y = 0; y < height; y++)
            {
                int i = y * width + x;

                if (!on[x, y])
                {
                    pixels[i] = Color.clear;
                    continue;
                }

                // An outline pixel touches the clear field or the texture's own edge within one
                // texel - the same "quantise, don't blur" cartoon edge everything else on this
                // HUD already draws with, here baked into the sprite instead of a separate pass.
                bool edge = false;
                for (int ox = -1; ox <= 1 && !edge; ox++)
                {
                    for (int oy = -1; oy <= 1 && !edge; oy++)
                    {
                        int nx = x + ox, ny = y + oy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height || !on[nx, ny])
                            edge = true;
                    }
                }

                if (edge)
                {
                    pixels[i] = new Color(0.06f, 0.07f, 0.08f, 1f);
                    continue;
                }

                // Lit from the top, the same top-bright/bottom-dark read the old Shine/Shadow
                // bands gave the rectangular bar - 0 at the slice's bottom edge, 1 at its top.
                float local = half > 0.01f ? Mathf.Clamp01((y + 0.5f - (centre - half)) / (half * 2f)) : 0.5f;
                float shade = Mathf.Lerp(0.62f, 1.15f, local);
                pixels[i] = new Color(shade, shade, shade, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        bananaSprite = Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, width, height),
                                     new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        bananaSprite.name = "~bananaSprite";

        return bananaSprite;
    }

    /// <summary>
    /// One layer of the banana meter - the husk, the trail or the fill are all this, stretched to
    /// fill whatever container they're built into so the three stack exactly on top of each
    /// other. `filled` off gives the always-visible husk; on gives a horizontally clipped layer
    /// for the trail and the live reading.
    /// </summary>
    static Image BananaLayer(Transform parent, string name, Color colour, bool filled)
    {
        Image image = Image(parent, name, Vector2.zero, Vector2.one, Center,
                            Vector2.zero, Vector2.zero, colour);
        image.sprite = BananaSprite();
        image.type = filled ? UnityEngine.UI.Image.Type.Filled : UnityEngine.UI.Image.Type.Simple;

        if (filled)
        {
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            image.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
        }

        return image;
    }

    /// A stack that lays its own children out top down. The feed and the standings both grow
    /// and shrink by rows, and a layout group means neither has to compute row heights from a
    /// font size that the editor is free to change.
    static RectTransform Column(Transform parent, string name, Vector2 anchor, Vector2 pivot,
                                Vector2 position, Vector2 size, TextAnchor alignment)
    {
        GameObject go = Panel(parent, name, anchor, anchor, pivot, position, size, null);

        VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = alignment;
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return (RectTransform)go.transform;
    }

    // A near-black rather than pure black, matched to the same edge colour the world's own toon
    // outline shader draws - the HUD's text reads as drawn in the same ink as everything else on
    // screen instead of borrowing a convention from another game's UI.
    static readonly Color OutlineColour = new Color(0.07f, 0.08f, 0.1f);

    // Raised from 0.2 - reported as reading "raw" against the map's own grass and wood, which a
    // thin line doesn't fix. This is a proper cartoon sticker border now, not a hairline.
    const float OutlineWidth = 0.38f;

    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float size,
                         TextAlignmentOptions alignment, Vector2 anchor,
                         Vector2 position, Vector2 dimensions, float outlineWidth = OutlineWidth)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;

        if (font != null)
            text.font = font;

        // A hard cartoon outline on every label by default, the same move `ScreenOutline`
        // already makes on every 3D object in view - a HUD drawn in a different visual language
        // to the rest of the game is what read as "generic" no matter which font sat behind it.
        // Each label gets its own material instance (`fontMaterial` does this on first access),
        // which costs a draw call each rather than batching - fine here, the HUD is a couple of
        // dozen labels, not thousands. `outlineWidth` is overridable per label - the weapon name
        // wants more presence than the shared default gives it.
        Material material = text.fontMaterial;
        material.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColour);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        text.fontMaterial = material;

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        return text;
    }

    /// <summary>
    /// Jersey 10 - a condensed, chunky display face built on a pixel grid without being an 8x8
    /// arcade font. Replaces Helvetica Punk here specifically: reported directly as not fitting
    /// the game's own cartoon-retro direction (the skybox's posterised, faceted look, not a
    /// tactical shooter's stencil). Menus stay on Helvetica Punk/Chomsky for now - this only
    /// changes what the in-match HUD itself builds with.
    /// </summary>
    static TMP_FontAsset FindFont()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("Jersey10"))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return TMP_Settings.defaultFontAsset;
    }
}
