using UnityEngine;

/// <summary>
/// How deep the current movement-tech chain is - a second, separate counter from StyleScore's
/// kill-based multiplier, tracking traversal instead of combat. Added after the kill meter
/// started working and the gap next to it became obvious: "no slide combo text still which
/// should show up... make new movement tech combos and stuff" - a grapple, a self-knockback
/// grenade jump and a slide-hop chain all feed the same counter here rather than each having its
/// own, so switching between techs mid-run reads as one continuous run instead of resetting every
/// time you change what you're doing. Bhop was in this list too originally and was removed the
/// same day, per direct request.
///
/// Deliberately not folded into StyleScore itself - that component is about combat performance
/// and decides a match's winner; this is a HUD flourish about traversal that never leaves this
/// client, shown bottom left rather than sharing the kill meter's top-right spot.
///
/// Local only, mine-only, added by PlayerController.Start the same way StyleScore is.
/// </summary>
public class MovementCombo : MonoBehaviour
{
    int chain;
    float expiresAt = -99f;
    string lastTech = "";

    const float ComboWindow = 3f;      // seconds to land the next trick before this resets

    /// Not a hard cap - the chain itself can climb past this - just where the HUD's own colour
    /// ramp (white toward comboColour) finishes heating up, the same shape StyleScore.
    /// MaxMultiplier gives GameHud's kill meter.
    public const int HeatChain = 6;

    public int Chain => Time.unscaledTime < expiresAt ? chain : 0;
    public bool Active => Chain > 0;
    public string LastTech => lastTech;

    /// How much of the current window is left, 0 to 1 - same shape as StyleScore.DecayFraction,
    /// for the HUD's own draining meter under the label.
    public float DecayFraction => Active
        ? Mathf.Clamp01((expiresAt - Time.unscaledTime) / ComboWindow)
        : 0f;

    /// Called by whichever movement system just landed a trick - a slide entry
    /// (PlayerMovement.GroundMove), a grapple attach (VineGrapple.RPC_Attach) or a self-knockback
    /// grenade jump (Projectile.Explode). Each one just extends the same chain rather than
    /// keeping its own counter.
    public void Register(string tech)
    {
        chain = Time.unscaledTime < expiresAt ? chain + 1 : 1;
        expiresAt = Time.unscaledTime + ComboWindow;
        lastTech = tech;
    }

    /// Momentum meeting a wall breaks the chain outright - the same "crashing into something
    /// costs you" reasoning StyleScore.RegisterWallSmash already established for the kill
    /// multiplier. See PlayerMovement.WallSmash, which calls both.
    public void Break()
    {
        chain = 0;
        expiresAt = -99f;
    }
}
