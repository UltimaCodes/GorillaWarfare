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

    // Retuned once already - the first middle-right placement (40 above the vertical middle)
    // was reported as "a bit too high up, stuff overlaps or doesn't fit properly" against the
    // style meter cluster above it. Shared so Run() and Repair() can't drift apart on this again.
    static readonly Vector2 FeedPosition = new Vector2(-48f, -60f);

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
        TMP_FontAsset rankFont = FindRankFont();

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

        // ---------------------------------------------------------------- health, top left
        // Moved up from the bottom left and turned vertical - the Cruelty Squad health bar was
        // named directly as the reference: top left, depletes downward, the peel and the banana
        // distinct from each other rather than one shape, a backshadow/outline so "how much you
        // lost" and "how much there should be on full" can be read at a glance, and an animated
        // overshield bar beside it, tilted like ULTRAKILL's own HUD rather than sharing the main
        // bar's shape.
        GameObject health = Panel(rootObject.transform, "Health", TopLeft, TopLeft, TopLeft,
                                  new Vector2(48f, -48f), Vector2.zero, null);

        // Close to the sprite's own native aspect (285x250, close to square) rather than
        // stretched hard into a tall rectangle - the first two attempts at this (one at 90x320,
        // one at 150x210, both relying on Image.preserveAspect to keep the shape honest) both
        // rendered wrong in ways that weren't reproducible from reading the code, and reported
        // back as "disgusting... does not look like my example" - so this trades some of the
        // exaggerated verticality for a shape that is mathematically guaranteed not to distort:
        // no preserveAspect at all, just a plain stretch-fill (see BananaLayer) into a box close
        // enough to the source aspect that stretching it barely moves anything. The vertical
        // "meter" read leans on the layout instead - the number above, the ladder below, the
        // overshield beside it - rather than on the banana itself being pulled out of shape.
        Vector2 meterSize = new Vector2(150f, 160f);

        // Everything under `health` uses a TopLeft pivot and grows downward from there (negative
        // Y), matching the group's own TopLeft anchor - the bar sits below the number rather than
        // beside a BottomLeft-pivoted stack, which would grow upward off the top of the screen
        // instead of down into it. First build of this section got exactly that backwards - see
        // `bug-log.md`'s twenty-first pass.
        const float numberHeight = 90f;
        const float numberGap = 14f;
        float meterTop = -(numberHeight + numberGap);

        // The overshield sits behind and slightly offset from the main banana, sharing its own
        // silhouette rather than being a straight bar - reported directly: "the overshield one
        // being a curve and it underlapping the banana like in the reference picture." Built
        // first (and as a plain sibling, not a child of `meter`) specifically so it renders
        // *behind* the main banana in draw order - Unity UI draws children in hierarchy order,
        // so whatever is added first sits underneath everything added after it. A second real
        // asset was never sourced for this - it's the exact same sourced sprite the main banana
        // uses (see BananaSprite), just tinted and offset, which is what actually makes it read
        // as "the same banana, shielded" rather than an unrelated shape bolted on the side.
        Vector2 shieldSize = meterSize * 1.12f;
        Vector2 shieldOffset = new Vector2(22f, -14f);

        RectTransform shieldMeter = Panel(health.transform, "OvershieldMeter", TopLeft, TopLeft, TopLeft,
                                          new Vector2(shieldOffset.x, meterTop + shieldOffset.y), shieldSize, null)
                                     .GetComponent<RectTransform>();

        // Same lean as the main banana below - two bananas at different angles would read as
        // misaligned rather than as one shielded piece of fruit.
        shieldMeter.localRotation = Quaternion.Euler(0f, 0f, -22f);

        BananaOutline(shieldMeter, new Color(0.03f, 0.1f, 0.14f, 1f), 3f);

        Image shieldTrack = BananaLayer(shieldMeter, "Track", new Color(0.15f, 0.45f, 0.55f, 0.55f), false);

        Image shield = BananaLayer(shieldMeter, "Fill", new Color(0.4f, 0.9f, 1f, 1f), true);
        shield.gameObject.SetActive(false);

        // The host itself (outline + empty track) stays visible rather than hiding with the fill
        // - an empty socket peeking out from behind the main banana reads better than a shape
        // that appears from nowhere, and it's the "animated" half of the request: even idle it
        // sits there waiting to be filled instead of only existing at the one moment it has
        // something in it.

        RectTransform meter = Panel(health.transform, "BananaMeter", TopLeft, TopLeft, TopLeft,
                                    new Vector2(0f, meterTop), meterSize, null).GetComponent<RectTransform>();

        // Rotated toward the crosshair rather than left at the sprite's own natural lean -
        // reported directly: "With the banana facing the crosshair (not the opposite)". The
        // sourced sprite's own stem points up and to the right in its native orientation (see
        // BananaHealth-CREDIT.txt) - sitting in the top-left corner, that pointed further into
        // the corner instead of down toward the centre of the screen. Deliberately a modest
        // correction rather than a full turn toward it: Image.Type.Filled's Vertical/Bottom clip
        // (see BananaLayer) operates in this same rotated local space, so a large rotation would
        // swap which screen direction "depletes downward" actually empties toward - a small tilt
        // nudges the facing without fighting that. First-pass estimate, not measured against a
        // render - see roadmap.md's Unverified section, same as every other pose number here.
        meter.localRotation = Quaternion.Euler(0f, 0f, -22f);

        // A cell-shaded outline hugging the banana's own silhouette - reported directly against
        // the first attempt, which was a rectangle behind the sprite rather than an outline round
        // its actual shape ("What you did is add a box around it"). See BananaOutline.
        BananaOutline(meter, new Color(0.07f, 0.05f, 0.02f, 1f), 3f);

        // Split into the peel and the banana itself, per the direct request, rather than one
        // shape doing both jobs. Both layers are the same sourced sprite (BananaSprite) rather
        // than a second asset - the peel isn't a separate texture that happens to sit behind the
        // fruit, it's the same banana, always shown at full size and coloured like a peel. As the
        // banana layer on top depletes it reveals more of the peel underneath, which reads as
        // "eaten down toward the skin" as you take damage - the metaphor a second, unrelated
        // sprite would not have given for free. Still a whole banana, not a peeled-open one -
        // reported directly not to take the peel off; the split here is tint only, never shape.
        Image peel = BananaLayer(meter, "Peel", new Color(0.8f, 0.66f, 0.14f, 1f), false);
        Image trail = BananaLayer(meter, "Trail", new Color(1f, 0.97f, 0.86f, 0.55f), true);
        Image fill = BananaLayer(meter, "Fill", Color.white, true);

        TMP_Text healthNumber = Text(health.transform, "Number", font, 76f,
                                     TextAlignmentOptions.TopLeft, TopLeft,
                                     Vector2.zero, new Vector2(300f, numberHeight));
        healthNumber.text = "140";
        healthNumber.characterSpacing = 3f;

        TMP_Text streak = Text(health.transform, "Streak", font, 22f,
                               TextAlignmentOptions.TopLeft, TopLeft,
                               new Vector2(0f, meterTop - meterSize.y - 10f), new Vector2(400f, 28f));
        streak.text = "3 IN A ROW";
        streak.color = new Color(1f, 0.55f, 0.1f);

        TMP_Text heal = Text(health.transform, "Heal", font, 34f,
                             TextAlignmentOptions.TopLeft, TopLeft,
                             new Vector2(310f, 0f), new Vector2(200f, 50f));
        heal.text = "+35";

        // ---------------------------------------------------------------- ladder, below health
        // Was above health when health sat at the bottom of the screen - now sits under the
        // vertical bar instead, still directly attached to it.
        GameObject ladder = Panel(rootObject.transform, "Ladder", TopLeft, TopLeft, TopLeft,
                                  new Vector2(48f, -48f + meterTop - meterSize.y - 40f), Vector2.zero, null);

        TMP_Text ladderLabel = Text(ladder.transform, "Label", font, 22f,
                                    TextAlignmentOptions.TopLeft, TopLeft,
                                    Vector2.zero, new Vector2(400f, 28f));
        ladderLabel.text = "RUNG 1 / 5";
        ladderLabel.color = new Color(1f, 1f, 1f, 0.55f);

        Image[] pips = new Image[PipCount];
        for (int i = 0; i < PipCount; i++)
        {
            pips[i] = Image(ladder.transform, $"Pip{i}", TopLeft, TopLeft, TopLeft,
                            new Vector2(i * 22f, -32f), new Vector2(15f, 15f),
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
        TMP_Text title = Text(centre.transform, "Title", rankFont, 120f,
                              TextAlignmentOptions.Center, Center,
                              new Vector2(0f, 230f), new Vector2(1600f, 150f), 0.5f);
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

        // Standings, listed under the winner when the round is over. Reworked entirely - reported
        // directly as looking "pretty bad... not updated with the game... old bad fonts". Widened
        // and the type bumped up a size (28->32) to carry the new rank-number prefix
        // (GameHud.UpdateStandings colours 1ST/2ND/3RD gold/silver/bronze via a rich-text tag)
        // without the row feeling any more cramped than the old, plainer one did.
        // Low enough to clear the subtitle above it. The first row starts at the top of this
        // box, so anchoring it any higher puts the standings through the winner's name.
        RectTransform standings = Column(centre.transform, "Standings", Center, Center,
                                         new Vector2(0f, -110f), new Vector2(820f, 420f),
                                         TextAnchor.UpperCenter);

        TMP_Text standingRow = Text(standings, "RowTemplate", font, 32f,
                                    TextAlignmentOptions.Center, Center,
                                    Vector2.zero, new Vector2(820f, 38f));
        standingRow.text = "<color=#FFD11F>1ST</color>  someone   0 STYLE   0 KILLS";

        // Hidden - a template exists only to be cloned (see GameHud.Row), and is otherwise just
        // an untracked extra child neither Update method's own pool bookkeeping ever reaches.
        // Same bug shape fixed on feedRow and breakdownRow below, all three found in the same
        // sweep - reported directly, on the breakdown list specifically, as "a constant 1.35x
        // headshot thingy on the right at all times."
        standingRow.gameObject.SetActive(false);

        // ---------------------------------------------------------------- kill feed, middle right
        // Moved down off the top right corner to make room for the style meter there instead -
        // reported directly: "I want something on the top right for this so you will need to
        // move the killfeed in the middle right". Anchored to the vertical middle rather than
        // the top, so it grows toward the bottom edge instead of toward the style meter above it.
        RectTransform feed = Column(rootObject.transform, "Feed", MiddleRight, MiddleRight,
                                    FeedPosition, new Vector2(760f, 320f),
                                    TextAnchor.UpperRight);

        TMP_Text feedRow = Text(feed, "RowTemplate", font, 28f,
                                TextAlignmentOptions.Right, TopRight,
                                Vector2.zero, new Vector2(760f, 34f));
        feedRow.text = "someone got peeled by someone else";
        feedRow.gameObject.SetActive(false);

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

        // The unified style score, top right - moved up off the vertical middle to its own
        // corner, the same treatment health and ammo already get in theirs. Reworked entirely
        // from a slide-only combo (see StyleScore.cs); the rank name now carries the multiplier
        // alongside it ("GOING BANANAS  x2.4") rather than a separate label.
        //
        // No permanent score readout - reported directly against an earlier version that showed
        // one always: "why is there a permanent score on the screen all the time I dont need to
        // see it like that, i Like the ultrakill style." ULTRAKILL's own meter only ever shows
        // while there's something to show; the running total still exists (StyleScore.Score) for
        // the post-match standings and the MOST STYLISH award, it just isn't a fixture of the
        // live HUD any more.
        //
        // Tilted rather than flat - "it should be tilted inwards and not horizontally flat",
        // matching the lean every other bar on this HUD already uses. Deliberately NOT a child of
        // `centre` - that panel is built with zero size (it only ever needed to anchor centred
        // children at its own origin), so a "right" anchor on a child of a zero-*width* parent
        // collapses to the same point a centred one would. Parented on the root canvas instead,
        // where the anchor means what it says.
        const float styleTilt = -6f;

        TMP_Text slideRank = Text(rootObject.transform, "SlideCombo", rankFont, 42f,
                                  TextAlignmentOptions.Right, TopRight,
                                  new Vector2(-48f, -48f), new Vector2(560f, 60f), 0.5f);
        slideRank.text = "PEELING  x1.0";
        slideRank.rectTransform.localRotation = Quaternion.Euler(0f, 0f, styleTilt);
        slideRank.gameObject.SetActive(false);
        Wire(so, "slideCombo", slideRank);

        // The breakdown - "1.35x HEADSHOT", "1.5x NO SCOPE" and so on for whatever fired on the
        // last kill, per the reference image's own layout: a named reason next to a number,
        // stacked. Reported directly that the multiplier's contributing factors needed to be
        // visible rather than folded into one number with nothing to show for it. Same tilt as
        // the rank line above it, so the whole cluster reads as one leaning block.
        // Y=-150, not -118: the rank text above sits in a TopRight-pivoted box whose own meter
        // bar/border hangs below it down to Y=-129 (rank text top edge at -48, 60 tall, plus the
        // meter's own ~21px past that) - -118 put the breakdown's top edge eleven pixels inside
        // that, which is exactly the overlap reported from a real screenshot ("FULL SILVERBACK
        // x5.4" overlapping "1.30x POINT BLANK"). -150 clears it with room to spare.
        RectTransform breakdown = Column(rootObject.transform, "Breakdown", TopRight, TopRight,
                                         new Vector2(-48f, -150f), new Vector2(400f, 200f),
                                         TextAnchor.UpperRight);
        breakdown.localRotation = Quaternion.Euler(0f, 0f, styleTilt);

        TMP_Text breakdownRow = Text(breakdown, "RowTemplate", font, 22f,
                                     TextAlignmentOptions.Right, TopRight,
                                     Vector2.zero, new Vector2(400f, 26f));
        breakdownRow.text = "1.35x HEADSHOT";
        breakdownRow.color = new Color(1f, 0.9f, 0.4f);
        breakdownRow.gameObject.SetActive(false);

        Wire(so, "breakdownContainer", breakdown);
        Wire(so, "breakdownTemplate", breakdownRow);

        // The rank text alone was just a word changing, four times - asked directly for an
        // ULTRAKILL/DMC style meter under it instead, the same idea as the style bar under
        // "CHAOTIC" or a stale-combo readout: a number draining, not only a name. Reads the
        // live chain window (`PlayerMovement.ChainWindowFraction`) rather than counting hits -
        // full the instant you land a slide, empty by the time the chain would expire, so the
        // bar itself is the "hurry up and slide again" cue the rank name alone never gave.
        // A child of `slideRank`'s own GameObject, not a sibling - that box has real width and
        // height (620x80), unlike `Centre`, so anchoring a child to its corner actually means
        // something, and it shows/hides for free with the text's own SetActive.
        // A black frame round it - reported directly that this bar was bare and flat, same
        // complaint the health bar had before it got one. Background-behind-foreground border,
        // same technique as everywhere else on this HUD that wants one.
        const float meterBorder = 3f;
        Panel(slideRank.transform, "MeterBorder", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f),
              new Vector2(meterBorder, -6f + meterBorder), new Vector2(300f + meterBorder * 2f, 12f + meterBorder * 2f),
              Color.black);

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

        // The movement-tech combo, bottom left - a mirror of the style meter above (same name +
        // chain-count shape, same tilted bar underneath), reading MovementCombo instead of
        // StyleScore. Reported directly: "no slide combo text still which should show up... put
        // that on the middle/bottom left... make new movement tech combos and stuff... this
        // includes grappling, grenade jumping, bhopping, slide hopping" - a second, separate
        // counter from the kill-based one above, for traversal instead of combat. Tilted the
        // other way (+6 rather than -6) so it leans inward from the left edge the same way the
        // style meter leans inward from the right - two mirrored corners, not the same tilt
        // copy-pasted. The meter bar hangs ABOVE the text rather than below it, unlike the style
        // meter's - this cluster sits near the bottom of the screen, and below would push it
        // off-screen entirely.
        const float comboTilt = 6f;

        TMP_Text moveCombo = Text(rootObject.transform, "MovementCombo", rankFont, 42f,
                                  TextAlignmentOptions.Left, BottomLeft,
                                  new Vector2(48f, 48f), new Vector2(560f, 60f));
        moveCombo.text = "SLIDE HOP  x1";
        moveCombo.rectTransform.localRotation = Quaternion.Euler(0f, 0f, comboTilt);
        moveCombo.gameObject.SetActive(false);
        Wire(so, "movementCombo", moveCombo);

        Panel(moveCombo.transform, "MeterBorder", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
              new Vector2(-meterBorder, 6f - meterBorder), new Vector2(300f + meterBorder * 2f, 12f + meterBorder * 2f),
              Color.black);

        GameObject moveMeterTrack = Panel(moveCombo.transform, "MeterTrack",
                                          new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
                                          new Vector2(0f, 6f), new Vector2(300f, 12f),
                                          new Color(0f, 0f, 0f, 0.55f));

        Image moveMeterFill = Image(moveMeterTrack.transform, "Fill", Vector2.zero, Vector2.one, Center,
                                    Vector2.zero, Vector2.zero, new Color(0.25f, 0.85f, 1f, 0.95f));
        moveMeterFill.type = UnityEngine.UI.Image.Type.Filled;
        moveMeterFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        moveMeterFill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
        moveMeterFill.fillAmount = 1f;
        Wire(so, "movementMeterFill", moveMeterFill);

        // The slide-exhaustion notice, its own element now. First placement (right side,
        // middle-right) was reported as wrong the same day - "put the spent from the sliding to
        // the left on top of the movement combo thing." Bottom-left now, directly above the
        // movement combo's own cluster (which tops out around Y=129 including its meter border -
        // see the movement combo block above) rather than sharing its spot. Left-aligned to match
        // sitting over a left-anchored element, no tilt either way - a neutral wait counting down,
        // not a hype number climbing.
        TMP_Text spent = Text(rootObject.transform, "SpentText", rankFont, 34f,
                              TextAlignmentOptions.Left, BottomLeft,
                              new Vector2(48f, 150f), new Vector2(400f, 50f));
        spent.text = "SPENT  1.0";
        spent.color = new Color(0.55f, 0.55f, 0.6f, 0.9f);
        spent.gameObject.SetActive(false);
        Wire(so, "spentText", spent);

        // "Make sure people get tokens at the end of each round (and a whole animation plays...
        // with sound effects and vfx etc)." Below centreTitle/centreSubtitle (Y=230/148) so it
        // never competes with the win/lose announcement - this is a personal, separate moment,
        // not part of who won. tokenRewardBurst (a particle flourish) is left unwired - optional,
        // null-checked in GameHud.AwardRoundTokens, a reasonable follow-up rather than a blocker.
        TMP_Text tokenReward = Text(rootObject.transform, "TokenReward", font, 54f,
                                    TextAlignmentOptions.Center, TopCenter,
                                    new Vector2(0f, 60f), new Vector2(700f, 80f));
        tokenReward.text = "+50 TOKENS";
        tokenReward.color = new Color(1f, 0.86f, 0.2f);
        tokenReward.gameObject.SetActive(false);
        Wire(so, "tokenRewardText", tokenReward);

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
                TMP_Text made = Text(hud.transform, "SlideCombo", sibling.font, 42f,
                                     TextAlignmentOptions.Right, TopRight,
                                     new Vector2(-48f, -48f), new Vector2(560f, 60f));
                made.text = "PEELING  x1.0";
                made.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -6f);
                made.gameObject.SetActive(false);

                comboSlot.objectReferenceValue = made;
                added++;

                Debug.Log("[hud] added the slide rank");
            }
        }

        // The kill feed's move off the top right corner to make room for the style meter there -
        // Retune only repositions a TMP_Text, and the feed is a plain RectTransform with its own
        // anchor to change, so it gets its own check rather than a Retune call.
        SerializedProperty feedSlot = so.FindProperty("feedContainer");
        RectTransform feedRect = feedSlot != null ? feedSlot.objectReferenceValue as RectTransform : null;

        if (feedRect != null && feedRect.anchorMin == TopRight)
        {
            feedRect.anchorMin = feedRect.anchorMax = feedRect.pivot = MiddleRight;
            feedRect.anchoredPosition = FeedPosition;

            EditorUtility.SetDirty(feedRect);
            added++;

            Debug.Log("[hud] moved the kill feed to the middle right");
        }
        else if (feedRect != null && feedRect.pivot == MiddleRight && feedRect.anchoredPosition != FeedPosition)
        {
            // Retuned again after the first middle-right placement was reported as "a bit too
            // high up, stuff overlaps or doesn't fit properly".
            feedRect.anchoredPosition = FeedPosition;
            EditorUtility.SetDirty(feedRect);
            added++;

            Debug.Log("[hud] retuned the kill feed's middle-right position");
        }

        // Sizes and positions that changed after the HUD was first built. Applied by name so a
        // scene built by an older version catches up without being replaced - the alternative
        // is telling Ryaan to rebuild and lose whatever he has moved.
        added += Retune(so, "centreTitle", 130f, new Vector2(0f, 230f), new Vector2(1600f, 150f));
        added += Retune(so, "centreSubtitle", 42f, new Vector2(0f, 148f), new Vector2(1600f, 56f));
        added += Retune(so, "comboText", 76f, new Vector2(0f, -170f), new Vector2(400f, 96f));

        // The leaderboard rework - reported directly as looking "pretty bad... not updated with
        // the game... old bad fonts." Retune handles the row template's own size/position, but
        // not its font asset or its container's width - two things a live scene built by an
        // older version of this file could still be carrying, since nothing before this checked
        // either.
        added += Retune(so, "standingsTemplate", 32f, Vector2.zero, new Vector2(820f, 38f));

        SerializedProperty standingsRowSlot = so.FindProperty("standingsTemplate");
        TMP_Text standingsRowText = standingsRowSlot != null ? standingsRowSlot.objectReferenceValue as TMP_Text : null;
        TMP_FontAsset bodyFont = FindFont();

        if (standingsRowText != null && standingsRowText.font != bodyFont && bodyFont != null)
        {
            standingsRowText.font = bodyFont;
            EditorUtility.SetDirty(standingsRowText);
            added++;

            Debug.Log("[hud] fixed the standings row's stale font");
        }

        SerializedProperty standingsContainerSlot = so.FindProperty("standingsContainer");
        RectTransform standingsContainerRect = standingsContainerSlot != null
            ? standingsContainerSlot.objectReferenceValue as RectTransform : null;
        Vector2 standingsSize = new Vector2(820f, 420f);

        if (standingsContainerRect != null && standingsContainerRect.sizeDelta != standingsSize)
        {
            standingsContainerRect.sizeDelta = standingsSize;
            EditorUtility.SetDirty(standingsContainerRect);
            added++;

            Debug.Log("[hud] widened the standings container");
        }

        // Moved up into the top right corner as part of becoming the unified style meter - see
        // Run()'s own note. Alignment passed explicitly: Retune defaults to Center, and this one
        // was built Right to hug the edge of its group rather than sit centred in its box like
        // the other three.
        added += Retune(so, "slideCombo", 42f, new Vector2(-48f, -48f), new Vector2(560f, 60f),
                        TextAlignmentOptions.Right);

        // Retuned twice the same day it was added. First position (right side, Y=48) sat inside
        // the kill feed's own fixed-height box (MiddleRight-pivoted, 320 tall, so its content
        // starts near the *top* of that box at roughly Y=100, not near its Y=-60 anchor point the
        // way a box growing from empty would suggest) - a real screenshot caught "SPENT 3.0"
        // printed directly on top of a feed line. Moved to Y=140 on the right to clear the feed,
        // then reported directly the same day as wrong entirely - "put the spent from the
        // sliding to the left on top of the movement combo thing." Bottom-left now, Y=150,
        // clearing the movement combo cluster's own top edge (Y=129 including its meter border).
        added += Retune(so, "spentText", 34f, new Vector2(48f, 150f), new Vector2(400f, 50f),
                        TextAlignmentOptions.Left);

        // Retune only ever touches size/position/alignment, not rotation - the tilt is applied
        // separately here so a scene repaired rather than fully rebuilt still picks it up.
        SerializedProperty tiltSlot = so.FindProperty("slideCombo");
        TMP_Text tiltTarget = tiltSlot != null ? tiltSlot.objectReferenceValue as TMP_Text : null;

        if (tiltTarget != null && tiltTarget.rectTransform.localRotation == Quaternion.identity)
        {
            tiltTarget.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -6f);
            EditorUtility.SetDirty(tiltTarget);
            added++;

            Debug.Log("[hud] tilted the style rank text");
        }

        // The breakdown sat eleven pixels inside the style meter's own bar/border below the rank
        // text - a real screenshot caught the overlap ("FULL SILVERBACK x5.4" overlapping "1.30x
        // POINT BLANK"). Breakdown is a plain RectTransform, not a TMP_Text, so it gets its own
        // conditional fix rather than going through Retune, same shape as the feed's own reposition
        // above.
        SerializedProperty breakdownSlot = so.FindProperty("breakdownContainer");
        RectTransform breakdownRect = breakdownSlot != null ? breakdownSlot.objectReferenceValue as RectTransform : null;
        Vector2 breakdownPosition = new Vector2(-48f, -150f);

        if (breakdownRect != null && breakdownRect.anchoredPosition != breakdownPosition)
        {
            breakdownRect.anchoredPosition = breakdownPosition;
            EditorUtility.SetDirty(breakdownRect);
            added++;

            Debug.Log("[hud] moved the style breakdown down to clear the meter bar");
        }

        // The movement-tech combo, bottom left - see Run()'s own note for the full reasoning.
        // Built as a fallback the same shape slideCombo's own block above uses: found missing,
        // built fresh off an existing element's font rather than replacing anything.
        SerializedProperty moveSlot = so.FindProperty("movementCombo");

        if (moveSlot != null && moveSlot.objectReferenceValue == null)
        {
            SerializedProperty rankSlot = so.FindProperty("slideCombo");
            TMP_Text rankSibling = rankSlot != null ? rankSlot.objectReferenceValue as TMP_Text : null;

            if (rankSibling != null)
            {
                const float comboTilt = 6f;
                const float meterBorder = 3f;

                TMP_Text moveCombo = Text(hud.transform, "MovementCombo", rankSibling.font, 42f,
                                          TextAlignmentOptions.Left, BottomLeft,
                                          new Vector2(48f, 48f), new Vector2(560f, 60f));
                moveCombo.text = "SLIDE HOP  x1";
                moveCombo.rectTransform.localRotation = Quaternion.Euler(0f, 0f, comboTilt);
                moveCombo.gameObject.SetActive(false);

                Panel(moveCombo.transform, "MeterBorder", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
                      new Vector2(-meterBorder, 6f - meterBorder), new Vector2(300f + meterBorder * 2f, 12f + meterBorder * 2f),
                      Color.black);

                GameObject moveMeterTrack = Panel(moveCombo.transform, "MeterTrack",
                                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
                                                  new Vector2(0f, 6f), new Vector2(300f, 12f),
                                                  new Color(0f, 0f, 0f, 0.55f));

                Image moveMeterFill = Image(moveMeterTrack.transform, "Fill", Vector2.zero, Vector2.one, Center,
                                            Vector2.zero, Vector2.zero, new Color(0.25f, 0.85f, 1f, 0.95f));
                moveMeterFill.type = UnityEngine.UI.Image.Type.Filled;
                moveMeterFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                moveMeterFill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
                moveMeterFill.fillAmount = 1f;

                moveSlot.objectReferenceValue = moveCombo;

                SerializedProperty moveFillSlot = so.FindProperty("movementMeterFill");
                if (moveFillSlot != null)
                    moveFillSlot.objectReferenceValue = moveMeterFill;

                added++;

                Debug.Log("[hud] added the movement combo");
            }
        }

        // The slide-exhaustion notice, its own element now - see Run()'s own note.
        SerializedProperty spentSlot = so.FindProperty("spentText");

        if (spentSlot != null && spentSlot.objectReferenceValue == null)
        {
            SerializedProperty rankSlot2 = so.FindProperty("slideCombo");
            TMP_Text rankSibling2 = rankSlot2 != null ? rankSlot2.objectReferenceValue as TMP_Text : null;

            if (rankSibling2 != null)
            {
                TMP_Text spent = Text(hud.transform, "SpentText", rankSibling2.font, 34f,
                                      TextAlignmentOptions.Left, BottomLeft,
                                      new Vector2(48f, 150f), new Vector2(400f, 50f));
                spent.text = "SPENT  1.0";
                spent.color = new Color(0.55f, 0.55f, 0.6f, 0.9f);
                spent.gameObject.SetActive(false);

                spentSlot.objectReferenceValue = spent;
                added++;

                Debug.Log("[hud] added the spent indicator");
            }
        }

        // The round-end token reward callout - see Run()'s own note.
        SerializedProperty tokenSlot = so.FindProperty("tokenRewardText");

        if (tokenSlot != null && tokenSlot.objectReferenceValue == null)
        {
            SerializedProperty titleSlot = so.FindProperty("centreTitle");
            TMP_Text titleSibling = titleSlot != null ? titleSlot.objectReferenceValue as TMP_Text : null;

            if (titleSibling != null)
            {
                TMP_Text tokenReward = Text(hud.transform, "TokenReward", titleSibling.font, 54f,
                                            TextAlignmentOptions.Center, TopCenter,
                                            new Vector2(0f, 60f), new Vector2(700f, 80f));
                tokenReward.text = "+50 TOKENS";
                tokenReward.color = new Color(1f, 0.86f, 0.2f);
                tokenReward.gameObject.SetActive(false);

                tokenSlot.objectReferenceValue = tokenReward;
                added++;

                Debug.Log("[hud] added the token reward callout");
            }
        }

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
    static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    static readonly Vector2 TopRight = new Vector2(1f, 1f);
    static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
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

    const string BananaTexturePath = "Assets/Textures/UI/BananaHealth.png";
    static Sprite bananaSprite;

    /// <summary>
    /// A real pixel-art banana - "find a pixel art banana that fits our vibe" meant an actual
    /// sourced sprite, not a procedurally drawn stand-in; a first pass generating one from sine
    /// curves was reported back directly as not what was meant, on top of a broader note not to
    /// keep reaching for a generated shape when a real asset does the job better. One frame
    /// (frame 9 of 20) cropped from "Spinning Banana" by lawrence_laz on OpenGameArt.org, CC0 -
    /// see `Assets/Textures/UI/BananaHealth-CREDIT.txt`. Import settings (Sprite, point filtered,
    /// uncompressed) are applied once by `Tools/Gorilla Warfare/Configure banana sprite import`,
    /// not here - this just loads whatever's already sitting in the AssetDatabase.
    /// </summary>
    static Sprite BananaSprite()
    {
        if (bananaSprite != null)
            return bananaSprite;

        bananaSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BananaTexturePath);

        if (bananaSprite == null)
            Debug.LogError($"[hud] no banana sprite at {BananaTexturePath} - run "
                           + "Tools/Gorilla Warfare/Configure banana sprite import first");

        return bananaSprite;
    }

    /// <summary>
    /// One layer of the vertical health meter - the peel, the trail or the banana itself are all
    /// this, stretched to fill whatever container they're built into so the three stack exactly
    /// on top of each other. `filled` off gives the always-visible peel; on gives a vertically
    /// clipped layer for the trail and the live reading.
    /// </summary>
    static Image BananaLayer(Transform parent, string name, Color colour, bool filled)
    {
        Image image = Image(parent, name, Vector2.zero, Vector2.one, Center,
                            Vector2.zero, Vector2.zero, colour);
        image.sprite = BananaSprite();
        image.type = filled ? UnityEngine.UI.Image.Type.Filled : UnityEngine.UI.Image.Type.Simple;

        // The sourced sprite is a whole banana on a natural diagonal lean, not a tall thin
        // shape - stretching it to fill an arbitrary box (the first version of this) turned it
        // into a distorted yellow wedge the moment the box wasn't close to its own aspect ratio.
        // preserveAspect keeps the real shape intact regardless of the container; Filled clipping
        // still works correctly on top of an aspect-preserved sprite; it just clips the
        // aspect-correct rect instead of the stretched one.
        if (filled)
        {
            // Vertical, anchored at the bottom - losing health clips the layer away from the
            // top down rather than shrinking it from either side, which is the "goes down" the
            // Cruelty Squad reference bar asked for. Was horizontal, hugging the left edge, back
            // when this whole meter ran sideways along the bottom of the screen.
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Vertical;
            image.fillOrigin = (int)UnityEngine.UI.Image.OriginVertical.Bottom;
            image.fillAmount = 1f;
        }

        return image;
    }

    /// <summary>
    /// A cell-shaded outline round the banana's actual silhouette, not a box behind it -
    /// reported directly: "there should be a small cell shaded type outline around the health
    /// banana... What you did is add a box around it." A rectangular backing reads as a frame
    /// for a non-rectangular shape; this is the standard cheap trick for an outline that actually
    /// hugs an arbitrary silhouette without a custom shader - eight solid-tinted copies of the
    /// same sprite, nudged a couple of points in a ring of directions and drawn behind the real
    /// layers, so the only pixels that show through are the ones just outside the true shape.
    /// Always full size (Type.Simple, not Filled) - this is the "how much there should be on
    /// full" half of the comparison, so it never depletes with the fill on top of it.
    /// </summary>
    static void BananaOutline(Transform parent, Color colour, float thickness)
    {
        Vector2[] ring =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(0.7071f, 0.7071f), new Vector2(-0.7071f, 0.7071f),
            new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, -0.7071f),
        };

        for (int i = 0; i < ring.Length; i++)
        {
            Image copy = Image(parent, $"Outline{i}", Vector2.zero, Vector2.one, Center,
                               ring[i] * thickness, Vector2.zero, colour);
            copy.sprite = BananaSprite();
            copy.type = UnityEngine.UI.Image.Type.Simple;
            copy.preserveAspect = true;
        }
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

    // Raised from 0.2, then again from 0.38 - reported both times as still reading "raw" and
    // "blending in with everything else." This is a proper cartoon sticker border, not a line.
    const float OutlineWidth = 0.55f;

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
        //
        // A soft underlay on top of the outline, not instead of it - reported directly that text
        // was still "blending in with everything else" even with a hard border. The outline
        // keeps letterforms readable at their edges against any single colour; the underlay is a
        // dark, slightly offset blur *behind the whole glyph*, which is what actually separates
        // text from a busy, high-contrast background (foliage, another player, the sky) rather
        // than just tracing round it.
        Material material = text.fontMaterial;
        material.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColour);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
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
    static TMP_FontAsset FindFont() => FindFontNamed("Jersey10");

    /// <summary>
    /// The hype face - kill callouts and the slide rank, not the readouts. Reported directly:
    /// the rank meter needed its own font and style to actually stick out, the same way DMC's
    /// own style-rank lettering reads as a completely different object to the rest of its HUD
    /// rather than the same numerals in a different colour. Anton (Google Fonts, OFL) rather
    /// than Jersey10 - a poster-weight impact face, about as far from a pixel-grid readout font
    /// as this project's fonts get, so the two moments that want to shout (a kill, a rank-up)
    /// read as a different kind of text on screen, not just bigger.
    /// </summary>
    static TMP_FontAsset FindRankFont() => FindFontNamed("Anton");

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
}
