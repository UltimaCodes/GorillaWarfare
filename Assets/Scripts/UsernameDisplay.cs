using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>Shows the owning player's nickname above a remote player.</summary>
public class UsernameDisplay : MonoBehaviour
{
    [SerializeField] PhotonView playerPV;
    [SerializeField] TMP_Text text;

    void Start()
    {
        if (playerPV == null || text == null)
        {
            Debug.LogError("[UsernameDisplay] missing PhotonView or text reference.", this);
            enabled = false;
            return;
        }

        // Your own nameplate would sit inside your head. Return rather than falling through:
        // the old code disabled the object and then kept going to touch Owner anyway.
        if (playerPV.IsMine)
        {
            gameObject.SetActive(false);
            return;
        }

        if (!TryApplyNickname())
            StartCoroutine(ApplyNicknameWhenOwnerArrives());
    }

    // Owner is not guaranteed to be populated the moment an instantiated object awakes, and
    // dereferencing it blindly threw a NullReferenceException on the remote clients that lost
    // that race.
    bool TryApplyNickname()
    {
        if (playerPV.Owner == null)
            return false;

        string nick = playerPV.Owner.NickName;
        text.text = string.IsNullOrWhiteSpace(nick) ? $"Player {playerPV.Owner.ActorNumber}" : nick;
        return true;
    }

    IEnumerator ApplyNicknameWhenOwnerArrives()
    {
        for (int i = 0; i < 120; i++)
        {
            yield return null;

            if (TryApplyNickname())
                yield break;
        }

        Debug.LogWarning($"[UsernameDisplay] view {playerPV.ViewID} never resolved an Owner.", this);
    }
}
