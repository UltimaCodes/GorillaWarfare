using System.Collections.Generic;
using UnityEngine;

public class ShaderInteractor : MonoBehaviour
{
    public float radius = 1f;

    // Gorilla Warfare: a live registry GrassComputeScript reads every frame, instead of the
    // FindObjectsOfType it ran once when the grass set itself up - before any player had spawned,
    // so nobody who spawned later ever pushed the grass, and a destroyed one threw every frame.
    public static readonly List<ShaderInteractor> Active = new List<ShaderInteractor>();

    void OnEnable() => Active.Add(this);

    void OnDisable() => Active.Remove(this);
}
