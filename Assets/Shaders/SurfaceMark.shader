// A mark on the world - a bullet hole, or Red Hot Chili Pepper's char - as a multiply decal that
// actually takes a colour and a strength. BulletDecal used Legacy Particles/Multiply for this, which
// has neither: the tint and the fade set on it were ignored, and on a surface it all but vanished -
// reported as "bullet marks still dont work", and a close-up of a fresh one on a wall showed nothing
// at all. The chili's char was already on this shader and worked, so impacts moved over too.
//
// Drawn after the opaque world (the grass included) with a small depth offset, so blades in front
// of a mark on the ground still hide it.
Shader "Custom/SurfaceMark"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Colour", Color) = (0.1, 0.08, 0.07, 1)
        _Strength ("Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+60" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Strength;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float amount = saturate(tex2D(_MainTex, i.uv).a * 1.4) * _Strength;
                return fixed4(lerp(fixed3(1, 1, 1), _Color.rgb, amount), 1);
            }
            ENDCG
        }
    }
}
