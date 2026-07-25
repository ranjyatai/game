Shader "SkyPrison/Animation Layer FX/Apollo Brain Blast"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.35

        _BrainColorA ("Brain Color A", Color) = (1.00, 0.55, 0.18, 1.0)
        _BrainColorB ("Brain Color B", Color) = (0.90, 0.30, 1.00, 1.0)

        _MoveSpeed ("Move Speed", Range(-10,10)) = 1.0
        _RaySteps ("Ray Steps", Range(4,20)) = 10
        _StepDistance ("Step Distance", Range(1,40)) = 20.0
        _StepPulse ("Step Pulse", Range(0,10)) = 4.0

        _FractalIterations ("Fractal Iterations", Range(1,9)) = 9
        _FractalScale ("Fractal Scale", Range(1,8)) = 3.0
        _FractalDivisor ("Fractal Divisor", Range(1,40)) = 20.0
        _DensityStrength ("Density Strength", Range(0.1,8)) = 3.0
        _ExtinctionStrength ("Extinction Strength", Range(0.0,4.0)) = 1.0

        _LightStrength ("Light Strength", Range(0,5)) = 0.6
        _GlowStrength ("Glow Strength", Range(0,5)) = 1.25
        _BandStrength ("Band Strength", Range(0,5)) = 1.0

        _UVScale ("UV Scale", Range(0.1,3.0)) = 1.0
        _UVDistort ("UV Distort", Range(0,0.1)) = 0.012
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.004
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
            float _BrightnessGain;
            float4 _BrainColorA;
            float4 _BrainColorB;
            float _MoveSpeed;
            float _RaySteps;
            float _StepDistance;
            float _StepPulse;
            float _FractalIterations;
            float _FractalScale;
            float _FractalDivisor;
            float _DensityStrength;
            float _ExtinctionStrength;
            float _LightStrength;
            float _GlowStrength;
            float _BandStrength;
            float _UVScale;
            float _UVDistort;
            float _ChromaticShift;
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

            float3 SafeNormalize(float3 v)
            {
                return normalize(v + 1e-6);
            }

            float MapBrain(float3 p, float time)
            {
                p.z += time * 2.0 * _MoveSpeed;
                p /= max(_FractalDivisor, 0.001);

                float w = 0.5;
                [unroll(9)]
                for (int i = 0; i < 9; i++)
                {
                    if (i >= (int)_FractalIterations)
                        break;

                    p = abs(sin(p));
                    float denom = max(dot(p, p), 1e-4);
                    float l = _FractalScale / denom;
                    p *= l;
                    w *= l;
                }

                return length(p * _FractalDivisor) / max(w, 1e-4);
            }

            float LightBrain(float3 p, float time)
            {
                p.z -= time * 2.0 * _MoveSpeed;
                float3 sun = cos(time + p.yzx * 0.25) * 30.0;

                float s = 0.0;
                s += MapBrain(p + sun * 0.1, time);
                s += MapBrain(p + sun * 0.2, time);
                s += MapBrain(p + sun * 0.4, time);
                s += MapBrain(p + sun * 0.8, time);
                return exp(-s * _LightStrength);
            }

            float3 RenderBrain(float2 uv, float time)
            {
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 p = (uv * 2.0 - 1.0) * _UVScale;
                p.x *= aspect;

                float iCount = 0.0;
                float a = 1.0;
                float d = 0.0;
                float l = 0.0;
                float4 o = 0.0;

                float3 pos = 0.0;
                float3 dir = SafeNormalize(float3(p, 5.0));

                [loop]
                for (int i = 0; i < 20; i++)
                {
                    if (i >= (int)_RaySteps)
                        break;

                    iCount += 1.0;
                    pos += dir * a * (_StepDistance + (_StepPulse + _StepPulse * sin(time)));
                    d = MapBrain(pos, time);
                    a *= exp(-d * _ExtinctionStrength);
                    l = LightBrain(pos, time);

                    float4 pulseCol = (1.0 + cos(iCount * 0.3 + float4(2.0, 1.0, 0.0, 0.0))) * l * a * 10.0;
                    float4 fogCol = float4(4.0, 2.0, 1.0, 0.0) * a * l * d;
                    o += (d < 0.01 ? pulseCol * _BandStrength : fogCol * _DensityStrength);
                }

                o.r += 0.01 * dot(p, p);
                o = tanh(o * o * 50.0);

                float3 baseFx = o.rgb;
                float t = saturate(length(baseFx));
                float3 tint = lerp(_BrainColorA.rgb, _BrainColorB.rgb, saturate(baseFx.b + baseFx.g * 0.5));
                return saturate(baseFx * tint * _GlowStrength * _BrightnessGain);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 center = i.uv - 0.5;
                float radius = length(center);
                float2 distort = SafeNormalize(float3(center, 0.0)).xy * sin(radius * 20.0 - time * 2.5) * _UVDistort;
                float2 uv = saturate(i.uv + distort);

                fixed4 baseCol = tex2D(_MainTex, uv) * _Color;
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float3 fx = RenderBrain(uv, time);

                float2 shiftDir = normalize(center + float2(1e-5, 0.0));
                float2 ca = shiftDir * _ChromaticShift;
                float3 chroma;
                chroma.r = RenderBrain(saturate(uv + ca), time).r;
                chroma.g = fx.g;
                chroma.b = RenderBrain(saturate(uv - ca), time).b;
                fx = lerp(fx, chroma, 0.65);

                float effectMask = saturate(length(fx) * 1.4);
                float3 finalRgb = lerp(baseCol.rgb, baseCol.rgb + fx, _EffectOpacity * effectMask);

                float effectAlpha = saturate(baseCol.a + effectMask * 0.65);
                float finalAlpha = lerp(baseCol.a, effectAlpha, _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
