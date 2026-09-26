using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crate shop and the opening ceremony itself - three crates, spend tokens, watch a
/// scrolling carousel decelerate onto one of five rarity tiers. No actual items exist yet
/// ("no rewards for now") - this builds the whole experience the brief asked for so real items
/// have somewhere to land later without touching this screen again.
///
/// Researched rather than guessed: real case-opening games spin for several seconds before
/// landing (CS2's own runs close to six), because the anticipation *during* the spin is most of
/// the actual payoff - a Stanford study (Knutson) found the dopamine response peaks during
/// anticipation of a reward, not on receiving it, which is exactly why the spin is long and the
/// landing itself is comparatively quick. One thing deliberately left out of that same research:
/// "near-miss" rigging, where the reel is engineered to almost-but-not-quite land on a rare item
/// to manufacture false hope. CrateInfo.Roll() decides the real result up front and every filler
/// card around it is drawn from the same honest odds table - nothing here is staged to look
/// closer than it is.
/// </summary>
public class CrateOpeningScreen : MonoBehaviour
{
    /// Same singleton-on-a-persistent-object shape SettingsMenu already uses - instantiated onto
    /// RoomManager once (see RoomManager.cs), which survives the trip between the menu and the
    /// game, so a button anywhere just needs CrateOpeningScreen.Instance.Open() rather than a
    /// scene reference to something that doesn't exist yet when the scene loads.
    public static CrateOpeningScreen Instance { get; private set; }

    /// Everything actually visible lives under this, separate from the root the component
    /// itself sits on. The root has to stay active permanently - Unity never calls Awake on an
    /// inactive GameObject, including one instantiated already-inactive, so a first cut of this
    /// that deactivated the *root* to "close" the screen meant Instance never got set at all and
    /// every button click logged "RoomManager never instantiated the screen" despite it having
    /// done exactly that. SettingsMenu already gets this right the same way (its own `panel`
    /// field) - this just hadn't copied that part of the shape, only the Instance/Open() half.
    [SerializeField] GameObject panel;

    [Header("Pages")]
    [SerializeField] GameObject selectPage;
    [SerializeField] GameObject openingPage;

    [Header("Selection")]
    [SerializeField] TMP_Text tokenBalanceText;
    [SerializeField] CrateButton rottenButton;
    [SerializeField] CrateButton ripeButton;
    [SerializeField] CrateButton holyButton;

    [Header("Carousel")]
    [SerializeField] RectTransform carouselViewport;
    [SerializeField] RectTransform carouselStrip;
    [SerializeField] RectTransform cardTemplate;
    [SerializeField] RectTransform landingPointer;

    [Header("Reveal")]
    [SerializeField] GameObject resultPanel;
    [SerializeField] TMP_Text resultRarityText;
    [SerializeField] Image resultGlow;
    [SerializeField] Image resultCardImage;
    [SerializeField] Button openAnotherButton;
    [SerializeField] Button closeButton;

    const float CardWidth = 190f;
    const float CardSpacing = 18f;
    const int FillerCount = 56;

    // Not the very last card - a spin that used every card it built would look like it ran out
    // of room rather than like it chose to stop. Leaves a handful visible sliding past afterward.
    const int LandingIndex = 48;

    const float SpinDuration = 5.6f;

    readonly List<RectTransform> spawnedCards = new List<RectTransform>();

    bool spinning;

    void Awake()
    {
        Instance = this;
        PlayerWallet.Changed += RefreshBalance;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        PlayerWallet.Changed -= RefreshBalance;
    }

    /// A button anywhere just calls CrateOpeningScreen.Instance.Open() - same call shape
    /// SettingsMenu.Open() already has. Toggles `panel`, not this object's own root - see
    /// panel's own field comment for why that distinction matters here specifically.
    public void Open()
    {
        if (panel != null)
            panel.SetActive(true);

        RefreshBalance();
        ShowSelectPage();
    }

