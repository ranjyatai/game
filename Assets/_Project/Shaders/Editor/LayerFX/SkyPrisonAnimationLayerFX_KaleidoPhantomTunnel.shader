Shader "SkyPrison/Animation Layer FX/Kaleido Phantom Tunnel"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.15

        _TunnelColorA ("Tunnel Color A", Color) = (0.25, 0.65, 1.0, 1.0)
        _TunnelColorB ("Tunnel Color B", Color) = (0.95, 0.35, 1.0, 1.0)

        _MoveSpeed ("Move Speed", Range(-10,10)) = 3.0
        _RaySteps ("Ray Steps", Range(16,128)) = 72
        _StepScale ("Step Scale", Range(0.1,2.0)) = 0.5

        _IFSIterations ("IFS Iterations", Range(1,8)) = 5
        _TileZ ("Tile Z", Range(2,32)) = 16.0
        _FoldScale ("Fold Scale", Range(0.25,3.0)) = 1.0
        _SymmetryCount ("Symmetry Count", Range(2,12)) = 5.0

        _GlowStrength ("Glow Strength", Range(0,5)) = 1.0
        _PhantomBandStrength ("Phantom Band Strength", Range(0,5)) = 1.0
        _PhantomBandPeriod ("Phantom Band Period", Range(1,80)) = 30.0
        _PhantomBandWidth ("Phantom Band Width", Range(0.2,10)) = 3.0

        _DistMin ("Min Distance", Range(0.001,0.1)) = 0.02
        _DistFade ("Distance Fade", Range(0,0.2)) = 0.03

        _UVDistort ("UV Distort", Range(0,0.1)) = 0.02
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
            float4 _TunnelColorA;
            float4 _TunnelColorB;
            float _MoveSpeed;
            float _RaySteps;
            float _StepScale;
            float _IFSIterations;
            float _TileZ;
            float _FoldScale;
            float _SymmetryCount;
            float _GlowStrength;
            float _PhantomBandStrength;
            float _PhantomBandPeriod;
            float _PhantomBandWidth;
            float _DistMin;
            float _DistFade;
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

            float2x2 Rot(float a)
            {
                float s = sin(a), c = cos(a);
                return float2x2(c, s, -s, c);
            }

            float2 PMod(float2 p, float r)
            {
                float pi = 3.14159265359;
                float tau = 6.28318530718;
                float a = atan2(p.x, p.y) + pi / r;
                float n = tau / r;
                a = floor(a / n) * n;
                return mul(Rot(-a), p);
            }

            float BoxSDF(float3 p, float3 b)
            {
                float3 d = abs(p) - b;
                return min(max(d.x, max(d.y, d.z)), 0.0) + length(max(d, 0.0));
            }

            float IFSBox(float3 p, float time)
            {
                // 固定上限，按参数决定有效次数，避免动态循环编译问题
                [unroll(8)]
                for (int i = 0; i < 8; i++)
                {
                    if (i >= (int)_IFSIterations)
                        break;

                    p = abs(p) - _FoldScale;
                    p.xy = mul(Rot(time * 0.3), p.xy);
                    p.xz = mul(Rot(time * 0.1), p.xz);
                }

                p.xz = mul(Rot(time), p.xz);
                return BoxSDF(p, float3(0.4, 0.8, 0.3));
            }

            float Map(float3 p, float time)
            {
                float3 p1 = p;
                p1.x = fmod(p1.x - 5.0, 10.0) - 5.0;
                p1.y = fmod(p1.y - 5.0, 10.0) - 5.0;
                p1.z = fmod(p1.z, _TileZ) - _TileZ * 0.5;
                p1.xy = PMod(p1.xy, _SymmetryCount);
                return IFSBox(p1, time);
            }

            float3 RenderTunnel(float2 uv, float time)
            {
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 p = (uv * 2.0 - 1.0);
                p.x *= aspect;

                float3 cPos = float3(0.0, 0.0, -_MoveSpeed * time);
                float3 cDir = normalize(float3(0.0, 0.0, -1.0));
                float3 cUp = float3(sin(time), 1.0, 0.0);
                float3 cSide = cross(cDir, cUp);

                float3 ray = normalize(cSide * p.x + cUp * p.y + cDir);

                float acc = 0.0;
                float acc2 = 0.0;
                float t = 0.0;

                [loop]
                for (int i = 0; i < 128; i++)
                {
                    if (i >= (int)_RaySteps)
                        break;

                    float3 pos = cPos + ray * t;
                    float dist = Map(pos, time);
                    dist = max(abs(dist), _DistMin);

                    float a = exp(-dist * 3.0) * _GlowStrength;

                    if (fmod(length(pos) + 24.0 * time, _PhantomBandPeriod) < _PhantomBandWidth)
                    {
                        a *= (1.0 + _PhantomBandStrength);
                        acc2 += a;
                    }

                    acc += a;
                    t += dist * _StepScale;
                }

                float3 col = float3(
                    acc * 0.010,
                    acc * 0.011 + acc2 * 0.002,
                    acc * 0.012 + acc2 * 0.005
                );

                float hueMask = saturate(acc * 0.03 + acc2 * 0.02);
                col *= lerp(_TunnelColorA.rgb, _TunnelColorB.rgb, hueMask);
                col *= _BrightnessGain;

                return saturate(col);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 center = i.uv - 0.5;
                float radius = length(center);
                float2 distort = normalize(center + 1e-5) * sin(radius * 18.0 - time * 2.2) * _UVDistort;

                float2 uv = saturate(i.uv + distort);

                fixed4 baseCol = tex2D(_MainTex, uv) * _Color;
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float3 effect = RenderTunnel(uv, time);

                float2 shiftDir = normalize(center + float2(1e-5, 0.0));
                float2 ca = shiftDir * _ChromaticShift;
                float3 chroma;
                chroma.r = RenderTunnel(saturate(uv + ca), time).r;
                chroma.g = effect.g;
                chroma.b = RenderTunnel(saturate(uv - ca), time).b;
                effect = lerp(effect, chroma, 0.65);

                float blendMask = saturate(length(effect) * 1.8);
                float3 finalRgb = lerp(baseCol.rgb, baseCol.rgb + effect, _EffectOpacity * blendMask);

                float effectAlpha = saturate(baseCol.a + blendMask - radius * _DistFade);
                float finalAlpha = lerp(baseCol.a, effectAlpha, _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
