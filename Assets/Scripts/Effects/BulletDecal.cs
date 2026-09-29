using UnityEngine;

/// <summary>
/// The mark a shot leaves behind.
///
/// The old one was a 0.02 scale quad from a prefab, dropped at whatever `OverlapSphere` happened
/// to return first - which is how you end up with a small white square hanging in mid air near
/// where you shot rather than a mark on the thing you hit.
///
/// Three things make this read as damage instead of geometry:
///
/// - It multiplies rather than draws over. A multiply blend darkens whatever is underneath, so
///   the mark is automatically a darker version of that surface - concrete, sand, gorilla -
///   without anyone sampling a texture or picking a colour per material. Which is also what a
///   real scorch or a bruise does to the thing it's on.
/// - The texture is a soft blotch with a bit of noise, generated once and shared, so the edge
///   isn't a straight line and no two hits look identical.
/// - It has to land on something. The shooter's client decides where the shot went, but every
///   client re-checks locally that there is still a surface there before drawing anything -
///   which is what stops blood hanging in the air after the body it belonged to has gone.
/// </summary>
public class BulletDecal : MonoBehaviour
{
    // How far to look for a surface either side of the reported hit point. Generous enough to
    // survive a little disagreement between clients, tight enough that a shot into thin air
    // finds nothing.
    const float SearchDistance = 0.35f;

    // Lifted off the surface, or it z-fights with it.
    //
    // Raised from 0.008 on 2026-08-22, investigated after a report that impacts "don't work" on
    // mesh colliders specifically. Built a diagnostic (Tools > Gorilla Warfare, since removed)
    // that re-raycast every mesh and box collider actually in Game.unity the way Spawn() does
    // below - all 67 mesh colliders and all 120 box colliders re-raycast clean, and a decal spawned
    // on a scaled, rotated tree came out an undistorted, correctly sized quad, not the sheared or
    // degenerate shape a non-uniform parent scale would produce. No difference between the two
    // collider types was ever found. What is true: the decal itself has always been a faint
    // multiply blend by design (see the class doc above), and a low-poly decoration mesh has much
    // coarser per-triangle normals than a flat wall - a raycast hitting near a facet seam on a
    // rock or trunk can report a normal that's a few degrees off the true local surface, which at
    // this offset was close enough to risk sitting just inside the mesh instead of just outside
    // it. Raised as a hedge against that specific failure mode rather than left at a value tuned
    // only ever tested against flat, unscaled geometry - if impacts on mesh props still read as
    // missing after this, it needs a person shooting one and saying so, not another guess.
    const float LiftOff = 0.02f;

    const float WorldLifetime = 18f;
    const float BloodLifetime = 7f;
    const float FadeSeconds = 1.2f;

    static readonly Color Blood = new Color(0.62f, 0.05f, 0.06f, 1f);

    static Texture2D splat;
    static Material markMaterial;
    static Material fallbackMaterial;
    static Mesh quad;
    static Sprite[] boomShapes;

    Transform anchor;
    Renderer view;
    MaterialPropertyBlock block;
    Color tint;
    float diesAt;
    float lifetime;
    float fadeSeconds = FadeSeconds;
    bool onMarkShader;

