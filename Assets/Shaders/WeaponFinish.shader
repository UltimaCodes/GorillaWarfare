// A weapon skin - a WeaponFinish drawn on the weapon's own model and texture.
//
// Everything here is a way of colouring the model that's already there: paint over its texture
// (keeping the texture's light and dark, so a banana still has its stalk and tip), a two-colour
// pattern from Kenney's Pattern Pack (Assets/Art/Patterns - white where the second colour goes),
// chrome, a glow that can pulse, a hue that turns over time and along the weapon, a light round
// its edges, a glint that runs tip to grip, and a jelly wobble. No texture of its own - "make them
// different colours and make them glow... ill make actual weapon skin models" - see WeaponFinish.
//
// WeaponSkins sets _Axis and _Span per renderer: which way the weapon runs in that mesh's own
// space, and where along it the model starts and how long it is - so a pattern repeats the same
// number of times, and a rainbow runs the same way, along every weapon whatever its size.
Shader "Custom/WeaponFinish"
{
    Properties
    {
        _MainTex ("Weapon texture", 2D) = "white" {}
        _Paint ("Paint", Color) = (1, 1, 1, 1)
        _Repaint ("Repaint (0 keeps the weapon's own colours)", Range(0, 1)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.3

        _PatternTex ("Pattern (white takes the pattern colour)", 2D) = "black" {}
        _PatternColour ("Pattern colour (alpha is how much)", Color) = (0, 0, 0, 0)
        _PatternRepeats ("Pattern repeats along the weapon", Float) = 6
        _PatternScroll ("Pattern scroll, repeats per second", Vector) = (0, 0, 0, 0)
        _PatternGlow ("Pattern glow", Float) = 0

        _GlowColour ("Glow", Color) = (0, 0, 0, 0)
        _GlowStrength ("Glow strength", Float) = 0
        _PulseSpeed ("Pulses per second", Float) = 0
        _PulseDepth ("Pulse depth", Range(0, 1)) = 0

        _HueCycle ("Hue turns per second", Float) = 0
        _HueSpread ("Hue turns along the weapon", Float) = 0

        _RimColour ("Edge light", Color) = (0, 0, 0, 0)
        _RimStrength ("Edge light strength", Float) = 0
        _RimPower ("Edge light tightness", Float) = 3

        _SweepColour ("Glint (alpha is strength)", Color) = (0, 0, 0, 0)
        _SweepEvery ("Glint every n seconds (0 = never)", Float) = 0
        _SweepWidth ("Glint width, along the weapon", Range(0.02, 1)) = 0.15

        _Wobble ("Jelly wobble", Float) = 0
        _WobbleSpeed ("Wobble speed", Float) = 6

        _Axis ("Weapon axis, in the mesh's space", Vector) = (0, 0, 1, 0)
        _Span ("Start and length along it", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _PatternTex;
        fixed4 _Paint;
        half _Repaint;
        half _Metallic;
        half _Glossiness;
        fixed4 _PatternColour;
        float _PatternRepeats;
        float4 _PatternScroll;
        float _PatternGlow;
        fixed4 _GlowColour;
        float _GlowStrength;
        float _PulseSpeed;
        float _PulseDepth;
        float _HueCycle;
        float _HueSpread;
        fixed4 _RimColour;
        float _RimStrength;
        float _RimPower;
        fixed4 _SweepColour;
        float _SweepEvery;
        float _SweepWidth;
        float _Wobble;
        float _WobbleSpeed;
        float4 _Axis;
        float4 _Span;

        struct Input
        {
            float2 uv_MainTex;
            float3 objPos;
            float3 viewDir;
            float3 worldNormal;
        };

        // How far along the weapon a point in the mesh is, 0 at the grip end to 1 at the tip.
        float Along(float3 p)
        {
            return (dot(p, _Axis.xyz) - _Span.x) / max(_Span.y, 1e-4);
        }

        // Turns a colour round the colour wheel, `turns` of the way, keeping its brightness.
        float3 HueShift(float3 c, float turns)
        {
            float a = turns * 6.2831853;
            const float3 k = float3(0.57735, 0.57735, 0.57735);
            float ca = cos(a);
            return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - ca);
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // Jelly: the surface swells and settles in a wave that travels along the weapon.
            if (_Wobble > 0)
            {
                float wave = sin(_Time.y * _WobbleSpeed - Along(v.vertex.xyz) * 9.42);
                v.vertex.xyz += v.normal * wave * _Wobble * _Span.y;
            }

            o.objPos = v.vertex.xyz;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float along = Along(IN.objPos);
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);

            // Paint that keeps the texture's shading: its light parts take the paint, its dark
            // parts (a banana's stalk and tip) stay darker.
            float lum = dot(tex.rgb, float3(0.299, 0.587, 0.114));
            float3 albedo = lerp(tex.rgb, _Paint.rgb * saturate(lum * 1.6 + 0.15), _Repaint);

            // The pattern, laid on three ways and blended by which way each face points, so it
            // wraps round a curved banana without a seam down one side.
            float3 p = IN.objPos / max(_Span.y, 1e-4) * _PatternRepeats;
            // The mesh's own normal, from the world one - one fewer thing to carry from the vertex
            // (shader model 3.0 has room for ten, and this wanted eleven).
            float3 objNormal = mul(normalize(IN.worldNormal), (float3x3)unity_ObjectToWorld);
            float3 w = pow(abs(normalize(objNormal)), 4.0);
            w /= max(w.x + w.y + w.z, 1e-4);
            float2 scroll = _PatternScroll.xy * _Time.y;
            float mask = tex2D(_PatternTex, p.yz + scroll).r * w.x
                       + tex2D(_PatternTex, p.xz + scroll).r * w.y
                       + tex2D(_PatternTex, p.xy + scroll).r * w.z;
            mask *= _PatternColour.a;
            albedo = lerp(albedo, _PatternColour.rgb, mask);

            float hue = _Time.y * _HueCycle + along * _HueSpread;
            albedo = saturate(HueShift(albedo, hue));

            float pulse = 1.0 - _PulseDepth * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed * 6.2831853));
            float3 glow = HueShift(_GlowColour.rgb, hue) * _GlowStrength * pulse;
            glow += HueShift(_PatternColour.rgb, hue) * mask * _PatternGlow * pulse;

            float rim = pow(1.0 - saturate(dot(normalize(IN.worldNormal), normalize(IN.viewDir))), _RimPower);
            glow += HueShift(_RimColour.rgb, hue) * rim * _RimStrength;

            // The glint: a bright band that runs from the grip past the tip every so often.
            if (_SweepEvery > 0)
            {
                float at = frac(_Time.y / _SweepEvery) * 1.6 - 0.3;
                float band = 1.0 - saturate(abs(along - at) / _SweepWidth);
                glow += _SweepColour.rgb * band * band * _SweepColour.a * 3.0;
            }

            o.Albedo = albedo;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = max(glow, 0);
            o.Alpha = 1;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
