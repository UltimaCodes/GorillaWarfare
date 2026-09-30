using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A weapon in the inventory's list - its name, and the finish it's wearing in that finish's colour.
/// </summary>
public class InventoryWeaponRow : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text finishText;
    [SerializeField] Image accent;
    [SerializeField] Image face;

    public string Weapon { get; private set; }

    public void Bind(string weapon, Action onClick)
    {
        Weapon = weapon;
        if (nameText != null)
            nameText.text = WeaponLoadout.DisplayName(weapon).ToUpperInvariant();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick());
        }

        Refresh(false);
    }

    public void Refresh(bool selected)
    {
        WeaponFinish wearing = SkinInventory.Equipped(Weapon);

        if (finishText != null)
        {
            finishText.text = wearing != null ? wearing.displayName.ToUpperInvariant() : "STOCK";
            finishText.color = wearing != null ? CrateRarityInfo.ColorFor(wearing.rarity) : new Color(1f, 1f, 1f, 0.45f);
        }

        if (accent != null)
            accent.color = selected ? new Color(1f, 0.82f, 0.12f) : new Color(1f, 0.82f, 0.12f, 0f);
        if (face != null)
            face.color = selected ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.02f);
        if (nameText != null)
            nameText.color = selected ? new Color(1f, 0.82f, 0.12f) : Color.white;
    }
}
