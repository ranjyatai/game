Shader "Sky Prison/LootDrop Hologram"
{
    Properties
    {
        _Color               ("发光颜色",    Color)            = (0.55, 0.95, 1.0, 1.0)
        _RimPower            ("边缘锐度",   Range(0.5, 4.0))  = 1.5
        _RimScale            ("边缘范围",   Range(0.0, 2.0))  = 0.85
        _RimBrightness       ("边缘亮度",   Range(0.1, 3.0))  = 1.5
        _BodyAlpha           ("内部基础透明", Range(0.0, 0.3)) = 0.0
        _BodyBrightness      ("内部亮度",   Range(0.0, 0.5))  = 0.0
        _GridDensity         ("网格密度",   Range(0.5, 20.0)) = 6.0
        _GridLineWidth       ("格线粗细",   Range(0.01, 0.49))= 0.05
        _GridBright          ("格线亮度",   Range(0.0, 2.0))  = 0.9
        _PulseSpeed          ("整体呼吸速度", Range(0.0, 4.0)) = 1.8
        _WaveSpeed           ("扫描波速度", Range(0.1, 6.0))  = 1.2
        _WaveFreq            ("扫描波频率", Range(0.5, 8.0))  = 2.5
        _WaveMin             ("波谷透明度", Range(0.0, 1.0))  = 0.05
        _WaveMax             ("波峰透明度", Range(0.0, 1.0))  = 0.45
        _CharMaskThreshold   ("角色蒙版阈值", Range(0.005, 1.0)) = 0.02
        _OccludeOutlineColor ("遮挡描边颜色", Color)           = (1.0, 1.0, 1.0, 0.90)
        _OccludeOutlineWidth ("遮挡描边宽度", Range(0.0, 0.06))= 0.022
        // ── LV9 七彩模式 ──
        _UseRainbow          ("七彩模式(LV9)", Range(0,1))     = 0
        _RainbowFreq         ("彩虹空间频率", Range(0.1, 4.0)) = 0.5
        _RainbowSpeed        ("彩虹流动速度", Range(0.0, 1.0)) = 0.08
        _RainbowSat          ("彩虹饱和度",   Range(0.1, 1.0)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        // ══════════════════════════════════════════════════════════════
        // 共用 CBUFFER 宏（两个 Pass 展开使用，保证 SRP Batcher 一致性）
        // ══════════════════════════════════════════════════════════════

        // Pass 1 : 全息主体
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest  LEqual
            Cull   Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _OccludeOutlineColor;
                float  _RimPower;
                float  _RimScale;
                float  _RimBrightness;
                float  _BodyAlpha;
                float  _BodyBrightness;
                float  _GridDensity;
                float  _GridLineWidth;
                float  _GridBright;
                float  _PulseSpeed;
                float  _WaveSpeed;
                float  _WaveFreq;
                float  _WaveMin;
                float  _WaveMax;
                float  _CharMaskThreshold;
                float  _OccludeOutlineWidth;
                float  _HoloRootZ;
                float  _ObjectWorldRadius;
                float  _UseRainbow;
                float  _RainbowFreq;
                float  _RainbowSpeed;
                float  _RainbowSat;
                float3 _ObjectWorldCenter;
            CBUFFER_END

            TEXTURE2D(_SP_CharPresence); SAMPLER(sampler_SP_CharPresence);
            float _SP_CharWorldZ;
            float _SP_CharPresenceActive;

            struct Attributes { float4 posOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 posHCS    : SV_POSITION;
                float3 posWS     : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // HSV → RGB（用于位置彩虹）
            float3 HsvToRgb(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs vi = GetVertexPositionInputs(IN.posOS.xyz);
                OUT.posHCS    = vi.positionCS;
                OUT.screenPos = ComputeScreenPos(vi.positionCS);
                OUT.posWS     = vi.positionWS;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // 角色遮挡由 SortingGroup sortingOrder 处理（角色=0 > 掉落物≈-800）
                // CharPresence 已不再需要：透明物件按 sortingOrder 排序，高值后渲染=压在前面

                // ── 轴距离 rim ────────────────────────────────────────────
                float2 offsetXZ = IN.posWS.xz - _ObjectWorldCenter.xz;
                float  normDist = length(offsetXZ) / max(_ObjectWorldRadius, 0.001);
                float  rim      = saturate(pow(normDist * (1.0 + _RimScale), _RimPower));
                float  rimA     = sqrt(rim);

                // ── 基础颜色（普通品级 or 七彩模式）─────────────────────
                float3 baseColor;
                if (_UseRainbow > 0.5)
                {
                    // 低频正弦：一个模型高度内只有少量色相变化，色带宽而柔和
                    float t   = sin(IN.posWS.y * _RainbowFreq * 3.14159) * 0.5 + 0.5;
                    float hue = frac(_Time.y * _RainbowSpeed + t * 0.6);
                    float3 raw = HsvToRgb(float3(hue, 1.0, 1.0));
                    // 大量混白：保持原亮度，只是颜色很淡
                    baseColor = lerp(float3(1,1,1), raw, _RainbowSat);
                }
                else
                {
                    baseColor = _Color.rgb;
                }

                // ── 世界空间 XY 网格 ──────────────────────────────────────
                float2 gridPos  = float2(IN.posWS.x, IN.posWS.y) * _GridDensity;
                float2 cellFrac = frac(gridPos);
                float2 toLine   = min(cellFrac, 1.0 - cellFrac);
                float  aa       = fwidth(min(toLine.x, toLine.y));
                float  gridMask = 1.0 - smoothstep(_GridLineWidth - aa,
                                                    _GridLineWidth + aa,
                                                    min(toLine.x, toLine.y));
                float  gridA    = gridMask * _GridBright * (1.0 - rim * 0.6);

                // ── 从下到上扫描波 ────────────────────────────────────────
                float wavePh    = frac(IN.posWS.y * _WaveFreq - _Time.y * _WaveSpeed);
                float waveMask  = saturate(pow(wavePh, 2.5) * (1.0 - pow(wavePh, 0.3)) * 2.5);
                float scanAlpha = lerp(_WaveMin, _WaveMax, waveMask);

                // ── 整体微呼吸 ────────────────────────────────────────────
                float pulse = 0.9 + 0.1 * sin(_Time.y * _PulseSpeed + IN.posWS.y * 2.5);

                // ── 合成 ─────────────────────────────────────────────────
                float3 col  = baseColor * (_BodyBrightness + _RimBrightness * rimA + gridA + waveMask * 0.6) * pulse;
                float  alpha = saturate(scanAlpha * (1.0 - rim * 0.7) + rimA + gridA * 0.4);

                return float4(col, alpha);
            }
            ENDHLSL
        }

        // Pass 2 : 遮挡描边（已由 RT 描边系统接管，此 pass 保留但默认 alpha=0 禁用）
        Pass
        {
            Name "OccludedOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest  Greater
            ZWrite Off
            Cull   Front
            Blend  SrcAlpha OneMinusSrcAlpha
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vert_outline
            #pragma fragment frag_outline
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _OccludeOutlineColor;
                float  _RimPower;
                float  _RimScale;
                float  _RimBrightness;
                float  _BodyAlpha;
                float  _BodyBrightness;
                float  _GridDensity;
                float  _GridLineWidth;
                float  _GridBright;
                float  _PulseSpeed;
                float  _WaveSpeed;
                float  _WaveFreq;
                float  _WaveMin;
                float  _WaveMax;
                float  _CharMaskThreshold;
                float  _OccludeOutlineWidth;
                float  _HoloRootZ;
                float  _ObjectWorldRadius;
                float  _UseRainbow;
                float  _RainbowFreq;
                float  _RainbowSpeed;
                float  _RainbowSat;
                float3 _ObjectWorldCenter;
            CBUFFER_END

            struct AttrOutline  { float4 posOS : POSITION; float3 normOS : NORMAL; };
            struct VaryOutline  { float4 posHCS : SV_POSITION; };

            VaryOutline vert_outline(AttrOutline IN)
            {
                VaryOutline OUT;
                float4 posCS  = TransformObjectToHClip(IN.posOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normOS);
                float4 normCS = TransformWorldToHClip(normWS);
                posCS.xy += normalize(normCS.xy) * _OccludeOutlineWidth * posCS.w;
                OUT.posHCS = posCS;
                return OUT;
            }

            float4 frag_outline(VaryOutline IN) : SV_Target
            {
                return _OccludeOutlineColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
