using Photon.Pun;
using TMPro;
using UnityEngine;

public class PlayerNameManager : MonoBehaviour
{
    const string prefsKey = "username";

    [SerializeField] TMP_InputField usernameInput;

    void Start()
    {
        if (usernameInput == null)
        {
            Debug.LogError("[PlayerNameManager] no input field assigned.", this);
            enabled = false;
            return;
        }

        // HasKey was true for a key holding an empty string, which then set an empty NickName --
        // players showed up as blank on the scoreboard and on nameplates with no way to fix it
        // short of clearing PlayerPrefs.
        string saved = PlayerPrefs.GetString(prefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(saved))
            saved = "Jahil " + Random.Range(0, 10000).ToString("0000");

        usernameInput.text = saved;
        Apply(saved);
    }

    /// <summary>Wired to the input field's value-changed event.</summary>
    public void OnUserNameInputValueChanged()
    {
        if (usernameInput != null)
            Apply(usernameInput.text);
    }

    void Apply(string name)
    {
        name = name.Trim();

        // Refuse to persist an empty nickname rather than saving it and reading it back next launch.
        if (string.IsNullOrEmpty(name))
            return;

        PhotonNetwork.NickName = name;
        PlayerPrefs.SetString(prefsKey, name);
    }
}
