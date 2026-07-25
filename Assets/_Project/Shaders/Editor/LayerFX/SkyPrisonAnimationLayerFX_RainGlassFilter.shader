Shader "SkyPrison/Animation Layer FX/Rain Glass Filter"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.0
        _ColdTint ("Cold Tint", Color) = (0.78, 0.88, 1.18, 1.0)

        _RainAmount ("Rain Amount", Range(0,1)) = 0.72
        _RainSpeed ("Rain Speed", Range(-5,5)) = 1.0
        _DropScale ("Drop Scale", Range(0.25,4.0)) = 1.0
        _StaticDrops ("Static Drops", Range(0,2)) = 1.0
        _LargeDrops ("Large Drops", Range(0,2)) = 1.0
        _SmallDrops ("Small Drops", Range(0,2)) = 0.75

        _Distortion ("Refraction Distortion", Range(0,0.08)) = 0.026
        _TrailStrength ("Trail Strength", Range(0,2)) = 0.85
        _FogAmount ("Glass Fog Amount", Range(0,1)) = 0.38
        _ClearByDrops ("Clear By Drops", Range(0,1)) = 0.65

        _BlurMix ("Fake Blur Mix", Range(0,1)) = 0.45
        _BlurRadius ("Fake Blur Radius", Range(0,0.02)) = 0.006

        _LightningAmount ("Lightning Amount", Range(0,1)) = 0.15
        _VignetteStrength ("Vignette Strength", Range(0,1)) = 0.22
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.025

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
            #pragma target 2.5
            #include "UnityCG.cginc"

            #define S(a,b,t) smoothstep(a,b,t)

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _SkyPrisonTime;
            float _PreviewTime;

            float _EffectOpacity;
            float _BrightnessGain;
            float4 _ColdTint;

            float _RainAmount;
            float _RainSpeed;
            float _DropScale;
            float _StaticDrops;
            float _LargeDrops;
            float _SmallDrops;

            float _Distortion;
            float _TrailStrength;
            float _FogAmount;
            float _ClearByDrops;

            float _BlurMix;
            float _BlurRadius;

            float _LightningAmount;
            float _VignetteStrength;
            float _NoiseAmount;
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

            float N(float t)
            {
                return frac(sin(t * 12345.564) * 7658.76);
            }

            float3 N13(float p)
            {
                float3 p3 = frac(float3(p, p, p) * float3(0.1031, 0.11369, 0.13787));
                p3 += dot(p3, p3.yzx + 19.19);
                return frac(float3((p3.x + p3.y) * p3.z, (p3.x + p3.z) * p3.y, (p3.y + p3.z) * p3.x));
            }

            float Saw(float b, float t)
            {
                return S(0.0, b, t) * S(1.0, b, t);
            }

            float2 DropLayer(float2 uv, float t)
            {
                float2 UV = uv;

                uv.y += t * 0.75;
                float2 a = float2(6.0, 1.0);
                float2 grid = a * 2.0;
                float2 id = floor(uv * grid);

                float colShift = N(id.x);
                uv.y += colShift;

                id = floor(uv * grid);
                float3 n = N13(id.x * 35.2 + id.y * 2376.1);
                float2 st = frac(uv * grid) - float2(0.5, 0.0);

                float x = n.x - 0.5;

                float y = UV.y * 20.0;
                float wiggle = sin(y + sin(y));
                x += wiggle * (0.5 - abs(x)) * (n.z - 0.5);
                x *= 0.7;

                float ti = frac(t + n.z);
                y = (Saw(0.85, ti) - 0.5) * 0.9 + 0.5;
                float2 p = float2(x, y);

                float d = length((st - p) * a.yx);
                float mainDrop = S(0.4, 0.0, d);

                float r = sqrt(S(1.0, y, st.y));
                float cd = abs(st.x - x);
                float trail = S(0.23 * r, 0.15 * r * r, cd);
                float trailFront = S(-0.02, 0.02, st.y - y);
                trail *= trailFront * r * r;

                y = UV.y;
                float trail2 = S(0.2 * r, 0.0, cd);
                float droplets = max(0.0, (sin(y * (1.0 - y) * 120.0) - st.y)) * trail2 * trailFront * n.z;
                y = frac(y * 10.0) + (st.y - 0.5);
                float dd = length(st - float2(x, y));
                droplets = max(droplets, S(0.3, 0.0, dd) * r * trailFront);

                float m = mainDrop + droplets * r * trailFront;
                return float2(m, trail);
            }

            float StaticDrops(float2 uv, float t)
            {
                uv *= 40.0;

                float2 id = floor(uv);
                uv = frac(uv) - 0.5;

                float3 n = N13(id.x * 107.45 + id.y * 3543.654);
                float2 p = (n.xy - 0.5) * 0.7;
                float d = length(uv - p);

                float fade = Saw(0.025, frac(t + n.z));
                return S(0.3, 0.0, d) * frac(n.z * 10.0) * fade;
            }

            float2 Drops(float2 uv, float t, float l0, float l1, float l2)
            {
                float s = StaticDrops(uv, t) * l0;
                float2 m1 = DropLayer(uv, t) * l1;
                float2 m2 = DropLayer(uv * 1.85, t) * l2;

                float c = s + m1.x + m2.x;
                c = S(0.3, 1.0, c);

                return float2(c, max(m1.y * l0, m2.y * l1));
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

            float3 BlurSprite(float2 uv, float radius)
            {
                float2 r = float2(radius, 0.0);
                float2 u = float2(0.0, radius);

                float3 col = SampleSprite(uv).rgb * 0.40;
                col += SampleSprite(uv + r).rgb * 0.15;
                col += SampleSprite(uv - r).rgb * 0.15;
                col += SampleSprite(uv + u).rgb * 0.15;
                col += SampleSprite(uv - u).rgb * 0.15;
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);
                float t = time * 0.20 * _RainSpeed;

                float rainAmount = saturate(_RainAmount);

                float2 uv = i.uv;
                float2 centered = (uv - 0.5);
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                centered.x *= aspect;

                float2 rainUv = centered * lerp(0.85, 1.35, rainAmount) * max(_DropScale, 0.001);

                float staticLayer = S(-0.5, 1.0, rainAmount) * _StaticDrops;
                float layer1 = S(0.25, 0.75, rainAmount) * _LargeDrops;
                float layer2 = S(0.0, 0.5, rainAmount) * _SmallDrops;

                float2 c = Drops(rainUv, t, staticLayer, layer1, layer2);

                float2 e = float2(0.0015, 0.0);
                float cx = Drops(rainUv + e, t, staticLayer, layer1, layer2).x;
                float cy = Drops(rainUv + e.yx, t, staticLayer, layer1, layer2).x;
                float2 n = float2(cx - c.x, cy - c.x);

                float2 refractUv = uv + n * _Distortion;
                fixed4 baseCol = SampleSprite(uv);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                fixed4 refracted = SampleSprite(refractUv);

                float blurRadius = _BlurRadius * lerp(0.75, 1.75, rainAmount);
                float3 blurCol = BlurSprite(refractUv, blurRadius);

                float dropMask = c.x;
                float trailMask = c.y * _TrailStrength;

                float fog = _FogAmount * rainAmount;
                fog *= 1.0 - saturate(dropMask * _ClearByDrops + trailMask * 0.45);

                float3 glassCol = lerp(refracted.rgb, blurCol, _BlurMix * fog);
                glassCol = lerp(glassCol, glassCol * _ColdTint.rgb, saturate(0.28 * rainAmount));

                // Droplets become clearer and slightly brighter at their cores.
                float3 dropHighlight = float3(1.0, 1.0, 1.0) * (dropMask * 0.18 + trailMask * 0.08);
                glassCol += dropHighlight;

                // Foggy glass veil.
                float3 fogColor = float3(0.78, 0.86, 0.94);
                glassCol = lerp(glassCol, fogColor, fog * 0.55);

                // Lightning flicker, controlled for future weather controller.
                float lightningT = (time + 3.0) * 0.5;
                float lightning = sin(lightningT * sin(lightningT * 10.0));
                lightning *= pow(max(0.0, sin(lightningT + sin(lightningT))), 10.0);
                glassCol *= 1.0 + lightning * _LightningAmount;

                float vignette = uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y);
                vignette = clamp(pow(16.0 * vignette, 0.28), 0.0, 1.0);
                glassCol = lerp(glassCol, glassCol * vignette, _VignetteStrength);

                float noise = (N(dot(floor(uv * _MainTex_TexelSize.zw), float2(12.9898, 78.233)) + floor(time * 60.0)) - 0.5) * _NoiseAmount;
                glassCol += noise;

                glassCol *= _BrightnessGain;

                float3 finalRgb = lerp(baseCol.rgb, saturate(glassCol), _EffectOpacity);

                float effectAlpha = saturate(baseCol.a + (dropMask + trailMask + fog) * 0.12);
                float finalAlpha = lerp(baseCol.a, effectAlpha, _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
