using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes whatever button it's on open the inventory - the same shape as OpenCrateShopButton,
/// because the inventory is a Resources prefab RoomManager instantiates, so there's nothing in a
/// scene for an inspector reference to point at.
/// </summary>
[RequireComponent(typeof(Button))]
public class OpenInventoryButton : MonoBehaviour
{
    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
        {
            GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);

            if (InventoryScreen.Instance != null)
                InventoryScreen.Instance.Open();
            else
                Debug.LogWarning("[skins] no inventory to open - RoomManager never instantiated it");
        });
    }
}
