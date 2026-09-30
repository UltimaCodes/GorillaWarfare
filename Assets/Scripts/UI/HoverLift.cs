using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Scales up a touch when the pointer's over it, and down when it's pressed - on anything that can
/// be clicked in the skins and crate screens, so everything under the mouse answers it.
/// </summary>
public class HoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.05f;
    public float pressScale = 0.96f;

    Vector3 rest = Vector3.one;
    float target = 1f;
    float current = 1f;
    bool over;

    void Awake() => rest = transform.localScale;

    void OnDisable()
    {
        over = false;
        target = current = 1f;
        transform.localScale = rest;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        over = true;
        target = hoverScale;
        GameAudio.PlayPitched(GameAudio.UI, "click_001", GameAudio.UiVolume * 0.25f, 1.6f);
    }

    public void OnPointerExit(PointerEventData e)
    {
        over = false;
        target = 1f;
    }

    public void OnPointerDown(PointerEventData e) => target = pressScale;
    public void OnPointerUp(PointerEventData e) => target = over ? hoverScale : 1f;

    void Update()
    {
        current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        transform.localScale = rest * current;
    }
}