    /// <summary>
    /// Places a mark at a reported hit - a bullet's, or one of Purple Haze's grapes (`scale` a little
    /// under 1). Returns null when there is nothing there to mark, or when it went on a body.
    /// </summary>
    public static BulletDecal Spawn(Vector3 point, Vector3 normal, int shooterLayerMask, float scale = 1f)
    {
        // Look back along the normal for the surface that was actually hit. Doing this per
        // client rather than trusting the shooter's collider means a decal can never outlive
        // the thing it was drawn on - if the body is gone, there is nothing to find and nothing
        // gets drawn.
        Vector3 from = point + normal * SearchDistance;

        if (!Physics.Raycast(from, -normal, out RaycastHit hit, SearchDistance * 2f,
                             shooterLayerMask, QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        bool flesh = hit.collider.GetComponentInParent<IDamageable>() != null;

        // Gore is the player's choice (GameSettings.Gore). Off, a shot on someone is dressed exactly
        // like a shot on a wall - the same hole, the same puff - and nothing red appears anywhere.
        bool gore = flesh && GameSettings.Gore;

        // Added 2026-08-22. Runs here rather than off the damage RPC because that RPC only ever
        // reaches the victim (see PlayerController.TakeDamage) - this, like the rest of
        // PlayFireEffects, is broadcast to every client, which is what a shooter's own hit
        // confirmation actually needs: to be seen by whoever's looking at the target, not just
        // felt by the target themselves.
        MonkeyRig body = flesh ? hit.collider.GetComponentInParent<MonkeyRig>() : null;
        if (body != null)
            body.Flash();

        if (gore)
            BloodSpray(hit.point, hit.normal, scale);
        else
            Puff(hit.point, hit.normal, false);

        // On a gorilla: on the body itself, where the shot landed, on the bone it hit (BodyMarks) -
        // the way the chili's char goes on. It was a decal on the hitbox, a sphere a little in or out
        // of the mesh, and at best a red dot; then a soft red patch, reported as "it just tints that
        // part somewhat red and doesnt feel like that gorilla got hit by an actual gun". Now it's the
        // walls' own bullet hole laid on the body - a wound, with gore on.
        if (body != null)
        {
            BodyMarks marks = BodyMarks.On(body.gameObject);

            if (gore)
            {
                Transform part = hit.collider.transform;

                // The wound, a heavy smear round it, and a run of blood down from it - "down" the
                // way the body was standing when it was hit, laid along the surface.
                marks.Add(part, hit.point, hit.normal, Random.Range(0.15f, 0.2f) * scale,
                          BodyMarks.Blood, BodyMarks.Shape.Wound, WoundHold, WoundFade);
                marks.Add(part, hit.point, hit.normal, Random.Range(0.3f, 0.38f) * scale,
                          BloodSmear, BodyMarks.Shape.Soft, WoundHold * 0.7f, WoundFade);

                Vector3 down = Vector3.ProjectOnPlane(Vector3.down, hit.normal);
                if (down.sqrMagnitude > 0.01f)
                    marks.Add(part, hit.point + down.normalized * Random.Range(0.1f, 0.16f) * scale, hit.normal,
                              Random.Range(0.12f, 0.16f) * scale, BloodRun, BodyMarks.Shape.Soft, WoundHold * 0.7f, WoundFade);

                Splatter(hit.point, hit.normal, scale);
            }
            else
            {
                marks.Add(hit.collider.transform, hit.point, hit.normal, Random.Range(0.07f, 0.12f) * scale,
                          Impact, BodyMarks.Shape.Hole, WorldLifetime - FadeSeconds, FadeSeconds);
            }

            return null;
        }

        // Anything else - a wall, the floor, something that takes damage but isn't a gorilla.
        float size = (gore ? Random.Range(0.16f, 0.28f) : Random.Range(0.14f, 0.24f)) * scale;
        GameObject host = PlaceOn(hit, size, gore ? "~blood" : "~impact");

        BulletDecal decal = host.AddComponent<BulletDecal>();
        decal.Build(hit.collider.transform, gore ? Blood : Impact, gore ? BloodLifetime : WorldLifetime, FadeSeconds);

        // Into grassy ground, the mark under the blades is mostly hidden by them - a scuff in the
        // grass itself too, lighter than a burn.
        if (!flesh && hit.normal.y > 0.6f)
            GrassMarks.Add(hit.point, Random.Range(0.35f, 0.5f) * scale, GrassMarks.Dirt, 0.6f, ScuffHold, ScuffFade);

        return decal;
    }

    // ---- the Grenada ----

    const float BlastHold = 16f;
    const float BlastFade = 4f;

    /// <summary>
    /// A blast's mark - the Grenada's. It used to leave the same bullet hole every gun does, "just
    /// one black dot". Now: a scorch the size of the fireball on the floor under it, the grass round
    /// it burnt, and scorch on any wall close enough to have taken it. `radius` is the blast's own.
    /// </summary>
    public static void Blast(Vector3 at, float radius)
    {
        // The splat's solid core is about a third of it, so the mark is laid well past the fireball
        // for the black itself to read as blast-sized.
        float across = radius * 0.6f;

        if (Physics.Raycast(at + Vector3.up * 0.4f, Vector3.down, out RaycastHit ground, radius,
                            Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
        {
            // Nearer the ground, the bigger and blacker the mark.
            float height = Mathf.Clamp01(1f - (at.y - ground.point.y) / radius);
            float size = across * Mathf.Lerp(0.6f, 1f, height) * Random.Range(0.9f, 1.1f);

            ScorchOn(ground, size);
            GrassMarks.Add(ground.point, size * 0.9f, GrassMarks.Soot, Mathf.Lerp(0.7f, 1f, height), BlastHold, BlastFade);
        }

        // The walls round it - eight ways out, a little down.
        for (int i = 0; i < 8; i++)
        {
            Vector3 way = Quaternion.Euler(0f, i * 45f + Random.Range(-10f, 10f), 0f) * new Vector3(0f, -0.25f, 1f);
            if (Physics.Raycast(at, way.normalized, out RaycastHit wall, across, Hitbox.WorldMask, QueryTriggerInteraction.Ignore)
                && wall.normal.y < 0.6f)
                ScorchOn(wall, across * 0.7f * (1f - wall.distance / across) + 0.4f);
        }
    }

    /// <summary>
    /// Everyone a blast caught: soot on the side of them that faced it, darker the closer they were,
    /// and with gore, a wound and blood from anyone close enough to have been torn up by it. Once per
    /// body, on its hitbox nearest the blast.
    /// </summary>
    public static void BlastBodies(Vector3 at, float radius, Collider[] caught)
    {
        System.Collections.Generic.HashSet<MonkeyRig> done = new System.Collections.Generic.HashSet<MonkeyRig>();

        foreach (Collider collider in caught)
        {
            MonkeyRig rig = collider != null ? collider.GetComponentInParent<MonkeyRig>() : null;
            if (rig == null || done.Contains(rig))
                continue;

            done.Add(rig);

            Collider nearest = collider;
            float best = (collider.bounds.center - at).sqrMagnitude;
            foreach (Collider other in caught)
            {
                if (other == null || other.GetComponentInParent<MonkeyRig>() != rig)
                    continue;

                float d = (other.bounds.center - at).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = other;
                }
            }

            Vector3 centre = nearest.bounds.center;
            Vector3 toward = at - centre;
            float strength = Mathf.Clamp01(1f - toward.magnitude / radius);
            if (strength < 0.05f)
                continue;

            Vector3 facing = toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.up;

            // A point on the hitbox's surface on the side facing the blast.
            Vector3 surface = nearest.ClosestPoint(centre + facing * 3f);

            BodyMarks marks = BodyMarks.On(rig.gameObject);
            marks.Add(nearest.transform, surface, facing, Mathf.Lerp(0.3f, 0.55f, strength),
                      Color.Lerp(Color.white, BodyMarks.Soot, Mathf.Lerp(0.5f, 1f, strength)),
                      BodyMarks.Shape.Soft, BlastHold * 0.4f, BlastFade);

            if (GameSettings.Gore && strength > 0.35f)
            {
                marks.Add(nearest.transform, surface, facing, Random.Range(0.18f, 0.24f),
                          BodyMarks.Blood, BodyMarks.Shape.Wound, WoundHold, WoundFade);
                BloodSpray(surface, facing, 1.3f);
                Splatter(surface, facing, 1.3f);
            }
        }
    }

    static void ScorchOn(RaycastHit hit, float size)
    {
        GameObject host = PlaceOn(hit, size, "~blast");
        host.AddComponent<BulletDecal>().Build(hit.collider.transform, Charcoal, BlastHold + BlastFade, BlastFade);
    }

    // A bullet hole's colour - the old one (0.32) was never actually applied, see SurfaceMark.shader.
    static readonly Color Impact = new Color(0.16f, 0.14f, 0.13f, 1f);
    const float ScuffHold = 6f;
    const float ScuffFade = 2f;

    // ---- gore ----

    // Round a wound, lighter than the wound itself - a smear, not a second hole - and the run of it
    // down from the wound, between the two. Turned up 2026-09-29, "turn up the gore a bit".
    static readonly Color BloodSmear = Color.Lerp(Color.white, BodyMarks.Blood, 0.62f);
    static readonly Color BloodRun = Color.Lerp(Color.white, BodyMarks.Blood, 0.8f);
    const float WoundHold = 12f;
    const float WoundFade = 2f;
    const float SplatterReach = 3.5f;
    const float SplatterLifetime = 14f;

    static Material sprayMaterial;
    static Texture sprayTexture;

    /// Blood, only with gore on: through the body onto whatever's behind it - two splashes, one
    /// either side of the line - and on the ground underneath, where it ran.
    static void Splatter(Vector3 point, Vector3 normal, float scale)
    {
        // Out the far side, along the line the shot was travelling. World only, so it passes the
        // body's own hitboxes.
        for (int i = 0; i < 2; i++)
        {
            Vector3 through = (-normal + Random.insideUnitSphere * 0.35f).normalized;
            if (Physics.Raycast(point, through, out RaycastHit behind, SplatterReach, Hitbox.WorldMask,
                                QueryTriggerInteraction.Ignore))
                BloodOn(behind, Random.Range(0.45f, 0.85f) * scale * (i == 0 ? 1f : 0.6f));
        }

        if (Physics.Raycast(point, Vector3.down, out RaycastHit below, 3f, Hitbox.WorldMask,
                            QueryTriggerInteraction.Ignore))
        {
            BloodOn(below, Random.Range(0.45f, 0.7f) * scale);
            GrassMarks.Add(below.point, Random.Range(0.55f, 0.8f) * scale, GrassMarks.BloodRed, 0.9f,
                           SplatterLifetime - WoundFade, WoundFade);
        }
    }

    static void BloodOn(RaycastHit hit, float size)
    {
        GameObject host = PlaceOn(hit, size, "~blood");
        host.AddComponent<BulletDecal>().Build(hit.collider.transform, Blood, SplatterLifetime, WoundFade);
    }

    /// <summary>
    /// The moment a shot goes into someone, with gore on: blood thrown back toward the shooter and
    /// sprayed out through the far side, falling as it goes. Dark droplets, blended - not the
    /// additive red sparks a hit used to throw, which glowed like embers rather than looking wet.
    /// </summary>
    static void BloodSpray(Vector3 point, Vector3 normal, float scale = 1f)
    {
        if (sprayMaterial == null)
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                            ?? Shader.Find("Particles/Alpha Blended")
                            ?? Shader.Find("Sprites/Default");
            sprayMaterial = new Material(shader) { name = "~bloodSpray", enableInstancing = true };

            foreach (Sprite sprite in Resources.LoadAll<Sprite>("Particles/Boom"))
            {
                if (sprite.name.StartsWith("circle"))
                {
                    sprayTexture = sprite.texture;
                    break;
                }
            }
        }

        int more = Mathf.RoundToInt(scale * 10f);
        SprayBurst(point, normal, more, 1.5f, 4f, 45f, 0.04f, 0.1f, 0.35f, 0.7f);          // back toward the shooter
        SprayBurst(point, -normal, more * 2 + 4, 3f, 8f, 30f, 0.04f, 0.1f, 0.35f, 0.7f);   // out through the far side

        // A puff of red mist where it went in, hanging for a moment.
        SprayBurst(point + normal * 0.05f, normal, 4, 0.2f, 0.8f, 70f, 0.18f, 0.38f, 0.2f, 0.4f, 0.6f, 0f);
    }

    static void SprayBurst(Vector3 point, Vector3 direction, int count, float minSpeed, float maxSpeed, float cone,
                           float minSize, float maxSize, float minLife, float maxLife,
                           float alpha = 1f, float gravity = 1.4f)
    {
        GameObject host = new GameObject("~bloodSpray");
        host.transform.SetPositionAndRotation(point, Quaternion.LookRotation(direction));

        ParticleSystem ps = host.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.03f, 0.04f, alpha),
                                                            new Color(0.3f, 0.01f, 0.02f, alpha));
        main.gravityModifier = gravity;
        main.maxParticles = Mathf.Max(8, count + 4);
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = 0.03f;

        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        Gradient over = new Gradient();
        over.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = over;

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Billboard;
        view.sharedMaterial = sprayMaterial;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;

        if (sprayTexture != null)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", sprayTexture);
            view.SetPropertyBlock(block);
        }

