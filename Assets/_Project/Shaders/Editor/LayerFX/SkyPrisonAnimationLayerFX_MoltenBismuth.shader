Shader "SkyPrison/Animation Layer FX/Molten Bismuth"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.25
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.15

        _MetalBase ("Metal Base", Color) = (0.18, 0.18, 0.20, 1.0)
        _OxideA ("Oxide Color A", Color) = (0.20, 0.85, 1.00, 1.0)
        _OxideB ("Oxide Color B", Color) = (0.85, 0.20, 1.00, 1.0)
        _OxideC ("Oxide Color C", Color) = (1.00, 0.75, 0.20, 1.0)
        _HighlightColor ("Highlight Color", Color) = (1.00, 0.98, 0.92, 1.0)

        _MainScale ("Main Scale", Range(0.2,8.0)) = 1.2
        _FlowStrength ("Flow Strength", Range(0,2.0)) = 0.55
        _FlowSpeed ("Flow Speed", Range(-5,5)) = 1.0
        _SwirlStrength ("Swirl Strength", Range(0,2.0)) = 0.75
        _CellScale ("Cell Scale", Range(0.5,20.0)) = 6.0
        _LayeredNoise ("Layered Noise", Range(0,2.0)) = 1.0

        _RefractionStrength ("Refraction Strength", Range(0,0.08)) = 0.025
        _IridescenceStrength ("Iridescence Strength", Range(0,4.0)) = 1.35
        _EdgeGlow ("Edge Glow", Range(0,4.0)) = 1.15
        _SpecularStrength ("Specular Strength", Range(0,4.0)) = 1.2
        _RimStrength ("Rim Strength", Range(0,4.0)) = 0.65

        _FacetStrength ("Facet Strength", Range(0,2.0)) = 0.65
        _BandingStrength ("Banding Strength", Range(0,2.0)) = 0.55
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.0035
        _NoiseAmount ("Noise Amount", Range(0,1.0)) = 0.018
        _VignetteStrength ("Vignette Strength", Range(0,1.0)) = 0.12

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

            float4 _MetalBase;
            float4 _OxideA;
            float4 _OxideB;
            float4 _OxideC;
            float4 _HighlightColor;

            float _MainScale;
            float _FlowStrength;
            float _FlowSpeed;
            float _SwirlStrength;
            float _CellScale;
            float _LayeredNoise;

            float _RefractionStrength;
            float _IridescenceStrength;
            float _EdgeGlow;
            float _SpecularStrength;
            float _RimStrength;

            float _FacetStrength;
            float _BandingStrength;
            float _ChromaticShift;
            float _NoiseAmount;
            float _VignetteStrength;

            float _AlphaMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
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
                    p = p * 2.03 + float2(17.13, 9.27);
                    a *= 0.5;
                }
                return v;
            }

            float VoronoiLike(float2 p)
            {
                float2 g = floor(p);
                float2 f = frac(p);
                float md = 10.0;

                [unroll(3)]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll(3)]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2((float)x, (float)y);
                        float2 h = Hash22(g + o) * 0.5 + 0.5;
                        float2 r = o + h - f;
                        float d = dot(r, r);
                        md = min(md, d);
                    }
                }

                return sqrt(md);
            }

            float2 FlowWarp(float2 p, float t)
            {
                float2 q = float2(
                    FBM(p + float2(0.0, t * 0.12)),
                    FBM(p + float2(5.2, -t * 0.16))
                );

                float2 r = float2(
                    FBM(p + 4.0 * q + float2(1.7, 9.2) + t * 0.09),
                    FBM(p + 4.0 * q + float2(8.3, 2.8) - t * 0.11)
                );

                float angle = (r.x - r.y) * 6.2831853 * _SwirlStrength;
                float s = sin(angle), c = cos(angle);
                float2x2 rot = float2x2(c, -s, s, c);

                return mul(rot, p) + (q + r - 1.0) * _FlowStrength;
            }

            float3 OxidePalette(float x)
            {
                float3 c = lerp(_MetalBase.rgb, _OxideA.rgb, smoothstep(0.08, 0.35, x));
                c = lerp(c, _OxideB.rgb, smoothstep(0.28, 0.68, x));
                c = lerp(c, _OxideC.rgb, smoothstep(0.55, 1.00, x));
                c = lerp(c, _HighlightColor.rgb, smoothstep(0.92, 1.35, x));
                return c;
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

            float3 RenderBismuth(float2 p, float t, out float effectMask, out float2 gradOut)
            {
                float2 q = FlowWarp(p * _CellScale, t);
                float h1 = FBM(q);
                float h2 = FBM(q * 1.93 + float2(3.7, -1.9));
                float cell = 1.0 - VoronoiLike(q * 0.85);

                float surface = h1 * 0.65 + h2 * 0.35 * _LayeredNoise + cell * _FacetStrength;
                float band = frac(surface * 4.0 + cell * 1.5);
                surface += band * _BandingStrength * 0.2;

                float e = 0.0025;
                float sx = FBM(FlowWarp((p + float2(e, 0.0)) * _CellScale, t));
                float sy = FBM(FlowWarp((p + float2(0.0, e)) * _CellScale, t));
                float2 grad = float2(sx - h1, sy - h1) / e;
                gradOut = grad;

                float edge = smoothstep(0.10, 0.42, cell);
                float ridge = pow(saturate(1.0 - abs(frac(surface * 3.5) - 0.5) * 2.0), 3.0);

                float irid = surface * 1.25 + ridge * 0.85 + edge * 0.65;
                float3 col = OxidePalette(irid * _IridescenceStrength);

                float3 n = normalize(float3(-grad * 0.18, 1.0));
                float3 l = normalize(float3(0.35, 0.55, 1.0));
                float spec = pow(max(0.0, dot(n, l)), 28.0) * _SpecularStrength;
                float fres = pow(1.0 - saturate(n.z), 3.0) * _RimStrength;

                col += _HighlightColor.rgb * spec;
                col += _OxideA.rgb * edge * _EdgeGlow * 0.18;
                col += _HighlightColor.rgb * ridge * 0.15;
                col += fres * _HighlightColor.rgb * 0.25;

                effectMask = saturate(0.35 + edge * 0.45 + ridge * 0.35 + spec * 0.25);
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y) * _FlowSpeed;

                fixed4 baseCol = SampleSprite(i.uv);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float2 uv = i.uv * 2.0 - 1.0;
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                uv.x *= aspect;
                uv *= _MainScale;

                float2 grad;
                float effectMask;
                float3 fx = RenderBismuth(uv, time, effectMask, grad);

                float2 refractOffset = grad * _RefractionStrength;
                float3 refractedBase = SampleSprite(i.uv + refractOffset).rgb;

                float2 caDir = normalize(uv + float2(1e-5, 0.0)) * _ChromaticShift;
                float tmpMask; float2 tmpGrad;
                float r = RenderBismuth(uv + caDir, time, tmpMask, tmpGrad).r;
                float g = fx.g;
                float b = RenderBismuth(uv - caDir, time, tmpMask, tmpGrad).b;
                fx = float3(r, g, b);

                float texel = max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 2.0;
                float edgeAlpha = max(max(AlphaAt(i.uv + float2(texel, 0)), AlphaAt(i.uv - float2(texel, 0))),
                                      max(AlphaAt(i.uv + float2(0, texel)), AlphaAt(i.uv - float2(0, texel))));
                float alphaRim = saturate(edgeAlpha - baseCol.a) * 0.25;
                fx += alphaRim * _HighlightColor.rgb;

                float rad = length(uv);
                float vignette = smoothstep(1.75, 0.15, rad);
                fx *= lerp(1.0, vignette, _VignetteStrength);

                float noise = (Hash21(floor(i.uv * _MainTex_TexelSize.zw) + floor(time * 24.0)) - 0.5) * _NoiseAmount;
                fx += noise;

                fx = 1.0 - exp(-fx * 1.18);
                fx = pow(saturate(fx), 0.92.xxx);
                fx *= _BrightnessGain;

                float3 effectRgb = fx + refractedBase * _BaseMix;
                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + (effectMask + alphaRim) * 0.18), _AlphaMode);
                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
