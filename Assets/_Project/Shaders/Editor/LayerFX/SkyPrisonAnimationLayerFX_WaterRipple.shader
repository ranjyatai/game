Shader "SkyPrison/Animation Layer FX/Water Ripple"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.35
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.12

        _DeepColor ("Deep Color", Color) = (0.005, 0.025, 0.070, 1.0)
        _BlueColor ("Blue Color", Color) = (0.010, 0.220, 0.620, 1.0)
        _AquaColor ("Aqua Color", Color) = (0.000, 0.850, 1.000, 1.0)
        _WhiteColor ("Highlight Color", Color) = (0.900, 1.000, 1.000, 1.0)

        _MainScale ("Main Scale", Range(0.2,8.0)) = 1.0
        _WaveStrength ("Wave Strength", Range(0,3.0)) = 1.0
        _CurrentStrength ("Current Strength", Range(0,1.0)) = 0.18
        _CurrentSpeed ("Current Speed", Range(-5,5)) = 1.0

        _RefractionStrength ("Refraction Strength", Range(0,0.12)) = 0.045
        _CausticStrength ("Caustic Strength", Range(0,4.0)) = 1.0
        _FoamStrength ("Foam Strength", Range(0,2.0)) = 1.0
        _RippleLineStrength ("Ripple Line Strength", Range(0,2.0)) = 1.0
        _SpecularStrength ("Specular Strength", Range(0,2.0)) = 1.0
        _LightRayStrength ("Light Ray Strength", Range(0,2.0)) = 1.0

        _ChromaticDispersion ("Chromatic Dispersion", Range(0,0.03)) = 0.006
        _RadialDepthFade ("Radial Depth Fade", Range(0,2.0)) = 1.0
        _VignetteStrength ("Vignette Strength", Range(0,1.0)) = 0.75
        _NoiseAmount ("Noise Amount", Range(0,1.0)) = 0.01

        _InteractionRippleStrength ("Interaction Ripple Strength", Range(0,2.0)) = 0.0
        _InteractionCenter ("Interaction Center", Vector) = (0.5, 0.5, 0, 0)
        _InteractionRadius ("Interaction Radius", Range(0.1,20.0)) = 7.0
        _InteractionFrequency ("Interaction Frequency", Range(1.0,120.0)) = 55.0
        _InteractionSpeed ("Interaction Speed", Range(-20.0,20.0)) = 5.0

        _AlphaMode ("Alpha Keep/Effect", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _SkyPrisonTime;
            float _PreviewTime;

            float _EffectOpacity;
            float _BaseMix;
            float _BrightnessGain;

            float4 _DeepColor;
            float4 _BlueColor;
            float4 _AquaColor;
            float4 _WhiteColor;

            float _MainScale;
            float _WaveStrength;
            float _CurrentStrength;
            float _CurrentSpeed;

            float _RefractionStrength;
            float _CausticStrength;
            float _FoamStrength;
            float _RippleLineStrength;
            float _SpecularStrength;
            float _LightRayStrength;

            float _ChromaticDispersion;
            float _RadialDepthFade;
            float _VignetteStrength;
            float _NoiseAmount;

            float _InteractionRippleStrength;
            float4 _InteractionCenter;
            float _InteractionRadius;
            float _InteractionFrequency;
            float _InteractionSpeed;

            float _AlphaMode;

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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float3 Palette(float x)
            {
                float3 c = lerp(_DeepColor.rgb, _BlueColor.rgb, smoothstep(0.02, 0.45, x));
                c = lerp(c, _AquaColor.rgb, smoothstep(0.30, 0.90, x));
                c = lerp(c, _WhiteColor.rgb, smoothstep(0.82, 1.45, x));
                return c;
            }

            float WaveField(float2 p, float t)
            {
                float v = 0.0;

                p += _CurrentStrength * float2(
                    sin(p.y * 2.2 + t * 0.35) + sin(p.y * 5.1 - t * 0.22),
                    cos(p.x * 2.0 - t * 0.31) + cos(p.x * 4.7 + t * 0.27)
                );

                v += sin(p.x * 7.0  + t * 0.9);
                v += sin(p.y * 9.0  - t * 1.1);
                v += sin((p.x + p.y) * 6.0 + t * 0.75);
                v += sin((p.x - p.y) * 8.5 - t * 0.85);
                v += sin(length(p) * 15.0 - t * 1.7);

                float2 q = p;
                q += 0.22 * float2(
                    sin(q.y * 6.0 + t * 0.8),
                    cos(q.x * 6.0 - t * 0.9)
                );

                v += sin(q.x * 17.0 + sin(q.y * 3.0 + t) * 2.5);
                v += cos(q.y * 19.0 + cos(q.x * 3.5 - t) * 2.5);
                v += sin((q.x + q.y) * 25.0 + sin(t + q.x * 2.0) * 3.0) * 0.45;

                return (v / 8.45) * _WaveStrength;
            }

            float InteractionRipple(float2 uv, float t)
            {
                float2 m = _InteractionCenter.xy * 2.0 - 1.0;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                m.x *= aspect;
                float d = length(uv - m);
                float ripple = sin(d * _InteractionFrequency - t * _InteractionSpeed) * exp(-d * _InteractionRadius);
                return ripple * _InteractionRippleStrength;
            }

            fixed4 SampleSprite(float2 uv)
            {
                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);
                fixed4 c = tex2D(_MainTex, saturate(uv)) * _Color;
                c.a *= inside;
                return c;
            }

            float3 WaterSample(float2 uv, float t, float offset)
            {
                float2 p = uv;

                p += 0.09 * float2(
                    sin(t * 0.25 + uv.y * 1.8),
                    cos(t * 0.22 + uv.x * 1.7)
                );

                const float e = 0.0035;
                float h  = WaveField(p + offset, t);
                h += InteractionRipple(uv, t) * 0.18;

                float hx = WaveField(p + float2(e, 0.0) + offset, t);
                float hy = WaveField(p + float2(0.0, e) + offset, t);

                float2 grad = float2(hx - h, hy - h) / e;
                float2 q = p + grad * _RefractionStrength;

                float w1 = WaveField(q, t);
                float w2 = WaveField(q * 1.75 + float2(0.2, -0.1), t * 1.25);
                float w3 = WaveField(q * 3.1  - float2(0.1, 0.3), t * 0.78);
                float w4 = WaveField(q * 5.0  + float2(0.4, 0.2), t * 1.65);

                float caustic =
                    pow(abs(w1), 3.0) +
                    pow(abs(w2), 5.5) * 0.75 +
                    pow(abs(w3), 8.0) * 0.38 +
                    pow(abs(w4), 11.0) * 0.20;
                caustic *= _CausticStrength;

                float gradMag = length(grad);
                float foam = smoothstep(2.0, 8.0, gradMag) * 0.38 * _FoamStrength;

                float rippleLines =
                    smoothstep(0.72, 0.97, abs(sin(w1 * 9.0  + t * 0.45))) * 0.22 +
                    smoothstep(0.80, 0.99, abs(sin(w2 * 12.0 - t * 0.35))) * 0.16 +
                    smoothstep(0.88, 1.00, abs(sin(w3 * 16.0 + t * 0.25))) * 0.10;
                rippleLines *= _RippleLineStrength;

                float depth = smoothstep(1.45, 0.05, length(uv));
                depth = lerp(1.0, depth, _RadialDepthFade);
                float light = caustic * 0.9 + rippleLines + foam + depth * 0.16;

                float3 col = Palette(light);

                float3 n = normalize(float3(-grad * 0.12, 1.0));
                float3 l = normalize(float3(0.35, 0.55, 1.0));
                float spec = pow(max(0.0, dot(n, l)), 24.0) * 0.35 * _SpecularStrength;

                col += float3(0.55, 0.85, 1.0) * spec;
                col *= 0.72 + 0.28 * depth;

                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y) * _CurrentSpeed;

                float2 uv = i.uv;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 p = (uv * 2.0 - 1.0);
                p.x *= aspect;
                p *= _MainScale;

                fixed4 baseCol = SampleSprite(i.uv);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float offset = _ChromaticDispersion;
                float3 rCol = WaterSample(p, time,  offset);
                float3 gCol = WaterSample(p, time,  0.0);
                float3 bCol = WaterSample(p, time, -offset);
                float3 waterCol = float3(rCol.r, gCol.g, bCol.b);

                float ang = atan2(p.y, p.x);
                float rad = length(p);
                float rays = pow(max(0.0, sin(ang * 7.0 + time * 0.22 + rad * 3.0)), 6.0)
                           * smoothstep(1.35, 0.1, rad) * 0.11 * _LightRayStrength;
                waterCol += float3(0.10, 0.35, 0.55) * rays;

                float underwaterVignette = smoothstep(1.65, 0.10, rad);
                waterCol *= lerp(1.0, underwaterVignette, _VignetteStrength);

                float noise = (Hash21(floor(i.uv * _MainTex_TexelSize.zw) + floor(time * 24.0)) - 0.5) * _NoiseAmount;
                waterCol += noise;

                waterCol = 1.0 - exp(-waterCol * 1.22);
                waterCol = pow(saturate(waterCol), 0.92.xxx);
                waterCol *= _BrightnessGain;

                float3 refractedBase = SampleSprite(i.uv + (gCol.rg - 0.5) * (_RefractionStrength * 0.25)).rgb;
                float3 effectRgb = lerp(waterCol, refractedBase * _BaseMix + waterCol, _BaseMix);
                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float effectMask = saturate(length(waterCol) * 0.35);
                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + effectMask * 0.12), _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
