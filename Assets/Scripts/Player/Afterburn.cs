using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Being on fire - Red Hot Chili Pepper's afterburn, TF2's rule that the flame keeps hurting
/// after it stops touching you.
///
/// On every player and on any training dummy that gets lit. On a player's own client it ticks
/// the damage (through PlayerController.TakeBurn, so the kill goes to whoever lit you); on
/// everyone else's it only shows the flames, from a flag the owner streams. On a dummy - local,
/// like everything in the one-person sandbox - it does both.
/// </summary>
public class Afterburn : MonoBehaviour
{
    const float TickSeconds = 0.25f;
    const float FlameEvery = 0.09f;

    float burningUntil;
    float perSecond;
    Player igniter;
    float nextTick;
    float nextFlame;
    bool shownRemotely;

    PlayerController player;
    TrainingDummy dummy;

    static Sprite[] fire;

    /// Whether this body is burning, as far as this client decides it.
    public bool Burning => Time.time < burningUntil;

    public static Afterburn On(GameObject target)
    {
        Afterburn burn = target.GetComponent<Afterburn>();
        if (burn == null)
            burn = target.AddComponent<Afterburn>();
        return burn;
    }

    void Awake()
    {
        player = GetComponent<PlayerController>();
        dummy = GetComponent<TrainingDummy>();
    }

    /// Lit, or lit again - more flame refreshes the burn rather than stacking it.
    public void Ignite(float seconds, float damagePerSecond, Player by)
    {
        if (!Burning)
            nextTick = Time.time + TickSeconds;

        burningUntil = Mathf.Max(burningUntil, Time.time + seconds);
        perSecond = Mathf.Max(damagePerSecond, Burning ? perSecond : 0f);
        igniter = by;
    }

    /// A remote copy's flames, from the owner's stream.
    public void ShowBurning(bool burning) => shownRemotely = burning;

    void Update()
    {
        bool mine = player == null || (player.View != null && player.View.IsMine);
        bool visible = mine ? Burning : shownRemotely;

        if (visible && Time.time >= nextFlame)
        {
            nextFlame = Time.time + FlameEvery;
            Flame();
        }

        if (!mine || !Burning || Time.time < nextTick)
            return;

        nextTick = Time.time + TickSeconds;
        float damage = perSecond * TickSeconds;

        if (player != null)
            player.TakeBurn(damage, igniter);
        else if (dummy != null)
            dummy.TakeDamage(damage, "Flamer", false);
    }

    /// A lick of flame somewhere on the body, drifting up.
    void Flame()
    {
        if (fire == null || fire.Length == 0)
            fire = System.Array.FindAll(Resources.LoadAll<Sprite>("Particles/Boom"), s => s.name.StartsWith("fire"));

        if (fire.Length == 0)
            return;

        Vector3 at = transform.position + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.6f, 0.9f),
                                                      Random.Range(-0.35f, 0.35f));
        FlashSprite.Spawn(fire[Random.Range(0, fire.Length)], at, 0.25f, 0.55f, 0.3f, new Color(1f, 0.45f, 0.1f, 0.6f));
    }
}
