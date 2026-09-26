using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// The in-game HUD, as real objects.
///
/// Replaces the two IMGUI scripts that drew everything from code. Those worked, but every
/// position, size, colour and font was a number in a source file, so nothing could be moved
/// without editing C# and nothing could be seen without entering play mode. It's now a Canvas
/// full of ordinary Images and TMP_Texts that can be dragged, recoloured and re-fonted like
/// anything else in the scene.
///
/// This script decides what the elements say and whether they're visible. Where they sit and
/// what they look like belongs to the scene, and nothing here should fight that. The two
/// deliberate exceptions are the scope, which has to track the window's aspect ratio, and the
/// crosshair, which opens and closes with the weapon's spread.
///
/// Anything that appears and disappears - feed lines, damage numbers, standings - comes from a
/// pool grown off a hidden template rather than being created per event. A shotgun lands nine
/// hits on one trigger pull and a busy match writes a feed line every couple of seconds;
/// allocating for each would make the best part of the game the reason it stutters.
/// </summary>
public class GameHud : MonoBehaviour
{
    public static GameHud Instance { get; private set; }

    [Header("Health")]
    [SerializeField] RectTransform healthTrack;
    [SerializeField] Image healthFill;
    [SerializeField] Image healthTrail;
    [SerializeField] Image healthShield;
    [SerializeField] TMP_Text healthNumber;
    [SerializeField] TMP_Text streakText;
    [SerializeField] TMP_Text healText;

    [Header("Ammo")]
    [SerializeField] TMP_Text weaponName;
    [SerializeField] TMP_Text ammoNumber;
    [SerializeField] TMP_Text spareNumber;

    [Header("Crosshair")]
    [SerializeField] RectTransform crosshairUp;
    [SerializeField] RectTransform crosshairDown;
    [SerializeField] RectTransform crosshairLeft;
    [SerializeField] RectTransform crosshairRight;
    [SerializeField] Image hitMarker;
    [SerializeField] Image crosshairDot;

    [Header("Scope")]
    [SerializeField] GameObject scope;
    [SerializeField] Image scopeGlass;
    [SerializeField] RectTransform scopeLeft;
    [SerializeField] RectTransform scopeRight;

    [Header("Match")]
    [SerializeField] TMP_Text clock;
    [SerializeField] TMP_Text modeLabel;
    [SerializeField] TMP_Text centreTitle;
    [SerializeField] TMP_Text centreSubtitle;
    [SerializeField] TMP_Text comboText;

    /// The unified style score's rank name. Field name kept from the slide-only combo it used
    /// to be - see StyleScore.cs and UpdateStyleMeter - rather than renamed, so an already-wired
    /// scene reference doesn't need re-wiring for what is the same GameObject doing a bigger job.
    [SerializeField] TMP_Text slideCombo;

    /// The meter under the rank name - an ULTRAKILL/DMC style bar rather than just a word
    /// changing four times. Reads StyleScore.DecayFraction now, same shape it always drew.
    [SerializeField] Image slideMeterFill;

    /// The breakdown list under the rank line - "1.35x HEADSHOT", "1.5x NO SCOPE" and so on for
    /// whichever bonuses actually fired on the last kill. Reported directly: the multiplier's
    /// contributing factors needed to be visible, not folded into one number with nothing to
    /// show for it.
    [SerializeField] RectTransform breakdownContainer;
    [SerializeField] TMP_Text breakdownTemplate;

    /// The movement-tech combo - MovementCombo.cs, bottom left. A second, separate meter from
    /// the style score above: grapples, grenade jumps, bhops and slide hops all feed this one
    /// chain instead of each getting their own counter, and it never affects the score.
    [SerializeField] TMP_Text movementCombo;
    [SerializeField] Image movementMeterFill;

    /// The slide-chain exhaustion notice - "you can't slide-hop again yet, here's how long."
    /// Used to borrow the style meter's own rank text for this (the one piece of the old
    /// slide-only combo meter worth keeping standalone), but that meant it could only ever show
    /// up in the one spot the style meter itself occupies and read as part of that system rather
    /// than its own thing. Reported directly: "add the spent... to the right, not connected to
    /// either combo bar." Its own element now, independent of both.
    [SerializeField] TMP_Text spentText;

    // Drives the punch on a rank-up. Not serialised - purely runtime state for UpdateStyleMeter.
    string lastStyleRank = string.Empty;
    float slidePunchUntil = -99f;

    // Same trick, for the movement combo - see UpdateMovementCombo.
    string lastComboTech = string.Empty;
    float comboPunchUntil = -99f;

    // Same trick, two more places. UpdateSlideCombo was the only thing on the HUD that punched
    // on a change instead of just snapping to a new value - added 2026-08-22 to the two other
    // numbers that change constantly enough to want it: health dropping and ammo counting down.
    int lastHealthPoints = -1;
    float healthPunchUntil = -99f;

    // The bananameter's inertia - eases down to catch up with a drop rather than snapping with
    // the live reading, so a big hit reads as a bite taken out of the banana instead of the
    // whole bar just being a smaller rectangle a frame later. Starts at 1 so a fresh spawn at
    // full health doesn't show a trail catching up from zero.
    float healthTrailFraction = 1f;
    int lastAmmoCount = -1;
    bool lastReloading;
    float ammoPunchUntil = -99f;

    // Drives the punch-in on the big centre callout - reported as the one piece of text on the
    // whole HUD with no reaction to it at all, arriving and leaving at a flat scale of 1 while
    // everything else (the hitmarker, damage numbers, the ammo/health groups, the slide rank)
    // already arrives big and settles. Set alongside killFlash rather than replacing it - the
    // flash still owns how long the callout stays up, this only owns the first third of a
    // second of it.
    float titlePunchUntil = -99f;
    const float titlePunchDuration = 0.35f;

    /// The token reward callout on the results screen - "make sure people get tokens at the end
    /// of each round (and a whole animation plays... with sound effects and vfx etc)". Local and
    /// personal by design, same reasoning a kill sound only plays for the killer: what you earned
    /// is your own moment, not a shared spectacle everyone else has to sit through too.
    [SerializeField] TMP_Text tokenRewardText;
    [SerializeField] ParticleSystem tokenRewardBurst;
    bool roundTokensAwarded;
    float tokenRewardUntil = -99f;
    float tokenPunchUntil = -99f;

    /// Hard ceiling regardless of how well the round went - "cap it at 50."
    const int MaxRoundTokenReward = 50;

    /// Where RoundTokensFor's curve reaches the cap - a style score of this many points earns
    /// the full 50. No real match has been played against this number yet, same first-pass
    /// caveat every other feel number in this project carries (see roadmap.md's Unverified).
    const float RewardScoreScale = 2500f;

    int lastRoundTokenReward;

    /// "You can get tokens proportional to how many points you got in that game (but cap it at
    /// 50 and make sure the token to score ratio is exponential and not linear or logarithmic)."
    /// Base-2 exponential rather than a power curve - 2^(score/scale) climbs slowly at low scores
    /// and accelerates into the cap rather than the other way around, which reads as "you have to
    /// actually be doing well before this ramps up" instead of most of the reward being handed
    /// out for a merely middling score.
    static int RoundTokensFor(float score)
    {
        float raw = MaxRoundTokenReward * (Mathf.Pow(2f, score / RewardScoreScale) - 1f);
        return Mathf.Clamp(Mathf.RoundToInt(raw), 0, MaxRoundTokenReward);
    }

    /// See PlayerWallet.cs - tokens spend on crates (CrateOpeningScreen), which exists
    /// separately from the HUD and is opened from outside a live match.
    void AwardRoundTokens()
    {
        float score = player != null && player.Style != null ? player.Style.Score : 0f;
        lastRoundTokenReward = RoundTokensFor(score);

        PlayerWallet.Add(lastRoundTokenReward);
        tokenRewardUntil = Time.unscaledTime + 3.5f;
        tokenPunchUntil = Time.unscaledTime + 0.3f;

        // Not tokenRewardBurst?.Play() - a real run threw UnassignedReferenceException from
        // exactly that. tokenRewardBurst is deliberately left unwired in HudBuilder.cs (an
        // optional particle flourish, not built yet), and Unity's "missing reference" state on a
        // SerializeField isn't true C# null - `?.` uses a raw reference check that misses it,
        // where `!= null` correctly goes through UnityEngine.Object's own overload and catches it.
        if (tokenRewardBurst != null)
            tokenRewardBurst.Play();

        GameAudio.Play2D(GameAudio.Kill, GameAudio.KillVolume * 0.8f);
    }

    void UpdateTokenReward()
    {
        if (tokenRewardText == null)
            return;

        bool active = Time.unscaledTime < tokenRewardUntil;
        tokenRewardText.gameObject.SetActive(active);

        if (!active)
            return;

        tokenRewardText.text = $"+{lastRoundTokenReward} TOKENS";
        tokenRewardText.rectTransform.localScale = Vector3.one * PunchScale(tokenPunchUntil, 0.3f);
    }

