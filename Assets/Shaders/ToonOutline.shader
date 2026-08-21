// A solid-colour silhouette line, the standard inverted-hull trick: render the mesh's
// back faces, pushed outward along their own normals, in a flat unlit colour, with the real
// mesh drawn normally on top of it. Where the real mesh covers the pushed-out shell, the shell
// is invisible; where it doesn't - right at the silhouette edge - the shell shows through as an
// outline, because it was drawn first and the real mesh's depth write covers it everywhere else.
//
// Added as an *extra* material on an existing renderer rather than replacing that renderer's own
// shader - Unity renders one pass per material slot beyond the mesh's own submesh count, so
// appending one more material here is the entire mechanism, no render feature or extra camera
// needed the way URP would want. Deliberately never touches the object's own material, which
// still does its own real lighting and (for the gorillas) ripeness/team colour work untouched.
Shader "Custom/ToonOutline"
{
    Properties
    {
        _OutlineColor ("Outline colour", Color) = (0.05, 0.05, 0.05, 1)
        _OutlineWidth ("Outline width (object space)", Range(0.0, 0.15)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-1" }

        Pass
        {
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;

                // Object-space push rather than clip-space/view-space compensation for constant
                // screen size - simpler, and everything this is used on (a thrown pineapple, a
                // player-sized gorilla) sits in a narrow enough range of camera distances during
                // actual play that a fixed width reads fine. Worth revisiting only if this ever
                // gets used on something seen from very close and very far in the same match.
                float3 expanded = v.vertex.xyz + normalize(v.normal) * _OutlineWidth;
                o.pos = UnityObjectToClipPos(float4(expanded, 1.0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
}
