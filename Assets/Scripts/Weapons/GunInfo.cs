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

    [Tooltip("How a melee weapon is held, in degrees. Exposed rather than hard coded because I "
             + "have guessed it wrong twice - drag it in the inspector until the peel points the "
             + "way you want and it will stay there.")]
    public Vector3 meleeHold = new Vector3(72f, 0f, 18f);

    [Tooltip("How far the stab drives forward, in degrees about the same axis.")]
    public float meleeSwing = 65f;

    /// <summary>
    /// Knockback applied to the shooter the instant this is fired, in metres per second.
    ///
    /// Separate from the blast. Raze's ult throws you when you fire it, not when it lands - the
    /// launch is the recoil of putting something that heavy out of a tube, and waiting for the
    /// explosion means the timing never feels like yours. Zero on everything that is not a
    /// launcher.
    /// </summary>
    public float fireKnockback;

    [Tooltip("Both hands on it. A pistol is one banana in one fist; everything longer needs a "
             + "second hand further up. Drives the arm pose on the copy of you other people see.")]
    public bool twoHanded = true;

    [Header("Aim")]
    [Tooltip("Right click pulls the weapon up to eye level and narrows the view.")]
    public bool canAim;

    [Tooltip("Field of view while aiming. The lower it goes the more it magnifies, and mouse "
             + "sensitivity is scaled by the same ratio so aiming doesn't also make you twitchy.")]
    [Range(10f, 60f)] public float aimFov = 26f;

    [Tooltip("Spread while aiming, as a fraction of the hip fired figure.")]
    [Range(0f, 1f)] public float aimSpreadScale = 0.15f;

    [Header("Projectile")]
    [Tooltip("Fires a thing that travels instead of a raycast that arrives. Turns the whole "
             + "weapon into a different kind of problem for whoever is being shot at.")]
    public bool projectile;

    [Tooltip("Metres per second. Slow enough to see and dodge is the point; much past 60 and it "
             + "may as well be hitscan.")]
    public float projectileSpeed = 34f;

    [Tooltip("How much of normal gravity the shell feels. The arc is what lets you drop one "
             + "behind cover, and a launcher that fires flat is just a slow rifle.")]
    public float projectileGravity = 0.55f;

    [Tooltip("Metres it must travel before it will detonate on anything. Stops firing at a wall "
             + "you are touching from killing you instead of launching you.")]
    public float armingDistance = 1.2f;

    [Tooltip("Blast radius in metres. Damage falls off linearly to nothing at the edge.")]
    public float blastRadius = 5f;

    [Tooltip("How hard the blast throws other people.")]
    public float knockback = 9f;

    [Tooltip("How hard it throws you. Larger than the figure above on purpose - this is the "
             + "number that decides whether the weapon is a mobility tool or a nudge.")]
    public float selfKnockback = 15f;

    [Tooltip("Fraction of the damage you take from your own blast. Zero means rocket jumping "
             + "costs you nothing but commitment, which is the right trade for five friends.")]
    [Range(0f, 1f)] public float selfDamageScale;

    [Header("Ammo")]
    [Tooltip("Shots per banana.")]
    public int magazineSize = 30;

    [Tooltip("Spare bananas. Run out and the weapon is dead until you find more.")]
    public int spareMagazines = 5;

    public float reloadTime = 1.8f;

    [Header("Feel")]
    /// <summary>
    /// What one trigger pull is worth, for shake, punch and the low layer under the shot.
    ///
    /// Derived rather than typed, because the shotgun proved that a hand-entered number and the
    /// weapon it describes drift apart: shake was computed from `damage`, which on a shotgun is
    /// the damage of one pellet out of nine. It shook a fifth as hard as the sniper while
    /// hitting three times harder per pull, which is exactly why it did not feel like a shotgun.
    /// </summary>
    public float PullDamage => damage * Mathf.Max(1, pelletsPerShot);

    /// Normalised weight, where a rifle round is light and a shotgun pull is heavy.
    public float Weight => Mathf.Clamp01(PullDamage / 110f);

    [Tooltip("Shots this heavy get a second, lower layer under them. Below it, one sound is "
             + "plenty - a rifle at ten rounds a second does not want two samples per shot.")]
    public float layeredAbove = 0.45f;

    public enum Reticle
    {
        /// Four ticks. Everything that fires one accurate round.
        Cross,

        /// Three marks in a triangle. Reads as a spread weapon without being a huge cross.
        Triangle,

        /// Just the dot, for weapons where the spread is the whole point and drawing it is noise.
        Dot,
    }

    [Tooltip("Whether a flash appears at the muzzle. Off for anything with no barrel to flash "
             + "from - a launcher throws its payload out, it does not fire it.")]
    public bool muzzleFlash = true;

    [Tooltip("Which reticle this weapon draws, unless the player has overridden crosshairs.")]
    public Reticle reticle = Reticle.Cross;

    [Tooltip("How much of this weapon's spread the crosshair shows. A shotgun's cone is real but "
             + "drawing all of it makes a reticle the size of a dinner plate.")]
    [Range(0f, 1f)] public float reticleSpreadScale = 1f;

    [Header("Ripeness")]
    [Tooltip("Bananas go green to brown as the magazine empties. Nothing else does - a pineapple "
             + "that ripens as you fire it is a banana mechanic wearing a pineapple.")]
    public bool ripens = true;

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
