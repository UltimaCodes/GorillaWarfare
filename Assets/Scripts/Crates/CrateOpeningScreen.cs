using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crates, and opening one.
///
/// Rebuilt 2026-09-30: "Revamp the entire crates system to match the rest of the UI and all but
/// try to add alot of animations, add like a proper crate opening and all animation, make it as
/// addictive and dopamine inducing as possible." And a crate wins something now - a weapon finish
/// (WeaponFinish), kept in your inventory (SkinInventory).
///
/// Three chests stand over their cards, alive - bobbing, turning, peeking their lids open when you
/// hover. Open one and it takes the stage: the others drop away, the camera pushes in, it jolts
/// three times harder each time with dust and sparks leaking out, then bursts - flash, stars, real
/// coins - and the reel runs: cards of real finishes streaming past a pointer, slowing, ticking,
/// for six seconds (click to skip). Then the reveal, scaled by how rare it is: the finish on a
/// weapon turning under light, its name punched in, NEW or how many you've got, and one click to
/// equip it everywhere, scrap it, or open another.
///
/// Honest all the way through, the same line the first version drew: CrateInfo.RollFinish decides
/// the real result before anything moves, it's in your inventory before the chest has finished
/// shaking (closing mid-spin loses nothing), and every other card on the reel is drawn from the
/// same odds - nothing is placed to look like a near miss. The odds are printed on every card.
/// </summary>
public class CrateOpeningScreen : MonoBehaviour
{
    /// Instantiated onto RoomManager (see RoomManager.cs), so it rides between the menu and a match
    /// and a button anywhere just calls Instance.Open().
    public static CrateOpeningScreen Instance { get; private set; }

    /// Everything visible hangs off this - the root has to stay active for Awake to run at all.
    [SerializeField] GameObject panel;
    [SerializeField] CanvasGroup fade;
    [SerializeField] RawImage stageView;
    [SerializeField] TokenCounter tokens;

    [Header("Pick a crate")]
    [SerializeField] GameObject selectPage;
    [SerializeField] CrateCard[] cards = new CrateCard[3];
    [Tooltip("Where each crate's chest stands - the middle of its base - over its card.")]
    [SerializeField] RectTransform[] chestSpots = new RectTransform[3];
    [SerializeField] Button closeButton;
    [SerializeField] Button inventoryButton;

    [Header("Chests - Rotten, Ripe, Holy")]
    [SerializeField] GameObject[] chestModels = new GameObject[3];
    [SerializeField] WeaponFinish[] chestLooks = new WeaponFinish[3];
    [SerializeField] GameObject coinModel;
    [SerializeField] Texture glowSprite;
    [SerializeField] Texture starSprite;
    [SerializeField] Texture sparkSprite;
    [SerializeField] Texture smokeSprite;
    [SerializeField] Texture confettiSprite;
    [SerializeField] Texture raySprite;

    [Header("Opening")]
    [SerializeField] GameObject openingPage;
    [SerializeField] TMP_Text openingTitle;
    [SerializeField] Image flash;
    [SerializeField] Image dim;
    [SerializeField] TMP_Text skipHint;

    [Header("Reel")]
    [SerializeField] RectTransform reel;
    [SerializeField] RectTransform reelViewport;
    [SerializeField] RectTransform reelStrip;
    [SerializeField] FinishCard reelCardTemplate;
    [SerializeField] Image pointer;

    [Header("Reveal")]
    [SerializeField] GameObject revealGroup;
    [SerializeField] RawImage revealView;
    [SerializeField] Image revealFrame;
    [SerializeField] Image raysA;
    [SerializeField] Image raysB;
    [SerializeField] TMP_Text revealName;
    [SerializeField] TMP_Text revealRarity;
    [SerializeField] Image revealRarityBar;
    [SerializeField] TMP_Text revealFlavour;
    [SerializeField] TMP_Text newBadge;
    [SerializeField] TMP_Text countText;
    [SerializeField] TMP_Text collectionText;
    [SerializeField] CanvasGroup actions;
    [SerializeField] Button equipAllButton;
    [SerializeField] TMP_Text equipAllLabel;
    [SerializeField] Button inventoryFromResultButton;
    [SerializeField] Button scrapButton;
    [SerializeField] TMP_Text scrapLabel;
    [SerializeField] Button openAnotherButton;
    [SerializeField] TMP_Text openAnotherLabel;
    [SerializeField] Button backButton;

