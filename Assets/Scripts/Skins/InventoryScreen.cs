using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The inventory - "an inventory system where players can select their weapon skins (like in
/// counter strike) in the main menu." The weapons down the left, each with what it's wearing;
/// the one you've picked turning in the middle in whichever finish you've got selected; every
/// finish you own on the right, with how many copies. Equip it on that weapon, or on all of them
/// (a finish is universal), or scrap a copy for tokens.
///
/// Built as a prefab (InventoryBuilder) that RoomManager instantiates onto itself, the same as the
/// crate shop and the settings menu, so a button anywhere just calls Instance.Open().
/// </summary>
public class InventoryScreen : MonoBehaviour
{
    public static InventoryScreen Instance { get; private set; }

    [SerializeField] GameObject panel;
    [SerializeField] CanvasGroup fade;
    [SerializeField] RectTransform body;

    [Header("Top")]
    [SerializeField] TMP_Text collectedText;
    [SerializeField] TokenCounter tokens;
    [SerializeField] Button closeButton;
    [SerializeField] Button cratesButton;

    [Header("Weapons")]
    [SerializeField] RectTransform weaponList;
    [SerializeField] InventoryWeaponRow weaponRowTemplate;

    [Header("Preview")]
    [SerializeField] RawImage preview;
    [SerializeField] TMP_Text weaponName;
    [SerializeField] TMP_Text finishName;
    [SerializeField] TMP_Text rarityText;
    [SerializeField] Image rarityPill;
    [SerializeField] TMP_Text flavourText;
    [SerializeField] TMP_Text ownedText;

    [Header("Actions")]
    [SerializeField] Button equipButton;
    [SerializeField] TMP_Text equipLabel;
    [SerializeField] Button equipAllButton;
    [SerializeField] Button scrapButton;
    [SerializeField] TMP_Text scrapLabel;
    [SerializeField] Button scrapSparesButton;
    [SerializeField] TMP_Text scrapSparesLabel;
    [SerializeField] TMP_Text payout;

    [Header("Finishes")]
    [SerializeField] RectTransform finishGrid;
    [SerializeField] FinishCard finishCardTemplate;
    [SerializeField] GameObject emptyNote;

    readonly List<InventoryWeaponRow> rows = new List<InventoryWeaponRow>();
    readonly List<FinishCard> cards = new List<FinishCard>();

    PreviewStage stage;
    string weapon = "Rifle";
    WeaponFinish selected;
    bool scrapArmed;
    float scrapArmedUntil;

    static readonly Color Background = new Color(0.035f, 0.04f, 0.05f, 1f);

    void Awake()
    {
        Instance = this;
        SkinInventory.Changed += Refresh;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        SkinInventory.Changed -= Refresh;
    }

    void Start()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (equipButton != null) equipButton.onClick.AddListener(Equip);
        if (equipAllButton != null) equipAllButton.onClick.AddListener(EquipAll);
        if (scrapButton != null) scrapButton.onClick.AddListener(Scrap);
        if (scrapSparesButton != null) scrapSparesButton.onClick.AddListener(ScrapSpares);
        if (cratesButton != null)
        {
            cratesButton.onClick.AddListener(() =>
            {
                Close();
                if (CrateOpeningScreen.Instance != null)
                    CrateOpeningScreen.Instance.Open();
            });
        }

