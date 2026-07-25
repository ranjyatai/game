Shader "SkyPrison/Animation Layer FX/Wave Hex Scan"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,3)) = 1.18

        _WaveColorA ("Wave Color A", Color) = (0.45, 0.75, 1.0, 1.0)
        _WaveColorB ("Wave Color B", Color) = (1.0, 0.65, 0.35, 1.0)

        _WaveSpeed ("Wave Speed", Range(-5,5)) = 0.95
        _WaveFrequency ("Wave Frequency", Range(1,80)) = 18.0
        _WaveAmplitude ("Wave Amplitude", Range(0,0.08)) = 0.018
        _WaveSharpness ("Wave Sharpness", Range(0.5,8)) = 2.6

        _HexScale ("Hex Scale", Range(2,80)) = 20.0
        _HexStrength ("Hex Strength", Range(0,2)) = 0.85
        _HexLineWidth ("Hex Line Width", Range(0.001,0.2)) = 0.045
        _HexScroll ("Hex Scroll", Range(-5,5)) = 0.75

        _SpherePulse ("Sphere Pulse", Range(0,1)) = 0.45
        _PulseScale ("Pulse Scale", Range(0.1,4)) = 1.35
        _PulseSpeed ("Pulse Speed", Range(-5,5)) = 1.1

        _RimStrength ("Alpha Rim Strength", Range(0,5)) = 1.2
        _ChromaticShift ("RGB Wave Shift", Range(0,0.03)) = 0.004
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.06
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
            float4 _WaveColorA;
            float4 _WaveColorB;
            float _WaveSpeed;
            float _WaveFrequency;
            float _WaveAmplitude;
            float _WaveSharpness;
            float _HexScale;
            float _HexStrength;
            float _HexLineWidth;
            float _HexScroll;
            float _SpherePulse;
            float _PulseScale;
            float _PulseSpeed;
            float _RimStrength;
            float _ChromaticShift;
            float _NoiseAmount;

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

            // Lightweight hex-cell line field inspired by the Shadertoy hex helper.
            float HexLine(float2 p)
            {
                p.x *= 1.1547005;
                p.y += floor(p.x) * 0.5;
                p = abs(frac(p) - 0.5);
                float h = abs(max(p.x * 1.5 + p.y, p.y * 2.0) - 1.0);
                return 1.0 - smoothstep(_HexLineWidth, _HexLineWidth * 2.5, h);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);
                float2 uv = i.uv;

                float2 center = uv - 0.5;
                float radius = length(center);
                float angle = atan2(center.y, center.x);

                float travellingWave = sin((center.y + center.x * 0.35) * _WaveFrequency + time * _WaveSpeed * 6.28318);
                float radialWave = sin(radius * _WaveFrequency * 2.0 - time * _PulseSpeed * 6.28318);
                float waveMask = pow(abs(travellingWave * 0.65 + radialWave * 0.35), _WaveSharpness);

                float2 waveDir = normalize(float2(cos(angle + time * 0.25), sin(angle + time * 0.25)) + 1e-4);
                float2 distortUv = uv + waveDir * travellingWave * _WaveAmplitude;

                float inside =
                    step(0.0, distortUv.x) * step(distortUv.x, 1.0) *
                    step(0.0, distortUv.y) * step(distortUv.y, 1.0);

                fixed4 baseCol = tex2D(_MainTex, saturate(distortUv)) * _Color;
                baseCol.a *= inside;

                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float2 hexUv = uv * _HexScale + float2(time * _HexScroll, -time * _HexScroll * 0.27);
                hexUv += travellingWave * 0.08;
                float hex = HexLine(hexUv) * _HexStrength;

                float pulseRing = 1.0 - smoothstep(0.03, 0.18, abs(radius - frac(time * _PulseSpeed) * _PulseScale * 0.45));
                pulseRing *= _SpherePulse;

                float texel = max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 2.0;
                float edgeAlpha = max(max(AlphaAt(uv + float2(texel, 0)), AlphaAt(uv - float2(texel, 0))),
                                      max(AlphaAt(uv + float2(0, texel)), AlphaAt(uv - float2(0, texel))));
                float rim = saturate(edgeAlpha - baseCol.a) * _RimStrength * baseCol.a;

                float shift = _ChromaticShift * saturate(hex + pulseRing + waveMask);
                float3 rgbSplit;
                rgbSplit.r = tex2D(_MainTex, saturate(distortUv + float2(shift, 0))).r * _Color.r;
                rgbSplit.g = baseCol.g;
                rgbSplit.b = tex2D(_MainTex, saturate(distortUv - float2(shift, 0))).b * _Color.b;

                float noise = (Hash21(floor(uv * float2(180.0, 260.0)) + floor(time * 24.0)) - 0.5) * _NoiseAmount;

                float3 waveColor = lerp(_WaveColorA.rgb, _WaveColorB.rgb, saturate(radialWave * 0.5 + 0.5));
                float effectMask = saturate(waveMask * 0.45 + hex * 0.75 + pulseRing * 0.85 + rim);

                float3 effectRgb = lerp(baseCol.rgb, rgbSplit, effectMask * 0.65);
                effectRgb *= _BrightnessGain;
                effectRgb += waveColor * effectMask;
                effectRgb += noise;

                float3 finalRgb = lerp(baseCol.rgb, effectRgb, _EffectOpacity);

                return fixed4(saturate(finalRgb), baseCol.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
