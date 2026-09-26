using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A live crosshair preview on the settings screen's own Crosshair tab - "I want there to be a
/// crosshair visual on the crosshair menu so you can see what crosshair youre working with."
///
/// Deliberately simpler than GameHud.UpdateCrosshair, not a shared copy of it: the real one also
/// handles per-weapon reticle styles (dot/triangle/cross) and dynamic spread, neither of which
/// means anything with no weapon equipped on the settings screen - this only ever draws the plain
/// static cross a player actually spends most of their time looking at, redrawn from the same
/// GameSettings values the sliders on this tab write to.
/// </summary>
public class CrosshairPreview : MonoBehaviour
{
    [SerializeField] RectTransform up;
    [SerializeField] RectTransform down;
    [SerializeField] RectTransform left;
    [SerializeField] RectTransform right;
    [SerializeField] Image dot;

    Image upImage, downImage, leftImage, rightImage;

    void Awake()
    {
        if (up != null) upImage = up.GetComponent<Image>();
        if (down != null) downImage = down.GetComponent<Image>();
        if (left != null) leftImage = left.GetComponent<Image>();
        if (right != null) rightImage = right.GetComponent<Image>();
    }

    void OnEnable()
    {
        Refresh();
        GameSettings.Changed += Refresh;
    }

    void OnDisable()
    {
        GameSettings.Changed -= Refresh;
    }

    public void Refresh()
    {
        float gap = GameSettings.CrosshairGap;
        float length = GameSettings.CrosshairSize;
        float thickness = GameSettings.CrosshairThickness;
        Color colour = GameSettings.CrosshairColour;

        Place(up, upImage, new Vector2(0f, gap + length * 0.5f), new Vector2(thickness, length), colour);
        Place(down, downImage, new Vector2(0f, -gap - length * 0.5f), new Vector2(thickness, length), colour);
        Place(left, leftImage, new Vector2(-gap - length * 0.5f, 0f), new Vector2(length, thickness), colour);
        Place(right, rightImage, new Vector2(gap + length * 0.5f, 0f), new Vector2(length, thickness), colour);

        if (dot != null)
        {
            dot.gameObject.SetActive(GameSettings.CrosshairDot);
            dot.rectTransform.sizeDelta = new Vector2(thickness, thickness);
            dot.color = colour;
        }
    }

    static void Place(RectTransform rect, Image image, Vector2 position, Vector2 size, Color colour)
    {
        if (rect == null)
            return;

        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        if (image != null)
            image.color = colour;
    }
}
