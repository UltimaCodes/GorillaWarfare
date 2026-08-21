using UnityEngine;

/// <summary>
/// Full-screen toon outline, driven off the camera's own depth+normal buffer rather than pushed
/// mesh geometry (see Custom/ToonOutline.shader, and Assets/Shaders/README.txt for why that one
/// isn't what's actually wired up). One component on one camera outlines everything it sees -
/// players, projectiles, weapons, world geometry - which is also the honest answer to "try it on
/// other things too": there's no separate per-object step to remember to add.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ScreenOutline : MonoBehaviour
{
    [SerializeField] Color outlineColor = new Color(0.04f, 0.04f, 0.04f, 1f);
    [SerializeField] float depthSensitivity = 2.5f;
    [SerializeField] float normalSensitivity = 3.0f;
    [SerializeField] float thickness = 1.5f;

    static Material sharedMaterial;

    void Awake()
    {
        GetComponent<Camera>().depthTextureMode |= DepthTextureMode.DepthNormals;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (sharedMaterial == null)
        {
            Shader shader = Shader.Find("Custom/ScreenOutline");

            if (shader == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            sharedMaterial = new Material(shader);
        }

        sharedMaterial.SetColor("_OutlineColor", outlineColor);
        sharedMaterial.SetFloat("_DepthSensitivity", depthSensitivity);
        sharedMaterial.SetFloat("_NormalSensitivity", normalSensitivity);
        sharedMaterial.SetFloat("_Thickness", thickness);

        Graphics.Blit(src, dst, sharedMaterial);
    }
}