    const float CardWidth = 200f;
    const float CardGap = 14f;
    const int ReelLength = 60;
    const int LandingIndex = 52;
    const float SpinSeconds = 5.8f;
    const float StageDepth = 3.8f;

    static readonly Color Backdrop = new Color(0.035f, 0.04f, 0.05f, 1f);

    PreviewStage crateStage;
    PreviewStage revealStage;
    Renderer revealGlow;
    Renderer revealRays;
    readonly ChestRig[] rigs = new ChestRig[3];
    readonly Vector3[] baseOffsets = new Vector3[3];
    readonly List<FinishCard> reelCards = new List<FinishCard>();

    bool busy;
    bool skip;
    bool scrapArmed;
    float scrapArmedUntil;
    CrateInfo lastCrate;
    WeaponFinish lastFinish;

    /// The result is up and the screen is waiting on you - what the checks wait for.
    public bool ShowingResult => revealGroup != null && revealGroup.activeInHierarchy && !busy;

    public bool IsOpen => panel != null && panel.activeSelf;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        for (int i = 0; i < cards.Length && i < CrateInfo.All.Length; i++)
        {
            if (cards[i] == null)
                continue;
            CrateInfo crate = CrateInfo.All[i];
            int index = i;
            cards[i].Bind(crate, () => TryOpen(crate), on => Hover(index, on));
        }

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backButton != null) backButton.onClick.AddListener(ShowSelectPage);
        if (inventoryButton != null) inventoryButton.onClick.AddListener(() => ToInventory(null));
        if (inventoryFromResultButton != null) inventoryFromResultButton.onClick.AddListener(() => ToInventory(lastFinish));
        if (equipAllButton != null) equipAllButton.onClick.AddListener(EquipEverywhere);
        if (scrapButton != null) scrapButton.onClick.AddListener(ScrapResult);
        if (openAnotherButton != null) openAnotherButton.onClick.AddListener(() => TryOpen(lastCrate));

        if (reelCardTemplate != null) reelCardTemplate.gameObject.SetActive(false);
    }

    void OnDisable() => PlayerWallet.Changed -= Afford;
    void OnEnable() => PlayerWallet.Changed += Afford;

    // ---------------------------------------------------------------- open and close

    public void Open()
    {
        if (panel != null)
            panel.SetActive(true);

        EnsureStages();
        crateStage.Drawing = true;
        if (tokens != null)
            tokens.Snap();

        ShowSelectPage();
        StartCoroutine(FadeIn());
    }

    public void Close()
    {
        StopAllCoroutines();
        busy = false;
        skip = false;

        if (panel != null)
            panel.SetActive(false);
        if (crateStage != null)
            crateStage.Drawing = false;
        if (revealStage != null)
            revealStage.Drawing = false;

        foreach (ChestRig rig in rigs)
        {
            if (rig != null)
                rig.Reset();
        }
    }

    IEnumerator FadeIn()
    {
        if (fade == null)
            yield break;
        fade.alpha = 0f;
        yield return UiMotion.Tween(0.25f, UiMotion.OutCubic, k => fade.alpha = k);
    }

    void ToInventory(WeaponFinish finish)
    {
        Close();
        if (InventoryScreen.Instance != null)
            InventoryScreen.Instance.Open(null, finish);
    }

    void ShowSelectPage()
    {
        if (busy)
            return;

        StopAllCoroutines();
        scrapArmed = false;

        if (selectPage != null) selectPage.SetActive(true);
        if (openingPage != null) openingPage.SetActive(false);
        if (revealGroup != null) revealGroup.SetActive(false);
        if (revealStage != null) revealStage.Drawing = false;

        EnsureStages();
        crateStage.Camera.fieldOfView = 30f;
        for (int i = 0; i < rigs.Length; i++)
        {
            if (rigs[i] == null)
                continue;
            rigs[i].Reset();
            rigs[i].Show(true);
            rigs[i].SetHome(SpotOnStage(chestSpots[i]) + baseOffsets[i]);
        }

        Afford();
    }

    void Afford()
    {
        for (int i = 0; i < cards.Length && i < CrateInfo.All.Length; i++)
        {
            if (cards[i] != null)
                cards[i].SetAffordable(PlayerWallet.Tokens >= CrateInfo.All[i].Cost, CrateInfo.All[i].Cost - PlayerWallet.Tokens);
        }

        if (openAnotherButton != null && lastCrate.Name != null)
            openAnotherButton.interactable = PlayerWallet.Tokens >= lastCrate.Cost;
    }

    void Hover(int index, bool on)
    {
        if (!busy && index < rigs.Length && rigs[index] != null)
            rigs[index].SetHover(on);
    }

    void Update()
    {
        if (!IsOpen)
            return;

        if (busy && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
            skip = true;

        if (!busy && Input.GetKeyDown(KeyCode.Escape))
            Close();

        if (revealRays != null && revealRays.gameObject.activeInHierarchy)
            revealRays.transform.Rotate(0f, 0f, -18f * Time.unscaledDeltaTime, Space.Self);
        if (raysA != null) raysA.rectTransform.Rotate(0f, 0f, -14f * Time.unscaledDeltaTime);
        if (raysB != null) raysB.rectTransform.Rotate(0f, 0f, 9f * Time.unscaledDeltaTime);

        if (scrapArmed && Time.unscaledTime > scrapArmedUntil)
        {
            scrapArmed = false;
            DrawResultActions();
        }
    }

    // ---------------------------------------------------------------- the stages

    void EnsureStages()
    {
        if (crateStage != null)
            return;

        Rect view = stageView != null ? stageView.rectTransform.rect : new Rect(0, 0, 1920, 1080);
        int width = 1600;
        int height = Mathf.Max(64, Mathf.RoundToInt(width * view.height / Mathf.Max(view.width, 1f)));

        crateStage = PreviewStage.Create("crates", width, height, Backdrop);
        crateStage.spin = 0f;
        crateStage.Camera.transform.localPosition = new Vector3(0f, 0.45f, -StageDepth);
        crateStage.Camera.transform.localRotation = Quaternion.identity;
        crateStage.Camera.fieldOfView = 30f;
        if (stageView != null)
            stageView.texture = crateStage.Target;

        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < rigs.Length && i < chestModels.Length; i++)
        {
            Vector3 spot = SpotOnStage(chestSpots[i]);
            GameObject chest = crateStage.Place(chestModels[i], spot, 195f, 0.5f);
            if (chest == null)
                continue;

            baseOffsets[i] = chest.transform.localPosition - spot;
            WeaponSkins.ApplyToModel(chest, i < chestLooks.Length ? chestLooks[i] : null);
            PreviewStage.SetLayer(chest.transform, PreviewStage.Layer);
            rigs[i] = ChestRig.On(chest, CrateInfo.All[i].Colour, glowSprite);
            PreviewStage.SetLayer(crateStage.transform, PreviewStage.Layer);
        }

        revealStage = PreviewStage.Create("reveal", 900, 700, Backdrop);
        revealStage.spin = 40f;
        revealStage.Drawing = false;
        if (revealView != null)
            revealView.texture = revealStage.Target;

        // The showcase behind the weapon: a soft glow and rays turning, in the rarity's colour.
        revealGlow = StageFx.Glow(revealStage.transform, new Vector3(0f, 0f, 1.6f), glowSprite, Color.white, 3.4f);
        revealRays = StageFx.Glow(revealStage.transform, new Vector3(0f, 0f, 1.5f), raySprite != null ? raySprite : starSprite, Color.white, 4.2f);
    }

    /// Where a point on the screen is on the crate stage, at the chests' depth.
    Vector3 SpotOnStage(RectTransform spot) =>
        spot != null ? StageAt(Normalised(spot)) : Vector3.zero;

    Vector2 Normalised(RectTransform spot)
    {
        RectTransform view = stageView.rectTransform;
        Vector3 local = view.InverseTransformPoint(spot.position);
        Rect rect = view.rect;
        return new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
    }

    Vector3 StageAt(Vector2 viewport)
    {
        Vector3 world = crateStage.Camera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, StageDepth));
        return crateStage.transform.InverseTransformPoint(world);
    }

    // ---------------------------------------------------------------- opening

    /// Pays and opens. Kept by this name - the menu checks and photographers call it.
    void TryOpen(CrateInfo crate)
    {
        if (busy || crate.Name == null)
            return;

        if (!PlayerWallet.Spend(crate.Cost))
        {
            GameAudio.Play2D(GameAudio.UI, "error_001", GameAudio.UiVolume);
            Juice.Shake(0.2f);
            return;
        }

        StopAllCoroutines();
        StartCoroutine(RunOpening(crate));
    }

    IEnumerator RunOpening(CrateInfo crate)
    {
        busy = true;
        skip = false;
        scrapArmed = false;
        lastCrate = crate;

        int tier = System.Array.IndexOf(CrateInfo.All, crate);
        ChestRig rig = tier >= 0 && tier < rigs.Length ? rigs[tier] : null;

        // Decided now, and yours now - before anything has moved.
        WeaponFinish won = crate.RollFinish();
        int copies = SkinInventory.Grant(won);
        lastFinish = won;

        EnsureStages();
        if (selectPage != null) selectPage.SetActive(false);
        if (openingPage != null) openingPage.SetActive(true);
        if (revealGroup != null) revealGroup.SetActive(false);
        if (reel != null) reel.gameObject.SetActive(false);
        if (skipHint != null) skipHint.gameObject.SetActive(false);
        SetAlpha(flash, 0f);
        SetAlpha(dim, 0f);
        if (openingTitle != null)
        {
            openingTitle.text = crate.Name.ToUpperInvariant();
            openingTitle.color = crate.Colour;
        }

        if (rig != null)
        {
            // Everything else drops away; this one takes the middle and the camera leans in.
            for (int i = 0; i < rigs.Length; i++)
            {
                if (rigs[i] != null && rigs[i] != rig)
                    StartCoroutine(DropAway(rigs[i]));
            }

            rig.SetHover(false);
            Vector3 from = rig.Home;
            Vector3 centre = StageAt(new Vector2(0.5f, 0.3f)) + baseOffsets[tier];
            yield return UiMotion.Tween(0.55f, UiMotion.InOutCubic, k =>
            {
                rig.SetHome(Vector3.Lerp(from, centre, k));
                crateStage.Camera.fieldOfView = Mathf.Lerp(30f, 18f, k);
                SetAlpha(dim, 0.3f * k);
            });

            // Three jolts, each harder, dust and light getting out.
            for (int jolt = 0; jolt < 3; jolt++)
            {
                float strength = (jolt + 1) / 3f;
                rig.Rattle(strength);
                GameAudio.PlayPitched(GameAudio.WallSmash, $"impactPlank_medium_00{jolt % 3}", GameAudio.WallSmashVolume * (0.7f + 0.3f * strength), 0.85f + 0.15f * jolt);
                if (jolt == 2)
                    GameAudio.Play2D(GameAudio.Slam, GameAudio.ExplosionVolume * 0.35f);
                Juice.Shake(0.06f + 0.12f * strength);

                Vector3 at = rig.transform.localPosition;
                StageFx.Burst(crateStage.transform, at + new Vector3(0f, 0.05f, -0.1f), smokeSprite, new Color(0.6f, 0.55f, 0.5f),
                              5 + 4 * jolt, 0.5f, 0.35f, 0.7f, glows: false, gravity: -0.05f);
                StageFx.Burst(crateStage.transform, at + new Vector3(0f, 0.5f, -0.1f), sparkSprite, crate.Colour,
                              3 + 5 * jolt, 1.4f, 0.14f, 0.4f, gravity: 0.3f);

                yield return UiMotion.Wait(0.38f + 0.1f * jolt);
            }

            // It goes.
            GameAudio.Play2D(GameAudio.Explosion, GameAudio.ExplosionVolume * 0.4f);
            GameAudio.Play2D(GameAudio.Shield, GameAudio.ShieldVolume * 0.6f);
            GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);
            Juice.Shake(0.4f);
            Juice.Hit(0.6f);
            StartCoroutine(rig.Burst());

            Vector3 lid = rig.transform.localPosition + new Vector3(0f, 0.55f, -0.1f);
            StageFx.Burst(crateStage.transform, lid, starSprite, crate.Colour, 70, 3.2f, 0.22f, 1.1f, gravity: 0.25f);
            StageFx.Burst(crateStage.transform, lid, glowSprite, Color.Lerp(crate.Colour, Color.white, 0.5f), 3, 0.2f, 2.6f, 0.6f, gravity: 0f);
            StageFx.Coins(crateStage.transform, lid, coinModel, 14 + tier * 10, 3.4f, 0.16f);
            StartCoroutine(Flash(Color.Lerp(Color.white, crate.Colour, 0.3f), 0.45f));

            yield return UiMotion.Wait(0.75f);
        }

        // The reel.
        BuildReel(crate, won);
        if (reel != null)
        {
            reel.gameObject.SetActive(true);
            Vector2 rest = reel.anchoredPosition;
            GameAudio.Play2D(GameAudio.Vine, "swish-10", GameAudio.UiVolume);
            yield return UiMotion.Tween(0.35f, UiMotion.OutQuint, k =>
            {
                reel.anchoredPosition = rest + new Vector2(0f, -420f * (1f - k));
                SetAlpha(dim, Mathf.Lerp(0.3f, 0.78f, k));
            });
            reel.anchoredPosition = rest;
        }

        if (skipHint != null) skipHint.gameObject.SetActive(true);
        skip = false;
        yield return SpinReel();
        if (skipHint != null) skipHint.gameObject.SetActive(false);

        yield return Reveal(won, copies);
        busy = false;
    }

    IEnumerator DropAway(ChestRig rig)
    {
        Vector3 from = rig.Home;
        yield return UiMotion.Tween(0.4f, UiMotion.InQuad, k => rig.SetHome(from + Vector3.down * 2.5f * k));
        rig.Show(false);
    }

    IEnumerator Flash(Color colour, float seconds)
    {
        if (flash == null)
            yield break;
        flash.color = new Color(colour.r, colour.g, colour.b, 1f);
        yield return UiMotion.Tween(seconds, UiMotion.OutQuad, k => SetAlpha(flash, 1f - k));
    }

    static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
            return;
        Color c = graphic.color;
        graphic.color = new Color(c.r, c.g, c.b, alpha);
    }

    // ---------------------------------------------------------------- the reel

    float landingOffset;

    void BuildReel(CrateInfo crate, WeaponFinish won)
    {
        foreach (FinishCard card in reelCards)
            Destroy(card.gameObject);
        reelCards.Clear();

        if (reelCardTemplate == null || reelStrip == null)
            return;

        float step = CardWidth + CardGap;
        for (int i = 0; i < ReelLength; i++)
        {
            // The winner where it lands; everything else drawn from the same odds, honestly.
            WeaponFinish shown = i == LandingIndex ? won : crate.RollFinish();
            FinishCard card = Instantiate(reelCardTemplate, reelStrip);
            card.gameObject.SetActive(true);
            card.Bind(shown, "Rifle", 0, false, null);
            card.Rect.anchoredPosition = new Vector2(i * step + CardWidth * 0.5f, 0f);
            reelCards.Add(card);
        }

        // Somewhere across the winning card, not always dead centre - a real reel stops where it stops.
        landingOffset = Random.Range(-0.38f, 0.38f) * CardWidth;
        reelStrip.anchoredPosition = Vector2.zero;
    }

    IEnumerator SpinReel()
    {
        if (reelStrip == null || reelViewport == null)
            yield break;

        float step = CardWidth + CardGap;
        float centre = reelViewport.rect.width * 0.5f;
        float target = LandingIndex * step + CardWidth * 0.5f + landingOffset - centre;
        int lastTick = -1;
        float x = 0f;

        void Place(float at, float speedFraction)
        {
            x = at;
            reelStrip.anchoredPosition = new Vector2(-at, 0f);

            int under = Mathf.FloorToInt((at + centre) / step);
            if (under != lastTick && under >= 0 && under < reelCards.Count)
            {
                lastTick = under;
                GameAudio.PlayPitched(GameAudio.UI, "click_001", GameAudio.UiVolume * 0.55f, 0.8f + speedFraction * 0.7f);
                if (pointer != null)
                    pointer.rectTransform.localScale = new Vector3(1.4f, 1f, 1f);
            }

            // The card under the pointer lifts.
            for (int i = Mathf.Max(0, under - 4); i < Mathf.Min(reelCards.Count, under + 5); i++)
            {
                float middle = i * step + CardWidth * 0.5f - at;
                float near = 1f - Mathf.Clamp01(Mathf.Abs(middle - centre) / step);
                reelCards[i].Rect.localScale = Vector3.one * (1f + 0.1f * near);
            }

            if (pointer != null)
                pointer.rectTransform.localScale = Vector3.Lerp(pointer.rectTransform.localScale, Vector3.one, 0.3f);
        }

        float t = 0f;
        while (t < SpinSeconds && !skip)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / SpinSeconds);
            float eased = UiMotion.OutQuint(k);
            Place(Mathf.Lerp(0f, target, eased), 1f - eased);
            yield return null;
        }

        if (skip)
        {
            float from = x;
            yield return UiMotion.Tween(0.35f, UiMotion.OutCubic, k => Place(Mathf.Lerp(from, target, k), 1f - k));
        }

        Place(target, 0f);
    }

    // ---------------------------------------------------------------- the reveal

    IEnumerator Reveal(WeaponFinish won, int copies)
    {
        CrateRarity rarity = won != null ? won.rarity : CrateRarity.Scrap;
        Color colour = CrateRarityInfo.ColorFor(rarity);
        float weight = CrateRarityInfo.Weight(rarity);

        // The winner pops out of the reel.
        FinishCard winner = LandingIndex < reelCards.Count ? reelCards[LandingIndex] : null;
        if (winner != null)
        {
            yield return UiMotion.Tween(0.28f, UiMotion.OutBack, k => winner.Rect.localScale = Vector3.one * Mathf.LerpUnclamped(1.1f, 1.45f, k));
            yield return UiMotion.Wait(0.12f + 0.25f * weight);
        }

        StartCoroutine(Flash(colour, 0.55f));
        if (reel != null) reel.gameObject.SetActive(false);
        ShowResult(won, copies);

        Juice.Shake(0.1f + 0.45f * weight);
        Juice.Hit(0.3f + 0.7f * weight);
        GameAudio.Play2D(GameAudio.Hit, "headshot", GameAudio.HitVolume * (0.6f + 0.4f * weight));
        if (rarity >= CrateRarity.Primal)
            GameAudio.Play2D(GameAudio.Shield, GameAudio.ShieldVolume * 0.7f);
        if (rarity >= CrateRarity.Mythic)
            GameAudio.Play2D(GameAudio.Kill, GameAudio.KillVolume);
        if (rarity >= CrateRarity.Apex)
            GameAudio.Play2D(GameAudio.Explosion, GameAudio.ExplosionVolume * 0.5f);

        // Light round the weapon, more the rarer.
        Vector3 middle = revealStage.Turntable.localPosition;
        StageFx.Burst(revealStage.transform, middle, starSprite, colour, 20 + Mathf.RoundToInt(70 * weight), 2.2f + weight, 0.16f, 1.1f, gravity: 0.2f);
        if (rarity >= CrateRarity.Mythic)
        {
            yield return UiMotion.Wait(0.3f);
            StageFx.Burst(revealStage.transform, middle, sparkSprite, Color.Lerp(colour, Color.white, 0.4f), 50, 3.2f, 0.2f, 0.9f, gravity: 0.1f);
        }
        if (rarity >= CrateRarity.Apex)
        {
            StageFx.Burst(revealStage.transform, middle + Vector3.up * 1.2f, confettiSprite, Color.white, 140, 1.4f, 0.09f, 2.6f,
                          glows: false, gravity: 0.35f, rainbow: true);
        }

        yield return AnimateResult(weight, copies);
    }

    /// The result's text and buttons, filled in - the photographers call this straight.
    public void ShowResult(WeaponFinish won, int copies)
    {
        busy = false;
        lastFinish = won;
        if (selectPage != null) selectPage.SetActive(false);
        if (openingPage != null) openingPage.SetActive(true);
        if (revealGroup != null) revealGroup.SetActive(true);
        if (reel != null) reel.gameObject.SetActive(false);
        SetAlpha(dim, 0.78f);

        CrateRarity rarity = won != null ? won.rarity : CrateRarity.Scrap;
        Color colour = CrateRarityInfo.ColorFor(rarity);
        float weight = CrateRarityInfo.Weight(rarity);

        EnsureStages();
        revealStage.Drawing = true;
        string[] weapons = WeaponLoadout.Everything;
        revealStage.ShowWeapon(weapons[Random.Range(0, weapons.Length)], won, 1f);
        PreviewStage.SetLayer(revealStage.Turntable, PreviewStage.Layer);
        if (revealGlow != null) revealGlow.material.SetColor("_TintColor", new Color(colour.r, colour.g, colour.b, 0.35f + 0.4f * weight));
        if (revealRays != null) revealRays.material.SetColor("_TintColor", new Color(colour.r, colour.g, colour.b, 0.2f + 0.45f * weight));
        if (revealFrame != null) revealFrame.color = colour;

        if (raysA != null) raysA.color = new Color(colour.r, colour.g, colour.b, 0.25f + 0.3f * weight);
        if (raysB != null) raysB.color = new Color(colour.r, colour.g, colour.b, 0.15f + 0.25f * weight);
        if (revealName != null) revealName.text = won != null ? won.displayName.ToUpperInvariant() : "";
        if (revealRarity != null)
        {
            revealRarity.text = CrateRarityInfo.NameFor(rarity);
            revealRarity.color = colour;
        }
        if (revealRarityBar != null) revealRarityBar.color = colour;
        if (revealFlavour != null) revealFlavour.text = won != null ? won.flavour : "";
        if (newBadge != null) newBadge.gameObject.SetActive(copies <= 1);
        if (countText != null) countText.text = copies <= 1 ? "NEW FINISH - WORKS ON EVERY WEAPON" : $"YOU HAVE {copies} NOW";
        if (collectionText != null) collectionText.text = $"COLLECTION  {SkinInventory.Owned().Count} / {FinishCatalog.All.Count}";

        scrapArmed = false;
        DrawResultActions();
    }

    IEnumerator AnimateResult(float weight, int copies)
    {
        if (actions != null) actions.alpha = 0f;

        RectTransform view = revealView != null ? revealView.rectTransform : null;
        RectTransform name = revealName != null ? revealName.rectTransform : null;

        yield return UiMotion.Tween(0.5f, UiMotion.OutBack, k =>
        {
            if (view != null) view.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, k);
            if (name != null) name.localScale = Vector3.one * Mathf.LerpUnclamped(1.8f, 1f, k);
        });

        if (newBadge != null && copies <= 1)
            StartCoroutine(Wiggle(newBadge.rectTransform));

        yield return UiMotion.Wait(0.15f + 0.35f * weight);

        if (actions != null)
        {
            RectTransform row = (RectTransform)actions.transform;
            Vector2 rest = row.anchoredPosition;
            yield return UiMotion.Tween(0.3f, UiMotion.OutCubic, k =>
            {
                actions.alpha = k;
                row.anchoredPosition = rest + new Vector2(0f, -30f * (1f - k));
            });
        }
    }

    IEnumerator Wiggle(RectTransform badge)
    {
        float until = Time.unscaledTime + 30f;
        while (badge != null && badge.gameObject.activeInHierarchy && Time.unscaledTime < until)
        {
            float t = Time.unscaledTime;
            badge.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 6f) * 7f);
            badge.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 9f));
            yield return null;
        }
    }

    void DrawResultActions()
    {
        int have = SkinInventory.Count(lastFinish);

        if (scrapButton != null) scrapButton.interactable = have > 0;
        if (scrapLabel != null && lastFinish != null)
            scrapLabel.text = have <= 0 ? "SCRAPPED" : scrapArmed ? $"SURE? +{lastFinish.ScrapValue}" : $"SCRAP  +{lastFinish.ScrapValue}";

        if (equipAllButton != null) equipAllButton.interactable = have > 0;
        if (equipAllLabel != null) equipAllLabel.text = "EQUIP ON ALL";

        if (openAnotherLabel != null && lastCrate.Name != null)
            openAnotherLabel.text = $"OPEN ANOTHER  {lastCrate.Cost}";
        if (openAnotherButton != null && lastCrate.Name != null)
            openAnotherButton.interactable = PlayerWallet.Tokens >= lastCrate.Cost;
    }

    void EquipEverywhere()
    {
        if (lastFinish == null)
            return;
        SkinInventory.EquipEverywhere(lastFinish);
        GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);
        Juice.Hit(0.3f);
        if (equipAllLabel != null) equipAllLabel.text = "ON EVERY WEAPON";
    }

    void ScrapResult()
    {
        if (lastFinish == null || SkinInventory.Count(lastFinish) <= 0)
            return;

        if (!scrapArmed)
        {
            scrapArmed = true;
            scrapArmedUntil = Time.unscaledTime + 3f;
            GameAudio.Play2D(GameAudio.UI, "error_001", GameAudio.UiVolume * 0.6f);
            DrawResultActions();
            return;
        }

        scrapArmed = false;
        if (SkinInventory.Scrap(lastFinish))
        {
            GameAudio.Play2D(GameAudio.Hit, "hit", GameAudio.HitVolume * 0.6f);
            GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);
            Juice.Hit(0.35f);
            if (countText != null)
                countText.text = $"SCRAPPED FOR {lastFinish.ScrapValue} TOKENS";
        }

        DrawResultActions();
    }
}
