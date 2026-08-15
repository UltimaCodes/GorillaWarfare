// Anything a bullet can meaningfully land on.
//
// The weapon name and the headshot flag ride along with the damage because the kill feed needs
// them and the victim is the only client that knows it died - by the time anyone else could
// ask, the shot is long gone.
public interface IDamageable
{
    void TakeDamage(float damage, string weapon, bool headshot);
}
