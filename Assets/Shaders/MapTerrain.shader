// The maps' terrain, in the kits' own flat colours - no textures, the same look as everything
// standing on it.
//
// Coloured by how steep the ground is: _FlatColour on level ground, _BankColour on a slope,
// _CliffColour on anything near vertical. The colours are set by each map's builder from
// materials that already exist (the jungle kit's grass, dirt and stone; the holiday kit's snow and
// rock) - nothing is made up here. _TopColour takes over above _TopHeight, for snow on peaks.
//
// A plain surface shader on the terrain's material template, so it needs the terrain's
// "Draw Instanced" off - the builders set that.
Shader "Custom/MapTerrain"
{
    Properties
    {
        _FlatColour ("Flat", Color) = (0.31, 0.77, 0.27, 1)
        _BankColour ("Slope", Color) = (0.95, 0.74, 0.62, 1)
        _CliffColour ("Cliff", Color) = (0.8, 0.86, 0.87, 1)
        _TopColour ("Top", Color) = (1, 1, 1, 1)
        _TopHeight ("Top from height", Float) = 10000
        _BankFrom ("Slope starts", Range(0, 1)) = 0.22
        _CliffFrom ("Cliff starts", Range(0, 1)) = 0.55
        _Glossiness ("Smoothness", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _FlatColour;
        fixed4 _BankColour;
        fixed4 _CliffColour;
        fixed4 _TopColour;
        float _TopHeight;
        float _BankFrom;
        float _CliffFrom;
        half _Glossiness;

        struct Input
        {
            float3 worldNormal;
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // 0 on level ground, 1 on a vertical face.
            float steep = 1.0 - saturate(IN.worldNormal.y);

            fixed3 colour = _FlatColour.rgb;
            colour = lerp(colour, _BankColour.rgb, smoothstep(_BankFrom, _BankFrom + 0.1, steep));
            colour = lerp(colour, _CliffColour.rgb, smoothstep(_CliffFrom, _CliffFrom + 0.1, steep));

            // Above the snow line, level-ish ground goes to the top colour; cliffs keep theirs.
            float high = smoothstep(_TopHeight, _TopHeight + 1.5, IN.worldPos.y) * (1.0 - smoothstep(_CliffFrom - 0.1, _CliffFrom + 0.1, steep));
            colour = lerp(colour, _TopColour.rgb, high);

            o.Albedo = colour;
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
