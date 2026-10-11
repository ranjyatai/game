// 灯光投影（角色轮廓版）：把角色自己的网格压到地面上，沿「背离灯」的方向倾斜拉长，
// 用角色图集的透明度做形状，乘法混合压暗地面。由 SkyPrisonCharacterEnvironmentLightReceiver 每帧用
// Graphics.DrawMesh 画（见 DrawLampSilhouette）。
//
// 为什么不用真实阴影：真实点光源阴影是透视投影，离灯近的头会被放大，在 45° 相机下是「越远越大」的反透视。
// 这里用的是平行倾斜（剪切）：地面上的点 = 原位置 + 方向 × 离地高度 × K，整个轮廓只被拉长，不会局部放大。
//
// 渲染规则和脚下那块 Shadow_Projector 保持一致：Transparent-80（永远在角色下面）、ZTest Always、
// 模板 Ref 1 NotEqual（前景遮挡物写了模板的像素不画，影子不会盖到前景遮挡物上）。
//
// 部件重叠去重：网格是 Spine 一片片部件，乘法混合下重叠处会被乘两次、更暗。
// HologramCoverageFeature 先用第二条 Pass（LampShadowCoverage）把每个像素上的部件形状 alpha
// 累加成 S，正式 Pass 每片输出 T^(a/S)，T = 一层影子的目标暗度——所有片乘起来正好是 T。
// 只有一层的地方 a/S = 1，和原来的结果完全一样。
Shader "Hidden/SkyPrison/LampSilhouetteShadow"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.01, 0.01, 0.08, 1)
    }

    SubShader
    {
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _ShadowColor;
        CBUFFER_END

        // 每帧由脚本通过 MaterialPropertyBlock 设置。
        float _SkyPrison_LampShadowGroundY;    // 地面高度
        float4 _SkyPrison_LampShadowShear;     // xy = 世界 XZ 的倾斜方向（单位向量），z = 倾斜系数 K（水平位移/离地高度），w = 整体强度 0~1

        // 投影灯（脚本每帧写）：影子逐像素按「这盏灯把这块地面照多亮」压暗。
        // 影子 = 挡住了这盏灯的光；灯照不到的地方没有光可挡，影子就该融进黑暗，
        // 而不是整块乘一个固定暗色——那样暗处有环境光的地面也会被压出清楚的黑轮廓，
        // 角色一离开灯，整块影子还会同时消失。
        // 用的是 URP 自己的衰减（强度/距离² + 范围平滑 + 聚光内外角 + 地面朝向），
        // 和地面实际被照亮的程度一致；角色受光那套「前 55% 满亮度」的风格化曲线比地面亮得多，不能用。
        float4 _SkyPrison_LampShadowLight;   // xyz = 灯位置, w = range
        float4 _SkyPrison_LampShadowSpot;    // xyz = 灯朝向, w = 1 聚光 / 0 点光
        float4 _SkyPrison_LampShadowCone;    // x = cos(外半角), y = cos(内半角), z = 强度×颜色亮度
        #define SKYPRISON_LAMP_SHADOW_FULL_BRIGHTNESS 0.25// 地面受这盏灯照到这个亮度以上，影子按满强度

        half LampLitAt(float3 groundPos)
        {
            float3 toL = _SkyPrison_LampShadowLight.xyz - groundPos;
            float d2 = max(dot(toL, toL), 1e-4);
            float range = max(_SkyPrison_LampShadowLight.w, 1e-3);
            float f = saturate(1.0 - (d2 / (range * range)) * (d2 / (range * range)));
            float atten = (f * f) / d2;
            float3 L = toL * rsqrt(d2);
            if (_SkyPrison_LampShadowSpot.w > 0.5)
            {
                float cosA = dot(_SkyPrison_LampShadowSpot.xyz, -L);
                float t = saturate((cosA - _SkyPrison_LampShadowCone.x) / max(_SkyPrison_LampShadowCone.y - _SkyPrison_LampShadowCone.x, 1e-4));
                atten *= t * t;
            }
            float brightness = _SkyPrison_LampShadowCone.z * atten * saturate(L.y); // 地面法线朝上
            return (half)saturate(brightness / SKYPRISON_LAMP_SHADOW_FULL_BRIGHTNESS);
        }

        TEXTURE2D(_SP_LampShadowCoverage);
        float _SP_LampShadowCoverageActive;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            float4 screenPos : TEXCOORD1;
            float3 groundPos : TEXCOORD2;
        };

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            float3 wp = TransformObjectToWorld(IN.positionOS.xyz);

            // 先把角色「扶正」：Spine 网格为了正对镜头整体绕 X 轴倾斜了 45°（头往后仰），
            // 但逻辑上角色是垂直站立的。直接用倾斜后的顶点，头部影子会天生往后偏，
            // 灯在身后时影子朝后、站在灯正下方时变成躺倒的人形。这里按网格自己的上/右轴
            // 拆出「离脚的高度」和「左右偏移」，重建成垂直站立的位置再做投影。
            // 轴心在脚底（Spine 根节点）。
            //
            // 灯在正左/正右时，这张竖直纸片侧对着灯，影子会压成一条线——这是有意保留的：
            // 试过让轮廓「正面朝灯」得到完整人形，但侧投时脚下的影子会横着散开、和脚接不上，
            // 比一条线更假（2026-10-03 定）。
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float3 upW = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0, 1, 0)));
            float3 rightW = normalize(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
            float3 d = wp - root;
            wp = root + rightW * dot(d, rightW) + float3(0, dot(d, upW), 0);

            float h = max(0.0, wp.y - _SkyPrison_LampShadowGroundY);
            wp.xz += _SkyPrison_LampShadowShear.xy * (h * _SkyPrison_LampShadowShear.z);
            // 压到地面，略高一点避免和地面共面（ZTest Always 本来不测深度，这里只是保持和 Projector 一致的高度）。
            wp.y = _SkyPrison_LampShadowGroundY + 0.02;
            OUT.positionCS = TransformWorldToHClip(wp);
            OUT.groundPos = wp;
            OUT.uv = IN.uv;
            OUT.color = IN.color;
            OUT.screenPos = ComputeScreenPos(OUT.positionCS);
            return OUT;
        }

        // 部件形状 alpha（不含整体强度）
        half ShapeAlpha(Varyings IN)
        {
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * IN.color.a;
        }
        ENDHLSL

        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent-80"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend DstColor Zero

            Stencil
            {
                Ref 1
                ReadMask 255
                Comp NotEqual
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings IN) : SV_Target
            {
                half shape = ShapeAlpha(IN);
                half strength = saturate(_SkyPrison_LampShadowShear.w) * LampLitAt(IN.groundPos);
                half3 shadowRgb = saturate(_ShadowColor.rgb);

                if (_SP_LampShadowCoverageActive > 0.5)
                {
                    float2 suv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                    float sum = SAMPLE_TEXTURE2D_LOD(_SP_LampShadowCoverage, sampler_PointClamp, suv, 0).r;
                    if (sum > 1e-4 && shape > 1e-4)
                    {
                        // 整块影子的目标暗度 T，按这一片在总 alpha 里的占比开方——所有片乘起来正好是 T。
                        half3 target = lerp(half3(1, 1, 1), shadowRgb, saturate(sum) * strength);
                        return half4(pow(max(target, 1e-4h), shape / sum), 1.0h);
                    }
                }

                // 乘法混合：1 = 不变，越小越暗。
                return half4(lerp(half3(1, 1, 1), shadowRgb, shape * strength), 1.0h);
            }
            ENDHLSL
        }

        // 去重计数 Pass：由 HologramCoverageFeature 用 DrawMesh 显式调用，URP 不会自己调度。
        // 必须和上面同一个 vert，计数图才和正式绘制逐像素对齐。
        Pass
        {
            Name "LampShadowCoverage"
            Tags { "LightMode"="SkyPrisonLampShadowCoverage" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragCoverage

            float4 fragCoverage(Varyings IN) : SV_Target
            {
                half shape = ShapeAlpha(IN);
                clip(shape - 0.001);
                return float4(shape, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
