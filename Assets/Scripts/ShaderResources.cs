using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// Holds the post-processing package's shader and texture references so runtime code can reach
/// them.
///
/// PostProcessLayer needs a PostProcessResources asset to work, and the only copy lives inside
/// the package, which nothing at runtime can load by path - the package folder carries a
/// content hash in its name that changes whenever the package is reinstalled. So an editor
/// script finds it once and points this at it, and this sits in Resources where the game can
/// pick it up.
/// </summary>
public class ShaderResources : ScriptableObject
{
    public PostProcessResources resources;

    public static PostProcessResources Load()
    {
        ShaderResources holder = Resources.Load<ShaderResources>("ShaderResources");

        if (holder == null || holder.resources == null)
        {
            Debug.LogError("[shaders] no ShaderResources in Resources - "
                           + "run Tools/Gorilla Warfare/Build the shader stack");
            return null;
        }

        return holder.resources;
    }
}
