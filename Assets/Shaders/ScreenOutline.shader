// A screen-space outline: sample the camera's depth+normal buffer at the current pixel and its
// four neighbours, draw a line wherever depth or surface normal jumps sharply between them.
// Chosen over expanding mesh geometry (Custom/ToonOutline, tried first) because it doesn't care
// about mesh topology at all - it can't produce a gap, because it isn't stitching together
// per-vertex pushes that can disagree with each other in the first place. That's the actual
// reason professional toon-shaded games use this technique instead of an inverted hull: a
// complex character mesh (fingers, overlapping limbs, tight joints) is exactly where the mesh
// version tears, and this doesn't have that failure mode by construction.
Shader "Custom/ScreenOutline"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _OutlineColor ("Outline colour", Color) = (0.04, 0.04, 0.04, 1)
        _DepthSensitivity ("Depth sensitivity", Range(0.1, 10)) = 2.5
        _NormalSensitivity ("Normal sensitivity", Range(0.1, 10)) = 3.0
        _Thickness ("Thickness (pixels)", Range(0.5, 4)) = 1.5
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthNormalsTexture;

            fixed4 _OutlineColor;
            float _DepthSensitivity;
            float _NormalSensitivity;
            float _Thickness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            void SampleDepthNormal(float2 uv, out float depth01, out float3 normal)
            {
                float4 packed = tex2D(_CameraDepthNormalsTexture, uv);
                DecodeDepthNormal(packed, depth01, normal);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 sceneCol = tex2D(_MainTex, i.uv);

                float2 texel = _MainTex_TexelSize.xy * _Thickness;

                float d0, dR, dU, dL, dD;
                float3 n0, nR, nU, nL, nD;
                SampleDepthNormal(i.uv, d0, n0);
                SampleDepthNormal(i.uv + float2(texel.x, 0), dR, nR);
                SampleDepthNormal(i.uv + float2(0, texel.y), dU, nU);
                SampleDepthNormal(i.uv - float2(texel.x, 0), dL, nL);
                SampleDepthNormal(i.uv - float2(0, texel.y), dD, nD);

                // DecodeDepthNormal's depth is 0-1 and non-linear (more precision near the
                // camera). Linear01Depth straightens that out, and dividing by the centre
                // sample's own depth makes the threshold proportional rather than absolute - a
                // silhouette edge a metre wide at ten metres away and one at a hundred metres
                // encode to very different raw depth gaps, so a fixed threshold would either
                // miss every distant edge or fire on every nearby surface's own curvature.
                float lin0 = Linear01Depth(d0);
                float linR = Linear01Depth(dR);
                float linU = Linear01Depth(dU);
                float linL = Linear01Depth(dL);
                float linD = Linear01Depth(dD);

                float depthDiff = abs(lin0 - linR) + abs(lin0 - linU)
                                 + abs(lin0 - linL) + abs(lin0 - linD);
                float depthEdge = saturate(depthDiff / max(0.0005, lin0) * _DepthSensitivity);

                // Normal edges catch what depth alone misses - two faces meeting at a hard angle
                // with no actual depth gap between them (a box's corner, a joint's crease). This
                // is also what gives outlines to the interior detail lines in the reference image
                // (Suzanne's brow, ears), not just the outer silhouette.
                float normalDiff = (1 - dot(n0, nR)) + (1 - dot(n0, nU))
                                  + (1 - dot(n0, nL)) + (1 - dot(n0, nD));
                float normalEdge = saturate(normalDiff * _NormalSensitivity);

                float edge = max(depthEdge, normalEdge);

                return lerp(sceneCol, _OutlineColor, edge);
            }
            ENDCG
        }
    }
}
