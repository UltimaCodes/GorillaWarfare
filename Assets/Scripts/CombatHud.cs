using UnityEngine;

// Ammo readout, hitmarker and a crosshair that reacts.
//
// Drawn with IMGUI rather than built as a Canvas, because it needs no prefab wiring and no
// scene edits - the same reason everything else here is built at runtime. It's replaced wholesale
// when the real UI lands in M5.
//
// The crosshair is the important part. Recoil is a pattern you're meant to learn and fight, and
// a static PNG hides all of it - you can't see the gun climbing, and you can't see your spread
// opening up. A crosshair that moves with the recoil and grows with the spread makes both legible.
public class CombatHud : MonoBehaviour
{
    [SerializeField] float gap = 6f;
    [SerializeField] float thickness = 2f;
    [SerializeField] float length = 10f;
    [SerializeField] float spreadScale = 18f;

    [SerializeField] Color crosshairColour = new Color(0.55f, 0.9f, 1f, 0.9f);
    [SerializeField] Color hitColour = new Color(1f, 0.25f, 0.2f, 1f);
    [SerializeField] Color ammoColour = new Color(0.95f, 0.85f, 0.3f, 1f);

    PlayerController player;
    Texture2D pixel;
    GUIStyle ammoStyle;
    GUIStyle spareStyle;

    float hitMarker;      // 1 right after a hit, decays
    bool lastHitWasHead;

    static readonly Color reloadingColour = new Color(1f, 0.55f, 0.25f, 1f);
    static readonly Color dryColour = new Color(1f, 0.3f, 0.25f, 1f);
    static readonly Color spareColour = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color nameColour = new Color(1f, 1f, 1f, 0.4f);

    static readonly Color healthyColour = new Color(0.55f, 1f, 0.1f, 1f);
    static readonly Color hurtColour = new Color(1f, 0.85f, 0f, 1f);
    static readonly Color criticalColour = new Color(1f, 0.1f, 0.25f, 1f);
    static readonly Color emptyColour = new Color(0.12f, 0.12f, 0.14f, 0.85f);

    GUIStyle healthStyle;
    GUIStyle streakStyle;
    string healthText = "140";
    int shownHealth = -1;

    float healFlash;
    int healAmount;

    static readonly Color healColour = new Color(0.4f, 1f, 0.5f, 1f);
    static readonly Color streakColour = new Color(1f, 0.55f, 0.1f, 1f);
    static readonly Color shieldColour = new Color(0.3f, 0.85f, 1f, 1f);
    static readonly Color headColour = new Color(1f, 0.95f, 0.25f, 1f);
    static readonly Color killColour = new Color(1f, 0.35f, 0.05f, 1f);

    float killFlash;
    string killText = string.Empty;
    string killSubtext = string.Empty;

    float comboFlash;
    int comboShown;

    GUIStyle numberStyle;
    GUIStyle killStyle;

    Texture2D scopeMask;
    int scopeSize;

    string ammoText = string.Empty;
    string spareText = string.Empty;
    string weaponText = string.Empty;
    int shownAmmo = int.MinValue;
    int shownSpares = int.MinValue;
    string shownWeapon;
    bool shownReloading;

    void CacheAmmoText(SingleShotGun gun)
    {
        if (gun.Ammo != shownAmmo || gun.Reloading != shownReloading)
        {
            shownAmmo = gun.Ammo;
            shownReloading = gun.Reloading;
            ammoText = shownReloading ? "--" : shownAmmo.ToString();
        }

        if (gun.SpareMagazines != shownSpares)
        {
            shownSpares = gun.SpareMagazines;
            spareText = shownSpares.ToString();
        }

        if (!ReferenceEquals(gun.name, shownWeapon))
        {
            shownWeapon = gun.name;
            weaponText = WeaponLoadout.DisplayName(shownWeapon).ToUpper();
        }
    }

    public void Bind(PlayerController owner)
    {
        player = owner;
    }

    // Numbers thrown off a hit, in world space so they stay where the shot landed while you
    // keep moving. Fixed size pool - a shotgun lands nine at once and allocating for each is
    // how a satisfying effect turns into a stutter.
    struct DamageNumber
    {
        public Vector3 at;
        public string text;
        public float born;
        public bool head;
        public bool live;
    }

    const int maxNumbers = 24;
    const float numberLife = 0.85f;

    readonly DamageNumber[] numbers = new DamageNumber[maxNumbers];
    int nextNumber;

    /// <param name="worldPoint">Where the shot actually landed, not where the crosshair is.</param>
    public void ShowDamage(Vector3 worldPoint, float amount, bool headshot)
    {
        numbers[nextNumber] = new DamageNumber
        {
            at = worldPoint,
            text = Mathf.RoundToInt(amount).ToString(),
            born = Time.unscaledTime,
            head = headshot,
            live = true,
        };

        nextNumber = (nextNumber + 1) % maxNumbers;
    }

