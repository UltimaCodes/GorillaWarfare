// Plain, opaque, unlit, vertex-coloured - for the vine's LineRenderer, which used to run on
// Sprites/Default. That works fine for colour but is tagged as a transparent/sprite render type,
// which Unity's depth+normals prepass skips - the vine never showed up in ScreenOutline's edge
// detection because of it, reported back as "make the vine have the toon shader too" once
// everything else already did. Explicit RenderType=Opaque here is the entire fix: same flat
// unlit colour the vine already had, just actually written into the buffer the outline reads.
Shader "Custom/UnlitVertexColor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}
