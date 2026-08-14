using UnityEngine;

public class Spawnpoint : MonoBehaviour
{
    // Just a marker in the editor, hidden at runtime.
    [SerializeField] GameObject graphics;

    void Awake()
    {
        if (graphics != null)
            graphics.SetActive(false);
    }
}
