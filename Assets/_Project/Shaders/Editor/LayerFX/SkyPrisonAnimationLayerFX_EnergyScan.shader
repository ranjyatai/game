Shader "SkyPrison/Animation Layer FX/Energy Scan Safe"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.15

        _ScanColor ("Scan Color", Color) = (1.0, 0.72, 0.42, 1.0)
        _AmbientColor ("Ambient Color", Color) = (0.55, 0.75, 1.0, 1.0)

        _ScanSpeed ("Scan Speed", Range(-5,5)) = 0.75
        _ScanWidth ("Scan Width", Range(0.005,0.5)) = 0.075
        _ScanSoftness ("Scan Softness", Range(0.001,0.5)) = 0.12
        _ScanIntensity ("Scan Intensity", Range(0,5)) = 1.65
        _SecondaryScanOffset ("Secondary Scan Offset", Range(0,1)) = 0.37

        _LineDensity ("Line Density", Range(10,260)) = 96
        _LineStrength ("Line Strength", Range(0,1)) = 0.34
        _LineSpeed ("Line Speed", Range(-10,10)) = 1.25

        _RimStrength ("Alpha Rim Strength", Range(0,5)) = 1.4
        _ChromaticShift ("RGB Scan Shift", Range(0,0.03)) = 0.004
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.08
        _DistortAmount ("Horizontal Distort", Range(0,0.05)) = 0.006
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _SkyPrisonTime;
            float _PreviewTime;

            float _EffectOpacity;
            float _BrightnessGain;
            float4 _ScanColor;
            float4 _AmbientColor;
            float _ScanSpeed;
            float _ScanWidth;
            float _ScanSoftness;
            float _ScanIntensity;
            float _SecondaryScanOffset;
            float _LineDensity;
            float _LineStrength;
            float _LineSpeed;
            float _RimStrength;
            float _ChromaticShift;
            float _NoiseAmount;
            float _DistortAmount;

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
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float AlphaAt(float2 uv)
            {
                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);

                return tex2D(_MainTex, saturate(uv)).a * inside;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(_SkyPrisonTime, _PreviewTime);
                time = max(time, _Time.y);

                float2 baseUv = i.uv;

                float distort = sin((baseUv.y + time * 0.07) * 42.0) * _DistortAmount;
                float2 uv = baseUv + float2(distort, 0.0);

                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);

                fixed4 baseCol = tex2D(_MainTex, saturate(uv)) * _Color;
                baseCol.a *= inside;

                float baseAlpha = baseCol.a;
                if (baseAlpha <= 0.0001)
                    return fixed4(0,0,0,0);

                float scanPhase = frac(time * _ScanSpeed);

                float dA = abs(baseUv.y - scanPhase);
                dA = min(dA, 1.0 - dA);

                float scanPhaseB = frac(scanPhase + _SecondaryScanOffset);
                float dB = abs(baseUv.y - scanPhaseB);
                dB = min(dB, 1.0 - dB);

                float scanA = 1.0 - smoothstep(_ScanWidth, _ScanWidth + _ScanSoftness, dA);
                float scanB = 1.0 - smoothstep(_ScanWidth * 0.55, _ScanWidth * 0.55 + _ScanSoftness, dB);
                float scanMask = saturate(scanA + scanB * 0.55);

                float lineWave = abs(0.5 - frac((baseUv.y + time * _LineSpeed * 0.035) * _LineDensity)) * 2.0;
                lineWave = pow(lineWave, 3.0);
                float scanLineMul = lerp(1.0, lineWave, _LineStrength);

                float texel = max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 2.0;
                float aR = AlphaAt(baseUv + float2(texel, 0));
                float aL = AlphaAt(baseUv - float2(texel, 0));
                float aU = AlphaAt(baseUv + float2(0, texel));
                float aD = AlphaAt(baseUv - float2(0, texel));
                float edgeAlpha = max(max(aR, aL), max(aU, aD));
                float rim = saturate(edgeAlpha - baseAlpha) * _RimStrength * baseAlpha;

                float2 rUv = saturate(uv + float2(_ChromaticShift * scanMask, 0));
                float2 bUv = saturate(uv - float2(_ChromaticShift * scanMask, 0));
                float3 rgbSplit;
                rgbSplit.r = tex2D(_MainTex, rUv).r * _Color.r;
                rgbSplit.g = baseCol.g;
                rgbSplit.b = tex2D(_MainTex, bUv).b * _Color.b;

                float noise = (Hash21(floor(baseUv * float2(180.0, 260.0)) + floor(time * 24.0)) - 0.5) * _NoiseAmount;

                float3 baseRgb = lerp(baseCol.rgb, rgbSplit, scanMask * 0.65);
                baseRgb *= _BrightnessGain;
                baseRgb *= scanLineMul;

                float3 scanGlow = _ScanColor.rgb * scanMask * _ScanIntensity;
                float3 rimGlow = _AmbientColor.rgb * rim;

                float3 effectRgb = baseRgb + scanGlow + rimGlow + noise;
                float3 finalRgb = lerp(baseCol.rgb, effectRgb, _EffectOpacity);

                return fixed4(saturate(finalRgb), baseAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
