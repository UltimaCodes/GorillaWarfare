using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// A Post-Processing Stack v2 custom effect - PPv2's own supported extension point, the same
/// package everything else in <see cref="ShaderStack"/> already runs on, rather than a second
/// OnRenderImage pass competing with it. Settings type and renderer are PPv2's required pair;
/// <see cref="ShaderStack"/> is the only thing that ever adds one to a profile.
/// </summary>
[System.Serializable]
[PostProcess(typeof(PsxFilterRenderer), PostProcessEvent.AfterStack, "Gorilla Warfare/PSX Filter")]
public sealed class PsxFilter : PostProcessEffectSettings
{
    [Range(0f, 1f)] public FloatParameter intensity = new FloatParameter { value = 0.88f };
}

public sealed class PsxFilterRenderer : PostProcessEffectRenderer<PsxFilter>
{
    static readonly int LowResId = Shader.PropertyToID("_PsxLowRes");

    Shader shader;

    public override void Init()
    {
        shader = Shader.Find("Hidden/Gorilla Warfare/PSX Filter");
    }

    public override void Render(PostProcessRenderContext context)
    {
        if (shader == null)
        {
            // Same silent-fallback shape as ScreenOutline - a stripped shader shouldn't take the
            // whole post stack down with it, just quietly do nothing. Registered in Always
            // Included Shaders specifically so this path is never actually taken in a build.
            context.command.BlitFullscreenTriangle(context.source, context.destination);
            return;
        }

        // The colour-quantize-and-dither pass alone was reported "does nothing" - real per the
        // pixel check, just too subtle to read as a look. The reference shots that came back
        // afterward (a PS1-era demake, a period FPS) are dominated by something that pass never
        // touched at all: real internal resolution, not full-resolution colour banding. Rendering
        // low and upscaling with point filtering is what actually produces that - the same
        // technique the PS1 shaders researched for this feature use, not a novel idea.
        int scale = 4;
        int lowWidth = Mathf.Max(4, context.camera.pixelWidth / scale);
        int lowHeight = Mathf.Max(4, context.camera.pixelHeight / scale);

        CommandBuffer cmd = context.command;

        // Allocated and released through the command buffer, not RenderTexture.GetTemporary:
        // Render() only records commands, which run later in the frame. A texture handed back to
        // the pool here, before those commands had run, was free for anything else to grab in
        // between - it only ever worked because nothing happened to.
        //
        // Point, not the default (bilinear) - this is the actual pixelation step. Sampling this
        // texture at full screen size with no interpolation between its texels is what turns
        // "rendered small" into "blocky," rather than just a soft, blurry downscale.
        cmd.GetTemporaryRT(LowResId, lowWidth, lowHeight, 0, FilterMode.Point);

        // PropertySheet rather than a hand-managed Material - PPv2's own factory owns the
        // instance and its lifetime (including across a settings hot-reload), which is what
        // every other effect in this same package already relies on. Quantize-and-dither happens
        // during the downscale, on the low-res image the upscale will actually show - dithering
        // at full resolution first and then shrinking it would let the downscale filter blur the
        // dither pattern straight back out.
        PropertySheet sheet = context.propertySheets.Get(shader);
        sheet.properties.SetFloat("_Intensity", settings.intensity);
        cmd.BlitFullscreenTriangle(context.source, LowResId, sheet, 0);

        cmd.Blit(LowResId, context.destination);
        cmd.ReleaseTemporaryRT(LowResId);
    }
}
