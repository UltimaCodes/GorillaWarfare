using UnityEngine;

// A brief light at the muzzle when the gun fires.
//
// A light rather than a sprite because it illuminates the surroundings for a frame, which is
// most of what makes a shot feel like it had force. Built at runtime so there's no prefab to
// wire and no particle asset to import.
public class MuzzleFlash : MonoBehaviour
{
    [SerializeField] float intensity = 6f;
    [SerializeField] float range = 7f;
    [SerializeField] float decay = 22f;

    Light flash;
    float level;
    float tipDistance = 0.35f;

    /// The point shots leave from. Tracers start here rather than at the camera - those are
    /// different places, and drawing from the camera makes shots appear to come out of your
    /// forehead and go edge-on invisible at exactly the range you want the feedback.
    public Transform Tip => flash != null ? flash.transform : transform;

    /// Where the business end is, in the weapon's local space.
    public void SetTipDistance(float distance)
    {
        tipDistance = distance;

        if (flash != null)
            flash.transform.localPosition = new Vector3(0f, 0f, tipDistance);
    }

    void Awake()
    {
        Build();
    }

    // Separate from Awake so it can be exercised outside play mode, where Awake never runs on
    // AddComponent and the light would silently never exist.
    public void Build()
    {
        if (flash != null)
            return;

        GameObject host = new GameObject("~MuzzleFlash");
        host.transform.SetParent(transform, false);

        // Out at the barrel tip. The banana models are built along +Z, so this sits at the end
        // of one rather than inside the grip. The distance is set by the weapon once it knows
        // how long its model is - a fixed value put the pistol's flash out in open air and the
        // sniper's somewhere in the middle of the fruit.
        host.transform.localPosition = new Vector3(0f, 0f, tipDistance);

        flash = host.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.85f, 0.5f);
        flash.range = range;
        flash.intensity = 0f;
        flash.enabled = false;
    }

    public void Fire()
    {
        level = 1f;
        if (flash != null)
            flash.enabled = true;
    }

    void Update()
    {
        if (flash == null || level <= 0f)
            return;

        level -= decay * Time.deltaTime;

        if (level <= 0f)
        {
            level = 0f;
            flash.enabled = false;   // disabled rather than zero intensity, so it costs nothing
            return;
        }

        flash.intensity = intensity * level;
    }
}
