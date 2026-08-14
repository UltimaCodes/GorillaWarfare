using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;

public class UsernameDisplay : MonoBehaviour
{
    [SerializeField] PhotonView playerPV;
    [SerializeField] TMP_Text text;

    void Start()
    {
        if (playerPV == null || text == null)
        {
            enabled = false;
            return;
        }

        // Don't want my own name floating inside my head.
        if (playerPV.IsMine)
        {
            gameObject.SetActive(false);
            return;
        }

        if (!TryApplyNickname())
            StartCoroutine(WaitForOwner());
    }

    bool TryApplyNickname()
    {
        // Owner isn't always set the instant an instantiated object wakes up.
        if (playerPV.Owner == null)
            return false;

        string nick = playerPV.Owner.NickName;
        text.text = string.IsNullOrWhiteSpace(nick) ? $"Player {playerPV.Owner.ActorNumber}" : nick;
        return true;
    }

    IEnumerator WaitForOwner()
    {
        for (int i = 0; i < 120; i++)
        {
            yield return null;

            if (TryApplyNickname())
                yield break;
        }

        Debug.LogWarning($"View {playerPV.ViewID} never got an Owner.", this);
    }
}
