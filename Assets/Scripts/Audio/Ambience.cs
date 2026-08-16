using UnityEngine;

/// <summary>
/// The sound of the map when nothing is happening.
///
/// Between fights the game is silent, and silence reads as the audio being broken rather than as
/// nothing happening. A jungle loop underneath everything fixes that for almost nothing.
///
/// Scaled by the effects slider rather than the music one. It isn't music - it doesn't have a
/// mood or a mix and turning the music off shouldn't leave you in a vacuum - but it also isn't a
/// one-shot, so it has its own source rather than going through GameAudio's pool.
///
/// Only in the game. The menu has its own track and doesn't need crickets over it.
/// </summary>
public class Ambience : MonoBehaviour
{
    [Tooltip("Level relative to the effects slider. Deliberately low - it sits under everything.")]
    [SerializeField] float level = 0.18f;

    [Tooltip("Seconds to fade in and out when the scene changes.")]
    [SerializeField] float fade = 1.5f;

    AudioSource source;
    bool wanted;

    void Awake()
    {
        AudioClip[] clips = Resources.LoadAll<AudioClip>("Audio/Ambience");

        if (clips.Length == 0)
        {
            // Not an error. The folder is meant to be filled in and the game is perfectly
            // playable without it - but it should say so once rather than silently do nothing.
            Debug.Log("[ambience] nothing in Resources/Audio/Ambience, so the map stays quiet");
            enabled = false;
            return;
        }

        source = gameObject.AddComponent<AudioSource>();
        source.clip = clips[Random.Range(0, clips.Length)];
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;

        // Flat 2D. A positioned ambience needs emitters placed round the map and this has one
        // clip and no map to place them in yet.
        source.spatialBlend = 0f;

        source.Play();

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        wanted = InGame();
    }

    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                       UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        wanted = InGame();
    }

    static bool InGame() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
        == RoomManager.gameSceneIndex;

    void Update()
    {
        if (source == null)
            return;

        float target = wanted ? level * GameSettings.SfxVolume : 0f;

        // Unscaled, so it doesn't stall during hitstop - a background loop that stutters every
        // time somebody gets a kill is worse than no background loop.
        source.volume = Mathf.MoveTowards(source.volume, target,
                                          Time.unscaledDeltaTime * Mathf.Max(0.05f, level) / Mathf.Max(0.1f, fade));
    }
}
