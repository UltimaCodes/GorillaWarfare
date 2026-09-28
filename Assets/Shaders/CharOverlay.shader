// Red Hot Chili Pepper's char on a body, only where the flame touched it.
//
// Drawn as a second pass over the gorilla's own mesh (BodyChar adds it as an extra material while
// anything is charred), multiplying what's already there: white leaves the body alone, soot
// darkens it. Which pixels go dark comes from up to CHAR_POINTS points in world space, each with
// a radius and a strength that fades - BodyChar keeps them on the hitbox bone the flame touched
// and re-sends them every frame, so a burnt arm stays burnt where it was hit as the arm swings.
//
// ZWrite off and a small depth offset: it lands exactly on the body's own depth, never in front.
// Tagged Transparent so the depth-normals pass the outline reads doesn't draw it a second time.
Shader "Custom/CharOverlay"
{
    SubShader
    {
        Tags { "Queue" = "Geometry+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // Keep in step with BodyChar.Points.
            #define CHAR_POINTS 16

            float4 _CharPoints[CHAR_POINTS];    // xyz: world position, w: radius
            float _CharStrength[CHAR_POINTS];   // 1 fresh, fading to 0

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float burnt = 0;

                for (int k = 0; k < CHAR_POINTS; k++)
                {
                    float radius = max(_CharPoints[k].w, 0.001);
                    // A solid core, then a soft edge - the point sits on the hitbox, which can be a
                    // little off the mesh, so the core has to reach past it to land on the skin.
                    float reach = smoothstep(0.0, 1.0, saturate(1.35 - distance(i.world, _CharPoints[k].xyz) / radius));
                    burnt = max(burnt, _CharStrength[k] * reach);
                }

                // Smooth, not speckled: a hash of the mesh position rendered as blocky pixel noise
                // at this scale, which read as a glitch rather than soot.

                fixed3 soot = fixed3(0.09, 0.075, 0.065);
                return fixed4(lerp(fixed3(1, 1, 1), soot, burnt), 1);
            }
            ENDCG
        }
    }
}
