using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The hover and focus look for a main-menu item: the label goes banana yellow, an accent bar
/// appears beside it and the text steps in a little - the Portal 2 / Black Ops menu move, no box
/// around the button at all. Mouse hover and keyboard/controller focus both count, so the menu reads
/// the same whichever you're using.
///
/// Every number here is a field so the look can be tuned in the inspector without code - the menu
/// is real scene objects on purpose (see MenuBuilder).
/// </summary>
[RequireComponent(typeof(Button))]
public class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] TMP_Text label;

    [Tooltip("Shown while hovered or focused. Optional.")]
    [SerializeField] Graphic accent;

    [SerializeField] Color normalColour = Color.white;
    [SerializeField] Color hoverColour = new Color(1f, 0.82f, 0.12f);
    [SerializeField] Color disabledColour = new Color(1f, 1f, 1f, 0.3f);

    [Tooltip("How far the label steps in while hovered, in canvas pixels.")]
    [SerializeField] float hoverShift = 14f;

    [SerializeField] float ease = 18f;

    Button button;
    RectTransform labelRect;
    Vector2 labelRest;
    bool pointerOver;
    bool selected;
    float amount;

    void Awake()
    {
        button = GetComponent<Button>();

        if (label != null)
        {
            labelRect = label.rectTransform;
            labelRest = labelRect.anchoredPosition;
        }

        Apply(0f);
    }

    void OnDisable()
    {
        // A menu closes with its buttons still "hovered" as far as they know - the pointer never
        // got to leave. Coming back to it shouldn't open with one lit up.
        pointerOver = false;
        selected = false;
        amount = 0f;
        Apply(0f);
    }

    public void OnPointerEnter(PointerEventData eventData) => pointerOver = true;
    public void OnPointerExit(PointerEventData eventData) => pointerOver = false;
    public void OnSelect(BaseEventData eventData) => selected = true;
    public void OnDeselect(BaseEventData eventData) => selected = false;

    void Update()
    {
        bool lit = button.interactable && (pointerOver || selected);

        // Unscaled - the menu runs at whatever the time scale happens to be.
        amount = Mathf.MoveTowards(amount, lit ? 1f : 0f, Time.unscaledDeltaTime * ease);
        Apply(amount);
    }

    void Apply(float t)
    {
        if (label != null)
        {
            Color rest = button != null && !button.interactable ? disabledColour : normalColour;
            label.color = Color.Lerp(rest, hoverColour, t);

            if (labelRect != null)
                labelRect.anchoredPosition = labelRest + new Vector2(hoverShift * t, 0f);
        }

        if (accent != null)
        {
            Color c = accent.color;
            c.a = t;
            accent.color = c;
        }
    }
}
