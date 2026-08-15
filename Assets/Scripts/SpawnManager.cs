using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides where you come back.
///
/// This used to pick a spawnpoint at random, which with eight pads and up to eight players
/// means landing on top of somebody roughly as often as not - and landing in front of somebody
/// is worse, because you spend your first second alive being shot by a person who did nothing
/// clever to earn it. Dying to a spawn is the least interesting way to lose a fight.
///
/// So it scores every pad by how far it is from the nearest living player, punishes any pad
/// that a living player can see, and picks at random from the best handful. The randomness at
/// the end matters: always taking the single best pad makes spawns predictable, and predictable
/// spawns can be camped just as effectively as bad ones.
/// </summary>
public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance;

    [Tooltip("How many of the best pads to choose between. One is predictable, all of them is "
             + "the random behaviour this replaced.")]
    [SerializeField] int shortlist = 3;

    [Tooltip("Metres of penalty for a pad a living player can see. Deliberately larger than the "
             + "map, so line of sight beats distance every time.")]
    [SerializeField] float sightPenalty = 100f;

    [Tooltip("Roughly chest height. Line of sight is tested between chests rather than between "
             + "feet, because feet are usually behind whatever cover the pad is next to.")]
    [SerializeField] float eyeHeight = 1.4f;

    Spawnpoint[] spawnpoints;
    Transform lastUsed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        spawnpoints = GetComponentsInChildren<Spawnpoint>();

        if (spawnpoints.Length == 0)
            Debug.LogError("No spawnpoints under SpawnManager - nobody can spawn.", this);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public Transform GetSpawnpoint()
    {
        // Random.Range(0, 0) returns 0, so an empty array used to blow up here.
        if (spawnpoints == null || spawnpoints.Length == 0)
            return null;

        List<Vector3> living = new List<Vector3>();

        foreach (PlayerController other in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            // The local player is destroyed while dead, so anything still standing is somebody
            // else - but a spectator or a copy mid-teardown might not have a body.
            if (other != null && other.isActiveAndEnabled)
                living.Add(other.transform.position);
        }

        // Nobody about. Anywhere is as good as anywhere else, and this is the common case at
        // the start of a match.
        if (living.Count == 0)
            return spawnpoints[Random.Range(0, spawnpoints.Length)].transform;

        float[] scores = new float[spawnpoints.Length];

        for (int i = 0; i < spawnpoints.Length; i++)
        {
            Vector3 pad = spawnpoints[i].transform.position;
            scores[i] = Score(pad, living);

            if (CanBeSeenFrom(pad, living))
                scores[i] -= sightPenalty;

            // Never twice running, if there's any alternative. Coming back on the exact pad you
            // just died on is the single most demoralising respawn there is.
            if (spawnpoints.Length > 1 && spawnpoints[i].transform == lastUsed)
                scores[i] -= sightPenalty * 0.5f;
        }

        int chosen = PickFromBest(scores, Mathf.Max(1, shortlist));
        lastUsed = spawnpoints[chosen].transform;

        return lastUsed;
    }

    /// <summary>
    /// How good a pad is, before line of sight: the distance to the nearest living player.
    ///
    /// The nearest one rather than the average, because the average is dominated by whoever
    /// happens to be on the far side of the map and says nothing about the person standing
    /// round the corner - who is the one who is going to kill you.
    ///
    /// Pure and static so it can be checked without a scene.
    /// </summary>
    public static float Score(Vector3 pad, IReadOnlyList<Vector3> living)
    {
        float nearest = float.MaxValue;

        for (int i = 0; i < living.Count; i++)
            nearest = Mathf.Min(nearest, Vector3.Distance(pad, living[i]));

        return nearest == float.MaxValue ? 0f : nearest;
    }

    /// <summary>
    /// Picks at random from the highest scoring pads.
    ///
    /// Static and taking the count so a check can drive it directly. The shortlist is the whole
    /// design: taking the best pad every time turns spawns into a fixed rotation that anybody
    /// can learn and sit on.
    /// </summary>
    public static int PickFromBest(float[] scores, int shortlist)
    {
        if (scores == null || scores.Length == 0)
            return 0;

        int take = Mathf.Clamp(shortlist, 1, scores.Length);

        // Partial selection rather than a full sort. There are eight of these and it runs once
        // per respawn, so this is about being obvious rather than about speed.
        List<int> order = new List<int>(scores.Length);

        for (int i = 0; i < scores.Length; i++)
            order.Add(i);

        order.Sort((a, b) => scores[b].CompareTo(scores[a]));

        return order[Random.Range(0, take)];
    }

    bool CanBeSeenFrom(Vector3 pad, List<Vector3> living)
    {
        Vector3 from = pad + Vector3.up * eyeHeight;

        foreach (Vector3 other in living)
        {
            Vector3 to = other + Vector3.up * eyeHeight;

            // Nothing solid between the two chests means they are looking at each other. The
            // world mask rather than everything, or this trips on the players themselves and
            // decides every pad is safe.
            if (!Physics.Linecast(from, to, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
                return true;
        }

        return false;
    }
}
