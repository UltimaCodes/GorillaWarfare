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

    [Header("Colours")]
    [SerializeField] Color healthy = new Color(0.55f, 1f, 0.1f);
    [SerializeField] Color hurt = new Color(1f, 0.85f, 0f);
    [SerializeField] Color critical = new Color(1f, 0.1f, 0.25f);
    [SerializeField] Color healed = new Color(0.4f, 1f, 0.5f);
    [SerializeField] Color headshotColour = new Color(1f, 0.95f, 0.25f);
    [SerializeField] Color killColour = new Color(1f, 0.35f, 0.05f);
    [SerializeField] Color joinColour = new Color(0.45f, 1f, 0.5f);
    [SerializeField] Color leaveColour = new Color(0.65f, 0.65f, 0.7f);
    [SerializeField] Color dim = new Color(1f, 1f, 1f, 0.55f);

    [Header("Feel")]
    [Tooltip("Canvas units the crosshair opens per degree of the weapon's spread cone.")]
    [SerializeField] float crosshairSpread = 24f;

    [Tooltip("How long a feed line stays up. The last second and a half of that is a fade.")]
    [SerializeField] float feedSeconds = 6f;

    [SerializeField] int feedRowLimit = 6;
    [SerializeField] float damageSeconds = 0.85f;

    [Tooltip("How far a damage number drifts up over its life, in canvas units.")]
    [SerializeField] float damageRise = 46f;

    PlayerController player;
    RectTransform canvasRect;

    float hitFlash;
    bool lastHitWasHead;
    float healFlash;
    float killFlash;
    float comboFlash;

    readonly List<TMP_Text> feedRows = new List<TMP_Text>();
    readonly List<TMP_Text> standingsRows = new List<TMP_Text>();
    readonly List<DamageLabel> damageLabels = new List<DamageLabel>();

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
            canvasRect = (RectTransform)canvas.transform;

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

        if (centreTitle != null)
            centreTitle.text = $"RUNG {rung + 1}";

        if (centreSubtitle != null)
            centreSubtitle.text = weapon.ToUpper();
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
        float ceiling = Mathf.Max(max, player.OvershieldCeiling);
        int points = player.HealthPoints;
        float fraction = Mathf.Clamp01(points / max);

        // Three steps rather than a gradient, so a colour change means something happened
        // rather than being a shade nobody can name.
        Color colour = fraction > 0.6f ? healthy : fraction > 0.3f ? hurt : critical;
        if (healFlash > 0f)
            colour = Color.Lerp(colour, healed, healFlash);

        if (healthNumber != null)
        {
            healthNumber.text = points.ToString();
            healthNumber.color = colour;
        }

        // The bar is scaled to the overshield ceiling rather than to max health, so a shielded
        // bar genuinely runs past where a full one stops instead of both reading as "full".
        float width = healthTrack != null ? healthTrack.rect.width : 0f;

        if (healthFill != null)
        {
            healthFill.rectTransform.sizeDelta =
                new Vector2(Mathf.Min(points, max) / ceiling * width,
                            healthFill.rectTransform.sizeDelta.y);
            healthFill.color = colour;
        }

        if (healthShield != null)
        {
            float over = player.Overshield / ceiling * width;

            healthShield.gameObject.SetActive(over > 0.5f);
            healthShield.rectTransform.anchoredPosition =
                new Vector2(max / ceiling * width, healthShield.rectTransform.anchoredPosition.y);
            healthShield.rectTransform.sizeDelta =
                new Vector2(over, healthShield.rectTransform.sizeDelta.y);
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

        ammoNumber.text = gun.Reloading ? "--" : gun.Ammo.ToString();
        ammoNumber.color = gun.Reloading ? hurt : gun.Ammo == 0 ? critical : Color.white;

        // Bare number, no "x". Next to something that size an x reads as multiplication, and
        // there's nothing else it could be counting.
        spareNumber.text = gun.SpareMagazines.ToString();
        spareNumber.color = gun.SpareMagazines > 0 ? dim : critical;
    }

    void UpdateCrosshair()
    {
        bool aiming = player != null && player.IsAiming;
        SingleShotGun gun = player != null ? player.ActiveGun : null;
        GunInfo info = gun != null ? gun.Info : null;

        // Opens with the cone the weapon actually fires into, so an inaccurate gun looks
        // inaccurate before you've missed with it. Switchable, because some people would rather
        // have a reticle that never moves than one that tells them the truth.
        float spread = GameSettings.CrosshairDynamic && info != null ? info.spread : 0f;
        float gap = GameSettings.CrosshairGap + spread * crosshairSpread;

        float length = GameSettings.CrosshairSize;
        float thickness = GameSettings.CrosshairThickness;
        Color colour = GameSettings.CrosshairColour;

        Tick(crosshairUp, new Vector2(0f, gap + length * 0.5f),
             new Vector2(thickness, length), colour, !aiming);
        Tick(crosshairDown, new Vector2(0f, -gap - length * 0.5f),
             new Vector2(thickness, length), colour, !aiming);
        Tick(crosshairLeft, new Vector2(-gap - length * 0.5f, 0f),
             new Vector2(length, thickness), colour, !aiming);
        Tick(crosshairRight, new Vector2(gap + length * 0.5f, 0f),
             new Vector2(length, thickness), colour, !aiming);

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

        // Snaps out big and shrinks in rather than only fading. A marker that just dims reads
        // as a light going out; one that moves reads as something landing.
        float pop = 1f + (1f - hitFlash) * 0.9f;
        hitMarker.rectTransform.localScale = Vector3.one * (lastHitWasHead ? 1.6f : 1f) * pop;

        Color c = lastHitWasHead ? headshotColour : Color.white;
        hitMarker.color = new Color(c.r, c.g, c.b, hitFlash);
    }

    /// <summary>
    /// One arm of the crosshair, positioned, sized and coloured from the settings.
    ///
    /// Position is the outer edge plus half the length, because the rects are centre pivoted -
    /// the gap is meant to be the distance from the middle of the screen to where the mark
    /// starts, not to where its centre happens to land, and getting that wrong makes the gap
    /// slider do something subtly different from what it says.
    /// </summary>
    static void Tick(RectTransform tick, Vector2 position, Vector2 size, Color colour, bool visible)
    {
        if (tick == null)
            return;

        tick.gameObject.SetActive(visible);
        tick.anchoredPosition = position;
        tick.sizeDelta = size;

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
            modeLabel.text = MatchState.Mode == MatchMode.GunGame ? "GUN GAME" : "DEATHMATCH";

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
        bool show = MatchState.Mode == MatchMode.GunGame && MatchState.Phase != MatchPhase.Over;

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

            SetCentre(winner != null ? MatchState.NameOf(winner).ToUpper() : "NOBODY", killColour,
                      $"WINS   -   next match in {left:F0}");

            UpdateStandings(true);
            return;
        }

        UpdateStandings(false);

        if (RoomManager.AwaitingRespawn)
        {
            SetCentre("YOU DIED", critical,
                      $"back in {Mathf.Max(0f, RoomManager.RespawnAt - Time.time):F1}");
            return;
        }

        if (phase == MatchPhase.Warmup)
        {
            string[] carrying = PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer);

            for (int i = 0; i < carrying.Length; i++)
                carrying[i] = WeaponLoadout.DisplayName(carrying[i]).ToUpper();

            SetCentre(MatchState.Mode == MatchMode.GunGame ? "CLIMB THE LADDER" : "GET READY", hurt,
                      $"{string.Join("   ", carrying)}   -   LIVE IN {Mathf.CeilToInt(left)}");
            return;
        }

        // Nothing to announce, so this is a kill callout fading out on its own.
        bool showing = killFlash > 0f;

        centreTitle.gameObject.SetActive(showing);
        Show(centreSubtitle, showing && centreSubtitle != null
                             && !string.IsNullOrEmpty(centreSubtitle.text));

        if (showing)
            centreTitle.color = new Color(killColour.r, killColour.g, killColour.b, killFlash);
    }

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
    void UpdateStandings(bool show)
    {
        if (standingsContainer == null || standingsTemplate == null)
            return;

        int shown = 0;

        if (show)
        {
            foreach (Player person in PhotonNetwork.PlayerList)
            {
                TMP_Text row = Row(standingsRows, standingsTemplate, standingsContainer, "Standing", shown);
                row.gameObject.SetActive(true);
                row.text = $"{MatchState.NameOf(person)}   "
                           + $"{RoomManager.GetStat(person, RoomManager.KillsKey)} / "
                           + $"{RoomManager.GetStat(person, RoomManager.DeathsKey)}";
                row.color = person == PhotonNetwork.LocalPlayer ? headshotColour : dim;
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
                                                 entry.headshot, entry.flavour);

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
