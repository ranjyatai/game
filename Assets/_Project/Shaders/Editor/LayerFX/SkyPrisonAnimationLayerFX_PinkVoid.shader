Shader "SkyPrison/Animation Layer FX/Pink Void"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.18
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.12

        _ColorR ("Pink Void R", Color) = (1.00, 0.22, 0.55, 1.0)
        _ColorG ("Pink Void G", Color) = (1.00, 0.42, 0.78, 1.0)
        _ColorB ("Pink Void B", Color) = (0.72, 0.22, 1.00, 1.0)
        _CoreColor ("Void Core Color", Color) = (1.00, 0.84, 1.00, 1.0)

        _MainScale ("Main Scale", Range(0.2,5.0)) = 1.0
        _RadialScale ("Radial Scale", Range(0.1,4.0)) = 1.5
        _AngularTwist ("Angular Twist", Range(-5,5)) = 1.1
        _VoidPowerA ("Radial Power A", Range(0.05,2.0)) = 0.3
        _VoidPowerB ("Radial Power B", Range(0.05,2.0)) = 0.5

        _TimeSpeed ("Time Speed", Range(-5,5)) = 0.2
        _FlowSpeed ("Flow Speed", Range(-5,5)) = 1.0
        _NoiseStrength ("Noise Strength", Range(0,3)) = 1.0
        _NoiseScale ("Noise Scale", Range(0.2,6.0)) = 1.0

        _FbmOctaveX ("FBM X Octaves", Range(1,6)) = 3
        _FbmOctaveY ("FBM Y Octaves", Range(1,6)) = 4
        _FbmOctaveR1 ("FBM R1 Octaves", Range(1,7)) = 5
        _FbmOctaveR2 ("FBM R2 Octaves", Range(1,7)) = 6

        _VoidDensity ("Void Density", Range(0.1,12)) = 5.0
        _ColorDivider ("Color Divider", Range(0.5,12)) = 6.0
        _ThresholdRMin ("R Threshold Min", Range(0,1)) = 0.30
        _ThresholdRMax ("R Threshold Max", Range(0,1)) = 0.40
        _ThresholdGMin ("G Threshold Min", Range(0,1)) = 0.40
        _ThresholdGMax ("G Threshold Max", Range(0,1)) = 0.55
        _ThresholdBMin ("B Threshold Min", Range(0,1)) = 0.20
        _ThresholdBMax ("B Threshold Max", Range(0,1)) = 0.55

        _SwirlStrength ("Swirl Strength", Range(0,2)) = 0.45
        _CoreGlow ("Core Glow", Range(0,3)) = 0.45
        _EdgeWarp ("Edge Warp", Range(0,2)) = 0.25
        _UVDistort ("UV Distort", Range(0,0.08)) = 0.012
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.004
        _NoiseAmount ("Film Noise", Range(0,1)) = 0.012
        _VignetteStrength ("Vignette Strength", Range(0,1)) = 0.22

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

            float4 _ColorR;
            float4 _ColorG;
            float4 _ColorB;
            float4 _CoreColor;

            float _MainScale;
            float _RadialScale;
            float _AngularTwist;
            float _VoidPowerA;
            float _VoidPowerB;

            float _TimeSpeed;
            float _FlowSpeed;
            float _NoiseStrength;
            float _NoiseScale;

            float _FbmOctaveX;
            float _FbmOctaveY;
            float _FbmOctaveR1;
            float _FbmOctaveR2;

            float _VoidDensity;
            float _ColorDivider;
            float _ThresholdRMin;
            float _ThresholdRMax;
            float _ThresholdGMin;
            float _ThresholdGMax;
            float _ThresholdBMin;
            float _ThresholdBMax;

            float _SwirlStrength;
            float _CoreGlow;
            float _EdgeWarp;
            float _UVDistort;
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

            fixed4 SampleSprite(float2 uv)
            {
                float inside =
                    step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);

                fixed4 c = tex2D(_MainTex, saturate(uv)) * _Color;
                c.a *= inside;
                return c;
            }

            float Hash11(float n)
            {
                return frac(sin(n) * 43758.5453123);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0,0,0));
                float n100 = Hash31(i + float3(1,0,0));
                float n010 = Hash31(i + float3(0,1,0));
                float n110 = Hash31(i + float3(1,1,0));
                float n001 = Hash31(i + float3(0,0,1));
                float n101 = Hash31(i + float3(1,0,1));
                float n011 = Hash31(i + float3(0,1,1));
                float n111 = Hash31(i + float3(1,1,1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            float FBM3D(float3 p, int octaves)
            {
                float v = 0.0;
                float a = 0.5;
                float norm = 0.0;

                [loop]
                for (int i = 0; i < 7; i++)
                {
                    if (i >= octaves)
                        break;

                    v += Noise3D(p) * a;
                    norm += a;

                    // Slightly non-axis aligned evolution to avoid grid feeling.
                    p = p * 2.03 + float3(13.17, 7.31, 5.11);
                    p.xy = float2(p.x * 0.82 - p.y * 0.57, p.x * 0.57 + p.y * 0.82);
                    a *= 0.5;
                }

                return v / max(norm, 1e-5);
            }

            float2 Rot(float2 p, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            float3 PinkVoidSample(float2 uv01, float time)
            {
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 uv = uv01 * 2.0 - 1.0;
                uv.x *= aspect;
                uv *= _MainScale;

                float lenUv = length(uv);
                float swirl = sin(lenUv * 7.0 - time * 0.7) * _SwirlStrength;
                uv = Rot(uv, swirl);

                float2 st = float2(
                    lenUv * _RadialScale,
                    atan2(uv.y, uv.x)
                );

                st.y += st.x * _AngularTwist;

                float t = time * _TimeSpeed * _FlowSpeed;
                float px = pow(max(st.x, 0.0001), _VoidPowerA);
                float py = pow(max(st.x, 0.0001), _VoidPowerB);

                float x = FBM3D(
                    float3(
                        sin(st.y),
                        cos(st.y),
                        px + t * 0.1
                    ) * _NoiseScale,
                    (int)_FbmOctaveX
                );

                float y = FBM3D(
                    float3(
                        cos(1.0 - st.y),
                        sin(1.0 - st.y),
                        py + t * 0.1
                    ) * _NoiseScale,
                    (int)_FbmOctaveY
                );

                float r = FBM3D(
                    float3(
                        x,
                        y,
                        st.x + t * 0.3
                    ) * _NoiseScale,
                    (int)_FbmOctaveR1
                );

                r = FBM3D(
                    float3(
                        r - x,
                        r - y,
                        r + t * 0.3
                    ) * _NoiseScale,
                    (int)_FbmOctaveR2
                );

                float c = (r * _NoiseStrength + st.x * _VoidDensity) / max(_ColorDivider, 0.001);

                float rr = smoothstep(_ThresholdRMin, _ThresholdRMax, c);
                float gg = smoothstep(_ThresholdGMin, _ThresholdGMax, c);
                float bb = smoothstep(_ThresholdBMin, _ThresholdBMax, c);

                float3 col = _ColorR.rgb * rr + _ColorG.rgb * gg + _ColorB.rgb * bb;
                col /= max(rr + gg + bb, 1.0);

                float core = smoothstep(0.85, 0.0, lenUv);
                float edgeWarp = smoothstep(0.15, 1.2, abs(r - 0.5)) * _EdgeWarp;
                col += _CoreColor.rgb * core * _CoreGlow;
                col += _ColorB.rgb * edgeWarp * 0.12;

                return saturate(col);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 center = i.uv - 0.5;
                float radius = length(center);
                float2 uvDistort = normalize(center + float2(1e-5, 0.0)) * sin(radius * 19.0 - time * 1.2) * _UVDistort;

                fixed4 baseCol = SampleSprite(i.uv + uvDistort);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float3 fx = PinkVoidSample(i.uv + uvDistort, time);

                if (_ChromaticShift > 0.00001)
                {
                    float2 ca = normalize(center + float2(1e-5, 0.0)) * _ChromaticShift;
                    float3 rFx = PinkVoidSample(i.uv + uvDistort + ca, time);
                    float3 bFx = PinkVoidSample(i.uv + uvDistort - ca, time);
                    fx = float3(rFx.r, fx.g, bFx.b);
                }

                float vignette = smoothstep(1.35, 0.08, radius * 2.0);
                fx *= lerp(1.0, vignette, _VignetteStrength);

                float noise = (Hash31(float3(floor(i.uv * _MainTex_TexelSize.zw), floor(time * 24.0))) - 0.5) * _NoiseAmount;
                fx += noise;

                fx = 1.0 - exp(-fx * 1.12);
                fx = pow(saturate(fx), 0.92.xxx);
                fx *= _BrightnessGain;

                float3 effectRgb = fx + baseCol.rgb * _BaseMix;
                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float effectMask = saturate(length(fx) * 0.25);
                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + effectMask * 0.18), _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
