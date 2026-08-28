// A soft, painterly daytime sky - a smooth blue gradient, wispy streaked cloud cover built from
// layered noise, and a glowing sun with a soft halo. Replaced the flat-banded jungle-canopy sky
// entirely 2026-08-23, direct request against a reference screenshot: this one reads as an open
// sky over the map rather than a canopy pressing down on it, which is a different mood on
// purpose - kept the file/shader name so nothing that already points at "Skybox/JungleSky"
// (SkyboxPhotographer.cs, the JungleSky.mat asset) needed to change.
//
// No source photography or texture to build a cubemap from, same reasoning the original had -
// this is entirely procedural, a gradient plus hand-rolled value noise, built as a skybox shader
// the same way: the vertex position on a skybox mesh (a cube centred on the camera) IS the view
// direction, so the fragment shader just normalises it.
Shader "Skybox/JungleSky"
{
    Properties
    {
        [Header(Sky gradient)]
        _ZenithColor ("Zenith (straight up)", Color) = (0.10, 0.30, 0.62, 1)
        _SkyColor ("Mid sky", Color) = (0.30, 0.52, 0.80, 1)
        _HorizonColor ("Horizon", Color) = (0.78, 0.85, 0.90, 1)
        // Where the gradient finishes resolving to zenith, as a fraction of the way up from the
        // horizon. Low = a thin bright band low in the sky, most of the dome reads as deep blue -
        // which is what a clear reference photo actually looks like, rather than a smooth blend
        // that fades all the way to the top.
        [Range(0.15, 1.0)] _GradientHeight ("Gradient height", Float) = 0.55

        [Header(Clouds)]
        _CloudColor ("Cloud colour, shadowed side", Color) = (0.72, 0.76, 0.84, 1)
        _CloudColorLit ("Cloud colour, sun side", Color) = (1.0, 0.98, 0.92, 1)
        // How large one cloud mass reads, in world-direction units - small numbers make bigger,
        // slower-changing shapes, since this samples noise directly on the unit view direction.
        [Range(0.4, 6.0)] _CloudScale ("Cloud scale", Float) = 1.6
        // Squashes the noise sample along one axis before reading it, which is what turns round
        // blobs into the long wispy streaks a real high sky has - a plain FBM alone reads as
        // cauliflower, not cirrus.
        [Range(1.0, 8.0)] _CloudStretch ("Cloud streak stretch", Float) = 3.5
        [Range(0.0, 1.0)] _CloudCoverage ("Cloud coverage", Float) = 0.42
        [Range(0.02, 0.5)] _CloudSoftness ("Cloud edge softness", Float) = 0.16
        [Range(0.0, 1.0)] _CloudOpacity ("Cloud opacity", Float) = 0.85
        // How much a second, smaller-scale noise layer bends the sample coordinates before the
        // main cloud shapes read it - the wispy, slightly turbulent quality real cloud streaks
        // have that a single noise octave never quite gets on its own.
        [Range(0.0, 1.0)] _CloudWarp ("Cloud warp strength", Float) = 0.35
        // Degrees per second the cloud layer drifts. Slow on purpose - the sky should read as
        // alive without anyone standing still long enough to actually watch it move.
        _CloudDriftSpeed ("Cloud drift speed", Float) = 0.6
        _CloudDriftAxis ("Cloud drift axis", Vector) = (1, 0, 0.3, 0)

        [Header(Sun)]
        _SunColor ("Sun glow", Color) = (1.0, 0.97, 0.85, 1)
        _SunDirection ("Sun direction", Vector) = (0.35, 0.55, -0.4, 0)
        _SunSize ("Sun core tightness", Range(4, 1024)) = 420
        // Retuned 2026-08-23 after actually rendering it - the first pass (10) put a halo big
        // enough to wash out most of the frame at anything near the sun, which reads as an
        // overexposed photo rather than a glowing disc in a blue sky. Tighter and dimmer both.
        [Range(1.0, 64.0)] _SunHaloSize ("Sun halo tightness", Float) = 26
        [Range(0.0, 2.0)] _SunHaloIntensity ("Sun halo strength", Float) = 0.32
        // A wide, gentle brightening of the sky itself on the sun's side of the dome - real
        // haze scatters light well beyond the disc itself, and without this the sun reads as a
        // bright sticker pasted onto an otherwise indifferent sky. Retuned alongside the halo,
        // same reason - was contributing to the same washed-out look.
        [Range(0.0, 1.0)] _SunScatter ("Sky brightening near sun", Float) = 0.16
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _ZenithColor;
            fixed4 _SkyColor;
            fixed4 _HorizonColor;
            float _GradientHeight;

            fixed4 _CloudColor;
            fixed4 _CloudColorLit;
            float _CloudScale;
            float _CloudStretch;
            float _CloudCoverage;
            float _CloudSoftness;
            float _CloudOpacity;
            float _CloudWarp;
            float _CloudDriftSpeed;
            float3 _CloudDriftAxis;

            fixed4 _SunColor;
            float3 _SunDirection;
            float _SunSize;
            float _SunHaloSize;
            float _SunHaloIntensity;
            float _SunScatter;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // Hash-based value noise, not a texture lookup - there's nothing to source a noise
            // texture from and this is cheap enough for a background pass. Standard technique:
            // a pseudo-random hash per lattice cell, smoothstep-interpolated between corners.
            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float valueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0, 0, 0));
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            // Four octaves, unrolled rather than a loop - old CG target hardware handles a fixed
            // unroll far more reliably than a variable-trip-count for loop in a fragment shader.
            float cloudFbm(float3 p)
            {
                float sum = 0.0;
                float amp = 0.5;

                sum += valueNoise(p) * amp; p *= 2.02; amp *= 0.5;
                sum += valueNoise(p) * amp; p *= 2.03; amp *= 0.5;
                sum += valueNoise(p) * amp; p *= 2.01; amp *= 0.5;
                sum += valueNoise(p) * amp;

                return sum;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // Smooth three-stop gradient rather than a hard band boundary - a photographic
                // sky's colour change is continuous, not stepped.
                float t = saturate(dir.y / max(0.05, _GradientHeight));
                fixed3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, smoothstep(0.0, 0.5, t));
                sky = lerp(sky, _ZenithColor.rgb, smoothstep(0.4, 1.0, t));

                float3 sunAxis = normalize(_SunDirection);
                float sunDot = saturate(dot(dir, sunAxis));

                // Wide scatter glow first, underneath the clouds - real haze brightens the sky
                // itself near the sun, and clouds sitting on top of that read as lit from one
                // side rather than pasted onto a flat backdrop.
                float scatter = pow(sunDot, 2.0) * _SunScatter;
                sky = lerp(sky, _SunColor.rgb, scatter * 0.5);

                // Only above the horizon-ish band, thinning out near the true zenith - clouds
                // don't cover the whole dome edge to edge in the reference, they sit as a mid-sky
                // layer.
                float cloudBand = saturate(dir.y * 3.0) * saturate(1.4 - dir.y);

                if (cloudBand > 0.001)
                {
                    float3 drift = normalize(_CloudDriftAxis) * _Time.y * _CloudDriftSpeed * 0.02;

                    // The streak: squash the sample hard along one axis before reading it, so a
                    // round noise blob comes out elongated - this is what makes it read as wind-
                    // blown cirrus rather than round cauliflower cloud.
                    float3 stretched = dir * float3(_CloudStretch, 1.0, 1.0);
                    float3 samplePos = stretched * _CloudScale + drift;

                    // Domain warp: bend the sample position with a coarser, independent noise
                    // field before the main shapes read it. Cheap and it's most of what makes
                    // procedural clouds stop looking like a single obvious noise function.
                    float3 warp = float3(
                        valueNoise(samplePos * 0.6 + 11.0),
                        valueNoise(samplePos * 0.6 + 27.0),
                        valueNoise(samplePos * 0.6 + 53.0)) - 0.5;
                    samplePos += warp * _CloudWarp;

                    float n = cloudFbm(samplePos);

                    float coverage = smoothstep(
                        1.0 - _CloudCoverage - _CloudSoftness,
                        1.0 - _CloudCoverage + _CloudSoftness,
                        n);

                    coverage *= cloudBand * _CloudOpacity;

                    // Tinted by how close this fragment is to the sun, same idea as scatter above
                    // but per-cloud rather than sky-wide - the edge of a cloud facing the sun
                    // reads brighter than its shadowed side, which is most of what sells depth on
                    // an otherwise flat-shaded shape.
                    fixed3 cloudTint = lerp(_CloudColor.rgb, _CloudColorLit.rgb, pow(sunDot, 1.5));
                    sky = lerp(sky, cloudTint, coverage);
                }

                // Sun disc: a tight hot core plus a much softer, wider halo - one power curve
                // alone reads as a flat clean coin, two together is what makes it glow instead of
                // just being bright.
                float core = pow(sunDot, _SunSize);
                float halo = pow(sunDot, max(1.0, _SunHaloSize)) * _SunHaloIntensity;
                sky += _SunColor.rgb * (core + halo);

                return fixed4(sky, 1);
            }
            ENDCG
        }
    }
}
