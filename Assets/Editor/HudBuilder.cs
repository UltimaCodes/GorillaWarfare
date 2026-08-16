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

        TMP_Text healthNumber = Text(health.transform, "Number", font, 76f,
                                     TextAlignmentOptions.BottomLeft, BottomLeft,
                                     new Vector2(0f, 44f), new Vector2(300f, 90f));
        healthNumber.text = "140";

        // The track is the full bar including the overshield stretch; the fill and the shield
        // are sized against it at runtime, so widening this one rect rescales the whole thing.
        RectTransform track = Image(health.transform, "Track", BottomLeft, BottomLeft, BottomLeft,
                                    Vector2.zero, new Vector2(460f, 24f),
                                    new Color(0f, 0f, 0f, 0.55f)).rectTransform;

        Image fill = Image(track, "Fill", BottomLeft, BottomLeft, BottomLeft,
                           new Vector2(0f, 0f), new Vector2(322f, 24f), new Color(0.55f, 1f, 0.1f));

        Image shield = Image(track, "Shield", BottomLeft, BottomLeft, BottomLeft,
                             new Vector2(322f, 0f), new Vector2(0f, 24f), new Color(0.35f, 0.8f, 1f));
        shield.gameObject.SetActive(false);

        TMP_Text streak = Text(health.transform, "Streak", font, 22f,
                               TextAlignmentOptions.BottomLeft, BottomLeft,
                               new Vector2(0f, -30f), new Vector2(400f, 28f));
        streak.text = "3 IN A ROW";
        streak.color = new Color(1f, 0.55f, 0.1f);

        TMP_Text heal = Text(health.transform, "Heal", font, 34f,
                             TextAlignmentOptions.BottomLeft, BottomLeft,
                             new Vector2(310f, 44f), new Vector2(200f, 50f));
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

        TMP_Text weaponName = Text(ammo.transform, "Name", font, 30f,
                                   TextAlignmentOptions.BottomRight, BottomRight,
                                   new Vector2(0f, 150f), new Vector2(560f, 36f));
        weaponName.text = "RIFLE";
        weaponName.color = new Color(1f, 1f, 1f, 0.75f);

        // The round count is the number you actually read mid-fight, so it gets the size.
        TMP_Text ammoNumber = Text(ammo.transform, "Rounds", font, 120f,
                                   TextAlignmentOptions.BottomRight, BottomRight,
                                   new Vector2(-70f, 0f), new Vector2(400f, 140f));
        ammoNumber.text = "30";

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
        title.color = new Color(1f, 0.35f, 0.05f);

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

        Wire(so, "healthTrack", track);
        Wire(so, "healthFill", fill);
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

        // Sizes and positions that changed after the HUD was first built. Applied by name so a
        // scene built by an older version catches up without being replaced - the alternative
        // is telling Ryaan to rebuild and lose whatever he has moved.
        added += Retune(so, "centreTitle", 130f, new Vector2(0f, 230f), new Vector2(1600f, 150f));
        added += Retune(so, "centreSubtitle", 42f, new Vector2(0f, 148f), new Vector2(1600f, 56f));
        added += Retune(so, "comboText", 76f, new Vector2(0f, -170f), new Vector2(400f, 96f));

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
    static int Retune(SerializedObject so, string field, float size, Vector2 position, Vector2 dimensions)
    {
        SerializedProperty slot = so.FindProperty(field);
        TMP_Text text = slot != null ? slot.objectReferenceValue as TMP_Text : null;

        if (text == null)
            return 0;

        RectTransform rect = (RectTransform)text.transform;

        if (Mathf.Approximately(text.fontSize, size)
            && rect.anchoredPosition == position
            && rect.sizeDelta == dimensions)
            return 0;

        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
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

    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float size,
                         TextAlignmentOptions alignment, Vector2 anchor,
                         Vector2 position, Vector2 dimensions)
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

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        return text;
    }

    /// <summary>
    /// Prefers Helvetica Punk out of the four fonts in the project.
    ///
    /// It's the only one of them a number is legible in at a glance - Chomsky is blackletter,
    /// The Wildeast is a western slab and Bring Me A Helicopter is a display face. A HUD is read
    /// in the corner of your eye while someone is shooting at you, so this is the one that
    /// works; the other three are still a dropdown away if that's the wrong call.
    /// </summary>
    static TMP_FontAsset FindFont()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("Helvetica Punk"))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return TMP_Settings.defaultFontAsset;
    }
}
