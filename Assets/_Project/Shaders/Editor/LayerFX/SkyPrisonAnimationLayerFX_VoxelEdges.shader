Shader "SkyPrison/Animation Layer FX/Voxel Edges"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _EffectOpacity ("Effect Opacity", Range(0,1)) = 1.0
        _BaseMix ("Base Mix", Range(0,1)) = 0.45
        _BrightnessGain ("Brightness Gain", Range(0,4)) = 1.25

        _VoxelColor ("Voxel Face Color", Color) = (0.20, 0.34, 0.42, 1.0)
        _EdgeColor ("Edge Glow Color", Color) = (1.0, 0.42, 0.04, 1.0)
        _ShadowColor ("Edge Shadow Color", Color) = (0.02, 0.04, 0.05, 1.0)

        _VoxelScale ("Voxel Scale", Range(4,160)) = 42.0
        _DepthSteps ("Depth Steps", Range(2,32)) = 10.0
        _DepthSpeed ("Depth Speed", Range(-10,10)) = 1.0

        _TerrainScale ("Terrain Noise Scale", Range(0.1,8)) = 1.6
        _TerrainHeight ("Terrain Height", Range(0,4)) = 1.25
        _EdgeWidth ("Edge Width", Range(0.001,0.45)) = 0.12
        _EdgeSoftness ("Edge Softness", Range(0.001,0.45)) = 0.09
        _EdgeGlow ("Edge Glow", Range(0,8)) = 2.35

        _FaceShade ("Face Shade", Range(0,2)) = 0.75
        _AmbientOcclusion ("Fake AO", Range(0,2)) = 0.85
        _LinePulse ("Line Pulse", Range(0,2)) = 0.45

        _PixelSnap ("Pixel Snap", Range(0,1)) = 1.0
        _UVDistort ("UV Distort", Range(0,0.08)) = 0.006
        _ChromaticShift ("Chromatic Shift", Range(0,0.03)) = 0.002
        _NoiseAmount ("Noise Amount", Range(0,1)) = 0.018

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

            float4 _VoxelColor;
            float4 _EdgeColor;
            float4 _ShadowColor;

            float _VoxelScale;
            float _DepthSteps;
            float _DepthSpeed;

            float _TerrainScale;
            float _TerrainHeight;
            float _EdgeWidth;
            float _EdgeSoftness;
            float _EdgeGlow;

            float _FaceShade;
            float _AmbientOcclusion;
            float _LinePulse;

            float _PixelSnap;
            float _UVDistort;
            float _ChromaticShift;
            float _NoiseAmount;
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
                float h = 0.0;
                float a = 0.5;

                [unroll(4)]
                for (int i = 0; i < 4; i++)
                {
                    h += Noise(p) * a;
                    p = p * 2.03 + float2(17.13, 9.27);
                    a *= 0.5;
                }

                return h * 0.5 + 0.5;
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

            float EdgeMaskFromCell(float2 cellUv)
            {
                float2 st = 1.0 - cellUv;

                float edgeX = max(
                    smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, cellUv.x),
                    smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, st.x));

                float edgeY = max(
                    smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, cellUv.y),
                    smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, st.y));

                float cornerA = smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, cellUv.x * cellUv.y);
                float cornerB = smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, st.x * cellUv.y);
                float cornerC = smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, st.x * st.y);
                float cornerD = smoothstep(1.0 - _EdgeWidth - _EdgeSoftness, 1.0 - _EdgeWidth, cellUv.x * st.y);

                return saturate(max(max(edgeX, edgeY), max(max(cornerA, cornerB), max(cornerC, cornerD))));
            }

            float HeightField(float2 grid, float time)
            {
                float2 p = grid * _TerrainScale;
                float h = FBM(p + float2(0.0, time * _DepthSpeed * 0.35));
                h += FBM(p * 2.1 + float2(time * 0.2, -time * 0.15)) * 0.35;
                return h * _TerrainHeight;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = max(max(_SkyPrisonTime, _PreviewTime), _Time.y);
                float2 uv = i.uv;

                float2 center = uv - 0.5;
                float2 waveDistort = float2(
                    sin((center.y * 18.0 + time * 1.7)) * _UVDistort,
                    cos((center.x * 13.0 - time * 1.2)) * _UVDistort * 0.45);

                fixed4 baseCol = SampleSprite(uv + waveDistort);
                if (baseCol.a <= 0.0001)
                    return fixed4(0,0,0,0);

                float2 texSize = _MainTex_TexelSize.zw;
                float2 gridUv = uv * _VoxelScale;

                float2 snappedGrid = floor(gridUv);
                float2 cellUv = frac(gridUv);

                // Optional pixel/voxel snapping for harder, blockier look.
                float2 snappedUv = (snappedGrid + 0.5) / max(_VoxelScale, 1.0);
                float2 sampleUv = lerp(uv, snappedUv, _PixelSnap);

                // Pseudo depth slices: gives the sense of voxel terrain layers moving under surface.
                float depth = floor(HeightField(snappedGrid / max(_VoxelScale, 1.0), time) * max(_DepthSteps, 1.0));
                float depthN = depth / max(_DepthSteps, 1.0);

                float hC = HeightField(snappedGrid / max(_VoxelScale, 1.0), time);
                float hR = HeightField((snappedGrid + float2(1,0)) / max(_VoxelScale, 1.0), time);
                float hU = HeightField((snappedGrid + float2(0,1)) / max(_VoxelScale, 1.0), time);
                float hL = HeightField((snappedGrid - float2(1,0)) / max(_VoxelScale, 1.0), time);
                float hD = HeightField((snappedGrid - float2(0,1)) / max(_VoxelScale, 1.0), time);

                float heightDiff = abs(hC - hR) + abs(hC - hU) + abs(hC - hL) + abs(hC - hD);
                heightDiff = saturate(heightDiff * 1.8);

                float edge = EdgeMaskFromCell(cellUv);
                float exposedEdge = saturate(edge * (0.35 + heightDiff * 1.2));

                // Dark wireframe/voxel creases.
                float crease = edge * (1.0 - heightDiff * 0.35);
                float ao = saturate(1.0 - (edge * 0.35 + heightDiff * 0.28) * _AmbientOcclusion);

                float fakeNormalShade = saturate(0.35 + hC * 0.55 + (hR - hL) * 0.45 + (hU - hD) * 0.25);
                float faceShade = lerp(1.0, fakeNormalShade, _FaceShade);

                float pulse = 0.75 + 0.25 * sin(time * 4.0 + depth * 0.7);
                pulse = lerp(1.0, pulse, _LinePulse);

                float3 spriteSnap = SampleSprite(sampleUv + waveDistort).rgb;
                float3 faceCol = lerp(_VoxelColor.rgb, spriteSnap, _BaseMix);
                faceCol *= faceShade * ao;

                float3 edgeGlow = _EdgeColor.rgb * exposedEdge * _EdgeGlow * pulse;
                float3 shadowLine = _ShadowColor.rgb * crease * 0.85;

                float texel = max(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * 2.0;
                float edgeAlpha = max(max(AlphaAt(uv + float2(texel, 0)), AlphaAt(uv - float2(texel, 0))),
                                      max(AlphaAt(uv + float2(0, texel)), AlphaAt(uv - float2(0, texel))));
                float alphaRim = saturate(edgeAlpha - baseCol.a) * 0.35;

                float2 ca = normalize(center + float2(1e-5, 0.0)) * _ChromaticShift * exposedEdge;
                float3 split;
                split.r = SampleSprite(sampleUv + ca + waveDistort).r;
                split.g = spriteSnap.g;
                split.b = SampleSprite(sampleUv - ca + waveDistort).b;

                float noise = (Hash21(snappedGrid + floor(time * 18.0)) - 0.5) * _NoiseAmount;

                float3 effectRgb = lerp(faceCol, split, exposedEdge * 0.35);
                effectRgb = effectRgb - shadowLine + edgeGlow + alphaRim * _EdgeColor.rgb;
                effectRgb += noise;
                effectRgb *= _BrightnessGain;

                float3 finalRgb = lerp(baseCol.rgb, saturate(effectRgb), _EffectOpacity);

                float effectMask = saturate(exposedEdge + alphaRim);
                float finalAlpha = lerp(baseCol.a, saturate(baseCol.a + effectMask * 0.18), _AlphaMode);

                return fixed4(saturate(finalRgb), finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