    /// Hits landed back to back. Drawn as a running count once it's worth counting.
    public void ShowCombo(int hits)
    {
        comboShown = hits;
        comboFlash = 1f;
    }

    /// <param name="multikill">Kills close enough together to be one moment.</param>
    public void ShowKill(int multikill, int streak)
    {
        killFlash = 1f;
        killText = MultikillName(multikill);
        killSubtext = streak > 1 ? $"{streak} IN A ROW" : string.Empty;
    }

    // Named rather than numbered, because "DOUBLE KILL" lands and "2" doesn't.
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

    /// Called when a kill puts health back, so the number doesn't silently jump while you're
    /// looking at something else.
    public void ShowHeal(float amount)
    {
        healFlash = 1f;
        healAmount = Mathf.RoundToInt(amount);
    }

    /// Called when one of our shots connects. headshot draws the marker differently.
    public void ShowHit(bool headshot)
    {
        hitMarker = 1f;
        lastHitWasHead = headshot;
    }

    void Update()
    {
        if (hitMarker > 0f)
            hitMarker = Mathf.Max(0f, hitMarker - Time.deltaTime * 3.2f);

        // Unscaled throughout - hitstop is running during exactly the moments these are on
        // screen, and on scaled time they'd hang frozen with the rest of the world.
        float dt = Time.unscaledDeltaTime;

        if (healFlash > 0f)
            healFlash = Mathf.Max(0f, healFlash - dt * 0.8f);

        if (killFlash > 0f)
            killFlash = Mathf.Max(0f, killFlash - dt * 0.75f);

        if (comboFlash > 0f)
            comboFlash = Mathf.Max(0f, comboFlash - dt * 1.6f);
    }

    void OnGUI()
    {
        if (player == null)
            return;

        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        SingleShotGun gun = player.ActiveGun;

        // The crosshair stays dead centre, like CS. Recoil already rotates the camera, so shots
        // always leave from screen centre - offsetting the crosshair on top of that counted the
        // kick twice and put the reticle above where the bullets actually went. You compensate
        // by pulling down against the view, not by chasing a moving reticle.

        // Nothing while aiming. A hip crosshair drawn over a scoped view is telling you about
        // a spread you no longer have, in a place the barrel isn't pointing.
        bool aiming = player.IsAiming;

        if (aiming)
        {
            DrawScope(cx, cy);
            DrawAmmo(gun);
            DrawHealth();
        DrawDamageNumbers();
        DrawCombo(cx, cy);
        DrawKillCallout(cx, cy);
            GUI.color = Color.white;
            return;
        }

        // Spread opens the gap, so an inaccurate weapon looks inaccurate.
        float spread = gun != null && gun.Info != null ? gun.Info.spread : 0f;
        float g = gap + spread * spreadScale * 0.5f;

        Color c = hitMarker > 0f ? Color.Lerp(crosshairColour, hitColour, hitMarker) : crosshairColour;
        GUI.color = c;

        // Four ticks around the centre.
        Rect(cx - thickness * 0.5f, cy - g - length, thickness, length);   // up
        Rect(cx - thickness * 0.5f, cy + g, thickness, length);            // down
        Rect(cx - g - length, cy - thickness * 0.5f, length, thickness);   // left
        Rect(cx + g, cy - thickness * 0.5f, length, thickness);            // right

        // Hitmarker: diagonal ticks, bigger and brighter for a headshot, so you can tell what
        // you did without reading a number.
        if (hitMarker > 0f)
        {
            // Snaps out large and shrinks in, rather than simply fading. A marker that only
            // fades reads as a light going off; one that moves reads as something happening.
            float pop = 1f + (1f - hitMarker) * 0.9f;
            float size = (lastHitWasHead ? 20f : 12f) * hitMarker * pop;
            float weight = lastHitWasHead ? thickness * 2f : thickness;

            GUI.color = lastHitWasHead
                ? new Color(1f, 0.95f, 0.3f, hitMarker)
                : new Color(hitColour.r, hitColour.g, hitColour.b, hitMarker);

            // Four corners of a box round the centre.
            Rect(cx - size, cy - size, size * 0.6f, weight);
            Rect(cx - size, cy - size, weight, size * 0.6f);

            Rect(cx + size - size * 0.6f, cy - size, size * 0.6f, weight);
            Rect(cx + size - weight, cy - size, weight, size * 0.6f);

            Rect(cx - size, cy + size - weight, size * 0.6f, weight);
            Rect(cx - size, cy + size - size * 0.6f, weight, size * 0.6f);

            Rect(cx + size - size * 0.6f, cy + size - weight, size * 0.6f, weight);
            Rect(cx + size - weight, cy + size - size * 0.6f, weight, size * 0.6f);
        }

        DrawHealth();
        DrawDamageNumbers();
        DrawCombo(cx, cy);
        DrawKillCallout(cx, cy);

        DrawAmmo(gun);

        GUI.color = Color.white;
    }