    /// <summary>
    /// The shared shape behind every punch on this HUD: fast in, decaying out, on top of a base
    /// scale of 1 - a snap rather than a fade, per this HUD's own "no fades" rule. One curve
    /// rather than three copies of the same maths, so retuning how a punch feels means changing
    /// it in one place.
    /// </summary>
    static float PunchScale(float punchUntil, float duration = 0.22f)
    {
        float t = Mathf.Clamp01((punchUntil - Time.unscaledTime) / duration);
        return 1f + t * t * 0.5f;
    }

    // The health and ammo groups drift outward at speed. Resolved from healthNumber/ammoNumber's
    // own parent rather than adding two more fields to wire up in the scene - HudBuilder already
    // parents each under its own panel, so the panel is sitting right there to be found.
    RectTransform healthGroup;
    RectTransform ammoGroup;
    Vector2 healthBasePos;
    Vector2 ammoBasePos;

    /// Drawn behind the winner's name when the round is over. Without it the result was a line
    /// of text floating over a firefight that had visibly stopped mattering, which is not what
    /// winning should look like.
    [SerializeField] GameObject resultsBackdrop;

    /// Red round the edges of the screen, pulsing, when you are nearly dead. Filled in at
    /// runtime if the scene has not got one, so an older HUD picks it up without a rebuild.
    [SerializeField] Image adrenalineEdge;

    [Header("Gun game ladder")]
    [SerializeField] GameObject ladder;
    [SerializeField] TMP_Text ladderLabel;
    [SerializeField] Image[] ladderPips = new Image[0];

    [Header("Pools")]
    [SerializeField] RectTransform feedContainer;
    [SerializeField] TMP_Text feedTemplate;
    [SerializeField] RectTransform standingsContainer;
    [SerializeField] TMP_Text standingsTemplate;
    [SerializeField] RectTransform damageContainer;
    [SerializeField] TMP_Text damageTemplate;
    [SerializeField] RectTransform arrowContainer;
    [SerializeField] Image arrowTemplate;

    // Retuned 2026-08-29 for the Cruelty Squad / ULTRAKILL pass - reported as too soft, reading
    // as a generic arcade shooter's traffic-light green/yellow/red rather than the harsh,
    // clashing palette both of those games build their HUDs from. killColour is the one real
    // hue change: orange to a hot magenta, the accent colour both references reach for on
    // anything meant to read as violence rather than status (a kill callout, a streak, who you
    // just beat). Everything else stays the same family, just pushed more saturated and less
    // pastel - green loses its warmth toward acid, red loses its pink toward blood.
    //
    // Retuned again the same day - playtesters said the reworked HUD had lost the personality
    // the old one had, and the direct ask was to make the health bar "a bananameter" rather than
    // a generic three-colour ramp. `healthy`/`hurt`/`critical` now walk the same green-to-brown
    // ripening a magazine already does in the player's own hands (`GunInfo.RipenessFor` - see
    // `SingleShotGun.ApplyRipeness`, which tints the banana itself the same way as it empties),
    // brightened a touch past the weapon's own values since a HUD element needs to carry more
    // presence than a small prop in the world. `critical` stays a genuine warning colour rather
    // than true overripe brown/black - a rotting banana that's gone the same colour as the grass
    // behind it would be a bar you can't read at exactly the moment you most need to.
    [Header("Colours")]
    [SerializeField] Color healthy = new Color(0.6f, 0.92f, 0.14f);
    [SerializeField] Color hurt = new Color(1f, 0.82f, 0.1f);
    [SerializeField] Color critical = new Color(0.85f, 0.4f, 0.08f);
    [SerializeField] Color healed = new Color(0.32f, 1f, 0.48f);
    [SerializeField] Color headshotColour = new Color(1f, 0.9f, 0.08f);
    [SerializeField] Color killColour = new Color(1f, 0.1f, 0.58f);
    [SerializeField] Color joinColour = new Color(0.38f, 1f, 0.48f);
    [SerializeField] Color leaveColour = new Color(0.6f, 0.6f, 0.68f);

    /// The movement combo's own accent - electric cyan rather than killColour's hot magenta, so
    /// the two meters (top right for combat, bottom left for traversal) read as two different
    /// systems at a glance instead of the same bar in two spots.
    [SerializeField] Color comboColour = new Color(0.25f, 0.85f, 1f);
    [SerializeField] Color dim = new Color(1f, 1f, 1f, 0.58f);

    [Header("Feel")]
    [Tooltip("Canvas units the crosshair opens per degree of the weapon's spread cone.")]
    [SerializeField] float crosshairSpread = 24f;

    [Tooltip("How long a feed line stays up. The last second and a half of that is a fade.")]
    [SerializeField] float feedSeconds = 6f;

    [SerializeField] int feedRowLimit = 6;
    [SerializeField] float damageSeconds = 0.85f;

    [Tooltip("How far a damage number drifts up over its life, in canvas units.")]
    [SerializeField] float damageRise = 46f;

    [Tooltip("How far from the middle the damage direction marks sit.")]
    [SerializeField] float arrowRadius = 210f;

    [Tooltip("How long a damage direction mark stays up.")]
    [SerializeField] float arrowSeconds = 1.6f;

    PlayerController player;
    RectTransform canvasRect;
    RectTransform shakeRoot;

    float hitFlash;
    bool lastHitWasHead;
    float hitMarkerSpin;
    float healFlash;
    float killFlash;
    float comboFlash;

    readonly List<TMP_Text> feedRows = new List<TMP_Text>();
    readonly List<TMP_Text> standingsRows = new List<TMP_Text>();
    readonly List<TMP_Text> breakdownRows = new List<TMP_Text>();
    readonly List<DamageLabel> damageLabels = new List<DamageLabel>();
    readonly List<DamageArrow> damageArrows = new List<DamageArrow>();

    class DamageArrow
    {
        public Image image;
        public RectTransform rect;
        public Vector3 from;
        public float born;
        public bool live;
    }

    class DamageLabel
    {
        public TMP_Text text;
        public RectTransform rect;
        public Vector3 at;
        public float born;
        public bool head;
        public bool live;
    }

    /// <summary>
    /// Wraps every existing HUD element in one child RectTransform the shake can actually move.
    ///
    /// The canvas is Screen Space - Overlay (HudBuilder), and Unity ignores an overlay canvas's
    /// *own* RectTransform entirely when placing it - it always fills the screen exactly regardless
    /// of anchoredPosition, which is why driving canvasRect directly did nothing at all ("the UI
    /// still doesn't shake"). A child RectTransform underneath it has no such exemption. Built at
    /// runtime rather than in HudBuilder so nothing about the existing prefab/hierarchy has to
    /// change - every current direct child of the canvas is reparented under this one, once, and
    /// everything drawn from here on (added by other systems after this runs) still lands directly
    /// on the canvas and won't shake, so this has to run before anything else populates it.
    /// </summary>
    static RectTransform BuildShakeRoot(RectTransform canvasRect)
    {
        GameObject host = new GameObject("~HudShakeRoot", typeof(RectTransform));
        RectTransform rect = (RectTransform)host.transform;
        rect.SetParent(canvasRect, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        int count = canvasRect.childCount;
        Transform[] existing = new Transform[count];
        for (int i = 0; i < count; i++)
            existing[i] = canvasRect.GetChild(i);

        foreach (Transform child in existing)
        {
            if (child != rect)
                child.SetParent(rect, false);
        }

        rect.SetAsFirstSibling();
        return rect;
    }

    /// <summary>
    /// Handed over by the local PlayerController when it spawns.
    ///
    /// Null between dying and respawning, which is why everything below checks before reading
    /// it rather than assuming a player exists.
    /// </summary>
    public void Bind(PlayerController owner) => player = owner;

    void Awake()
    {
        Instance = this;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvasRect = (RectTransform)canvas.transform;
            shakeRoot = BuildShakeRoot(canvasRect);
        }

        // Made here if the scene doesn't have one. The dot arrived after the HUD was built,
        // and re-running the builder to add it would throw away any restyling done since - so
        // an authored dot wins and a missing one is filled in.
        if (crosshairDot == null && crosshairUp != null && crosshairUp.parent != null)
        {
            GameObject made = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer),
                                             typeof(Image));
            made.transform.SetParent(crosshairUp.parent, false);

