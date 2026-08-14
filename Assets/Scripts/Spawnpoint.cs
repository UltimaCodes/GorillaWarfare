using UnityEngine;

/// <summary>Marks a spawn location. The graphics child is editor-only and hidden at runtime.</summary>
public class Spawnpoint : MonoBehaviour
{
    [SerializeField] GameObject graphics;

    void Awake()
    {
        // Guarded: an unassigned reference here threw during scene load, which aborted
        // SpawnManager's sibling initialisation depending on execution order.
        if (graphics != null)
            graphics.SetActive(false);
    }
}