    // Health, bottom left: a bar, with the number above it.
    //
    // Was ten discrete blocks. That reads as a quantity but not as a level - you could see you
    // had lost some without seeing how close the next shot was to finishing you. A continuous
    // bar answers that at a glance and the number answers it exactly.
    //
    // Scaled to the overshield ceiling rather than to normal health, so a shielded player's bar
    // genuinely runs past where a full one stops instead of both just looking full.
    void DrawHealth()
    {
        if (healthStyle == null)
        {
            healthStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerLeft,
                fontStyle = FontStyle.Bold,
                fontSize = 52,
            };
        }

        if (streakStyle == null)
        {
            streakStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerLeft,
                fontStyle = FontStyle.Bold,
                fontSize = 20,
            };
        }

        float max = Mathf.Max(1f, player.MaxHealth);
        float ceiling = Mathf.Max(max, player.OvershieldCeiling);

        int points = player.HealthPoints;
        if (points != shownHealth)
        {
            shownHealth = points;
            healthText = points.ToString();
        }

        float fraction = Mathf.Clamp01(points / max);

        // Steps rather than a blend, so a colour change means something happened.
        Color colour = fraction > 0.6f ? healthyColour
                     : fraction > 0.3f ? hurtColour
                     : criticalColour;

        Color shown = healFlash > 0f ? Color.Lerp(colour, healColour, healFlash) : colour;

        const float barWidth = 300f;
        const float barHeight = 20f;

        float left = 40f;
        float bottom = Screen.height - 44f;
        float top = bottom - barHeight;

        GUI.color = emptyColour;
        Rect(left, top, barWidth, barHeight);

        float normal = Mathf.Min(points, max) / ceiling * barWidth;
        GUI.color = shown;
        Rect(left, top, normal, barHeight);

        if (player.Overshield > 0f)
        {
            GUI.color = shieldColour;
            Rect(left + normal, top, player.Overshield / ceiling * barWidth, barHeight);
        }

        // A notch where normal health ends, so overshield reads as a bonus rather than as the
        // bar just being longer today.
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        Rect(left + max / ceiling * barWidth - 1f, top - 2f, 2f, barHeight + 4f);

        GUI.color = shown;
        GUI.Label(new Rect(left, 0f, 320f, top - 4f), healthText, healthStyle);

        if (healFlash > 0f)
        {
            GUI.color = new Color(healColour.r, healColour.g, healColour.b, healFlash);
            GUI.Label(new Rect(left + 112f, 0f, 200f, top - 14f), $"+{healAmount}", streakStyle);
        }

        // Only once it's worth mentioning - a "streak" of one is just a kill.
        if (player.Killstreak > 1)
        {
            GUI.color = streakColour;
            GUI.Label(new Rect(left, 0f, 300f, bottom + 26f), $"{player.Killstreak} IN A ROW", streakStyle);
        }

        GUI.color = Color.white;
    }

    // The scoped view: everything outside a circle is black, with hairlines across it.
    //
    // The circle is a generated texture rather than a drawn shape, because IMGUI has no way to
    // fill one - and a black square with a hole in it is exactly what a scope is anyway. It's
    // built once at the size it will be drawn, so the edge stays crisp instead of being scaled.
    void DrawScope(float cx, float cy)
    {
        float diameter = Screen.height;

        if (scopeMask == null || scopeSize != Mathf.RoundToInt(diameter))
        {
            scopeSize = Mathf.RoundToInt(diameter);
            scopeMask = BuildScopeMask(Mathf.Min(scopeSize, 1024));
        }

        GUI.color = Color.white;

        float left = cx - diameter * 0.5f;
        GUI.DrawTexture(new UnityEngine.Rect(left, 0f, diameter, diameter), scopeMask);

        // The circle is as tall as the screen, so anything wider than it is blacked out.
        GUI.color = Color.black;
        Rect(0f, 0f, left, Screen.height);
        Rect(left + diameter, 0f, Screen.width - (left + diameter), Screen.height);

        // Hairlines, all the way across, with a gap in the middle so they don't cover what you
        // are shooting at.
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        float gapHalf = 12f;
        float reach = diameter * 0.5f;

        Rect(cx - reach, cy - 0.5f, reach - gapHalf, 1f);
        Rect(cx + gapHalf, cy - 0.5f, reach - gapHalf, 1f);
        Rect(cx - 0.5f, cy - reach, 1f, reach - gapHalf);
        Rect(cx - 0.5f, cy + gapHalf, 1f, reach - gapHalf);

        GUI.color = Color.white;
    }

    /// <summary>
    /// Opaque black everywhere except a circle in the middle, with a soft edge and a dark rim.
    ///
    /// Public so the play mode probe can check it. The scope can't be photographed composited -
    /// IMGUI doesn't appear in a camera render - but the mask on its own is checkable, and a
    /// scope you can't see out of is the sort of thing worth catching automatically.
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

    // Ammo, bottom right. Weapon name on top, the number in the banana big underneath, and how
    // many spare bananas you have tucked under its right shoulder.
    //
    // The spare count is bare - "2", not "x2". The x reads as multiplication next to a number
    // that size, and there is nothing else it could be counting.
    void DrawAmmo(SingleShotGun gun)
    {
        if (gun == null || gun.Info == null || gun.Info.melee)
            return;

        if (ammoStyle == null)
        {
            ammoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            spareStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
        }

        CacheAmmoText(gun);

        float right = Screen.width - 46f;
        float bottom = Screen.height - 44f;

        spareStyle.fontSize = 17;
        GUI.color = nameColour;
        GUI.Label(new Rect(0f, 0f, right, bottom - 64f), weaponText, spareStyle);

        ammoStyle.fontSize = 64;
        GUI.color = gun.Reloading ? reloadingColour
                  : gun.Ammo == 0 ? dryColour
                  : ammoColour;
        GUI.Label(new Rect(0f, 0f, right, bottom), ammoText, ammoStyle);

        spareStyle.fontSize = 24;
        GUI.color = gun.SpareMagazines > 0 ? spareColour : dryColour;
        GUI.Label(new Rect(0f, 0f, right + 32f, bottom + 28f), spareText, spareStyle);

        GUI.color = Color.white;
    }

    // Each number rises and fades from where the shot landed. Projected every frame rather
    // than pinned on screen, so they stay attached to the world while you strafe past.
    void DrawDamageNumbers()
    {
        Camera cam = PlayerController.LocalCamera;
        if (cam == null)
            return;

        if (numberStyle == null)
        {
            numberStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
        }

        for (int i = 0; i < numbers.Length; i++)
        {
            if (!numbers[i].live)
                continue;

            float age = (Time.unscaledTime - numbers[i].born) / numberLife;

            if (age >= 1f)
            {
                numbers[i].live = false;
                continue;
            }

            Vector3 view = cam.WorldToViewportPoint(numbers[i].at);
            if (view.z <= 0f)
                continue;

            float x = view.x * Screen.width;
            float y = (1f - view.y) * Screen.height - age * 46f;

            // Big on arrival, settling as it fades. Same reason the hitmarker pops.
            numberStyle.fontSize = Mathf.RoundToInt((numbers[i].head ? 30f : 22f) * (1.25f - age * 0.25f));

            Color c = numbers[i].head ? headColour : Color.white;
            GUI.color = new Color(c.r, c.g, c.b, 1f - age * age);
            GUI.Label(new Rect(x - 60f, y - 20f, 120f, 40f), numbers[i].text, numberStyle);
        }

        GUI.color = Color.white;
    }

    // The running count of hits landed back to back, under the crosshair where you're looking.
    void DrawCombo(float cx, float cy)
    {
        int combo = player.Combo;
        if (combo < 2)
            return;

        if (killStyle == null)
            killStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        killStyle.fontSize = Mathf.RoundToInt(26f + Mathf.Min(combo, 10) * 1.6f + comboFlash * 10f);

        GUI.color = Color.Lerp(headColour, Color.white, comboFlash);
        GUI.Label(new Rect(cx - 150f, cy + 42f, 300f, 50f), $"x{combo}", killStyle);
        GUI.color = Color.white;
    }

    // The big one. Snaps in at full size and shrinks slightly as it fades - no easing in,
    // because arriving instantly is the whole point.
    void DrawKillCallout(float cx, float cy)
    {
        if (killFlash <= 0f)
            return;

        if (killStyle == null)
            killStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        float fade = Mathf.Clamp01(killFlash);

        killStyle.fontSize = Mathf.RoundToInt(52f + fade * 14f);
        GUI.color = new Color(killColour.r, killColour.g, killColour.b, fade);
        GUI.Label(new Rect(cx - 300f, cy - 150f, 600f, 80f), killText, killStyle);

        if (!string.IsNullOrEmpty(killSubtext))
        {
            killStyle.fontSize = 22;
            GUI.color = new Color(1f, 1f, 1f, fade * 0.8f);
            GUI.Label(new Rect(cx - 300f, cy - 96f, 600f, 40f), killSubtext, killStyle);
        }

        GUI.color = Color.white;
    }

    void Rect(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), pixel);
    }
}
