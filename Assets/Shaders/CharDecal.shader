// Red Hot Chili Pepper's char on the world - a multiply decal that actually takes a colour and a
// strength. BulletDecal's own impacts use Legacy Particles/Multiply, which has neither: the tint
// and the fade BulletDecal sets on it are ignored, so a mark is only ever the faint grey of its
// splat texture and pops out at the end instead of fading. Fine for a bullet hole; a char has to
// go properly black and come back over a few seconds.
//
// Drawn after the opaque world (the grass included) with a small depth offset, so blades in front
// of a mark on the ground still hide it.
Shader "Custom/CharDecal"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Color ("Soot", Color) = (0.1, 0.08, 0.07, 1)
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
