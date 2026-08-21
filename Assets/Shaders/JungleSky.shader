// A stylised jungle canopy sky - three flat-ish colour bands (ground, horizon haze, zenith)
// rather than a smooth realistic gradient, since everything else in this game is flat, bold and
// a little cartoonish (the map's own placeholder ground and the toon outlines built the same day
// as this). A photographic sky would have looked like it came from a different game.
//
// Built as a skybox shader rather than a cubemap because there's no source photography to build
// a cubemap from, and this is a handful of colours and a curve - a shader is both less work and
// smaller than six baked faces would be. Structure follows Unity's own built-in Procedural
// skybox: the vertex position IS the view direction (a skybox mesh is a cube/sphere centred on
// the camera), so the fragment shader just normalises it and reads .y for height.
Shader "Skybox/JungleSky"
{
    Properties
    {
        [Header(Bands)]
        _GroundColor ("Ground (below horizon)", Color) = (0.05, 0.16, 0.07, 1)
        // Pulled down from (0.85, 0.80, 0.32) - reported as too strong a yellow. Both dimmer and
        // less saturated (raised the blue channel), so it reads as a warm haze rather than a
        // block of vivid yellow paint across the middle of the sky.
        _HorizonColor ("Horizon haze", Color) = (0.70, 0.66, 0.40, 1)
        _ZenithColor ("Zenith", Color) = (0.09, 0.34, 0.24, 1)

        [Header(Shape)]
        // How wide the horizon haze band is. Small = a thin bright line where ground meets sky,
        // which is what sells "haze sitting low over a canopy" rather than a smooth blend that
        // reads as generic atmosphere.
        _HazeWidth ("Haze band width", Range(0.02, 0.6)) = 0.18
        // Where the haze band sits relative to the true horizon (y=0). Jungle haze sits low,
        // under the tree line rather than centred on the horizon itself.
        _HazeHeight ("Haze band centre", Range(-0.3, 0.3)) = -0.04
        // How far above the haze band the green canopy colour holds before giving way to open
        // sky at the zenith. Wide on purpose (retuned 2026-08-22, reference was a dense canopy
        // view, not open sky with a thin tree line at the bottom) - green is most of the dome
        // now, sky is what's left at the very top rather than the other way round.
        _CanopyReach ("Canopy reach above haze", Range(0.2, 1.2)) = 0.85

        [Header(Sun)]
        _SunColor ("Sun glow", Color) = (1.0, 0.92, 0.55, 1)
        _SunDirection ("Sun direction", Vector) = (0.35, 0.55, -0.4, 0)
        _SunSize ("Sun glow tightness", Range(4, 256)) = 48

        [Header(God rays)]
        // Retuned 2026-08-22 - the reference was full of visible light shafts breaking through
        // canopy, which a single sun glow doesn't sell on its own. Procedural rather than a
        // texture: several sine waves of a per-fragment angle around the sun axis, added
        // together at different frequencies so the streaks land irregular rather than a perfect
        // pinwheel, which is what would give away that it's a formula.
        _RayColor ("Ray colour", Color) = (1.0, 0.88, 0.55, 1)
        // Lowered from 0.55, and falloff raised from 3.5, on the same day - reported back as
        // "too much in your face" and not reading as jungle atmosphere. Both numbers push the
        // same direction: weaker rays, and confined much closer to the sun itself instead of
        // fanning out across most of the sky.
        _RayIntensity ("Ray strength", Range(0, 1)) = 0.28
        _RayFalloff ("Ray falloff from sun", Range(1, 12)) = 6.5
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

            fixed4 _GroundColor;
            fixed4 _HorizonColor;
            fixed4 _ZenithColor;
            float _HazeWidth;
            float _HazeHeight;
            float _CanopyReach;
            fixed4 _SunColor;
            float3 _SunDirection;
            float _SunSize;
            fixed4 _RayColor;
            float _RayIntensity;
            float _RayFalloff;

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
                // Object-space position doubles as the view direction on a skybox mesh - the
                // mesh is a unit cube/sphere and the camera always sits at its centre.
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Not renormalised further than this. i.dir is linearly interpolated across a
                // low-poly skybox cube, so it's only an approximation of the true per-fragment
                // direction near a face's edges - fine for a gentle gradient, but the first
                // version of this shader ran two independently-centred smoothsteps whose active
                // ranges overlapped right at the horizon, and that overlap was sensitive enough
                // to the interpolation error to show up as a visible dark seam across the cube's
                // face boundary. Rewritten as one continuous three-stop gradient (ground, haze,
                // zenith) with no second pass layered back on top, which is the same shape
                // Unity's own built-in gradient skybox uses and doesn't have that failure mode.
                float3 dir = normalize(i.dir);

                float lowerT = saturate((dir.y - (_HazeHeight - _HazeWidth)) / _HazeWidth);
                fixed3 col = lerp(_GroundColor.rgb, _HorizonColor.rgb, smoothstep(0.0, 1.0, lowerT));

                // Canopy green now carries most of the dome - _CanopyReach is wide by default,
                // so this second blend doesn't finish (and let open sky take over) until well
                // above the horizon, rather than a thin green strip under a mostly-blue sky.
                float upperT = saturate((dir.y - _HazeHeight) / max(0.05, _CanopyReach));
                col = lerp(col, _ZenithColor.rgb, smoothstep(0.0, 1.0, upperT));

                float3 sunAxis = normalize(_SunDirection);
                float sunDot = saturate(dot(dir, sunAxis));

                // God rays: a per-fragment angle measured around the sun axis (project dir onto
                // the plane perpendicular to it, atan2 the result), run through a few sine waves
                // at different frequencies and phases so the streaks land irregular rather than
                // a perfect pinwheel. Masked by both distance from the sun (rays fan out near it,
                // not opposite it) and height (canopy reads as blocking them low in the sky, so
                // they fade out approaching the haze band rather than cutting into the ground).
                float3 arbitraryUp = abs(sunAxis.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 tangent = normalize(cross(arbitraryUp, sunAxis));
                float3 bitangent = cross(sunAxis, tangent);
                float rayAngle = atan2(dot(dir, bitangent), dot(dir, tangent));

                float rays = sin(rayAngle * 9.0 + 1.3) * 0.5 + 0.5;
                rays += sin(rayAngle * 17.0 + 4.1) * 0.3;
                rays += sin(rayAngle * 5.0 + 2.7) * 0.2;
                rays = saturate(rays * 0.55);

                float rayHeightMask = saturate((dir.y - _HazeHeight) / 0.35);
                float rayMask = pow(sunDot, _RayFalloff) * rayHeightMask;

                col = lerp(col, _RayColor.rgb, rays * rayMask * _RayIntensity);

                // Sun glow: two layers rather than one, a tight hot core plus a much wider, softer
                // halo - a single tight power curve read as a small clean disc, closer to a clear
                // sky than to sunlight diffusing through humid, leaf-filtered air.
                float core = pow(sunDot, _SunSize);
                float halo = pow(sunDot, max(1.0, _SunSize * 0.06)) * 0.35;
                col += _SunColor.rgb * (core + halo);

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
