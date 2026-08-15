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
    [SerializeField] float spreadScale = 26f;

    [SerializeField] Color crosshairColour = new Color(0.55f, 0.9f, 1f, 0.9f);
    [SerializeField] Color hitColour = new Color(1f, 0.25f, 0.2f, 1f);
    [SerializeField] Color ammoColour = new Color(0.95f, 0.85f, 0.3f, 1f);

    PlayerController player;
    Texture2D pixel;
    GUIStyle ammoStyle;

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

        // Recoil pushes the crosshair, so you can see the climb you're fighting.
        Vector2 recoil = player.RecoilOffset;
        cx += recoil.y * spreadScale;
        cy -= recoil.x * spreadScale;

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

        // Ammo, bottom right.
        if (gun != null && gun.Info != null && !gun.Info.melee)
        {
            if (ammoStyle == null)
            {
                ammoStyle = new GUIStyle(GUI.skin.label);
                ammoStyle.fontSize = 34;
                ammoStyle.alignment = TextAnchor.LowerRight;
                ammoStyle.fontStyle = FontStyle.Bold;
            }

            GUI.color = gun.Reloading ? new Color(1f, 0.5f, 0.3f, 1f) : ammoColour;
            string text = gun.Reloading ? "RELOADING" : $"{gun.Ammo} / {gun.Info.magazineSize}";
            GUI.Label(new Rect(0f, 0f, Screen.width - 34f, Screen.height - 26f), text, ammoStyle);

            ammoStyle.fontSize = 18;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(0f, 0f, Screen.width - 34f, Screen.height - 66f), gun.name, ammoStyle);
            ammoStyle.fontSize = 34;
        }

        GUI.color = Color.white;
    }

    void Rect(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), pixel);
    }
}