        if (weaponRowTemplate != null) weaponRowTemplate.gameObject.SetActive(false);
        if (finishCardTemplate != null) finishCardTemplate.gameObject.SetActive(false);
        if (payout != null) payout.gameObject.SetActive(false);
    }

    public bool IsOpen => panel != null && panel.activeSelf;

    /// Opens on whatever weapon was picked last, or the one given - and with a finish picked, if
    /// one's given (the crate screen's INVENTORY does that, straight onto what you just opened).
    public void Open(string onWeapon = null, WeaponFinish onFinish = null)
    {
        if (panel != null)
            panel.SetActive(true);

        if (stage == null)
        {
            stage = PreviewStage.Create("inventory", 1024, 640, Background);
            if (preview != null)
            {
                preview.texture = stage.Target;
                PreviewDrag drag = preview.GetComponent<PreviewDrag>();
                if (drag == null)
                    drag = preview.gameObject.AddComponent<PreviewDrag>();
                drag.stage = stage;
            }
        }

        stage.Drawing = true;
        if (tokens != null)
            tokens.Snap();

        if (!string.IsNullOrEmpty(onWeapon))
            weapon = onWeapon;

        BuildRows();
        PickWeapon(weapon, onFinish);

        StopAllCoroutines();
        StartCoroutine(Arrive());
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        if (stage != null)
            stage.Drawing = false;
    }

    void Update()
    {
        if (scrapArmed && Time.unscaledTime > scrapArmedUntil)
        {
            scrapArmed = false;
            DrawActions();
        }

        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    IEnumerator Arrive()
    {
        if (fade != null) fade.alpha = 0f;
        Vector2 rest = body != null ? body.anchoredPosition : Vector2.zero;
        yield return UiMotion.Tween(0.3f, UiMotion.OutCubic, k =>
        {
            if (fade != null) fade.alpha = k;
            if (body != null) body.anchoredPosition = rest + new Vector2(0f, -40f * (1f - k));
        });
    }

    // ---------------------------------------------------------------- the lists

    void BuildRows()
    {
        if (rows.Count > 0 || weaponRowTemplate == null)
            return;

        int i = 0;
        foreach (string key in WeaponLoadout.Everything)
        {
            InventoryWeaponRow row = Instantiate(weaponRowTemplate, weaponList);
            row.gameObject.SetActive(true);
            row.name = $"Row {key}";
            string captured = key;
            row.Bind(key, () => PickWeapon(captured, null));
            PopIn pop = row.GetComponent<PopIn>();
            if (pop != null) pop.delay = 0.03f * i++;
            rows.Add(row);
        }
    }

    void BuildCards()
    {
        foreach (FinishCard card in cards)
            Destroy(card.gameObject);
        cards.Clear();

        if (finishCardTemplate == null)
            return;

        // Stock first, then the rarest you own first.
        List<WeaponFinish> owned = SkinInventory.Owned();
        owned.Reverse();
        owned.Insert(0, null);

        int i = 0;
        foreach (WeaponFinish finish in owned)
        {
            FinishCard card = Instantiate(finishCardTemplate, finishGrid);
            card.gameObject.SetActive(true);
            card.name = finish != null ? $"Card {finish.key}" : "Card stock";
            WeaponFinish captured = finish;
            card.Bind(finish, weapon, finish != null ? SkinInventory.Count(finish) : 0,
                      SkinInventory.Equipped(weapon) == finish, () => PickFinish(captured));
            card.Select(finish == selected);
            PopIn pop = card.GetComponent<PopIn>();
            if (pop != null) pop.delay = Mathf.Min(0.02f * i++, 0.5f);
            cards.Add(card);
        }

        if (emptyNote != null)
            emptyNote.SetActive(owned.Count <= 1);
    }

    // ---------------------------------------------------------------- picking

    void PickWeapon(string key, WeaponFinish finish)
    {
        weapon = key;
        selected = finish != null && SkinInventory.Count(finish) > 0 ? finish : SkinInventory.Equipped(key);
        scrapArmed = false;

        foreach (InventoryWeaponRow row in rows)
            row.Refresh(row.Weapon == weapon);

        BuildCards();
        ShowSelected();
        GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume * 0.6f);
    }

    void PickFinish(WeaponFinish finish)
    {
        selected = finish;
        scrapArmed = false;
        foreach (FinishCard card in cards)
            card.Select(card.Finish == finish);

        ShowSelected();
        GameAudio.PlayPitched(GameAudio.UI, "click_001", GameAudio.UiVolume * 0.6f, 1.2f);
    }

    void ShowSelected()
    {
        if (stage != null)
            stage.ShowWeapon(weapon, selected);

        if (weaponName != null)
            weaponName.text = WeaponLoadout.DisplayName(weapon).ToUpperInvariant();

        Color rarity = selected != null ? CrateRarityInfo.ColorFor(selected.rarity) : new Color(0.55f, 0.57f, 0.62f);
        if (finishName != null)
            finishName.text = selected != null ? selected.displayName.ToUpperInvariant() : "STOCK";
        if (rarityText != null)
        {
            rarityText.text = selected != null ? CrateRarityInfo.NameFor(selected.rarity) : "DEFAULT";
            rarityText.color = rarity;
        }
        if (rarityPill != null)
            rarityPill.color = rarity;
        if (flavourText != null)
            flavourText.text = selected != null ? selected.flavour : "The way nature intended.";
        if (ownedText != null)
            ownedText.text = selected != null ? $"{SkinInventory.Count(selected)} OWNED  -  SCRAPS FOR {selected.ScrapValue}" : "";

        DrawActions();
        DrawCollected();

        if (finishName != null)
            StartCoroutine(Punch(finishName.rectTransform));
    }

    IEnumerator Punch(RectTransform target)
    {
        yield return UiMotion.Tween(0.25f, UiMotion.OutBack, k => target.localScale = Vector3.one * Mathf.LerpUnclamped(1.15f, 1f, k));
    }

    void DrawActions()
    {
        bool wearing = SkinInventory.Equipped(weapon) == selected;

        if (equipButton != null) equipButton.interactable = !wearing;
        if (equipLabel != null) equipLabel.text = wearing ? "EQUIPPED" : selected == null ? "USE STOCK" : "EQUIP";

        if (scrapButton != null) scrapButton.interactable = selected != null;
        if (scrapLabel != null)
            scrapLabel.text = selected == null ? "SCRAP" : scrapArmed ? $"SURE? +{selected.ScrapValue}" : $"SCRAP  +{selected.ScrapValue}";

        int spare = 0;
        foreach (WeaponFinish finish in FinishCatalog.All)
            spare += Mathf.Max(0, SkinInventory.Count(finish) - 1) * finish.ScrapValue;
        if (scrapSparesButton != null) scrapSparesButton.interactable = spare > 0;
        if (scrapSparesLabel != null) scrapSparesLabel.text = spare > 0 ? $"SCRAP SPARES  +{spare}" : "NO SPARES";
    }

    void DrawCollected()
    {
        if (collectedText == null)
            return;

        int have = SkinInventory.Owned().Count;
        collectedText.text = $"{have} / {FinishCatalog.All.Count} FINISHES";
    }

    // ---------------------------------------------------------------- doing

    void Equip()
    {
        SkinInventory.Equip(weapon, selected);
        GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);
        Juice.Hit(0.2f);
    }

    void EquipAll()
    {
        SkinInventory.EquipEverywhere(selected);
        GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);
        Juice.Hit(0.3f);
    }

    /// Two clicks - the first asks, the second does it. A misclick shouldn't cost an Apex.
    void Scrap()
    {
        if (selected == null)
            return;

        if (!scrapArmed)
        {
            scrapArmed = true;
            scrapArmedUntil = Time.unscaledTime + 3f;
            GameAudio.Play2D(GameAudio.UI, "error_001", GameAudio.UiVolume * 0.6f);
            DrawActions();
            return;
        }

        WeaponFinish scrapped = selected;
        scrapArmed = false;
        if (SkinInventory.Scrap(scrapped))
        {
            Pay(scrapped.ScrapValue);
            if (SkinInventory.Count(scrapped) <= 0)
                selected = SkinInventory.Equipped(weapon);
        }
    }

    void ScrapSpares()
    {
        int paid = SkinInventory.ScrapSpares();
        if (paid > 0)
            Pay(paid);
    }

    void Pay(int amount)
    {
        GameAudio.Play2D(GameAudio.Hit, "hit", GameAudio.HitVolume * 0.6f);
        GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);
        Juice.Hit(0.35f);
        if (payout != null)
            StartCoroutine(Float(amount));
    }

    IEnumerator Float(int amount)
    {
        payout.text = $"+{amount} TOKENS";
        payout.gameObject.SetActive(true);
        RectTransform rect = payout.rectTransform;
        Vector2 start = rect.anchoredPosition;
        yield return UiMotion.Tween(1.1f, UiMotion.OutCubic, k =>
        {
            rect.anchoredPosition = start + new Vector2(0f, 90f * k);
            payout.alpha = 1f - UiMotion.InQuad(k);
            rect.localScale = Vector3.one * (1f + 0.3f * (1f - k));
        });
        rect.anchoredPosition = start;
        payout.gameObject.SetActive(false);
    }

    void Refresh()
    {
        if (!IsOpen)
            return;

        foreach (InventoryWeaponRow row in rows)
            row.Refresh(row.Weapon == weapon);

        BuildCards();
        ShowSelected();
    }
}
