Shader "SkyPrison/Animation Layer FX/Ink Diffusion White Space Chaotic Flow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.08
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.0

        _PaperColor ("Paper / White Space", Color) = (0.965, 0.968, 0.962, 1.0)
        _InkColor ("Ink Color", Color) = (0.11, 0.12, 0.12, 1.0)
        _LightInkColor ("Light Ink Wash", Color) = (0.58, 0.60, 0.60, 1.0)

        _InkAmount ("Ink Amount", Range(0,1)) = 0.54
        _InkContrast ("Ink Contrast", Range(0.1,5)) = 1.24
        _WashSoftness ("Wash Softness", Range(0.01,1.0)) = 0.52
        _EdgeFeather ("Edge Feather", Range(0.001,0.5)) = 0.26

        _MainZoom ("Main Zoom", Range(0.2,3.0)) = 0.56

        _FlowScale ("Flow Scale", Range(0.25,20)) = 2.2
        _FlowStrength ("Flow Strength", Range(0,1)) = 0.52
        _FlowSpeed ("Flow Speed", Range(-5,5)) = 0.32
        _CurlStrength ("Curl Strength", Range(0,1)) = 0.42

        _DiffusionScale ("Diffusion Scale", Range(0.25,32)) = 3.4
        _DiffusionSpeed ("Diffusion Speed", Range(-5,5)) = 0.20
        _DiffusionStrength ("Diffusion Strength", Range(0,2)) = 0.92

        _ChaosAdvection ("Chaos Advection", Range(0,2)) = 0.92
        _ChaosScaleA ("Chaos Scale A", Range(0.2,12)) = 1.10
        _ChaosScaleB ("Chaos Scale B", Range(0.2,12)) = 2.60
        _ChaosTimeScale ("Chaos Time Scale", Range(0,3)) = 0.35
        _DirectionalBias ("Directional Bias", Range(0,1)) = 0.06

        _RibbonStrength ("Ribbon Strength", Range(0,2)) = 1.00
        _RibbonDensity ("Ribbon Density", Range(0.5,20)) = 2.70
        _RibbonLength ("Ribbon Length", Range(0.2,4)) = 2.90
        _RibbonWarp ("Ribbon Warp", Range(0,2)) = 1.45
        _RibbonSoftness ("Ribbon Softness", Range(0.01,1.0)) = 0.38
        _RibbonBreakup ("Ribbon Breakup", Range(0,2)) = 1.10
        _RibbonAngleNoise ("Ribbon Angle Noise", Range(0,2)) = 1.35

        _BlobStrength ("Ink Blob Strength", Range(0,2)) = 0.62
        _BlobScale ("Ink Blob Scale", Range(0.25,16)) = 2.0

        _TendrilStrength ("Tendril Strength", Range(0,2)) = 0.70
        _TendrilDensity ("Tendril Density", Range(1,40)) = 7.0
        _SmokeStrength ("Smoke Strength", Range(0,2)) = 0.34
        _GrainAmount ("Paper Grain", Range(0,1)) = 0.018

        _HorizontalDrift ("Horizontal Drift", Range(-1,1)) = 0.00
        _VerticalDrift ("Vertical Drift", Range(-1,1)) = 0.00

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

            float4 _PaperColor;
            float4 _InkColor;
            float4 _LightInkColor;

            float _InkAmount;
            float _InkContrast;
            float _WashSoftness;
            float _EdgeFeather;

            float _MainZoom;

            float _FlowScale;
            float _FlowStrength;
            float _FlowSpeed;
            float _CurlStrength;

            float _DiffusionScale;
            float _DiffusionSpeed;
            float _DiffusionStrength;

            float _ChaosAdvection;
            float _ChaosScaleA;
            float _ChaosScaleB;
            float _ChaosTimeScale;
            float _DirectionalBias;

            float _RibbonStrength;
            float _RibbonDensity;
            float _RibbonLength;
            float _RibbonWarp;
            float _RibbonSoftness;
            float _RibbonBreakup;
            float _RibbonAngleNoise;

            float _BlobStrength;
            float _BlobScale;

            float _TendrilStrength;
            float _TendrilDensity;
            float _SmokeStrength;
            float _GrainAmount;

            float _HorizontalDrift;
            float _VerticalDrift;

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

                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }

            float FBM(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll(5)]
                for (int i = 0; i < 5; i++)
                {
                    v += Noise(p) * a;
                    p = p * 2.02 + float2(13.17, 7.31);
                    a *= 0.5;
                }
                return v;
            }

            float2 Curl(float2 p)
            {
                float e = 0.012;
                float n1 = FBM(p + float2(0, e));
                float n2 = FBM(p - float2(0, e));
                float n3 = FBM(p + float2(e, 0));
                float n4 = FBM(p - float2(e, 0));
                return float2(n1 - n2, -(n3 - n4)) / (2.0 * e);
            }

            float2 Rot(float2 p, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
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

            float2 ChaoticAdvection(float2 p, float time)
            {
                // 多层、双向、时变流场：不再整张图一起往左上推。
                float2 c1 = Curl(p * _ChaosScaleA + float2(time * _ChaosTimeScale, -time * (_ChaosTimeScale * 0.7)));
                float2 c2 = Curl(p * _ChaosScaleB + float2(-time * (_ChaosTimeScale * 1.3), time * (_ChaosTimeScale * 0.9)));

                float2 n1 = Hash22(floor(p * 2.2 + float2(time * 0.3, -time * 0.2)));
                float2 n2 = Hash22(floor(p * 4.6 + float2(-time * 0.15, time * 0.27)));

                float2 field = c1 * 0.70 + c2 * 0.50 + n1 * 0.18 + n2 * 0.08;

                // 只保留很弱的整体偏向，避免“整片一起跑”
                field += float2(_HorizontalDrift, _VerticalDrift) * _DirectionalBias;

                return field * _ChaosAdvection;
            }

            float RibbonField(float2 p, float time, float largeNoise, float mediumNoise)
            {
                float angleField = FBM(p * 1.35 + float2(time * 0.045, -time * 0.03)) * 6.2831853;
                angleField += FBM(p * 2.7 + float2(-time * 0.02, time * 0.05)) * 2.4 * _RibbonAngleNoise;

                float2 rp = Rot(p, (angleField - 3.1415926) * 0.23);
                rp.x *= _RibbonLength;

                float warp =
                    largeNoise * 1.25 +
                    mediumNoise * 0.85 +
                    FBM(p * 0.85 + float2(time * 0.04, 0.0)) * 0.75;

                float phaseJitter = FBM(float2(rp.x * 0.45, rp.y * 0.25) + float2(time * 0.035, 9.2));
                float axis = rp.y + warp * _RibbonWarp + phaseJitter * 0.85;

                float band = 1.0 - abs(sin(axis * _RibbonDensity + time * 0.16));
                band = smoothstep(1.0 - _RibbonSoftness, 1.0, band);

                float breakupA = FBM(rp * 0.70 + float2(time * 0.035, -time * 0.025)) * 0.5 + 0.5;
                float breakupB = FBM(rp * 1.85 + float2(-time * 0.06, time * 0.02)) * 0.5 + 0.5;
                float breakup = smoothstep(0.20, 0.92, breakupA * 0.65 + breakupB * 0.35);
                breakup = lerp(1.0, breakup, _RibbonBreakup);

                float taper = smoothstep(1.75, 0.05, abs(p.y + largeNoise * 0.40 + mediumNoise * 0.18));
                float uneven = smoothstep(0.12, 0.86, FBM(p * 1.15 + float2(4.7, time * 0.025)) * 0.5 + 0.5);

                return band * breakup * taper * uneven * _RibbonStrength;
            }

            float BlobField(float2 p, float time)
            {
                float2 bp = p * _BlobScale;
                float b = FBM(bp + float2(time * 0.025, -time * 0.018)) * 0.5 + 0.5;
                float b2 = FBM(bp * 2.1 + float2(5.2, 1.7)) * 0.5 + 0.5;
                float blob = smoothstep(0.42, 0.88, b * 0.72 + b2 * 0.28);
                return blob * _BlobStrength;
            }

            float InkDensity(float2 p, float time)
            {
                float2 baseP = p;

                float2 macroCurl = Curl(p * _FlowScale + float2(time * 0.12, -time * 0.08)) * _CurlStrength;
                float2 chaotic = ChaoticAdvection(p, time);

                // 使用局部时变 advection 替代单一方向 drift
                p += macroCurl * _FlowStrength;
                p += chaotic * (_FlowSpeed * 0.85);

                float large = FBM(p * _DiffusionScale + float2(time * _DiffusionSpeed * 0.10, -time * _DiffusionSpeed * 0.06));
                float medium = FBM(p * (_DiffusionScale * 1.65) + float2(-time * 0.08, time * 0.10));
                float fine = FBM(p * (_DiffusionScale * 3.85) + float2(time * 0.15, time * 0.13));

                float smoke = large * 0.64 + medium * 0.27 + fine * 0.09;
                smoke = smoke * 0.5 + 0.5;

                float ribbon = RibbonField(p + chaotic * 0.25, time, large, medium);
                float blob = BlobField(p + float2(large * 0.16, medium * 0.12) + chaotic * 0.18, time);

                float tendril =
                    abs(sin((p.x + large * 1.05 + medium * 0.3) * (_TendrilDensity * 0.52) + time * 0.22)) *
                    abs(sin((p.y - medium * 0.75) * (_TendrilDensity * 0.36) - time * 0.15));
                tendril = pow(saturate(tendril), 3.0) * _TendrilStrength;

                float centerBand = smoothstep(1.65, 0.05, abs(baseP.y * 1.08 + large * 0.30));
                float density = smoke * _SmokeStrength + ribbon + blob + tendril * 0.48 + centerBand * 0.20;
                density *= _DiffusionStrength;

                return saturate(density);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                fixed4 baseCol = SampleSprite(i.uv);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 p = i.uv * 2.0 - 1.0;
                p.x *= aspect;
                p *= _MainZoom;

                float density = InkDensity(p, time);
                density = pow(saturate(density), _InkContrast);

                float softMask = smoothstep(_InkAmount - _EdgeFeather, _InkAmount + _WashSoftness, density);
                float darkCore = smoothstep(_InkAmount + 0.08, _InkAmount + 0.42, density);

                float grain = (Hash21(floor(i.uv * _MainTex_TexelSize.zw * 0.40) + floor(time * 8.0)) - 0.5) * _GrainAmount;

                float3 wash = lerp(_PaperColor.rgb, _LightInkColor.rgb, softMask * 0.82);
                wash = lerp(wash, _InkColor.rgb, darkCore * 0.70);
                wash += grain;
                wash = lerp(_PaperColor.rgb, wash, 0.92);

                float3 effectRgb = lerp(wash, baseCol.rgb * _BaseMix + wash, _BaseMix);
                effectRgb *= _BrightnessGain;

                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float effectAlpha = saturate(baseCol.a + softMask * 0.10);
                float finalAlpha = lerp(baseCol.a, effectAlpha, _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
