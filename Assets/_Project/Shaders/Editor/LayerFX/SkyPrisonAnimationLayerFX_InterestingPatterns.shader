Shader "SkyPrison/Animation Layer FX/Interesting Patterns"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.22
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.10

        _ColorA ("Color A", Color) = (1.00, 0.62, 0.18, 1.0)
        _ColorB ("Color B", Color) = (1.00, 0.82, 0.35, 1.0)
        _ColorC ("Color C", Color) = (1.00, 0.96, 0.78, 1.0)

        _TileScale ("Tile Scale", Range(1,64)) = 20.0
        _RotationAngle ("Base Rotation Angle", Range(-180,180)) = -30.0
        _RotationSpeed ("Rotation Speed", Range(-20,20)) = 1.0
        _PulseSpeed ("Pulse Speed", Range(-20,20)) = 1.0
        _DistanceFrequency ("Distance Frequency", Range(0,80)) = 20.0

        _EdgeWidth ("Edge Width", Range(0.001,0.25)) = 0.050
        _PatternThreshold ("Pattern Threshold", Range(0,1)) = 0.95
        _DistanceAdd ("Distance Add", Range(0,1)) = 0.10
        _PatternScale ("Pattern Scale", Range(0,2)) = 0.60

        _RadialGlow ("Radial Glow", Range(0,2)) = 0.25
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.002
        _UVDistort ("UV Distort", Range(0,0.08)) = 0.006
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.008
        _VignetteStrength ("Vignette Strength", Range(0,1)) = 0.12

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

            float4 _ColorA;
            float4 _ColorB;
            float4 _ColorC;

            float _TileScale;
            float _RotationAngle;
            float _RotationSpeed;
            float _PulseSpeed;
            float _DistanceFrequency;

            float _EdgeWidth;
            float _PatternThreshold;
            float _DistanceAdd;
            float _PatternScale;

            float _RadialGlow;
            float _ChromaticShift;
            float _UVDistort;
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
                float2 uv : TEXCOORD0;
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2x2 Rot(float a)
            {
                float s = sin(a), c = cos(a);
                return float2x2(c, -s, s, c);
            }

            float3 PatternSample(float2 uv, float time)
            {
                float aspect = _MainTex_TexelSize.y / max(_MainTex_TexelSize.x, 1e-5);
                float2 p = uv;
                p.y *= aspect;
                p -= float2(0.5, 0.5 * aspect);

                float rot = radians(_RotationAngle) - time * _RotationSpeed;
                p = mul(Rot(rot), p);

                float2 scaledUv = _TileScale * p;
                float2 tile = frac(scaledUv);
                float tileDist = min(min(tile.x, 1.0 - tile.x), min(tile.y, 1.0 - tile.y));
                float squareDist = length(floor(scaledUv));

                float edge = sin(time * _PulseSpeed - squareDist * _DistanceFrequency);
                edge = frac(edge * edge + 1000.0);

                float value = lerp(tileDist, 1.0 - tileDist, step(1.0, edge));
                float edgeShape = pow(abs(1.0 - edge), 2.2) * 0.5;

                value = smoothstep(edgeShape - _EdgeWidth, edgeShape, _PatternThreshold * value);
                value += squareDist * _DistanceAdd;
                value *= _PatternScale;

                float v2 = saturate(pow(saturate(value), 2.0));
                float v15 = saturate(pow(saturate(value), 1.5));
                float v12 = saturate(pow(saturate(value), 1.2));

                float radial = smoothstep(1.35, 0.05, length(p));
                float3 col = lerp(_ColorA.rgb * v2, _ColorB.rgb * v15, 0.55);
                col = lerp(col, _ColorC.rgb * v12, 0.35 + radial * _RadialGlow * 0.25);
                col += radial * _RadialGlow * _ColorB.rgb * 0.12;
                return saturate(col);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);

                float2 centered = i.uv - 0.5;
                float radius = length(centered);
                float2 uvDistort = normalize(centered + float2(1e-5, 0.0)) * sin(radius * 18.0 - time * 1.6) * _UVDistort;

                fixed4 baseCol = SampleSprite(i.uv + uvDistort);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float2 ca = normalize(centered + float2(1e-5, 0.0)) * _ChromaticShift;
                float3 p0 = PatternSample(i.uv + uvDistort, time);
                float3 pr = PatternSample(i.uv + uvDistort + ca, time);
                float3 pb = PatternSample(i.uv + uvDistort - ca, time);
                float3 fx = float3(pr.r, p0.g, pb.b);

                float vignette = smoothstep(1.35, 0.08, radius * 2.0);
                fx *= lerp(1.0, vignette, _VignetteStrength);

                float noise = (Hash21(floor(i.uv * _MainTex_TexelSize.zw) + floor(time * 24.0)) - 0.5) * _NoiseAmount;
                fx += noise;
                fx *= _BrightnessGain;

                float3 effectRgb = baseCol.rgb * _BaseMix + fx;
                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float effectMask = saturate(length(fx) * 0.35);
                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + effectMask * 0.18), _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
