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
        _HorizonColor ("Horizon haze", Color) = (0.85, 0.80, 0.32, 1)
        _ZenithColor ("Zenith", Color) = (0.16, 0.46, 0.42, 1)

        [Header(Shape)]
        // How wide the horizon haze band is. Small = a thin bright line where ground meets sky,
        // which is what sells "haze sitting low over a canopy" rather than a smooth blend that
        // reads as generic atmosphere.
        _HazeWidth ("Haze band width", Range(0.02, 0.6)) = 0.18
        // Where the haze band sits relative to the true horizon (y=0). Jungle haze sits low,
        // under the tree line rather than centred on the horizon itself.
        _HazeHeight ("Haze band centre", Range(-0.3, 0.3)) = -0.04

        [Header(Sun)]
        _SunColor ("Sun glow", Color) = (1.0, 0.92, 0.55, 1)
        _SunDirection ("Sun direction", Vector) = (0.35, 0.55, -0.4, 0)
        _SunSize ("Sun glow tightness", Range(4, 256)) = 48
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
            fixed4 _SunColor;
            float3 _SunDirection;
            float _SunSize;

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

                float upperWidth = max(0.05, 0.8 - _HazeHeight);
                float upperT = saturate((dir.y - _HazeHeight) / upperWidth);
                col = lerp(col, _ZenithColor.rgb, smoothstep(0.0, 1.0, upperT));

                // Sun glow: a tight power curve on the dot product, the same trick as a Phong
                // specular highlight, because that's exactly what a glowing disc in the sky is.
                float sunDot = saturate(dot(dir, normalize(_SunDirection)));
                float glow = pow(sunDot, _SunSize);
                col += _SunColor.rgb * glow;

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
