using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One finish in a grid - its picture on a weapon, its name, its rarity along the bottom, how
/// many you have, and whether the weapon you're looking at wears it. The inventory's grid and the
/// crate reel are both made of these.
/// </summary>
public class FinishCard : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] RawImage picture;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text countText;
    [SerializeField] Image rarityStrip;
    [SerializeField] Image rarityWash;
    [SerializeField] Image selection;
    [SerializeField] GameObject equippedMark;

    public WeaponFinish Finish { get; private set; }

    public RectTransform Rect => (RectTransform)transform;

    /// <summary>
    /// `count` 0 hides the count (the reel's cards, the stock card); `equipped` shows the mark.
    /// </summary>
    public void Bind(WeaponFinish finish, string weapon, int count, bool equipped, Action onClick)
    {
        Finish = finish;

        Color rarity = finish != null ? CrateRarityInfo.ColorFor(finish.rarity) : new Color(0.55f, 0.57f, 0.62f);

        if (nameText != null)
            nameText.text = finish != null ? finish.displayName.ToUpperInvariant() : "STOCK";

        if (countText != null)
        {
            countText.gameObject.SetActive(count > 1);
            countText.text = $"x{count}";
        }

        if (rarityStrip != null)
            rarityStrip.color = rarity;
        if (rarityWash != null)
            rarityWash.color = new Color(rarity.r, rarity.g, rarity.b, 0.22f);

        if (equippedMark != null)
            equippedMark.SetActive(equipped);

        if (picture != null)
            picture.texture = PreviewStage.Icon(weapon, finish, new Color(0.06f, 0.07f, 0.09f));

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            button.interactable = onClick != null;
        }

        Select(false);
    }

    public void Select(bool selected)
    {
        if (selection != null)
            selection.enabled = selected;
    }
}
