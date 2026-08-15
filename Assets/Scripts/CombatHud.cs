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

    public void Bind(PlayerController owner)
    {
        player = owner;
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
            float size = (lastHitWasHead ? 14f : 9f) * hitMarker;
            GUI.color = new Color(hitColour.r, hitColour.g, hitColour.b, hitMarker);
            Rect(cx - size, cy - size, size, thickness);
            Rect(cx + size - size, cy - size, thickness, size);
            Rect(cx, cy + size - thickness, size, thickness);
            Rect(cx + size - thickness, cy, thickness, size);
        }

        // Ammo, bottom right: big number is what's in the banana, small one underneath is how
        // many spare bananas you're carrying.
        if (gun != null && gun.Info != null && !gun.Info.melee)
        {
            if (ammoStyle == null)
            {
                ammoStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
                spareStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            }

            float right = Screen.width - 40f;
            float bottom = Screen.height - 30f;

            ammoStyle.fontSize = 52;
            bool dry = gun.Ammo == 0;
            GUI.color = gun.Reloading ? new Color(1f, 0.55f, 0.25f, 1f)
                      : dry ? new Color(1f, 0.3f, 0.25f, 1f)
                      : ammoColour;
            GUI.Label(new Rect(0f, 0f, right, bottom), gun.Reloading ? "--" : gun.Ammo.ToString(), ammoStyle);

            // Spare bananas, smaller and dimmer, below and right of the main number.
            spareStyle.fontSize = 22;
            GUI.color = gun.SpareMagazines > 0
                ? new Color(1f, 1f, 1f, 0.55f)
                : new Color(1f, 0.3f, 0.25f, 0.9f);
            GUI.Label(new Rect(0f, 0f, right, bottom + 26f), $"x{gun.SpareMagazines}", spareStyle);

            spareStyle.fontSize = 16;
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GUI.Label(new Rect(0f, 0f, right, bottom - 52f), gun.name.ToUpper(), spareStyle);
        }

        GUI.color = Color.white;
    }

    void Rect(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), pixel);
    }
}
