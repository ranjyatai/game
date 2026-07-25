Shader "SkyPrison/Animation Layer FX/Mantle Strata Wave"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.35
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.0

        _StrataColorA ("Strata Color A", Color) = (0.14, 0.18, 0.24, 1.0)
        _StrataColorB ("Strata Color B", Color) = (0.28, 0.34, 0.42, 1.0)
        _HighlightColor ("Highlight Color", Color) = (0.86, 0.87, 0.83, 1.0)

        _LayerDensity ("Layer Density", Range(4,80)) = 18.0
        _LayerSharpness ("Layer Sharpness", Range(0.2,8.0)) = 2.2
        _BandSoftness ("Band Softness", Range(0.001,0.5)) = 0.09
        _BandContrast ("Band Contrast", Range(0,4)) = 1.1

        _WaveAmplitude ("Wave Amplitude", Range(0,1)) = 0.11
        _WaveFrequency ("Wave Frequency", Range(0.1,20)) = 2.0
        _WaveSpeed ("Wave Speed", Range(-10,10)) = 0.8
        _WarpStrength ("Warp Strength", Range(0,1)) = 0.14
        _WarpScale ("Warp Scale", Range(0.1,20)) = 3.0

        _HeightScale ("Height Scale", Range(0.5,20)) = 4.0
        _SlopeStrength ("Slope Strength", Range(0,3)) = 1.0
        _RimStrength ("Alpha Rim Strength", Range(0,5)) = 0.6

        _GrainAmount ("Grain Amount", Range(0,1)) = 0.015
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;

            float _SkyPrisonTime;
            float _PreviewTime;

            float _EffectOpacity;
            float _BaseMix;
            float _BrightnessGain;

            float4 _StrataColorA;
            float4 _StrataColorB;
            float4 _HighlightColor;

            float _LayerDensity;
            float _LayerSharpness;
            float _BandSoftness;
            float _BandContrast;

            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;
            float _WarpStrength;
            float _WarpScale;

            float _HeightScale;
            float _SlopeStrength;
            float _RimStrength;
            float _GrainAmount;
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

            float2 Hash22(float2 p)
            {
                float n = sin(dot(p, float2(41.0, 289.0)));
                return frac(float2(262144.0, 32768.0) * n) * 2.0 - 1.0;
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = dot(Hash22(i + float2(0,0)), f - float2(0,0));
                float b = dot(Hash22(i + float2(1,0)), f - float2(1,0));
                float c = dot(Hash22(i + float2(0,1)), f - float2(0,1));
                float d = dot(Hash22(i + float2(1,1)), f - float2(1,1));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float FBM(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll(4)]
                for (int i = 0; i < 4; i++)
                {
                    v += Noise(p) * a;
                    p = p * 2.03 + float2(7.31, 13.17);
                    a *= 0.5;
                }
                return v;
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

            float AlphaAt(float2 uv)
            {
                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);
                return tex2D(_MainTex, saturate(uv)).a * inside;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);
                float2 uv = i.uv;

                // 保持原图为底
                fixed4 baseCol = SampleSprite(uv);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                // 构造“地幔/地层”式的横向波动高度场
                float2 p = uv;
                float warp = FBM(float2(p.x * _WarpScale, p.y * (_WarpScale * 0.6) + time * _WaveSpeed * 0.15));
                float wave1 = sin((p.x * _WaveFrequency + time * _WaveSpeed) * 6.2831853);
                float wave2 = sin((p.x * (_WaveFrequency * 0.55) - time * _WaveSpeed * 0.7) * 6.2831853 + 1.2);
                float wave3 = sin((p.x * (_WaveFrequency * 1.75) + time * _WaveSpeed * 1.3) * 6.2831853 + p.y * 2.0);

                float verticalDisplace =
                    wave1 * _WaveAmplitude +
                    wave2 * _WaveAmplitude * 0.45 +
                    wave3 * _WaveAmplitude * 0.18 +
                    warp * _WarpStrength;

                // 高度值：核心是 y 方向分层，而不是点状噪声
                float height = (p.y + verticalDisplace) * _HeightScale;

                // 生成明显的分层地带
                float strata = frac(height * _LayerDensity);
                float band = abs(strata - 0.5) * 2.0;
                band = 1.0 - smoothstep(0.0, _BandSoftness, band);
                band = pow(saturate(band), _LayerSharpness);
                band *= _BandContrast;

                // 额外做一个“台阶分层”底色，得到地层块感
                float terrace = floor((height + 1.0) * _LayerDensity) / max(_LayerDensity, 1.0);
                float terraceMask = frac((height + 1.0) * _LayerDensity);
                terraceMask = smoothstep(0.15, 0.85, terraceMask);

                // 斜率感：让地层起伏边缘更有明暗
                float eps = 0.002;
                float warpDx = FBM(float2((p.x + eps) * _WarpScale, p.y * (_WarpScale * 0.6) + time * _WaveSpeed * 0.15));
                float waveDx1 = sin((((p.x + eps) * _WaveFrequency) + time * _WaveSpeed) * 6.2831853);
                float waveDx2 = sin((((p.x + eps) * (_WaveFrequency * 0.55)) - time * _WaveSpeed * 0.7) * 6.2831853 + 1.2);
                float waveDx3 = sin((((p.x + eps) * (_WaveFrequency * 1.75)) + time * _WaveSpeed * 1.3) * 6.2831853 + p.y * 2.0);
                float verticalDisplaceDx =
                    waveDx1 * _WaveAmplitude +
                    waveDx2 * _WaveAmplitude * 0.45 +
                    waveDx3 * _WaveAmplitude * 0.18 +
                    warpDx * _WarpStrength;

                float slope = abs((verticalDisplaceDx - verticalDisplace) / eps);
                slope = saturate(slope * 0.04 * _SlopeStrength);

                float3 strataBase = lerp(_StrataColorA.rgb, _StrataColorB.rgb, terraceMask);
                strataBase = lerp(strataBase, _HighlightColor.rgb, band * 0.35);
                strataBase += slope * 0.12;

                // alpha边缘轻微提亮
                float texel = max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 2.0;
                float edgeAlpha = max(max(AlphaAt(uv + float2(texel, 0)), AlphaAt(uv - float2(texel, 0))),
                                      max(AlphaAt(uv + float2(0, texel)), AlphaAt(uv - float2(0, texel))));
                float rim = saturate(edgeAlpha - baseCol.a) * _RimStrength;

                // 少量细颗粒，不再做星点噪声
                float grain = (Hash21(floor(uv * _MainTex_TexelSize.zw) + floor(time * 24.0)) - 0.5) * _GrainAmount;

                float3 effectRgb = strataBase;
                effectRgb = lerp(effectRgb, _HighlightColor.rgb, band * 0.55);
                effectRgb += rim * 0.12;
                effectRgb += grain;
                effectRgb *= _BrightnessGain;

                // 混合：保留原PSB体积感，只叠加地幔分层波动
                float3 mixed = lerp(baseCol.rgb, baseCol.rgb * _BaseMix + effectRgb, _EffectOpacity);
                float3 finalRgb = lerp(baseCol.rgb, mixed, _EffectOpacity);

                float effectMask = saturate(0.35 + band * 0.65 + slope * 0.25);
                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + effectMask * 0.18), _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
