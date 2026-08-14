using UnityEngine;

[CreateAssetMenu(menuName = "FPS/New Gun")]
public class GunInfo : ItemInfo
{
    [Header("Damage")]
    public float damage = 20f;
    public float maxRange = 200f;

    [Header("Fire")]
    [Tooltip("Rounds per second.")]
    public float fireRate = 8f;

    [Tooltip("Held trigger keeps firing. Off means one shot per click.")]
    public bool automatic;

    [Tooltip("Cone of inaccuracy in degrees. 0 is laser accurate.")]
    public float spread;

    [Header("Ammo")]
    public int magazineSize = 30;
    public float reloadTime = 1.8f;

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
