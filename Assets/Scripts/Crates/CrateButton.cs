using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One crate tile on the selection page - name, cost, and a greyed-out/denied look when you
/// can't afford it rather than just letting the click silently do nothing.
/// </summary>
public class CrateButton : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text costText;
    [SerializeField] CanvasGroup group;

    public void Bind(CrateInfo crate, Action onOpen)
    {
        if (nameText != null)
            nameText.text = crate.Name;

        if (costText != null)
            costText.text = $"{crate.Cost} TOKENS";

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onOpen());
        }
    }

    public void SetAffordable(bool affordable)
    {
        if (button != null)
            button.interactable = affordable;

        // Dimmed rather than hidden - seeing a crate you can't afford yet is part of what makes
        // the more expensive tiers worth saving toward.
        if (group != null)
            group.alpha = affordable ? 1f : 0.45f;
    }
}
