using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance;

    Spawnpoint[] spawnpoints;

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

        return spawnpoints[Random.Range(0, spawnpoints.Length)].transform;
    }
}
