// Marks on a body, only where they were made - Red Hot Chili Pepper's char where the flame touched,
// blood where a shot landed.
//
// Drawn as a second pass over the gorilla's own mesh (BodyMarks adds it as an extra material while
// any mark is showing), multiplying what's already there: white leaves the body alone. Which pixels
// change comes from up to MARKS points in world space, each with a radius, a colour and a strength
// that fades - BodyMarks keeps them on the hitbox bone they were made on and re-sends them every
// frame, so a mark stays where it was made as the limb swings.
//
// ZWrite off and a small depth offset: it lands exactly on the body's own depth, never in front.
// Tagged Transparent so the depth-normals pass the outline reads doesn't draw it a second time.
Shader "Custom/BodyMarks"
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

            // Keep in step with BodyMarks.Points.
            #define MARKS 16

            float4 _MarkPoints[MARKS];     // xyz: world position, w: radius
            float4 _MarkColours[MARKS];    // rgb: what the mark multiplies toward
            float _MarkStrength[MARKS];    // 1 fresh, fading to 0

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
                fixed3 result = fixed3(1, 1, 1);

                for (int k = 0; k < MARKS; k++)
                {
                    float radius = max(_MarkPoints[k].w, 0.001);
                    // A solid core, then a soft edge - the point sits on the hitbox, which can be a
                    // little off the mesh, so the core has to reach past it to land on the skin.
                    float reach = smoothstep(0.0, 1.0, saturate(1.35 - distance(i.world, _MarkPoints[k].xyz) / radius));
                    result = min(result, lerp(fixed3(1, 1, 1), _MarkColours[k].rgb, _MarkStrength[k] * reach));
                }

                return fixed4(result, 1);
            }
            ENDCG
        }
    }
}
