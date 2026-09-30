using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One crate on the crate screen - its name, its price, its line, the real odds of every rarity
/// in it as bars, and an OPEN button. Its chest stands above it, drawn by the crate screen's stage;
/// hovering the card tells the screen, which makes the chest peek open.
/// </summary>
public class CrateCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] Button openButton;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text priceText;
    [SerializeField] TMP_Text taglineText;
    [SerializeField] TMP_Text needText;
    [SerializeField] Image accent;
    [SerializeField] Image[] oddsFills = new Image[5];
    [SerializeField] TMP_Text[] oddsLabels = new TMP_Text[5];
    [SerializeField] CanvasGroup group;

    public CrateInfo Crate { get; private set; }

    Action<bool> hovered;
    float[] fullWidths;

    public void Bind(CrateInfo crate, Action onOpen, Action<bool> onHover)
    {
        Crate = crate;
        hovered = onHover;

        if (nameText != null) nameText.text = crate.Name.ToUpperInvariant();
        if (priceText != null) priceText.text = $"{crate.Cost} TOKENS";
        if (taglineText != null) taglineText.text = crate.Tagline;
        if (accent != null) accent.color = crate.Colour;

        // Straight from the odds table - the numbers the roll really uses.
        float most = 0f;
        foreach (CrateRarity rarity in CrateRarityInfo.All)
            most = Mathf.Max(most, crate.OddsFor(rarity));

        for (int i = 0; i < CrateRarityInfo.All.Length; i++)
        {
            CrateRarity rarity = CrateRarityInfo.All[i];
            float odds = crate.OddsFor(rarity);

            if (i < oddsFills.Length && oddsFills[i] != null)
            {
                RectTransform bar = oddsFills[i].rectTransform;
                if (fullWidths == null)
                    fullWidths = new float[oddsFills.Length];
                if (fullWidths[i] <= 0f)
                    fullWidths[i] = bar.sizeDelta.x;

                oddsFills[i].color = CrateRarityInfo.ColorFor(rarity);
                bar.sizeDelta = new Vector2(fullWidths[i] * Mathf.Max(0.02f, odds / Mathf.Max(most, 0.001f)), bar.sizeDelta.y);
            }

            if (i < oddsLabels.Length && oddsLabels[i] != null)
                oddsLabels[i].text = $"{CrateRarityInfo.NameFor(rarity)}  {odds * 100f:0.#}%";
        }

        if (openButton != null)
        {
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(() => onOpen());
        }
    }

    /// Dimmed, not hidden - seeing the crate you're saving towards is half of saving towards it.
    public void SetAffordable(bool affordable, int short_)
    {
        if (group != null) group.alpha = affordable ? 1f : 0.55f;
        if (openButton != null) openButton.interactable = affordable;
        if (needText != null)
        {
            needText.gameObject.SetActive(!affordable);
            needText.text = $"NEED {short_} MORE";
        }
    }

    public void OnPointerEnter(PointerEventData e) => hovered?.Invoke(true);
    public void OnPointerExit(PointerEventData e) => hovered?.Invoke(false);
}
