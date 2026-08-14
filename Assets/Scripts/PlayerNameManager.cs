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

        // HasKey is true even when the saved name is empty, so once you'd blanked the field
        // you came back nameless every launch.
        string saved = PlayerPrefs.GetString(prefsKey, string.Empty);
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
        name = name.Trim();

        if (string.IsNullOrEmpty(name))
            return;

        PhotonNetwork.NickName = name;
        PlayerPrefs.SetString(prefsKey, name);
    }
}
