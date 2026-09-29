// Marks on a body, only where they were made - Red Hot Chili Pepper's char where the flame touched,
// and where a shot landed, a bullet hole (gore off) or a wound (gore on).
//
// Drawn as a second pass over the gorilla's own mesh (BodyMarks adds it as an extra material while
// any mark is showing), multiplying what's already there: white leaves the body alone. The marks
// come from up to MARKS points in world space - BodyMarks keeps each on the hitbox bone it was made
// on and re-sends them every frame, so a mark stays where it was made as the limb swings.
//
// Two shapes:
// - Soft: a round patch that fades at the edge - the flame's char.
// - Hole: the bullet-hole splat the walls get (_MarkShape, BulletDecal's own texture), projected
//   onto the body along the direction the shot came in, only within a few centimetres of the
//   surface so it doesn't come out the far side of an arm. Wound is the same with a near-black
//   core, for gore.
//
// ZWrite off and a small depth offset: it lands exactly on the body's own depth, never in front.
// Tagged Transparent so the depth-normals pass the outline reads doesn't draw it a second time.
Shader "Custom/BodyMarks"
{
    Properties
    {
        _MarkShape ("Hole shape", 2D) = "white" {}
    }

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
            #define MARKS 24

            sampler2D _MarkShape;

            float4 _MarkPoints[MARKS];     // xyz: world position, w: radius
            float4 _MarkColours[MARKS];    // rgb: what the mark multiplies toward, a: shape (0 soft, 1 hole, 2 wound)
            float4 _MarkNormals[MARKS];    // xyz: out of the surface, w: spin in radians
            float _MarkStrength[MARKS];    // 1 fresh, fading to 0

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 result = fixed3(1, 1, 1);

                for (int k = 0; k < MARKS; k++)
                {
                    float s = _MarkStrength[k];
                    if (s <= 0)
                        continue;

                    float radius = max(_MarkPoints[k].w, 0.001);
                    float3 away = i.world - _MarkPoints[k].xyz;
                    float shape = _MarkColours[k].a;
                    fixed3 colour = _MarkColours[k].rgb;

                    if (shape < 0.5)
                    {
                        // Soft: a solid core, then a soft edge - the point sits on the hitbox, which
                        // can be a little off the mesh, so the core reaches past it onto the skin.
                        float reach = smoothstep(0.0, 1.0, saturate(1.35 - length(away) / radius));
                        result = min(result, lerp(fixed3(1, 1, 1), colour, s * reach));
                        continue;
                    }

                    // Hole or wound: the wall's splat, laid on the plane facing out of the hit.
                    float3 n = normalize(_MarkNormals[k].xyz);
                    float3 t = normalize(cross(n, abs(n.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
                    float3 b = cross(n, t);

                    float spin = _MarkNormals[k].w;
                    float2 flat = float2(dot(away, t), dot(away, b)) / (radius * 2.0);
                    float2 uv = float2(flat.x * cos(spin) - flat.y * sin(spin),
                                       flat.x * sin(spin) + flat.y * cos(spin)) + 0.5;

                    // The mark sits on the hitbox and the skin doesn't: measured 1.5 to 7cm inside it,
                    // so the hole reaches well in and a little out. A first window of 12cm either
                    // side turned a hole 7cm off the skin into a 40% smudge.
                    float along = dot(away, n);
                    float depth = saturate((along + 0.3) / 0.08) * saturate((0.15 - along) / 0.05);

                    // And only skin facing the shot - so a hole in the front of an arm doesn't also
                    // turn up on the back of it, inside that window.
                    float facingShot = saturate((dot(normalize(i.normal), n) - 0.05) * 4.0);

                    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0) * facingShot;

                    // lod, not tex2D: inside a loop with a branch, a sample that needs derivatives is
                    // a compile error waiting to happen, and the splat has no mips anyway.
                    float density = tex2Dlod(_MarkShape, float4(saturate(uv), 0, 0)).a * inside * depth;
                    float amount = saturate(density * 1.4) * s;

                    // A wound is darkest at its heart - nearly black in the middle of the red.
                    fixed3 tone = shape > 1.5 ? lerp(colour, colour * 0.15, saturate(density * density * density * 1.6)) : colour;
                    result = min(result, lerp(fixed3(1, 1, 1), tone, amount));
                }

                return fixed4(result, 1);
            }
            ENDCG
        }
    }
}
