Shader "Hidden/SP/OpticalCamo"
{
    // 怪人(Mirage)的光学迷彩：用 Spine 图集的 alpha 当轮廓，按轮廓去折射背后的画面(_CameraOpaqueTexture)，
    // 读起来像"空气里有一块会动的透明玻璃"，表面带油膜一样的彩虹干涉色。
    // 做法跟剑气扭曲(SkyPrisonSwordSlashDistortion)一样走 URP 不透明纹理，在这套相机栈下已经验证能用。
    // 不替换 Spine 自己的材质：由 SkyPrisonOpticalCamo 在子物体上共用同一份网格画这一层。
    //   · 现形 = 一条从头顶往脚底扫的分界线(_WipeY，世界高度)：线上方画真身(图集颜色)，线下方还是折射；
    //     分界线上有一道亮的彩虹扫描带。全部扫完后由组件切回 Spine 本体，这一层关掉。
    //   · 折射方向：图集亮度梯度(伪法线) + 随时间流动的噪声，静止时也有轻微水波；移动时扭曲更强(_Motion)。

    Properties
    {
        _MainTex            ("Spine 图集", 2D) = "white" {}
        _Camo               ("迷彩层整体不透明度", Range(0, 1)) = 1
        _Motion             ("移动程度", Range(0, 1)) = 0
        _DistortionStrength ("扭曲强度", Range(0, 0.05)) = 0.012
        _MotionBoost        ("全速移动时扭曲倍率", Range(1, 6)) = 3
        _FlowSpeed          ("流动速度", Range(0, 5)) = 1.3
        _EdgeGlint          ("边缘高光", Range(0, 1)) = 0.05
        _Iridescence        ("油膜彩虹强度", Range(0, 1)) = 0.12
        _IridScale          ("油膜条纹密度", Range(1, 40)) = 14
        _Tint               ("色调", Color) = (1, 1, 1, 1)
        _VertexAlphaScale   ("顶点 alpha 还原倍率", Float) = 1
        _WipeY              ("现形分界线(世界高度，线上方已现形)", Float) = 10000
        _WipeBand           ("扫描带宽度(米)", Float) = 0.25
        _BodyDim            ("现形部分亮度", Range(0, 1.5)) = 0.9
        _LensAmount         ("透镜放大量(中心)", Range(-0.6, 0.8)) = 0.35
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "OpticalCamo"
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float  _Camo, _Motion, _DistortionStrength, _MotionBoost, _FlowSpeed, _EdgeGlint, _VertexAlphaScale;
                float  _Iridescence, _IridScale, _WipeY, _WipeBand, _BodyDim, _LensAmount;
                float4 _Tint;
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings  { float4 posHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float4 screenPos : TEXCOORD1; float3 posWS : TEXCOORD2; float4 lens : TEXCOORD3; };

            float2 WorldToScreenUV(float3 w) { float4 h = TransformWorldToHClip(w); float4 sp = ComputeScreenPos(h); return sp.xy / max(sp.w, 0.0001); }

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(IN.posOS.xyz);
                o.posHCS = TransformWorldToHClip(o.posWS);
                o.uv = IN.uv;
                o.color = IN.color;
                o.screenPos = ComputeScreenPos(o.posHCS);
                // 透镜：以整个怪人(渲染器包围盒)的中心为原点、包围盒半宽/半高为半径——整块是一片连续的透镜，跟部件无关，没有接缝
                float3 bmin = unity_RendererBounds_Min.xyz, bmax = unity_RendererBounds_Max.xyz;
                float3 c = (bmin + bmax) * 0.5;
                float2 cUV = WorldToScreenUV(c);
                float2 hx = abs(WorldToScreenUV(c + float3((bmax.x - bmin.x) * 0.5, 0, 0)) - cUV);
                float2 hy = abs(WorldToScreenUV(c + float3(0, (bmax.y - bmin.y) * 0.5, 0)) - cUV);
                o.lens = float4(cUV, max(hx.x, 0.0001), max(hy.y, 0.0001));
                return o;
            }

            float Lum(float2 uv) { float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv); return dot(c.rgb, float3(0.3, 0.59, 0.11)) * c.a; }

            // 油膜薄膜干涉的彩虹：余弦调色板
            float3 Rainbow(float t) { return 0.5 + 0.5 * cos(6.28318 * (t + float3(0.0, 0.33, 0.67))); }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                // 本体被压到几乎透明是通过骨架颜色(乘进了顶点 alpha)，这里除回去，只保留插槽自己的 alpha
                float slotA = saturate(IN.color.a * _VertexAlphaScale);
                float mask = tex.a * slotA;
                if (mask < 0.02 || _Camo <= 0.001) discard;

                float time = _Time.y * _FlowSpeed;

                // ── 现形部分：分界线上方画真身 ──
                float above = IN.posWS.y - _WipeY;                 // >0 = 已现形
                float3 bodyRgb = tex.rgb * saturate(IN.color.rgb * _VertexAlphaScale) * _BodyDim;

                // 迷彩部分的歪曲改由 Hidden/SP/CamoWarp(屏幕空间模糊遮罩)负责；这一层只画现形扫描
                float3 camo = 0;

                // ── 合成：分界线两侧 + 扫描带 ──
                float revealed = smoothstep(-0.02, 0.02, above);
                float3 rgb = lerp(camo, bodyRgb, revealed);
                float band = 1.0 - saturate(abs(above) / max(0.01, _WipeBand));
                rgb += Rainbow(IN.posWS.x * 0.8 + time * 0.6) * band * 1.4;

                // 静止时整层透明(背景采样和最终画面不完全一致，静止也会留下淡淡人形)；移动时才出现。
                // 已经现形的部分和扫描带不受影响，保持不透明。
                float vis = max(revealed, band);
                return float4(rgb, saturate(mask) * _Camo * vis);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
