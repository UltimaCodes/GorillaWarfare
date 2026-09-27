// Anything a bullet can meaningfully land on.
//
// The weapon name and the headshot flag ride along with the damage because the kill feed needs
// them and the victim is the only client that knows it died - by the time anyone else could
// ask, the shot is long gone.
//
// Returns whether this specific call was the one that killed it. PlayerController always returns
// false here - a player's own death only resolves once RPC_Died round-trips back, and that RPC
// (not this call) is where a real kill gets credited to the style score. TrainingDummy resolves
// synchronously, so it can answer immediately - which is exactly what lets a dummy kill feed the
// style score at all in the sandbox, where there's no second real player to actually kill.
public interface IDamageable
{
    bool TakeDamage(float damage, string weapon, bool headshot);
}