    /// Matches SettingsMenu.Close()'s own shape - the root stays active, only `panel` hides.
    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);

        // Defensive: nothing in the current UI can close this mid-spin (the close button only
        // exists on selectPage, which isn't showing during one), but if `panel` is ever hidden
        // by something else while a spin is running, Unity stops every coroutine on a
        // deactivated GameObject regardless of cause - which would skip RunOpening's own
        // `spinning = false` at the end and leave that guard stuck true forever, permanently
        // refusing every future TryOpen/ShowSelectPage call (both check it) even after
        // reopening. One line now is cheaper than tracking down "crates stopped working" later.
        spinning = false;
    }

    void Start()
    {
        if (rottenButton != null) rottenButton.Bind(CrateInfo.Rotten, () => TryOpen(CrateInfo.Rotten));
        if (ripeButton != null) ripeButton.Bind(CrateInfo.Ripe, () => TryOpen(CrateInfo.Ripe));
        if (holyButton != null) holyButton.Bind(CrateInfo.Holy, () => TryOpen(CrateInfo.Holy));

        if (openAnotherButton != null)
            openAnotherButton.onClick.AddListener(ShowSelectPage);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    void RefreshBalance()
    {
        if (tokenBalanceText != null)
            tokenBalanceText.text = $"{PlayerWallet.Tokens} TOKENS";

        bool canAfford(CrateInfo crate) => PlayerWallet.Tokens >= crate.Cost;

        rottenButton?.SetAffordable(canAfford(CrateInfo.Rotten));
        ripeButton?.SetAffordable(canAfford(CrateInfo.Ripe));
        holyButton?.SetAffordable(canAfford(CrateInfo.Holy));
    }

    void ShowSelectPage()
    {
        if (spinning)
            return;

        if (selectPage != null) selectPage.SetActive(true);
        if (openingPage != null) openingPage.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);

        RefreshBalance();
    }

    void TryOpen(CrateInfo crate)
    {
        if (spinning)
            return;

        if (!PlayerWallet.Spend(crate.Cost))
        {
            // Insufficient funds - a shake and a denial sound rather than nothing happening,
            // same "wrong should sound wrong" philosophy GameAudio.UI's error clip already exists
            // for.
            GameAudio.Play2D(GameAudio.UI, "error_001", GameAudio.UiVolume);
            Juice.Shake(0.2f);
            return;
        }

        if (selectPage != null) selectPage.SetActive(false);
        if (openingPage != null) openingPage.SetActive(true);
        if (resultPanel != null) resultPanel.SetActive(false);

        StartCoroutine(RunOpening(crate));
    }

    IEnumerator RunOpening(CrateInfo crate)
    {
        spinning = true;

        CrateRarity result = crate.Roll();
        BuildCarousel(crate, result);

        yield return SpinCarousel();

        Reveal(result);
        spinning = false;
    }

    void BuildCarousel(CrateInfo crate, CrateRarity result)
    {
        // Undoes Reveal's own hide from the previous spin, if this isn't the first one this
        // session.
        if (carouselViewport != null)
            carouselViewport.gameObject.SetActive(true);

        foreach (RectTransform card in spawnedCards)
            Destroy(card.gameObject);

        spawnedCards.Clear();

        if (cardTemplate == null || carouselStrip == null)
            return;

        float step = CardWidth + CardSpacing;

        for (int i = 0; i < FillerCount; i++)
        {
            // The landing card is the one honest result this crate actually rolled - everything
            // else is filler drawn from the same odds table, not a uniform shuffle, so a cheap
            // crate's spin genuinely looks mostly grey the way its real odds say it should.
            CrateRarity shown = i == LandingIndex ? result : crate.Roll();

            RectTransform card = Instantiate(cardTemplate, carouselStrip);
            card.gameObject.SetActive(true);
            card.anchoredPosition = new Vector2(i * step, 0f);

            Image cardImage = card.GetComponentInChildren<Image>();
            TMP_Text cardLabel = card.GetComponentInChildren<TMP_Text>();

            Color tint = CrateRarityInfo.ColorFor(shown);

            if (cardImage != null)
                cardImage.color = tint;

            if (cardLabel != null)
            {
                cardLabel.text = CrateRarityInfo.NameFor(shown);
                cardLabel.color = tint;
            }

            spawnedCards.Add(card);
        }

        carouselStrip.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Fast, then a long, smooth decelerate onto the landing card - a quintic ease-out rather
    /// than linear-then-stop, so slowing down reads as a wheel losing momentum rather than
    /// hitting a wall. The landing pointer stays fixed at the viewport's own centre; the strip
    /// moves under it, same as every real case-opening reel works.
    /// </summary>
    IEnumerator SpinCarousel()
    {
        float step = CardWidth + CardSpacing;
        float viewportCentre = carouselViewport != null ? carouselViewport.rect.width * 0.5f : 0f;

        // The landing card's centre has to end up under the pointer - the strip starts at X=0
        // (card 0's own centre there) and needs to move left by exactly this much.
        float targetX = LandingIndex * step - viewportCentre + CardWidth * 0.5f;

        float t = 0f;
        int lastTickedCard = -1;

        while (t < SpinDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / SpinDuration);

            // Quintic ease-out: 1-(1-k)^5. Steep at the start, a long soft tail at the end -
            // tried cubic first and it still felt like it "arrived" rather than "settled".
            float eased = 1f - Mathf.Pow(1f - k, 5f);

            float x = Mathf.Lerp(0f, targetX, eased);

            if (carouselStrip != null)
                carouselStrip.anchoredPosition = new Vector2(-x, 0f);

            // A tick every time a new card crosses the pointer, not on a fixed timer - ties the
            // sound directly to what's on screen instead of two systems drifting out of sync as
            // the real speed decays under the easing curve.
            int currentCard = Mathf.FloorToInt((x + viewportCentre) / step);

            if (currentCard != lastTickedCard && currentCard >= 0 && currentCard < spawnedCards.Count)
            {
                lastTickedCard = currentCard;

                // Pitch drifts down as the spin slows (1-eased, not the raw k that drives
                // position - this needs to track how fast the strip is *actually* still moving,
                // which quintic ease-out makes nonlinear), so the tick itself sells the
                // deceleration even for someone not watching the strip closely.
                float speedFraction = 1f - eased;
                GameAudio.PlayPitched(GameAudio.UI, "click_001", GameAudio.UiVolume * 0.6f,
                                     0.85f + speedFraction * 0.5f);
            }

            yield return null;
        }

        if (carouselStrip != null)
            carouselStrip.anchoredPosition = new Vector2(-targetX, 0f);
    }

    /// <summary>
    /// The payoff. Scaled by CrateRarityInfo.Weight rather than a separate hand-built sequence
    /// per tier - the same "bigger moments get more of the existing budget" rule this project
    /// already applies everywhere else (a headshot over a body shot, a kill over a hit), turned
    /// into one formula instead of five hand-tuned copies.
    /// </summary>
    void Reveal(CrateRarity result)
    {
        if (resultPanel != null)
            resultPanel.SetActive(true);

        // The carousel and its landing pointer stay visible right up to this point on purpose -
        // watching the spin decelerate onto the actual result is the whole point of a carousel.
        // But a real screenshot showed the pointer line still faintly visible behind the reveal,
        // bleeding through resultGlow's own partial transparency - the reel needs to be fully
        // gone once the result is showing, not just dimmed under it.
        if (carouselViewport != null)
            carouselViewport.gameObject.SetActive(false);

        Color tint = CrateRarityInfo.ColorFor(result);
        float weight = CrateRarityInfo.Weight(result);

        if (resultRarityText != null)
        {
            resultRarityText.text = CrateRarityInfo.NameFor(result);
            resultRarityText.color = tint;
        }

        if (resultCardImage != null)
            resultCardImage.color = tint;

        if (resultGlow != null)
            resultGlow.color = new Color(tint.r, tint.g, tint.b, 0.25f + weight * 0.35f);

        Juice.Shake(0.15f + weight * 0.45f);
        Juice.Hit(0.3f + weight * 0.7f);

        // Escalating sound layers, not just louder - Hit alone for a plain result, Hit plus Kill
        // stacked under it for the top two tiers, the same "one bang alone reads as a tap, a bang
        // plus a low layer under it reads as something with actual mass" reasoning WallSmash's
        // own sound already uses.
        GameAudio.Play2D(GameAudio.Hit, GameAudio.HitVolume * (0.6f + weight * 0.4f));

        if (weight >= CrateRarityInfo.Weight(CrateRarity.Mythic))
            GameAudio.Play2D(GameAudio.Kill, GameAudio.KillVolume);
    }
}
