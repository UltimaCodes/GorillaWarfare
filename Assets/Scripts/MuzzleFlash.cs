using UnityEngine;

// The flash at the muzzle when the gun fires.
//
// This used to be a point light at intensity 6 with a 7 metre range and nothing else. Fired at
// open ground it read as a shot; fired at a wall two metres away it read as somebody switching a
// lamp on and off in your face, because that is exactly what it was. A muzzle flash is a shape,
// not an illumination - the light is the small part.
//
// So it is a sprite now, billboarded at the barrel tip, with a light that is a fifth as bright
// and less than half the range purely to catch the wall behind it. Four sprites picked at random
// with a random roll, so two shots never look like the same frame played twice.
public class MuzzleFlash : MonoBehaviour
{
    [Tooltip("How bright the accompanying light is. Deliberately small - the sprite is the "
             + "flash, this is only what it throws onto nearby surfaces.")]
    [SerializeField] float intensity = 1.2f;

    [Tooltip("Metres. Short enough that firing at a wall lights the wall rather than the room.")]
    [SerializeField] float range = 2.6f;

    [SerializeField] float decay = 30f;

    [Tooltip("Metres across. Scaled up by longer weapons, which have bigger muzzles.")]
    [SerializeField] float flashSize = 0.42f;

    [SerializeField] float flashSeconds = 0.045f;

    static Sprite[] shapes;

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

    /// <summary>
    /// The muzzle sprites, loaded once.
    ///
    /// From Resources so there is nothing to wire onto a weapon that is built at runtime. Four
    /// of them because one is a repeating animation and four is a gun going off.
    /// </summary>
    static Sprite[] Shapes()
    {
        if (shapes != null && shapes.Length > 0)
            return shapes;

        shapes = Resources.LoadAll<Sprite>("Particles/Muzzle");

        if (shapes.Length == 0)
            Debug.LogWarning("[muzzle] no sprites in Resources/Particles/Muzzle - falling back to the light alone");

        return shapes;
    }

    public void Fire()
    {
        level = 1f;

        if (flash != null)
            flash.enabled = true;

        Sprite[] set = Shapes();

        if (set.Length == 0 || flash == null)
            return;

        // Parented to the tip, so the flash rides the weapon through recoil rather than hanging
        // in the air where the barrel used to be.
        //
        // It grows slightly over its life. A flash that only fades looks like a light going out;
        // one that expands looks like gas leaving a barrel, which is what it is.
        FlashSprite.Spawn(set[Random.Range(0, set.Length)],
                          flash.transform.position,
                          flashSize * Mathf.Max(0.6f, tipDistance / 0.35f),
                          flashSize * Mathf.Max(0.6f, tipDistance / 0.35f) * 1.45f,
                          flashSeconds,
                          new Color(1f, 0.86f, 0.55f, 1f),
                          flash.transform);
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
