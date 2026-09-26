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
            enabled = false;
            return;
        }

        // Capped and bracket-free as you type, rather than only cleaned afterwards - a field that
        // shows one thing while everybody else sees another reads as broken.
        usernameInput.characterLimit = PlayerNames.MaxLength;
        usernameInput.onValidateInput += PlayerNames.RejectAngleBrackets;

        // HasKey is true even when the saved name is empty, so once you'd blanked the field
        // you came back nameless every launch. Cleaned too - a name saved before the cap existed
        // can be longer than the field now allows.
        string saved = PlayerNames.Clean(PlayerPrefs.GetString(prefsKey, string.Empty));
        if (string.IsNullOrWhiteSpace(saved))
            saved = "Jahil " + Random.Range(0, 10000).ToString("0000");

        usernameInput.text = saved;
        Apply(saved);
    }

    // Hooked up to the input field in the inspector.
    public void OnUserNameInputValueChanged()
    {
        if (usernameInput != null)
            Apply(usernameInput.text);
    }

    void Apply(string name)
    {
        name = PlayerNames.Clean(name);

        if (string.IsNullOrEmpty(name))
            return;

        PhotonNetwork.NickName = name;
        PlayerPrefs.SetString(prefsKey, name);
    }
}
