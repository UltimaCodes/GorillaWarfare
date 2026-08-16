using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// The row of colour swatches in the lobby.
///
/// Swatches are stamped from one template rather than authored eight times, for the same reason
/// the settings rows are: restyling one restyles all of them, and adding a ninth colour to the
/// palette needs no work here at all.
///
/// Colours are not enforced as unique. Two people who both insist on lime can have it and the
/// game still works - but a taken swatch is dimmed and marked, so it takes a deliberate act
/// rather than an accident.
/// </summary>
public class ColourPicker : MonoBehaviourPunCallbacks
{
    [SerializeField] RectTransform row;
    [SerializeField] Button swatchTemplate;
    [SerializeField] TMP_Text caption;

    [Tooltip("Border thickness added to the swatch you have picked.")]
    [SerializeField] float selectedOutline = 4f;

    readonly System.Collections.Generic.List<Button> swatches =
        new System.Collections.Generic.List<Button>();

    void Awake()
    {
        if (swatchTemplate != null)
            swatchTemplate.gameObject.SetActive(false);

        Build();
    }

    void Build()
    {
        if (row == null || swatchTemplate == null)
            return;

        for (int i = 0; i < PlayerColours.Palette.Length; i++)
        {
            int index = i;

            Button swatch = Instantiate(swatchTemplate, row);
            swatch.name = $"Swatch{i}";
            swatch.gameObject.SetActive(true);

            Image face = swatch.targetGraphic as Image;
            if (face == null)
                face = swatch.GetComponent<Image>();

            if (face != null)
                face.color = PlayerColours.Palette[i];

            swatch.onClick.AddListener(() =>
            {
                PlayerColours.Choose(index);
                GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);

                // Drawn immediately rather than waiting for the property to come back from the
                // server. The round trip is short but it is not instant, and a swatch that
                // doesn't respond to a click reads as a broken button.
                Draw();
            });

            swatches.Add(swatch);
        }

        Draw();
    }

    /// Redrawn whenever anybody's colour changes, not just your own - a swatch someone else has
    /// just taken has to dim on your screen too.
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey(PlayerColours.ColourKey))
            Draw();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) => Draw();
    public override void OnPlayerLeftRoom(Player otherPlayer) => Draw();

    void Draw()
    {
        int mine = PhotonNetwork.InRoom ? PlayerColours.IndexOf(PhotonNetwork.LocalPlayer) : 0;

        for (int i = 0; i < swatches.Count; i++)
        {
            bool taken = PlayerColours.Taken(i);
            bool chosen = i == mine;

            // Dimmed rather than disabled. A taken colour is discouraged, not forbidden, and a
            // dead button gives no way to find that out.
            Image face = swatches[i].GetComponent<Image>();

            if (face != null)
            {
                Color colour = PlayerColours.Palette[i];
                face.color = taken && !chosen ? new Color(colour.r, colour.g, colour.b, 0.28f) : colour;
            }

            // The one you have is outlined. Scale would be simpler and reads as a hover state
            // instead of a selection, which is the wrong thing to say.
            Outline edge = swatches[i].GetComponent<Outline>();

            if (edge == null)
                edge = swatches[i].gameObject.AddComponent<Outline>();

            edge.enabled = chosen;
            edge.effectColor = Color.white;
            edge.effectDistance = new Vector2(selectedOutline, -selectedOutline);
        }

        if (caption != null)
            caption.text = PlayerColours.Names[Mathf.Clamp(mine, 0, PlayerColours.Names.Length - 1)].ToUpper();
    }
}
