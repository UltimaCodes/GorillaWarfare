using System;
using UnityEngine;

/// <summary>
/// Tokens - the crate currency. Local to this machine and this player, same as key bindings and
/// settings (PlayerPrefs, same Prefix convention GameSettings already uses) rather than a Photon
/// custom property - a wallet has to survive between sessions and between matches, which a room
/// property never does (see working-notes.md's own repeated "PUN never clears custom properties"
/// bug class - the opposite problem here: this needs to persist past the room, not get cleared
/// with it).
///
/// No rewards exist yet - opening a crate spends tokens and reveals a rarity tier and nothing
/// else. This is the economy half of that system; see CrateInfo/CrateOpeningScreen for the rest.
/// </summary>
public static class PlayerWallet
{
    const string Prefix = "GW_Wallet_";
    const string TokensKey = Prefix + "Tokens";

    /// "Give every player 100 tokens from the start." The PlayerPrefs fallback value doubles as
    /// the starting grant - a fresh install with no key written yet reads 100 rather than 0, and
    /// the very first Add/Spend call writes a real value that takes over from then on. No
    /// separate "have I granted this yet" flag needed - the fallback only ever applies before
    /// anything has actually written to this key at all.
    const int StartingTokens = 100;

    public static event Action Changed;

    public static int Tokens => PlayerPrefs.GetInt(TokensKey, StartingTokens);

    /// Called at the end of every round - see MatchState's own end-of-match transition.
    public static void Add(int amount)
    {
        if (amount <= 0)
            return;

        PlayerPrefs.SetInt(TokensKey, Tokens + amount);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// Returns false (and takes nothing) if the balance is short - the caller decides what a
    /// failed spend looks like (a shake, a sound, a greyed-out button), this just holds the line.
    public static bool Spend(int amount)
    {
        if (amount <= 0 || Tokens < amount)
            return false;

        PlayerPrefs.SetInt(TokensKey, Tokens - amount);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }
}