        ps.Play();
    }

    /// The bullet-hole shape every mark is made of - BodyMarks lays the same one on a body.
    public static Texture2D Splat
    {
        get
        {
            if (splat == null)
                splat = BuildSplat();
            return splat;
        }
    }

    // ---- Red Hot Chili Pepper's char ----

    static readonly Color Charcoal = new Color(0.12f, 0.1f, 0.09f, 1f);
    const float CharHold = 1f;
    const float CharFade = 3f;
    const int MaxChars = 60;
    static readonly System.Collections.Generic.List<BulletDecal> chars = new System.Collections.Generic.List<BulletDecal>();

    /// <summary>
    /// A burn where the flame touched a surface - darkest at first, fading back over a few seconds.
    /// Reported: "make the weapon char whatever it touches (make it go back to normal over like 3-5
    /// seconds)". Four seconds all told: one held, three fading.
    ///
    /// Flame on a spot that's already charred renews that mark instead of stacking another, so
    /// holding the stream on a wall keeps one patch black rather than piling up sixty decals; the
    /// cap is for everywhere else at once. Bodies char through BodyMarks, not here - a decal on a
    /// moving gorilla would slide off it.
    /// </summary>
    public static void Char(Vector3 point, Vector3 normal, float size)
    {
        for (int i = chars.Count - 1; i >= 0; i--)
        {
            BulletDecal existing = chars[i];
            if (existing == null)
            {
                chars.RemoveAt(i);
                continue;
            }

            if ((existing.transform.position - point).sqrMagnitude < size * size * 0.16f)
            {
                existing.Renew();
                return;
            }
        }

        if (chars.Count >= MaxChars)
            return;

        Vector3 from = point + normal * SearchDistance;
        if (!Physics.Raycast(from, -normal, out RaycastHit hit, SearchDistance * 2f, Hitbox.WorldMask,
                             QueryTriggerInteraction.Ignore))
            return;

        GameObject host = PlaceOn(hit, size * Random.Range(0.85f, 1.15f), "~char");
        BulletDecal decal = host.AddComponent<BulletDecal>();
        decal.Build(hit.collider.transform, Charcoal, CharHold + CharFade, CharFade);
        chars.Add(decal);
    }

    /// Live char marks - for the probe.
    public static int CharCount
    {
        get
        {
            chars.RemoveAll(c => c == null);
            return chars.Count;
        }
    }

    void Renew()
    {
        diesAt = Time.time + lifetime;
        Apply(1f);
    }

    /// <summary>
    /// A quad on the surface a ray hit, facing out of it, `size` across in the world whatever the
    /// surface's own scale.
    /// </summary>
    static GameObject PlaceOn(RaycastHit hit, float size, string name)
    {
        GameObject host = new GameObject(name);

        // The decal never actually rendered at a visible size on world geometry with a
        // non-uniform scale, which is most of it - confirmed by firing a real shot at a real
        // wall (Wall1, lossyScale (4, 16, 5)) and reading the spawned decal's own renderer
        // bounds back: (0.04, 0.15, 0.02), a hairline sliver, not the roughly-cubic ~0.15 a size
        // in that range should produce. "The impact does not work at all and never shows up" is
        // exactly what that looks like from the player's side - the object was there the whole
        // time, just warped down to nearly nothing.
        //
        // The parent's own scale isn't the whole problem, though - a first attempt (dividing
        // local scale by the parent's lossyScale per axis, the same move Hitbox.Neutralise uses)
        // still came out warped, because that only cancels a non-uniform parent scale correctly
        // when the child has no rotation of its own. This decal always needs one, to face the
        // hit normal - and scale and rotation don't commute, so "undo the parent's scale" and
        // "apply my own rotation" give a different answer depending which happens first. The
        // fix that's actually correct regardless of rotation: build the exact world-space
        // transform wanted (position, facing, a uniform `size`), then solve for whatever local
        // transform produces that under this specific parent, via the parent's own
        // worldToLocalMatrix - rather than guessing at a local value and hoping it lands right.
        host.transform.SetParent(hit.collider.transform, false);

        Vector3 worldPos = hit.point + hit.normal * LiftOff;

        // Face out of the surface, then spin at random so repeated hits don't tile.
        Quaternion worldRot = Quaternion.LookRotation(-hit.normal, Vector3.up)
                              * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        Matrix4x4 worldMatrix = Matrix4x4.TRS(worldPos, worldRot, Vector3.one * size);
        Matrix4x4 localMatrix = hit.collider.transform.worldToLocalMatrix * worldMatrix;

        host.transform.localPosition = localMatrix.GetColumn(3);
        host.transform.localRotation = localMatrix.rotation;
        host.transform.localScale = localMatrix.lossyScale;

        return host;
    }

    // Every live mark, oldest first. Purple Haze lands fourteen grapes a second, and with gore each
    // one on a body throws up to three splashes onto the world - without a ceiling, a long spray at
    // a crowd would leave hundreds. The oldest go first.
    const int MaxMarks = 220;
    static readonly System.Collections.Generic.Queue<BulletDecal> live = new System.Collections.Generic.Queue<BulletDecal>();

    /// How many marks are on the world - for the probe.
    public static int Live
    {
        get
        {
            while (live.Count > 0 && live.Peek() == null)
                live.Dequeue();
            return live.Count;
        }
    }

    void Build(Transform surface, Color colour, float seconds, float fade)
    {
        EnsureShared();

        live.Enqueue(this);
        while (live.Count > 0 && live.Peek() == null)
            live.Dequeue();
        while (live.Count > MaxMarks)
        {
            BulletDecal oldest = live.Dequeue();
            if (oldest != null)
                Destroy(oldest.gameObject);
        }

        anchor = surface;
        tint = colour;
        lifetime = seconds;
        fadeSeconds = fade;
        diesAt = Time.time + seconds;

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = quad;

        view = gameObject.AddComponent<MeshRenderer>();
        onMarkShader = markMaterial != null;
        view.sharedMaterial = onMarkShader ? markMaterial : fallbackMaterial;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        block = new MaterialPropertyBlock();
        Apply(1f);
    }

    void Update()
    {
        // The surface went away - a player died, or something was destroyed under it. Without
        // this the mark stays exactly where it was, floating.
        if (anchor == null)
        {
            Destroy(gameObject);
            return;
        }

        float left = diesAt - Time.time;

        if (left <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (left < fadeSeconds)
            Apply(left / fadeSeconds);
    }

    // A multiply decal fades by going white - white multiplied over anything leaves it alone.
    void Apply(float strength)
    {
        if (view == null)
            return;

        view.GetPropertyBlock(block);

        // SurfaceMark.shader takes its colour and strength separately. The fallback (only if that
        // shader's missing) is the old Particles/Multiply, which honours neither.
        if (onMarkShader)
        {
            block.SetColor("_Color", tint);
            block.SetFloat("_Strength", strength);
            view.SetPropertyBlock(block);
            return;
        }

        block.SetColor("_TintColor", Color.Lerp(Color.white, tint, strength));
        block.SetColor("_Color", Color.Lerp(Color.white, tint, strength));
        view.SetPropertyBlock(block);
    }

    /// <summary>
    /// The moment of the hit, not the mark it leaves - FlashSprite's own doc comment already
    /// named "the impact puff" as one of its three jobs, alongside the muzzle flash and the
    /// explosion, but nothing ever actually called it for a bullet impact. The decal alone is a
    /// multiply blend the same colour as most of what it lands on, which reads as barely there
    /// on a light surface - reported as the impact "disappearing".
    ///
    /// Rebuilt 2026-08-22, still reported as not appearing after the first pass. Looked at how
    /// this is actually done elsewhere rather than guess again: the standard shape is a bright
    /// static core plus debris that *travels* - start speed a few metres a second, a shape module
    /// so it sprays rather than sitting still, real particles rather than a handful of
    /// independent stickers. The old version's "sparks" were `FlashSprite`s - billboards that
    /// fade in place at a fixed offset, with no velocity at all - which is exactly why this never
    /// read as an impact: nothing in it actually moved. The core flash stays a `FlashSprite`
    /// (a static bright point is correct for that part, per the same reference), but the debris is
    /// a real `ParticleSystem` burst now, with a Cone shape and actual outward speed plus a little
    /// gravity so it arcs and falls like debris rather than floating.
    /// </summary>
    static void Puff(Vector3 point, Vector3 normal, bool bloody)
    {
        if (boomShapes == null || boomShapes.Length == 0)
            boomShapes = Resources.LoadAll<Sprite>("Particles/Boom");

        if (boomShapes.Length == 0)
            return;

        Sprite spark = Pick("spark");
        Sprite core = Pick("circle");

        Color tint = bloody ? new Color(0.75f, 0.1f, 0.12f, 1f) : new Color(1f, 0.92f, 0.7f, 1f);

        // A quick bright core right at the surface, gone almost instantly - the same "arrives at
        // full size and collapses" trick the explosion's own core uses, just a fraction of it.
        // Offset raised alongside BulletDecal's own LiftOff, same reasoning - a coarse mesh's
        // per-triangle normal has more room to be slightly wrong than a flat wall's.
        FlashSprite.Spawn(core, point + normal * 0.05f, 0.10f, 0.22f, 0.07f, tint);

        SpawnDebris(point + normal * 0.05f, normal, tint, spark, bloody ? 6 : 9);
    }

    /// A short-lived, self-destroying ParticleSystem for debris that actually flies rather than
    /// sitting in place - see Puff()'s doc comment for why this replaced a loop of FlashSprites.
    static void SpawnDebris(Vector3 point, Vector3 normal, Color tint, Sprite sprite, int count)
    {
        if (sprite == null)
            return;

        GameObject host = new GameObject("~debris");
        host.transform.position = point;
        host.transform.rotation = Quaternion.LookRotation(normal);

        ParticleSystem ps = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = tint;
        main.gravityModifier = 1.1f;
        main.maxParticles = 24;

        // Cleans itself up the moment the last particle dies - nothing else has to track or
        // destroy this the way FlashSprite tracked its own lifetime, since there's no per-frame
        // behaviour left to run once Play() has fired the one burst.
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        // A forward-biased cone rather than a full sphere, so debris reads as leaving the impact
        // point along the surface normal instead of an even spray in every direction at once.
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.01f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Billboard;
        view.sharedMaterial = SharedDebrisMaterial();
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", sprite.texture);
        view.SetPropertyBlock(block);

        ps.Play();
    }

    static Material debrisMaterial;

    static Material SharedDebrisMaterial()
    {
        if (debrisMaterial != null)
            return debrisMaterial;

        Shader shader = Shader.Find("Particles/Additive")
                        ?? Shader.Find("Legacy Shaders/Particles/Additive")
                        ?? Shader.Find("Sprites/Default");

        debrisMaterial = new Material(shader) { name = "~debris", enableInstancing = true };
        return debrisMaterial;
    }

    static Sprite Pick(string prefix)
    {
        Sprite fallback = null;

        for (int i = 0; i < boomShapes.Length; i++)
        {
            if (fallback == null)
                fallback = boomShapes[i];

            if (boomShapes[i].name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return boomShapes[i];
        }

        return fallback;
    }

    static void EnsureShared()
    {
        if (quad == null)
        {
            quad = new Mesh { name = "~decalQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateNormals();
        }

        if (splat == null)
            splat = BuildSplat();

        // Every mark - impact, blood off a gorilla, char - on the one shader that honours a colour
        // and a fade (SurfaceMark.shader).
        if (markMaterial == null)
        {
            Shader shader = Shader.Find("Custom/SurfaceMark");
            if (shader != null)
                markMaterial = new Material(shader) { name = "~mark", mainTexture = splat };
        }

        if (markMaterial == null && fallbackMaterial == null)
        {
            Debug.LogWarning("[marks] no Custom/SurfaceMark shader - marks fall back to Particles/Multiply and barely show");
            fallbackMaterial = BuildMaterial();
        }
    }

    // Multiply blending, so the mark darkens whatever it sits on rather than painting a colour
    // over it. Falls back through a couple of shader names because which of these exists depends
    // on what the project has pulled in.
    static Material BuildMaterial()
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Multiply")
                        ?? Shader.Find("Particles/Multiply")
                        ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                        ?? Shader.Find("Sprites/Default");

        Material material = new Material(shader) { mainTexture = splat };
        material.renderQueue = 3000;
        return material;
    }

    /// A round blotch with a soft edge and a bit of noise, so it doesn't read as a shape.
    static Texture2D BuildSplat()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "~splat" };

        float seed = Random.Range(0f, 100f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) * 2f;

                // Wobble the radius so the outline isn't a circle either.
                float angle = Mathf.Atan2(dy, dx);
                float wobble = Mathf.PerlinNoise(Mathf.Cos(angle) * 2f + seed, Mathf.Sin(angle) * 2f + seed);
                float radius = 0.62f + wobble * 0.3f;

                // A real smoothstep of the distance between the inner and outer radius. This was
                // Mathf.SmoothStep(inner, outer, distance) - which isn't that: it blends from its
                // first argument to its second, so outside the blotch it came to 1 - outer, never
                // 0, and every mark was a faint square with the blotch in the middle of it.
                float edge = Mathf.InverseLerp(radius * 0.45f, radius, distance);
                float density = 1f - edge * edge * (3f - 2f * edge);

                // Speckle, so the middle isn't a flat disc.
                density *= 0.65f + Mathf.PerlinNoise(x * 0.22f + seed, y * 0.22f + seed) * 0.35f;

                // White is "leave the surface alone" under a multiply blend, so density drives
                // how far toward the tint each texel goes.
                float value = 1f - density;
                texture.SetPixel(x, y, new Color(value, value, value, density));
            }
        }

        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }
}
