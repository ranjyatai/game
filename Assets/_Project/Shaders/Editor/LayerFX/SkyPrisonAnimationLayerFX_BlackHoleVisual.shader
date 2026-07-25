Shader "SkyPrison/Animation Layer FX/Black Hole Visual"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.25

        _CoreRadius ("Core Radius", Range(0.01,0.5)) = 0.145
        _CoreSoftness ("Core Softness", Range(0.001,0.5)) = 0.075
        _LensStrength ("Lens Strength", Range(0,1)) = 0.32
        _LensRadius ("Lens Radius", Range(0.05,1.5)) = 0.72

        _DiskColorA ("Disk Inner Color", Color) = (1.0, 0.62, 0.24, 1.0)
        _DiskColorB ("Disk Outer Color", Color) = (0.40, 0.72, 1.00, 1.0)
        _DiskRadius ("Accretion Disk Radius", Range(0.05,1.0)) = 0.31
        _DiskWidth ("Accretion Disk Width", Range(0.005,0.5)) = 0.075
        _DiskTilt ("Disk Tilt", Range(0.1,4.0)) = 0.34
        _DiskIntensity ("Disk Intensity", Range(0,8)) = 2.4

        _SpinSpeed ("Spin Speed", Range(-5,5)) = 0.85
        _NoiseAmount ("Disk Noise Amount", Range(0,1)) = 0.16
        _PhotonRingIntensity ("Photon Ring Intensity", Range(0,8)) = 2.2
        _BloomFake ("Fake Bloom", Range(0,5)) = 1.4

        _ChromaticAberration ("Chromatic Aberration", Range(0,0.05)) = 0.006
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
            float _BrightnessGain;
            float _CoreRadius;
            float _CoreSoftness;
            float _LensStrength;
            float _LensRadius;
            float4 _DiskColorA;
            float4 _DiskColorB;
            float _DiskRadius;
            float _DiskWidth;
            float _DiskTilt;
            float _DiskIntensity;
            float _SpinSpeed;
            float _NoiseAmount;
            float _PhotonRingIntensity;
            float _BloomFake;
            float _ChromaticAberration;
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

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1));
                float d = Hash21(i + float2(1,1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 uv = i.uv;
                float2 centerUv = uv - 0.5;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                centerUv.x *= aspect;

                float r = length(centerUv);
                float angle = atan2(centerUv.y, centerUv.x);

                // Visual-only lensing: pull nearby pixels around the dark core.
                float lensMask = 1.0 - smoothstep(_LensRadius * 0.25, _LensRadius, r);
                float bend = _LensStrength * lensMask / max(r * 8.0 + 0.2, 0.001);
                float swirl = bend * sin(time * _SpinSpeed + r * 14.0);
                float2 dir = normalize(centerUv + 1e-5);
                float2 tangent = float2(-dir.y, dir.x);
                float2 lensedUv = uv + dir * bend * 0.065 + tangent * swirl * 0.055;

                float inside =
                    step(0.0, lensedUv.x) * step(lensedUv.x, 1.0) *
                    step(0.0, lensedUv.y) * step(lensedUv.y, 1.0);

                fixed4 baseCol = tex2D(_MainTex, saturate(lensedUv)) * _Color;
                baseCol.a *= inside;
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                // Accretion disk: an elliptical ring, not physically accurate, just the visual principle.
                float2 diskP = centerUv;
                diskP.y /= max(_DiskTilt, 0.001);
                float diskR = length(diskP);
                float diskBand = 1.0 - smoothstep(_DiskWidth, _DiskWidth * 2.4, abs(diskR - _DiskRadius));

                float diskPhase = angle * 3.0 + time * _SpinSpeed * 5.0;
                float streak = 0.55 + 0.45 * sin(diskPhase + ValueNoise(float2(angle * 2.0, r * 18.0)) * 6.28318);
                float n = ValueNoise(float2(angle * 6.0 + time * _SpinSpeed, diskR * 26.0));
                streak = lerp(streak, streak * (0.75 + n * 0.75), _NoiseAmount);

                // Doppler-ish asymmetry: one side brighter.
                float asymmetry = 0.72 + 0.55 * saturate(dot(normalize(diskP + 1e-5), float2(1.0, 0.18)));
                float diskMask = saturate(diskBand * streak * asymmetry);

                float3 diskColor = lerp(_DiskColorA.rgb, _DiskColorB.rgb, saturate(r * 2.1));
                float3 diskLight = diskColor * diskMask * _DiskIntensity;

                float photonRing = 1.0 - smoothstep(0.006, 0.026, abs(r - (_CoreRadius + 0.018)));
                photonRing *= _PhotonRingIntensity;

                float bloomWide = 1.0 - smoothstep(_DiskWidth * 0.6, _DiskWidth * 4.5, abs(diskR - _DiskRadius));
                bloomWide *= _BloomFake * 0.28;

                float core = smoothstep(_CoreRadius, _CoreRadius + _CoreSoftness, r);
                float eventHorizon = 1.0 - core;

                // Chromatic fringe around the bending area.
                float chromaMask = saturate(lensMask * (1.0 - eventHorizon));
                float2 ca = tangent * _ChromaticAberration * chromaMask;
                float3 splitCol;
                splitCol.r = tex2D(_MainTex, saturate(lensedUv + ca)).r * _Color.r;
                splitCol.g = baseCol.g;
                splitCol.b = tex2D(_MainTex, saturate(lensedUv - ca)).b * _Color.b;

                float3 source = lerp(baseCol.rgb, splitCol, chromaMask);
                source *= _BrightnessGain;

                float3 effect = source;
                effect += diskLight;
                effect += diskColor * photonRing;
                effect += diskColor * bloomWide;

                // Core eats light.
                effect = lerp(effect, float3(0,0,0), eventHorizon);

                float3 finalRgb = lerp(baseCol.rgb, effect, _EffectOpacity);

                // 默认保持原 PSB alpha；AlphaMode 越高，越像独立黑洞特效覆盖层。
                float effectAlpha = saturate(baseCol.a + (diskMask + photonRing + bloomWide) * 0.55);
                float finalAlpha = lerp(baseCol.a, effectAlpha, _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
