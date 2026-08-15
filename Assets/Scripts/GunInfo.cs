using UnityEngine;

[CreateAssetMenu(menuName = "FPS/New Gun")]
public class GunInfo : ItemInfo
{
    [Header("Damage")]
    public float damage = 20f;
    public float maxRange = 200f;

    [Tooltip("Distance at which damage starts dropping. Beyond it, damage falls to falloffFloor by maxRange.")]
    public float falloffStart = 25f;

    [Tooltip("Fraction of damage remaining at maxRange. 1 means no falloff at all.")]
    [Range(0.1f, 1f)] public float falloffFloor = 1f;

    [Header("Fire")]
    [Tooltip("Rounds per second.")]
    public float fireRate = 8f;

    [Tooltip("Held trigger keeps firing. Off means one shot per click.")]
    public bool automatic;

    [Tooltip("Cone of inaccuracy in degrees. 0 is laser accurate.")]
    public float spread;

    [Tooltip("Rays per trigger pull. Above 1 makes it a shotgun - each pellet rolls its own spread.")]
    public int pelletsPerShot = 1;

    [Tooltip("Melee weapons swing instead of shooting: no ammo, very short range, no recoil.")]
    public bool melee;

    [Tooltip("Both hands on it. A pistol is one banana in one fist; everything longer needs a "
             + "second hand further up. Drives the arm pose on the copy of you other people see.")]
    public bool twoHanded = true;

    [Header("Ammo")]
    [Tooltip("Shots per banana.")]
    public int magazineSize = 30;

    [Tooltip("Spare bananas. Run out and the weapon is dead until you find more.")]
    public int spareMagazines = 5;

    public float reloadTime = 1.8f;

    [Header("Ripeness")]
    [Tooltip("Green when full, yellow as you fire, brown when nearly out.")]
    public Color unripe = new Color(0.55f, 0.78f, 0.22f);
    public Color ripe = new Color(0.96f, 0.82f, 0.16f);
    public Color overripe = new Color(0.36f, 0.24f, 0.12f);

    /// <summary>
    /// The banana's colour for a given magazine state. Fresh is green, half spent is yellow,
    /// nearly empty is brown - so the weapon in your hands is the ammo counter and you can read
    /// it without looking at any UI.
    /// </summary>
    public Color RipenessFor(int ammo)
    {
        if (magazineSize <= 0)
            return ripe;

        float spent = 1f - Mathf.Clamp01(ammo / (float)magazineSize);

        // Two stages rather than one lerp, because green straight to brown passes through a
        // muddy olive and never looks like a ripe banana at any point.
        return spent < 0.5f
            ? Color.Lerp(unripe, ripe, spent * 2f)
            : Color.Lerp(ripe, overripe, (spent - 0.5f) * 2f);
    }

    [Header("Recoil")]
    [Tooltip("Degrees of upward kick per shot at the start of a spray.")]
    public float verticalKick = 1.4f;

    [Tooltip("Degrees of sideways drift once the spray gets going.")]
    public float horizontalKick = 0.5f;

    [Tooltip("How many shots before the climb flattens out into drift.")]
    public int patternLength = 8;

    [Tooltip("Fraction of accumulated recoil that returns when you stop firing. 1 gives it all back.")]
    [Range(0f, 1f)] public float recoilRecovery = 0.75f;

    [Tooltip("How fast recovery happens, in units per second.")]
    public float recoverySpeed = 6f;

    /// <summary>
    /// Damage after distance falloff. This is what stops a shotgun being a sniper - the pellets
    /// still reach, they just stop being worth anything. Smooth rather than a cliff, so there's
    /// no exact metre where a gun suddenly stops working.
    /// </summary>
    public float DamageAtRange(float distance)
    {
        if (falloffFloor >= 1f || distance <= falloffStart)
            return damage;

        float t = Mathf.InverseLerp(falloffStart, maxRange, distance);
        return damage * Mathf.Lerp(1f, falloffFloor, t * t);   // squared so it holds up then drops
    }

    public float SecondsBetweenShots => fireRate > 0f ? 1f / fireRate : 0.1f;

    /// <summary>
    /// Recoil for a given shot in the spray, as (pitch, yaw) degrees.
    ///
    /// Deterministic on purpose. A random cone is unlearnable, so spraying is just luck; a fixed
    /// pattern means you can learn to pull against it, which is the whole skill in CS and its
    /// descendants. Climbs hard and mostly straight for the first few rounds, then flattens and
    /// starts weaving sideways.
    /// </summary>
    public Vector2 RecoilForShot(int shotIndex)
    {
        float span = Mathf.Max(1, patternLength);
        float t = Mathf.Clamp01(shotIndex / span);

        // Climb is strongest at the start and tapers - that early rise is what you learn to
        // counter, and it stops the gun walking off the top of the screen on long sprays.
        float pitch = verticalKick * Mathf.Lerp(1f, 0.3f, t);

        // Sideways only really kicks in after the climb, and alternates so the pattern reads as
        // a shape rather than a drift in one direction.
        float yaw = horizontalKick * Mathf.Sin(shotIndex * 0.9f) * Mathf.Clamp01(shotIndex / (span * 0.5f));

        return new Vector2(pitch, yaw);
    }
}
