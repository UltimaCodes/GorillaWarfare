using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance;

    Spawnpoint[] spawnpoints;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SpawnManager] a second instance exists; destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        spawnpoints = GetComponentsInChildren<Spawnpoint>();

        if (spawnpoints.Length == 0)
            Debug.LogError("[SpawnManager] no Spawnpoint children found; players cannot spawn.", this);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Returns a random spawnpoint, or null if none are configured.</summary>
    public Transform GetSpawnpoint()
    {
        // Previously indexed straight into the array: with no spawnpoints, Random.Range(0, 0)
        // returns 0 and spawnpoints[0] threw IndexOutOfRangeException.
        if (spawnpoints == null || spawnpoints.Length == 0)
        {
            Debug.LogError("[SpawnManager] GetSpawnpoint called with no spawnpoints configured.", this);
            return null;
        }

        return spawnpoints[Random.Range(0, spawnpoints.Length)].transform;
    }
}
