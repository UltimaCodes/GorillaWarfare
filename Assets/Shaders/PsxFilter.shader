// Optional PSX-vibe post effect. Started colour-only - the PS1 held roughly 15-bit colour (5
// bits a channel, 32 levels) and leaned on an ordered dither to keep gradients from banding at
// that depth, which this does. Reported back as "does nothing" at a subtle setting, then handed
// real reference shots once the ask was to fix it properly - PsxFilterRenderer.cs now also
// renders low and upscales with point filtering (the actual pixelation those references are
// dominated by) before this shader's quantize-and-dither ever runs on the result. Still no vertex
// snapping or affine texture warp - both are per-material vertex-shader techniques that would
// need touching every shader already in the project (the banana materials, the skybox,
// ScreenOutline) to apply safely, not something a screen-space pass can add regardless of how
// aggressive the rest of this gets.
Shader "Hidden/Gorilla Warfare/PSX Filter"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float _Intensity;

        // Standard 4x4 ordered (Bayer) dither, normalised to -0.5..0.5. Applied before
        // quantizing so the rounding error scatters into a stipple instead of a flat band -
        // the same trick the original hardware used to fake more colour than it had.
        static const float Bayer4x4[16] = {
            0.0, 8.0, 2.0, 10.0,
            12.0, 4.0, 14.0, 6.0,
            3.0, 11.0, 1.0, 9.0,
            15.0, 7.0, 13.0, 5.0
        };

        float DitherOffset(float2 screenPos)
        {
            uint2 cell = uint2(fmod(screenPos.x, 4.0), fmod(screenPos.y, 4.0));
            return (Bayer4x4[cell.y * 4 + cell.x] / 16.0) - 0.5;
        }

        float4 Frag(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);

            // Levels per channel: 255 (off) down to 20 at full intensity. 20 is deliberately
            // short of the PS1's real 32 - full 32 read as barely-there once lit scenes were
            // actually on screen, and the ask was a vibe you can see, not a fact you can measure.
            float levels = lerp(255.0, 20.0, saturate(_Intensity));
            float dither = DitherOffset(i.vertex.xy) / levels;

            float3 quantized = floor((color.rgb + dither) * levels + 0.5) / levels;
            float3 result = lerp(color.rgb, quantized, saturate(_Intensity));

            return float4(result, color.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment Frag
            ENDHLSL
        }
    }
}
