Shader "Hidden/SP/VFXHueShift"
{
    Properties
    {
        _MainTex          ("粒子贴图", 2D)      = "white" {}
        _TargetColor      ("目标颜色", Color)   = (0, 1, 0.5, 1)
        _Brightness       ("亮度",  Range(0.5, 3.0)) = 1.0
        _CharMaskThreshold("角色蒙版阈值", Range(0.005, 1.0)) = 0.02
        _HoloRootZ        ("掉落物根节点Z", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        // Alpha 混合（柔和，不叠白）
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "VFXHueShift"
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);          SAMPLER(sampler_MainTex);
            TEXTURE2D(_SP_CharPresence);  SAMPLER(sampler_SP_CharPresence);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TargetColor;
                float  _Brightness;
                float  _CharMaskThreshold;
                float  _HoloRootZ;
            CBUFFER_END

            float _SP_CharWorldZ;
            float _SP_CharPresenceActive;

            struct Attributes
            {
                float4 posOS : POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;     // 粒子顶点色（startColor 写在这里）
            };

            struct Varyings
            {
                float4 posHCS    : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float4 color     : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            // ── HSV 工具 ────────────────────────────────────────────────────
            float3 RGBtoHSV(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
                float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
                float  d = q.x - min(q.w, q.y);
                float  e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)),
                              d / (q.x + e),
                              q.x);
            }

            float3 HSVtoRGB(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            // ── Vertex ──────────────────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posHCS    = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv        = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color     = IN.color;
                OUT.screenPos = ComputeScreenPos(OUT.posHCS);
                return OUT;
            }

            // ── Fragment ─────────────────────────────────────────────────────
            float4 frag(Varyings IN) : SV_Target
            {
                // ── 角色遮挡（与全息 shader 相同逻辑）──────────────────────
                if (_SP_CharPresenceActive > 0.5)
                {
                    float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
                    float  charMask = SAMPLE_TEXTURE2D(_SP_CharPresence, sampler_SP_CharPresence, screenUV).r;
                    if (charMask > _CharMaskThreshold && _SP_CharWorldZ < _HoloRootZ + 0.05)
                        discard;
                }

                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // 贴图的明度（V）保留形状细节
                float3 texHSV    = RGBtoHSV(tex.rgb);

                // 目标颜色的 H + 强制高饱和，叠加贴图明度
                float3 targetHSV = RGBtoHSV(_TargetColor.rgb);
                float3 resultHSV = float3(targetHSV.x,
                                          targetHSV.y,   // 保留原色饱和度，不强制
                                          texHSV.z);     // 保留贴图明度
                float3 resultRGB = HSVtoRGB(resultHSV) * _Brightness;

                // 顶点色只取 alpha 通道（粒子生命周期淡出）
                float alpha = tex.a * IN.color.a;

                return float4(resultRGB, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