            crosshairDot = made.GetComponent<Image>();
            crosshairDot.raycastTarget = false;
        }

        if (arrowTemplate != null)
            arrowTemplate.gameObject.SetActive(false);

        if (healthNumber != null)
        {
            healthGroup = healthNumber.rectTransform.parent as RectTransform;
            if (healthGroup != null)
                healthBasePos = healthGroup.anchoredPosition;
        }

        if (ammoNumber != null)
        {
            ammoGroup = ammoNumber.rectTransform.parent as RectTransform;
            if (ammoGroup != null)
                ammoBasePos = ammoGroup.anchoredPosition;
        }

        // Made here if the scene has not got one, same as the crosshair dot. A full screen
        // radial sprite, tinted red and pulsed - it darkens the edges and leaves the middle
        // clear, which is the only shape that can be loud without covering what you are aiming
        // at.
        if (adrenalineEdge == null)
        {
            GameObject made = new GameObject("AdrenalineEdge", typeof(RectTransform),
                                             typeof(CanvasRenderer), typeof(Image));
            made.transform.SetParent(transform, false);
            made.transform.SetAsFirstSibling();

            RectTransform rect = (RectTransform)made.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            adrenalineEdge = made.GetComponent<Image>();
            adrenalineEdge.raycastTarget = false;

            // The scope mask is already a circle that is clear in the middle and opaque at the
            // edges, which is exactly the shape wanted here. Reused rather than sourced again.
            Texture2D ring = BuildScopeMask(512);
            adrenalineEdge.sprite = Sprite.Create(ring, new UnityEngine.Rect(0f, 0f, ring.width, ring.height),
                                                  new Vector2(0.5f, 0.5f));
        }

        adrenalineEdge.gameObject.SetActive(false);

        // Built here if the scene predates it, same as the dot and the edge. Sits under the
        // combo counter on the right, where nothing else lives.
        if (slideCombo == null && comboText != null)
        {
            GameObject made = new GameObject("SlideCombo", typeof(RectTransform));
            made.transform.SetParent(comboText.transform.parent, false);

            slideCombo = made.AddComponent<TextMeshProUGUI>();
            slideCombo.font = comboText.font;
            slideCombo.fontSize = 54f;
            slideCombo.alignment = TextAlignmentOptions.Right;
            slideCombo.enableWordWrapping = false;
            slideCombo.raycastTarget = false;

            RectTransform rect = (RectTransform)made.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-70f, 120f);
            rect.sizeDelta = new Vector2(620f, 80f);
        }

        if (slideCombo != null)
            slideCombo.gameObject.SetActive(false);

        HideTemplate(feedTemplate);
        HideTemplate(standingsTemplate);
        HideTemplate(damageTemplate);

        // Generated rather than shipped as a PNG: it has to stay sharp at whatever height the
        // window is, and a circle is cheaper to draw than to store. Filled in only when the
        // slot is empty, so dropping a real scope image on the Image in the editor wins.
        if (scopeGlass != null && scopeGlass.sprite == null)
        {
            Texture2D mask = BuildScopeMask(1024);
            scopeGlass.sprite = Sprite.Create(mask, new UnityEngine.Rect(0f, 0f, mask.width, mask.height),
                                              new Vector2(0.5f, 0.5f));
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    static void HideTemplate(TMP_Text template)
    {
        if (template != null)
            template.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- things the game reports

    public void ShowHit(bool headshot)
    {
        hitFlash = 1f;
        lastHitWasHead = headshot;

        // A fresh spin every headshot, alternating direction, so a run of headshots doesn't
        // wind up looking like the same clip playing on a loop.
        hitMarkerSpin = Random.Range(50f, 100f) * (Random.value > 0.5f ? 1f : -1f);
    }

    public void ShowHeal(float amount)
    {
        healFlash = 1f;

        if (healText != null)
            healText.text = $"+{Mathf.RoundToInt(amount)}";
    }

    public void ShowCombo(int hits) => comboFlash = 1f;

    public void ShowKill(int multikill, int streak)
    {
        killFlash = 1f;
        titlePunchUntil = Time.unscaledTime + titlePunchDuration;

        if (centreTitle != null)
            centreTitle.text = MultikillName(multikill);

        if (centreSubtitle != null)
            centreSubtitle.text = streak > 1 ? $"{streak} IN A ROW" : string.Empty;
    }

    /// <summary>
    /// You climbed a rung and the gun changed in your hands.
    ///
    /// Gun game swaps your weapon with no warning, and without something saying so it reads as
    /// the game confiscating what you were holding - roughly the opposite of what just
    /// happened. Named rather than numbered for the same reason the multikills are: "THE BUNCH"
    /// tells you what you're now holding, "rung 3" tells you nothing you can act on.
    ///
    /// Rides on the kill callout's timer rather than having its own, because it fires at
    /// exactly the moment a kill callout would and two messages fighting over the middle of the
    /// screen is what made this confusing in the first place.
    /// </summary>
    public void ShowRungUp(int rung, string weapon)
    {
        killFlash = 1f;
        titlePunchUntil = Time.unscaledTime + titlePunchDuration;

        if (centreTitle != null)
            centreTitle.text = $"RUNG {rung + 1}";

        if (centreSubtitle != null)
            centreSubtitle.text = weapon.ToUpper();
    }

    /// <summary>
    /// Marks which way the shot came from.
    ///
    /// The single most disorienting thing about being shot in a first person game is not knowing
    /// where to look, and the map has no landmarks yet to work it out from. A mark on the edge
    /// of the screen at the right bearing turns "I am being shot" into "I am being shot from
    /// behind and to the left", which is a fact you can act on.
    ///
    /// Bearing only - the vertical is thrown away. Someone above you and someone level with you
    /// are the same problem, and an indicator that tries to say both ends up saying neither.
    /// </summary>
    public void ShowDamageFrom(Vector3 source)
    {
        if (arrowTemplate == null || arrowContainer == null)
            return;

        DamageArrow arrow = null;

        foreach (DamageArrow candidate in damageArrows)
        {
            if (!candidate.live)
            {
                arrow = candidate;
                break;
            }
        }

        if (arrow == null)
        {
            // Capped low. More than a handful at once is not information any more, it is a ring
            // of orange, and being shot by three people at once already communicates itself.
            if (damageArrows.Count >= 6)
            {
                // Oldest by born time, not just index 0 - the same eviction FreeDamageLabel
                // below uses. Index 0 could be a hit that landed this exact frame, stealing its
                // slot from under it while a genuinely stale arrow keeps pointing at nothing.
                arrow = damageArrows[0];

                foreach (DamageArrow candidate in damageArrows)
                {
                    if (candidate.born < arrow.born)
                        arrow = candidate;
                }
            }
            else
            {
                Image made = Instantiate(arrowTemplate, arrowContainer);
                made.name = $"DamageArrow{damageArrows.Count}";

                arrow = new DamageArrow { image = made, rect = made.rectTransform };
                damageArrows.Add(arrow);
            }
        }

        arrow.from = source;
        arrow.born = Time.unscaledTime;
        arrow.live = true;
        arrow.image.gameObject.SetActive(true);
    }

    void UpdateDamageArrows()
    {
        Camera camera = PlayerController.LocalCamera;

        foreach (DamageArrow arrow in damageArrows)
        {
            if (!arrow.live)
                continue;

            float age = (Time.unscaledTime - arrow.born) / arrowSeconds;

            if (age >= 1f || camera == null)
            {
                arrow.live = false;
                arrow.image.gameObject.SetActive(false);
                continue;
            }

            // Recomputed every frame rather than fixed at the moment of the hit, so turning
            // toward the shooter walks the mark round to the top of the screen. That feedback
            // loop is the whole point - it is what makes you turn the right way.
            Vector3 to = arrow.from - camera.transform.position;
            to.y = 0f;

            Vector3 forward = camera.transform.forward;
            forward.y = 0f;

            if (to.sqrMagnitude < 0.001f || forward.sqrMagnitude < 0.001f)
                continue;

            float bearing = Vector3.SignedAngle(forward, to, Vector3.up);

            // Screen space turns the other way to world yaw, hence the negative.
            Quaternion turn = Quaternion.Euler(0f, 0f, -bearing);

            arrow.rect.anchoredPosition = turn * Vector3.up * arrowRadius;
            arrow.rect.localRotation = turn;

            arrow.image.color = new Color(critical.r, critical.g, critical.b, 1f - age * age);
        }
    }

    public void ShowDamage(Vector3 worldPoint, float amount, bool headshot)
    {
        DamageLabel label = FreeDamageLabel();
        if (label == null)
            return;

        label.at = worldPoint;
        label.born = Time.unscaledTime;
        label.head = headshot;
        label.live = true;

        label.text.text = Mathf.RoundToInt(amount).ToString();
        label.text.color = headshot ? headshotColour : Color.white;
        label.text.gameObject.SetActive(true);
    }

    // Named rather than numbered, because DOUBLE lands and "2" doesn't.
    static string MultikillName(int count)
    {
        switch (count)
        {
            case 1: return "KILL";
            case 2: return "DOUBLE";
            case 3: return "TRIPLE";
            case 4: return "OVERRIPE";
            case 5: return "BLENDED";
            default: return "FRUIT SALAD";
        }
    }

    // ---------------------------------------------------------------- drawing

    void Update()
    {
        // Unscaled throughout. Hitstop drops Time.timeScale to a twentieth on every kill, which
        // is precisely when most of this is on screen; on scaled time the hitmarker and the kill
        // callout hang frozen at full stretch through the freeze and then snap away.
        float dt = Time.unscaledDeltaTime;

        hitFlash = Mathf.Max(0f, hitFlash - dt * 3.2f);
        healFlash = Mathf.Max(0f, healFlash - dt * 0.8f);
        killFlash = Mathf.Max(0f, killFlash - dt * 0.75f);
        comboFlash = Mathf.Max(0f, comboFlash - dt * 1.6f);

        UpdateHealth();
        UpdateAmmo();
        UpdateCrosshair();
        UpdateScope();
        UpdateClock();
        UpdateLadder();
        UpdateCentre();
        UpdateFeed();
        UpdateDamageNumbers();
        UpdateDamageArrows();
        UpdateAdrenaline();
        UpdateStyleMeter();
        UpdateMovementCombo();
        UpdateSpentIndicator();
        UpdateTokenReward();
        UpdateSpeedPush();
        UpdateHudShake();
    }

    Vector2 canvasRestPos;
    float hudShakeSeed = -1f;

    /// <summary>
    /// Shakes the whole HUD along with the world. Added 2026-08-22, direct request - a screen
    /// space overlay canvas is exactly that, an overlay, and never moved with the camera at all,
    /// so the one hit that should least be ignorable (a kill's own screenshake) left the HUD
    /// dead still while the world visibly got knocked around it. Reads `Juice.Amount`
    /// (normalised 0-1) rather than the world shake's own pixel/degree values, since the UI
    /// wants its own, smaller displacement - a health number thrown around as far as the camera
    /// shakes would be unreadable, not punchy.
    /// </summary>
    void UpdateHudShake()
    {
        if (shakeRoot == null)
            return;

        if (hudShakeSeed < 0f)
        {
            canvasRestPos = shakeRoot.anchoredPosition;
            hudShakeSeed = Random.Range(0f, 100f);
        }

        float amount = Juice.Amount;

        if (amount <= 0.001f)
        {
            shakeRoot.anchoredPosition = canvasRestPos;
            return;
        }

        float time = Time.unscaledTime * 42f;

        // Cut hard, 2026-08-29 - reported as the UI shake being "too much," with the camera's
        // own shake raised to take over most of the job a hit's punch is doing (see Juice.cs).
        // Not zero - the HUD should still visibly react to something big landing, just as a
        // quiet echo of the camera's own kick rather than competing with it.
        const float maxPixels = 5f;

        Vector2 offset = new Vector2(
            (Mathf.PerlinNoise(hudShakeSeed, time) - 0.5f) * 2f,
            (Mathf.PerlinNoise(hudShakeSeed + 11f, time) - 0.5f) * 2f) * (maxPixels * amount);

        shakeRoot.anchoredPosition = canvasRestPos + offset;
    }

    /// <summary>
    /// Health and ammo drift a little further into their own corners at speed - the same idea as
    /// the weapon's own push in WeaponSway, so the effect isn't only something that happens to
    /// the gun. Both read SpeedRush.Intensity directly rather than the HUD tracking its own
    /// notion of "going fast", so all three effects agree on the same number.
    ///
    /// Deliberately not applied to the crosshair, the hitmarker or the damage numbers - anything
    /// that has to stay exactly where the gameplay says it is stays put. This is only the chrome.
    /// </summary>
    const float speedPushPixels = 22f;

    void UpdateSpeedPush()
    {
        float rush = SpeedRush.Intensity;

        // Further into its own corner at speed, same idea as the ammo push below - health moved
        // from the bottom left to the top left with the Cruelty Squad rework, so "further into
        // the corner" flipped from down-left to up-left.
        if (healthGroup != null)
            healthGroup.anchoredPosition = healthBasePos + new Vector2(-1f, 1f) * speedPushPixels * rush;

        if (ammoGroup != null)
            ammoGroup.anchoredPosition = ammoBasePos + new Vector2(1f, -1f) * speedPushPixels * rush;
    }

    void UpdateHealth()
    {
        bool alive = player != null;

        Show(healthNumber, alive);
        Show(healthTrack, alive);
        Show(streakText, alive && player.Killstreak > 1);
        Show(healText, alive && healFlash > 0f);

        if (!alive)
            return;

        float max = Mathf.Max(1f, player.MaxHealth);
        int points = player.HealthPoints;
        float fraction = Mathf.Clamp01(points / max);

        // Three steps rather than a gradient, so a colour change means something happened
        // rather than being a shade nobody can name.
        Color colour = fraction > 0.6f ? healthy : fraction > 0.3f ? hurt : critical;
        if (healFlash > 0f)
            colour = Color.Lerp(colour, healed, healFlash);

        // Punches on any change, not only a drop - healing back up is worth a beat too, and
        // healFlash already owns the colour side of that so this only ever adds the motion.
        if (lastHealthPoints >= 0 && points != lastHealthPoints)
            healthPunchUntil = Time.unscaledTime + 0.22f;

        lastHealthPoints = points;

        if (healthNumber != null)
        {
            healthNumber.text = points.ToString();
            healthNumber.color = colour;
        }

        if (healthGroup != null)
            healthGroup.localScale = Vector3.one * PunchScale(healthPunchUntil);

        // The track is a hundred and forty, full stop.
        //
        // It used to be scaled to the overshield ceiling, which meant an unshielded player at
        // full health saw a bar that was only seventy percent filled - so the normal state of
        // the game looked like being hurt, and the number 140 read as low. Overshield gets its
        // own separate bar now instead of sharing this one, which is what a bonus should look
        // like: a second full gauge lighting up, not this one stretching past its own end.
        float fillFraction = Mathf.Min(points, max) / max;

        // Inertia - eases down toward a drop rather than snapping with it, and snaps straight
        // up on a heal (nothing about healing wants a lag). Direct request: "the bar decreasing
        // being inertia based."
        if (fillFraction < healthTrailFraction)
            healthTrailFraction = Mathf.MoveTowards(healthTrailFraction, fillFraction,
                                                     Time.unscaledDeltaTime * 0.55f);
        else
            healthTrailFraction = fillFraction;

        if (healthFill != null)
        {
            healthFill.fillAmount = fillFraction;

            // A dedicated tint rather than reusing `colour` - the sprite is a real, naturally
            // yellow banana now, not a neutral grey shape, and multiplying it by the same
            // green/gold/orange the number uses would just muddy it (yellow times green reads
            // as khaki, not "healthy"). White leaves it looking like an actual banana at good
            // health; only the danger end pulls it toward red, which reads as a warning
            // regardless of what colour was sitting under it a moment ago.
            Color bananaTint = fraction > 0.6f ? Color.white
                              : fraction > 0.3f ? new Color(1f, 0.8f, 0.55f)
                              : new Color(1f, 0.4f, 0.35f);
            if (healFlash > 0f)
                bananaTint = Color.Lerp(bananaTint, Color.white, healFlash);

            // A beat of white through the fill itself on every change, on top of the whole
            // panel's own punch - reported as "completely static" even with the punch already
            // moving the number and the bar together, because neither ever touched the bar's
            // own colour. This is what actually reads as the bar reacting to the hit rather
            // than just being shoved sideways with it.
            float flashT = Mathf.Clamp01((healthPunchUntil - Time.unscaledTime) / 0.22f);
            healthFill.color = Color.Lerp(bananaTint, Color.white, flashT * 0.65f);
        }

        // The pale ghost between the peel and the live fill - only ever visible for the sliver
        // it's lagging behind by, which is the whole point: it's showing exactly how much was
        // just lost, not standing in for the bar itself.
        if (healthTrail != null)
            healthTrail.fillAmount = healthTrailFraction;

        if (healthShield != null)
        {
            // Its own vertical gauge now, beside the main bar rather than an extension of it -
            // fillAmount against the shield's own ceiling, the same shape the main bar already
            // uses against max health. The empty track around it stays visible either way (see
            // HudBuilder); this only ever toggles the bright fill inside it.
            float overshield = player.Overshield;
            float shieldCeiling = Mathf.Max(1f, player.OvershieldCeiling - max);
            float shieldFraction = Mathf.Clamp01(overshield / shieldCeiling);

            bool shielded = overshield > 0.5f;
            healthShield.gameObject.SetActive(shielded);
            healthShield.fillAmount = shieldFraction;

            // A slow glimmer while it's up - reported directly as wanting an "animated" bar, and
            // fillAmount alone only ever moves when the shield itself changes, which for most of
            // a life it doesn't.
            if (shielded)
            {
                float glimmer = 0.85f + Mathf.Sin(Time.unscaledTime * 5f) * 0.15f;
                healthShield.color = new Color(0.4f, 0.9f, 1f, glimmer);
            }
        }

        if (streakText != null && player.Killstreak > 1)
            streakText.text = $"{player.Killstreak} IN A ROW";

        if (healText != null && healFlash > 0f)
            healText.color = new Color(healed.r, healed.g, healed.b, healFlash);
    }

    void UpdateAmmo()
    {
        SingleShotGun gun = player != null ? player.ActiveGun : null;
        GunInfo info = gun != null ? gun.Info : null;

        // A peel has no magazine, so showing it a round count would be a lie in two places.
        bool countsRounds = info != null && !info.melee;

        Show(weaponName, info != null);
        Show(ammoNumber, countsRounds);
        Show(spareNumber, countsRounds);

        if (info != null && weaponName != null)
            weaponName.text = WeaponLoadout.DisplayName(gun.name).ToUpper();

        if (!countsRounds)
            return;

        // The ammo readout ripens the same way the banana in your hands already does -
        // `RipenessFor` is the exact function `SingleShotGun.ApplyRipeness` tints the weapon
        // model with, so the number, the bar and the gun all brown together as the magazine
        // empties instead of the HUD inventing its own unrelated colour rule. Weapons that don't
        // ripen (`info.ripens` false - the pineapple, which has a real texture rather than a
        // tintable banana skin) fall back to plain white, same as the model itself does.
        Color ammoColour = gun.Reloading ? hurt : info.ripens ? info.RipenessFor(gun.Ammo) : Color.white;

        ammoNumber.text = gun.Reloading ? "--" : gun.Ammo.ToString();
        ammoNumber.color = ammoColour;

        // Bare number, no "x". Next to something that size an x reads as multiplication, and
        // there's nothing else it could be counting.
        spareNumber.text = gun.SpareMagazines.ToString();
        spareNumber.color = gun.SpareMagazines > 0 ? dim : critical;

        // Punches on every round fired, on starting a reload and on the magazine coming back -
        // reported directly that "--" sitting there for the whole reload with no reaction at
        // either end looked dead. Used to only fire on the round count itself changing, which
        // is exactly the two moments ("--" going up, "--" going away) that aren't a count
        // changing at all.
        if (lastAmmoCount >= 0 && (gun.Ammo != lastAmmoCount || gun.Reloading != lastReloading))
            ammoPunchUntil = Time.unscaledTime + 0.22f;

        lastAmmoCount = gun.Ammo;
        lastReloading = gun.Reloading;

        if (ammoGroup != null)
            ammoGroup.localScale = Vector3.one * PunchScale(ammoPunchUntil);

        // A slow, steady breathe while "--" is up, on top of the punch - reported as looking
        // "so weird" sitting there static for a second and a half. Continuous rather than
        // triggered, since there's no discrete moment to punch on in the middle of a reload,
        // only the fact that one is in progress.
        if (ammoNumber != null)
        {
            float breathe = gun.Reloading ? 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.08f : 1f;
            ammoNumber.rectTransform.localScale = Vector3.one * breathe;
        }
    }

    void UpdateCrosshair()
    {
        bool aiming = player != null && player.IsAiming;
        SingleShotGun gun = player != null ? player.ActiveGun : null;
        GunInfo info = gun != null ? gun.Info : null;

        // Which reticle. The weapon picks unless the player has said they want one crosshair
        // everywhere, which is a fair thing to want - otherwise a shotgun and a sniper are two
        // separate tuning jobs.
        GunInfo.Reticle style = GameSettings.CrosshairOverride || info == null
            ? GunInfo.Reticle.Cross
            : info.reticle;

        // Opens with the cone the weapon actually fires into, so an inaccurate gun looks
        // inaccurate before you've missed with it - but scaled, because a shotgun's real cone
        // drawn at full size is a reticle the size of a dinner plate, which is what made it feel
        // hopelessly inaccurate rather than merely wide.
        float scale = GameSettings.CrosshairOverride || info == null ? 1f : info.reticleSpreadScale;
        float spread = GameSettings.CrosshairDynamic && info != null ? info.spread * scale : 0f;

        float gap = GameSettings.CrosshairGap + spread * crosshairSpread;
        float length = GameSettings.CrosshairSize;
        float thickness = GameSettings.CrosshairThickness;
        Color colour = GameSettings.CrosshairColour;

        if (style == GunInfo.Reticle.Dot)
        {
            // Nothing but the centre. For weapons where the spread is the whole point and
            // drawing it would only be noise.
            Tick(crosshairUp, Vector2.zero, Vector2.zero, colour, false);
            Tick(crosshairDown, Vector2.zero, Vector2.zero, colour, false);
            Tick(crosshairLeft, Vector2.zero, Vector2.zero, colour, false);
            Tick(crosshairRight, Vector2.zero, Vector2.zero, colour, false);
        }
        else if (style == GunInfo.Reticle.Triangle)
        {
            // Three marks on a circle rather than four on the axes. Reads as a spread weapon
            // without being an enormous cross, and the flat bottom edge sits under what you are
            // aiming at instead of across it.
            TickAt(crosshairUp, 90f, gap, length, thickness, colour, !aiming);
            TickAt(crosshairLeft, 210f, gap, length, thickness, colour, !aiming);
            TickAt(crosshairRight, 330f, gap, length, thickness, colour, !aiming);
            Tick(crosshairDown, Vector2.zero, Vector2.zero, colour, false);
        }
        else
        {
            Tick(crosshairUp, new Vector2(0f, gap + length * 0.5f),
                 new Vector2(thickness, length), colour, !aiming);
            Tick(crosshairDown, new Vector2(0f, -gap - length * 0.5f),
                 new Vector2(thickness, length), colour, !aiming);
            Tick(crosshairLeft, new Vector2(-gap - length * 0.5f, 0f),
                 new Vector2(length, thickness), colour, !aiming);
            Tick(crosshairRight, new Vector2(gap + length * 0.5f, 0f),
                 new Vector2(length, thickness), colour, !aiming);
        }

        if (crosshairDot != null)
        {
            crosshairDot.gameObject.SetActive(GameSettings.CrosshairDot && !aiming);
            crosshairDot.rectTransform.anchoredPosition = Vector2.zero;
            crosshairDot.rectTransform.sizeDelta = new Vector2(thickness, thickness);
            crosshairDot.color = colour;
            Outline(crosshairDot);
        }

        if (hitMarker == null)
            return;

        hitMarker.gameObject.SetActive(hitFlash > 0f);

        if (hitFlash <= 0f)
            return;

        // Reworked 2026-08-29 - reported as not juicy enough, "not enough dopamine." A body hit
        // still pops and shrinks; a headshot now does noticeably more: bigger, spins in rather
        // than just scaling, and flashes white-hot before settling into headshotColour - the
        // same "arrives hot, cools into place" read the kill callout's own punch already uses,
        // rather than a flat tint for the marker's whole life.
        float age = 1f - hitFlash;
        float pop = 1f + age * 0.9f;

        if (lastHitWasHead)
        {
            // Spins down to the resting 45 degrees rather than starting there - the extra
            // motion is what separates "landed" from "landed solidly."
            float spin = hitMarkerSpin * (1f - age * age);
            hitMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + spin);
            hitMarker.rectTransform.localScale = Vector3.one * 2f * pop;

            Color hot = Color.Lerp(headshotColour, Color.white, hitFlash * hitFlash);
            hitMarker.color = new Color(hot.r, hot.g, hot.b, hitFlash);
        }
        else
        {
            hitMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            hitMarker.rectTransform.localScale = Vector3.one * pop;
            hitMarker.color = new Color(1f, 1f, 1f, hitFlash);
        }
    }

    /// <summary>
    /// One arm of the crosshair, positioned, sized and coloured from the settings.
    ///
    /// Position is the outer edge plus half the length, because the rects are centre pivoted -
    /// the gap is meant to be the distance from the middle of the screen to where the mark
    /// starts, not to where its centre happens to land, and getting that wrong makes the gap
    /// slider do something subtly different from what it says.
    /// </summary>
    /// <summary>
    /// One mark placed on a circle at a bearing, lying across the radius rather than along it.
    ///
    /// Tangential on purpose: three bars pointing outward read as a star, three bars lying
    /// across the circle read as the corners of a triangle, which is what was asked for.
    /// </summary>
    static void TickAt(RectTransform tick, float degrees, float radius, float length,
                       float thickness, Color colour, bool visible)
    {
        if (tick == null)
            return;

        float radians = degrees * Mathf.Deg2Rad;
        Vector2 at = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (radius + thickness);

        Tick(tick, at, new Vector2(length, thickness), colour, visible);

        // Rotated so the bar is perpendicular to the radius. Zero degrees already lies flat, so
        // the mark at the top needs no roll and the two lower ones tilt to match the corners.
        tick.localRotation = Quaternion.Euler(0f, 0f, degrees - 90f);
    }

    static void Tick(RectTransform tick, Vector2 position, Vector2 size, Color colour, bool visible)
    {
        if (tick == null)
            return;

        tick.gameObject.SetActive(visible);
        tick.anchoredPosition = position;
        tick.sizeDelta = size;

        // Cleared here rather than in the cross branch, so switching from a triangle weapon back
        // to a rifle does not leave two ticks permanently askew.
        tick.localRotation = Quaternion.identity;

        Image image = tick.GetComponent<Image>();

        if (image == null)
            return;

        image.color = colour;
        Outline(image);
    }

    /// <summary>
    /// A dark edge round the crosshair, so it stays visible against a bright wall.
    ///
    /// uGUI's own Outline component rather than a second set of images behind the first: it
    /// duplicates the graphic's mesh with an offset, which needs no extra objects and therefore
    /// no change to a hierarchy people are meant to be editing by hand.
    /// </summary>
    static void Outline(Image image)
    {
        UnityEngine.UI.Outline edge = image.GetComponent<UnityEngine.UI.Outline>();

        if (GameSettings.CrosshairOutline)
        {
            if (edge == null)
                edge = image.gameObject.AddComponent<UnityEngine.UI.Outline>();

            edge.enabled = true;
            edge.effectColor = new Color(0f, 0f, 0f, 0.85f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
        }
        else if (edge != null)
        {
            edge.enabled = false;
        }
    }

    /// <summary>
    /// The scope is a circle as tall as the window with the sides blacked out.
    ///
    /// Sized here rather than in the scene because it depends on the aspect ratio, which isn't
    /// known until the game is running and changes when the window is resized. Everything else
    /// about it - where the hairlines sit, how dark the surround is - is scene data.
    /// </summary>
    void UpdateScope()
    {
        bool aiming = player != null && player.IsAiming;

        if (scope != null)
            scope.SetActive(aiming);

        if (!aiming || canvasRect == null)
            return;

        float height = canvasRect.rect.height;
        float side = Mathf.Max(0f, (canvasRect.rect.width - height) * 0.5f);

        if (scopeGlass != null)
            scopeGlass.rectTransform.sizeDelta = new Vector2(height, height);

        // Only the width matters; both panels stretch vertically from their anchors.
        if (scopeLeft != null)
            scopeLeft.sizeDelta = new Vector2(side, scopeLeft.sizeDelta.y);

        if (scopeRight != null)
            scopeRight.sizeDelta = new Vector2(side, scopeRight.sizeDelta.y);
    }

    void UpdateClock()
    {
        float left = MatchState.TimeLeft;
        MatchPhase phase = MatchState.Phase;

        if (clock != null)
        {
            clock.text = $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}";

            // Red for the last thirty seconds. A clock that always looks the same is a clock
            // nobody reads.
            clock.color = phase == MatchPhase.Warmup ? hurt : left <= 30f ? critical : Color.white;
        }

        if (modeLabel != null)
            modeLabel.text = MatchModes.Of(MatchState.Mode).DisplayName;

        if (comboText != null)
        {
            int combo = player != null ? player.Combo : 0;
            comboText.gameObject.SetActive(combo > 1);

            if (combo > 1)
            {
                comboText.text = $"x{combo}";
                comboText.color = Color.Lerp(headshotColour, Color.white, comboFlash);
            }
        }
    }

    /// Gun game only. Without this you've no idea how close you are to the next weapon, which
    /// is the entire tension of the mode.
    void UpdateLadder()
    {
        bool show = MatchModes.Of(MatchState.Mode).ShowsLadder && MatchState.Phase != MatchPhase.Over;

        if (ladder != null)
            ladder.SetActive(show);

        if (!show)
            return;

        int rung = MatchState.LadderRung(PhotonNetwork.LocalPlayer);
        int done = MatchState.LadderKills(PhotonNetwork.LocalPlayer);
        int needed = MatchState.KillsToAdvance;

        string[] ladderKeys = WeaponLoadout.GunGameLadder;
        int top = ladderKeys.Length - 1;

        if (ladderLabel != null)
        {
            // What you're holding and what you're working towards, by name. A bare "RUNG 2 / 5"
            // says how far along you are and nothing about where you're going, which is most of
            // why the mode read as arbitrary - weapons kept changing and the number that was
            // supposed to explain it never named any of them.
            string holding = WeaponLoadout.DisplayName(ladderKeys[Mathf.Clamp(rung, 0, top)]).ToUpper();
            string next = rung < top
                ? WeaponLoadout.DisplayName(ladderKeys[rung + 1]).ToUpper()
                : "THE WIN";

            ladderLabel.text = $"{rung + 1}/{ladderKeys.Length}   {holding}   >   {next}";
        }

        // Pips rather than a number, because mid-fight you glance at this and can't read.
        for (int i = 0; i < ladderPips.Length; i++)
        {
            if (ladderPips[i] == null)
                continue;

            ladderPips[i].gameObject.SetActive(i < needed);
            ladderPips[i].color = i < done ? killColour : new Color(1f, 1f, 1f, 0.2f);
        }
    }

    /// <summary>
    /// The one line in the middle of the screen, and whatever sits under it.
    ///
    /// Strictly ordered, and the order is the point: the match ending beats a kill callout, and
    /// being dead beats the warmup countdown. All of these want the same piece of screen and
    /// more than one of them can be true at once.
    /// </summary>
    void UpdateCentre()
    {
        if (centreTitle == null)
            return;

        MatchPhase phase = MatchState.Phase;
        float left = MatchState.TimeLeft;

        if (phase == MatchPhase.Over)
        {
            Player winner = MatchState.Winner;
            bool youWon = winner != null && winner == PhotonNetwork.LocalPlayer;

            Show(resultsBackdrop, true);

            // Once per round, not once per frame across the whole 12-second results screen -
            // "make sure people get tokens at the end of each round." Reset the moment the
            // results screen itself goes away, below.
            if (!roundTokensAwarded)
            {
                roundTokensAwarded = true;
                AwardRoundTokens();
            }

            // A team match is won by a side, and saying "someone wins" over the top of that
            // would be answering a question nobody asked.
            if (MatchModes.Of(MatchState.Mode).UsesTeams)
            {
                int side = MatchState.WinningTeam;
                bool yours = side >= 0 && side == PlayerColours.TeamOf(PhotonNetwork.LocalPlayer);

                SetCentre(side >= 0 ? PlayerColours.TeamNames[side] + " WINS" : "DRAW",
                          side >= 0 ? PlayerColours.TeamPalette[side] : dim,
                          side < 0 ? $"nobody wins   -   next match in {left:F0}"
                          : yours ? $"that's you   -   next match in {left:F0}"
                          : $"{PlayerColours.TeamScore(0)} - {PlayerColours.TeamScore(1)}   "
                            + $"-   next match in {left:F0}");

                float beat = 1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.04f;
                centreTitle.rectTransform.localScale = Vector3.one * beat;

                UpdateStandings(true);
                return;
            }

            // Your own win reads differently to somebody else's. The name is the same size
            // either way, but being told YOU WIN is the part worth having.
            SetCentre(winner != null ? MatchState.NameOf(winner).ToUpper() : "NOBODY",
                      youWon ? headshotColour : killColour,
                      youWon ? $"YOU WIN   -   next match in {left:F0}"
                             : $"WINS   -   next match in {left:F0}");

            // A slow pulse rather than a static line. The results screen sits there for twelve
            // seconds and anything that doesn't move for twelve seconds stops being looked at.
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.04f;
            centreTitle.rectTransform.localScale = Vector3.one * pulse;

            UpdateStandings(true);
            return;
        }

        centreTitle.rectTransform.localScale = Vector3.one;
        Show(resultsBackdrop, false);
        UpdateStandings(false);
        roundTokensAwarded = false;

        if (RoomManager.AwaitingRespawn)
        {
            SetCentre("YOU DIED", critical,
                      $"back in {Mathf.Max(0f, RoomManager.RespawnAt - Time.time):F1}");
            return;
        }

        if (phase == MatchPhase.Warmup)
        {
            // Built into its own list rather than written back over the loadout. Rewriting what
            // LoadoutFor handed us corrupted the weapon table for the entire session - what it
            // returns is not always a copy, and a display name is not a weapon key.
            string[] loadout = PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer);
            string[] carrying = new string[loadout.Length];

            for (int i = 0; i < loadout.Length; i++)
                carrying[i] = WeaponLoadout.DisplayName(loadout[i]).ToUpper();

            SetCentre(MatchModes.Of(MatchState.Mode).ShowsLadder ? "CLIMB THE LADDER" : "GET READY", hurt,
                      $"{string.Join("   ", carrying)}   -   LIVE IN {Mathf.CeilToInt(left)}");
            return;
        }

        // Nothing to announce, so this is a kill callout fading out on its own.
        bool showing = killFlash > 0f;

        centreTitle.gameObject.SetActive(showing);
        Show(centreSubtitle, showing && centreSubtitle != null
                             && !string.IsNullOrEmpty(centreSubtitle.text));

        if (showing)
        {
            // Arrives big and settles, same shape as the hitmarker and the damage numbers -
            // this was the one thing on the HUD that just appeared at a flat scale of 1.
            float punchT = Mathf.Clamp01((titlePunchUntil - Time.unscaledTime) / titlePunchDuration);
            centreTitle.rectTransform.localScale = Vector3.one * (1f + punchT * punchT * 0.6f);

            // A beat of white at the very start of the punch, settling to the real colour - the
            // same "arrives hot, cools into place" read the hitmarker's own pop already uses.
            Color colour = Color.Lerp(new Color(killColour.r, killColour.g, killColour.b, killFlash),
                                      Color.white, punchT * 0.55f);
            colour.a = killFlash;
            centreTitle.color = colour;
        }
    }

    static int RankStat(Player player, bool byStyle) =>
        RoomManager.GetStat(player, byStyle ? RoomManager.StyleScoreKey : RoomManager.KillsKey);

    void SetCentre(string title, Color colour, string subtitle)
    {
        centreTitle.gameObject.SetActive(true);
        centreTitle.text = title;
        centreTitle.color = colour;

        if (centreSubtitle == null)
            return;

        centreSubtitle.gameObject.SetActive(true);
        centreSubtitle.text = subtitle;
        centreSubtitle.color = dim;
    }

    /// Full standings, shown only once the round is over. During a match this is screen you
    /// need to see through; afterwards there's nothing else to look at.
    /// 1ST/2ND/3RD in gold/silver/bronze, everything past that a plain "NTH" in a neutral
    /// colour - a real medal ladder rather than every row reading as flatly equal, which was
    /// most of what made the old leaderboard read as unfinished rather than just plain. The
    /// place/colour lookup itself moved to RankDisplay.cs so the tab scoreboard could share it.
    void UpdateStandings(bool show)
    {
        if (standingsContainer == null || standingsTemplate == null)
            return;

        int shown = 0;

        if (show)
        {
            // Style score is the win condition everywhere except gun game (see
            // MatchState.LeaderByScore), so the standings are ranked and read by it there
            // instead of by raw kills - a leaderboard that isn't ordered by the stat that
            // actually decided the match doesn't read as a leaderboard.
            bool byStyle = MatchModes.Of(MatchState.Mode).RanksByStyleScore;

            List<Player> ranked = new List<Player>(PhotonNetwork.PlayerList);
            ranked.Sort((a, b) => RankStat(b, byStyle).CompareTo(RankStat(a, byStyle)));

            for (int i = 0; i < ranked.Count; i++)
            {
                Player person = ranked[i];
                TMP_Text row = Row(standingsRows, standingsTemplate, standingsContainer, "Standing", shown);
                row.gameObject.SetActive(true);

                // In a team mode the side you were on is the thing worth reading first.
                int team = PlayerColours.TeamOf(person);
                string side = team >= 0 ? PlayerColours.TeamNames[team] + "  " : string.Empty;

                string stats = byStyle
                    ? $"{RoomManager.GetStat(person, RoomManager.StyleScoreKey)} STYLE   "
                      + $"{RoomManager.GetStat(person, RoomManager.KillsKey)} KILLS"
                    : $"{RoomManager.GetStat(person, RoomManager.KillsKey)} / "
                      + $"{RoomManager.GetStat(person, RoomManager.DeathsKey)}";

                string place = RankDisplay.Suffix(i);
                string placeHex = RankDisplay.ColourHex(i);

                // A rich-text colour tag for just the rank number rather than a second TMP_Text
                // per row - Row()'s own pooling is built around one text component per item, and
                // every other list on this HUD (feed, breakdown) already shares it, so this reuses
                // that instead of giving standings its own bespoke multi-element row structure.
                row.text = $"<color=#{placeHex}>{place}</color>  {side}{MatchState.NameOf(person).ToUpper()}   {stats}";

                row.color = person == PhotonNetwork.LocalPlayer ? headshotColour
                            : team >= 0 ? PlayerColours.TeamPalette[team] : dim;
                shown++;
            }

            // A real header instead of a blank line - what everybody was best at, cheap to
            // compute, gives the person who lost something to have won.
            List<MatchState.Award> awards = MatchState.Awards();

            if (awards.Count > 0)
            {
                TMP_Text header = Row(standingsRows, standingsTemplate, standingsContainer, "Standing", shown);
                header.gameObject.SetActive(true);
                header.text = "AWARDS";
                header.color = Fade(dim, 0.7f);
                shown++;
            }

            foreach (MatchState.Award award in awards)
            {
                TMP_Text row = Row(standingsRows, standingsTemplate, standingsContainer, "Standing", shown);
                row.gameObject.SetActive(true);
                row.text = $"{award.title}   {award.who}   {award.detail}";
                row.color = killColour;
                shown++;
            }
        }

        for (int i = shown; i < standingsRows.Count; i++)
            standingsRows[i].gameObject.SetActive(false);
    }

    void UpdateFeed()
    {
        if (feedContainer == null || feedTemplate == null)
            return;

        int shown = 0;

        // Newest first, so a fresh line arrives at the top and stale ones fall off the bottom,
        // rather than the whole list jumping every time somebody dies.
        for (int i = MatchState.Feed.Count - 1; i >= 0 && shown < feedRowLimit; i--)
        {
            MatchState.FeedEntry entry = MatchState.Feed[i];
            float age = Time.unscaledTime - entry.at;

            if (age > feedSeconds)
                continue;

            TMP_Text row = Row(feedRows, feedTemplate, feedContainer, "FeedRow", shown);
            row.gameObject.SetActive(true);

            float fade = Mathf.Clamp01((feedSeconds - age) / 1.5f);

            // Punches in on arrival rather than just appearing. Driven off the entry's own age
            // rather than separate per-row state, since age already measures exactly the thing a
            // punch needs to know - how long ago this specific line showed up - and every row
            // gets reused from the same pool as older entries scroll off, so an index-keyed punch
            // timer would have to be re-armed by hand every time a row changed which entry it was
            // showing anyway.
            row.rectTransform.localScale = Vector3.one * PunchScale(entry.at + 0.22f);

            switch (entry.kind)
            {
                case MatchState.FeedKind.Join:
                    row.text = $"{entry.actor} showed up";
                    row.color = Fade(joinColour, fade);
                    break;

                case MatchState.FeedKind.Leave:
                    row.text = $"{entry.actor} had enough";
                    row.color = Fade(leaveColour, fade);
                    break;

                default:
                    row.text = KillFeedLines.For(entry.actor, entry.subject, entry.weapon,
                                                 entry.headshot, entry.flavour, entry.revenge);

                    // Anything you were part of burns brighter. In a room of eight most of the
                    // feed is other people's business and reads as noise otherwise.
                    row.color = entry.involvesYou ? Fade(killColour, fade) : Fade(dim, fade);
                    break;
            }

            shown++;
        }

        for (int i = shown; i < feedRows.Count; i++)
            feedRows[i].gameObject.SetActive(false);
    }

    /// <summary>
    /// The edges go red and beat when you are nearly dead.
    ///
    /// Two things at once, which is the point: it is a warning you cannot miss and it is the tell
    /// that you are currently faster than everybody else. Beating rather than steady, and beating
    /// harder the worse it gets, so it reads as a pulse rather than as damage on the lens.
    ///
    /// Unscaled, so it keeps beating through a kill's hitstop - a heartbeat that stops when the
    /// world does is a strange thing to watch.
    /// </summary>
    // Which half-beat the heartbeat sound last fired on. Not reset when adrenaline drops out -
    // Time.unscaledTime keeps climbing regardless, so the next time it's needed the index has
    // simply moved on and the comparison below still fires cleanly on the next boundary.
    int lastHeartbeatIndex = -1;

    void UpdateAdrenaline()
    {
        if (adrenalineEdge == null)
            return;

        float amount = player != null ? player.Adrenaline : 0f;

        adrenalineEdge.gameObject.SetActive(amount > 0.01f);

        if (amount <= 0.01f)
            return;

        // Faster the closer to death, from about one beat a second to nearly three.
        float rate = 3f + amount * 5f;
        float phase = Time.unscaledTime * rate;

        // Retuned 2026-08-22 - reported as too intense. Peak alpha was 0.72 and the beat itself
        // swung a full 0.45 between dim and bright; together that's most of the screen edge
        // solid red at the worst moments, which reads as damage rather than a warning. Softened
        // both: a gentler swing (0.32) on a higher floor (0.68), and a lower ceiling on the alpha
        // (0.42) - still an unmissable pulse, not a strobe.
        float beat = 0.68f + 0.32f * Mathf.Abs(Mathf.Sin(phase));

        adrenalineEdge.color = new Color(0.75f, 0.03f, 0.06f, amount * beat * 0.42f);

        // Swelling slightly with the beat, so the edge breathes inward rather than only
        // brightening. Subtle, because a pumping screen is nauseating at any real size.
        adrenalineEdge.rectTransform.localScale = Vector3.one * (1f + (1f - beat) * 0.04f);

        // The audio half, added 2026-08-22 - the edge was already beating, nothing was making a
        // sound on the beat. abs(sin) completes a half-cycle (one visual pulse) every pi/rate
        // seconds, so floor(phase / pi) is a clean integer that ticks over exactly once per pulse
        // regardless of frame rate, rather than re-deriving "was that a peak" from noisy per-frame
        // comparisons.
        int beatIndex = Mathf.FloorToInt(phase / Mathf.PI);

        if (beatIndex != lastHeartbeatIndex)
        {
            lastHeartbeatIndex = beatIndex;
            GameAudio.PlayHeartbeat(amount);
        }
    }

    /// <summary>
    /// The unified style score: a running total plus a DMC/ULTRAKILL-shaped rank and decay bar
    /// on top of it - see StyleScore.cs for what actually feeds the multiplier. Reworked
    /// entirely from a slide-chain-only combo, which was reported directly as the most hated
    /// thing on the HUD; the chain is now one input among several rather than the whole system.
    ///
    /// Ranked rather than counted, same reasoning the old version had - a number tells you how
    /// many, a rank tells you how well. It climbs while you keep the rhythm and bleeds down when
    /// you don't (or get hit, or hit a wall). No permanent score readout - reported directly
    /// against an earlier version that kept one on screen at all times: "why is there a permanent
    /// score on the screen all the time I dont need to see it like that, i Like the ultrakill
    /// style." The running total (StyleScore.Score) still exists for the post-match standings and
    /// the MOST STYLISH award; it just isn't a fixture of the live HUD any more.
    ///
    /// The exhausted state is the one piece of the old version worth keeping standalone: being
    /// unable to slide-chain again with no explanation is the worst version of that mechanic, so
    /// it still borrows the rank line to say so even while the style multiplier itself is idle.
    /// </summary>
    void UpdateStyleMeter()
    {
        if (slideCombo == null)
            return;

        StyleScore style = player != null ? player.Style : null;

        // Runs unconditionally rather than only while the rank line itself is active - its own
        // BreakdownDuration timer is independent of the multiplier's decay, and the multiplier
        // can in principle bleed back down to idle before the breakdown's own window is up.
        UpdateStyleBreakdown(style);

        bool active = style != null && style.Active;

        slideCombo.gameObject.SetActive(active);

        if (!active)
        {
            lastStyleRank = string.Empty;

            if (slideMeterFill != null)
                slideMeterFill.fillAmount = 0f;

            return;
        }

        // The ULTRAKILL/DMC-style meter under the rank name - full the instant something lands,
        // draining toward empty by the time the multiplier would start bleeding off. The rank
        // name says how deep you are; the bar says how long you've got left to go deeper.
        if (slideMeterFill != null)
            slideMeterFill.fillAmount = style.DecayFraction;

        string rank = style.Rank;
        slideCombo.text = $"{rank}  x{style.Multiplier:F1}";

        // A rank that actually just changed gets a punch rather than only a slightly different
        // colour - the throb alone read as decoration, not as a reaction to something you just
        // did.
        if (lastStyleRank != string.Empty && rank != lastStyleRank)
            slidePunchUntil = Time.unscaledTime + 0.22f;

        lastStyleRank = rank;

        // Hotter and bigger the closer the multiplier is to its ceiling, so the top of the
        // range actually looks like the top of the range.
        float heat = Mathf.InverseLerp(1f, StyleScore.MaxMultiplier, style.Multiplier);
        slideCombo.color = Color.Lerp(Color.white, killColour, heat);

        // The punch decays fast and overshoots on the way in - a snap rather than a fade, per
        // the same "no fades" rule everything else in this HUD already follows.
        float punchT = Mathf.Clamp01((slidePunchUntil - Time.unscaledTime) / 0.22f);
        float punch = punchT * punchT * 0.5f;

        // A small continuous throb so it reads as live rather than printed, plus the punch on
        // top of it for the frames right after a rank-up.
        float throb = 1f + heat * 0.25f + Mathf.Sin(Time.unscaledTime * 9f) * 0.03f + punch;
        slideCombo.rectTransform.localScale = Vector3.one * throb;
    }

    /// <summary>
    /// The movement-tech combo, bottom left - a separate meter from the style score above it,
    /// same visual language (a name, a multiplier-shaped chain count, a draining bar, a punch on
    /// change) but reading MovementCombo instead of StyleScore. Reported directly after the kill
    /// meter started working: "no slide combo text still which should show up... make new
    /// movement tech combos and stuff" - grappling, a grenade jump, a bhop chain and a slide-hop
    /// chain all count here now, not just sliding.
    /// </summary>
    void UpdateMovementCombo()
    {
        if (movementCombo == null)
            return;

        MovementCombo combo = player != null ? player.MoveCombo : null;
        bool active = combo != null && combo.Active;

        movementCombo.gameObject.SetActive(active);

        if (!active)
        {
            lastComboTech = string.Empty;

            if (movementMeterFill != null)
                movementMeterFill.fillAmount = 0f;

            return;
        }

        if (movementMeterFill != null)
            movementMeterFill.fillAmount = combo.DecayFraction;

        string tech = combo.LastTech;
        movementCombo.text = $"{tech}  x{combo.Chain}";

        if (lastComboTech != string.Empty && tech != lastComboTech)
            comboPunchUntil = Time.unscaledTime + 0.22f;

        lastComboTech = tech;

        float heat = Mathf.InverseLerp(1f, MovementCombo.HeatChain, combo.Chain);
        movementCombo.color = Color.Lerp(Color.white, comboColour, heat);

        float punchT = Mathf.Clamp01((comboPunchUntil - Time.unscaledTime) / 0.22f);
        float punch = punchT * punchT * 0.5f;
        float throb = 1f + heat * 0.2f + Mathf.Sin(Time.unscaledTime * 9f) * 0.03f + punch;
        movementCombo.rectTransform.localScale = Vector3.one * throb;
    }

    /// <summary>
    /// "You can't slide-hop again yet" - its own element now, independent of both combo bars
    /// (see spentText's own field comment). Grey and steady rather than throbbing like the two
    /// combo readouts above - it isn't a rank climbing, it's a wait counting down.
    /// </summary>
    void UpdateSpentIndicator()
    {
        if (spentText == null)
            return;

        PlayerMovement mover = player != null ? player.GetComponent<PlayerMovement>() : null;
        bool exhausted = mover != null && mover.Exhausted;

        spentText.gameObject.SetActive(exhausted);

        if (exhausted)
            spentText.text = $"SPENT  {mover.ExhaustedFor:F1}";
    }

    /// The list of what actually fired on the last kill - "1.35x HEADSHOT" and so on, in
    /// descending order (StyleScore.RegisterKill already sorts it). Shows for
    /// StyleScore.BreakdownDuration then clears itself, the same shape the kill feed's own lines
    /// already fade on.
    void UpdateStyleBreakdown(StyleScore style)
    {
        if (breakdownContainer == null || breakdownTemplate == null)
            return;

        int shown = 0;

        if (style != null && style.BreakdownActive)
        {
            foreach (StyleScore.BreakdownEntry entry in style.LastBreakdown)
            {
                TMP_Text row = Row(breakdownRows, breakdownTemplate, breakdownContainer, "Breakdown", shown);
                row.gameObject.SetActive(true);
                row.text = $"{entry.shownMultiplier:F2}x {entry.label}";
                shown++;
            }
        }

        for (int i = shown; i < breakdownRows.Count; i++)
            breakdownRows[i].gameObject.SetActive(false);
    }

    static Color Fade(Color colour, float alpha) =>
        new Color(colour.r, colour.g, colour.b, colour.a * alpha);

    /// Grows a pool on demand off a hidden template, so every row inherits whatever font, size
    /// and colour the template was given in the editor.
    static TMP_Text Row(List<TMP_Text> pool, TMP_Text template, RectTransform parent,
                        string name, int index)
    {
        while (pool.Count <= index)
        {
            TMP_Text made = Instantiate(template, parent);
            made.name = $"{name}{pool.Count}";
            pool.Add(made);
        }

        return pool[index];
    }

    void UpdateDamageNumbers()
    {
        Camera camera = PlayerController.LocalCamera;

        foreach (DamageLabel label in damageLabels)
        {
            if (!label.live)
                continue;

            float age = (Time.unscaledTime - label.born) / damageSeconds;

            if (age >= 1f || camera == null)
            {
                label.live = false;
                label.text.gameObject.SetActive(false);
                continue;
            }

            // Reprojected every frame rather than pinned where it was born, so the number stays
            // stuck to the gorilla you hit while you strafe past it.
            Vector3 view = camera.WorldToViewportPoint(label.at);

            if (view.z <= 0f)
            {
                label.text.gameObject.SetActive(false);
                continue;
            }

            label.text.gameObject.SetActive(true);

            label.rect.anchorMin = label.rect.anchorMax = new Vector2(view.x, view.y);
            label.rect.anchoredPosition = new Vector2(0f, age * damageRise);

            // Arrives big and settles, same reason the hitmarker pops.
            label.rect.localScale = Vector3.one * (1.25f - age * 0.25f) * (label.head ? 1.35f : 1f);

            Color c = label.head ? headshotColour : Color.white;

            // Squared, so it holds full strength for most of its life and then leaves quickly
            // rather than lingering as a grey smear.
            label.text.color = new Color(c.r, c.g, c.b, 1f - age * age);
        }
    }

    DamageLabel FreeDamageLabel()
    {
        if (damageTemplate == null || damageContainer == null)
            return null;

        foreach (DamageLabel label in damageLabels)
        {
            if (!label.live)
                return label;
        }

        // Capped. A shotgun lands nine numbers on one trigger pull, and without a ceiling a
        // long fight grows this list until it's carrying hundreds of dead labels.
        if (damageLabels.Count >= 24)
        {
            DamageLabel oldest = damageLabels[0];

            foreach (DamageLabel label in damageLabels)
            {
                if (label.born < oldest.born)
                    oldest = label;
            }

            return oldest;
        }

        TMP_Text text = Instantiate(damageTemplate, damageContainer);
        text.name = $"Damage{damageLabels.Count}";

        DamageLabel made = new DamageLabel { text = text, rect = (RectTransform)text.transform };
        damageLabels.Add(made);

        return made;
    }

    static void Show(Component thing, bool visible)
    {
        if (thing != null)
            thing.gameObject.SetActive(visible);
    }

    static void Show(GameObject thing, bool visible)
    {
        if (thing != null)
            thing.SetActive(visible);
    }

    /// <summary>
    /// Opaque black everywhere except a circle in the middle, with a feathered edge and a soft
    /// darkening just inside the rim.
    ///
    /// Public and static so the play mode probe can check it. The scope can't be photographed
    /// composited - UI doesn't appear in a camera render to texture - but the mask on its own
    /// is checkable, and a scope you can't see out of is worth catching automatically.
    /// </summary>
    public static Texture2D BuildScopeMask(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "~scope" };

        float half = size * 0.5f;
        float radius = half * 0.985f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));

                // One pixel of feather, or the rim crawls with aliasing every time you move.
                float outside = Mathf.Clamp01(distance - radius + 1f);

                // A soft darkening just inside the rim, which is what makes it read as glass
                // rather than as a hole cut in a piece of card.
                float vignette = Mathf.Clamp01((distance - radius * 0.82f) / (radius * 0.18f)) * 0.45f;

                pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.Max(outside, vignette));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;

        return texture;
    }
}
