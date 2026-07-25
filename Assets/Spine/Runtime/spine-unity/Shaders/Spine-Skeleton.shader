Shader "Spine/Skeleton_SkyPrison_3DNativeFootDepthProxy_V11_HardFootDepth" {
    Properties {
        _Cutoff ("Shadow alpha cutoff", Range(0,1)) = 0.1
        [NoScaleOffset] _MainTex ("Main Texture", 2D) = "black" {}
        [Toggle(_STRAIGHT_ALPHA_INPUT)] _StraightAlphaInput("Straight Alpha Texture", Int) = 1
        [HideInInspector] _StencilRef("Stencil Reference", Float) = 1.0
        [HideInInspector][Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8

        // Sky Prison map environment receiver.
        _SkyPrison_EnvTint ("Sky Prison Env Tint", Color) = (0.78,0.88,0.82,1)
        _SkyPrison_EnvTintStrength ("Sky Prison Env Tint Strength", Range(0,1)) = 0
        _SkyPrison_EnvDarken ("Sky Prison Env Darken", Range(0,1)) = 0
        _SkyPrison_EnvSaturation ("Sky Prison Env Saturation", Range(0,2)) = 1
        _SkyPrison_EnvContrast ("Sky Prison Env Contrast", Range(0,2)) = 1
        _SkyPrison_EnvExposure ("Sky Prison Env Exposure", Range(-2,2)) = 0
        _SkyPrison_EnvShadowTintStrength ("Sky Prison Shadow Tint Strength", Range(0,1)) = 0

        // Legacy compatibility only. The alpha production route does not use FootDepth.
        _SkyPrison_OcclusionAlpha ("Sky Prison Occlusion Alpha", Range(0,1)) = 1
        _SkyPrison_OcclusionTint ("Sky Prison Occlusion Tint", Color) = (1,1,1,1)
        _SkyPrison_OcclusionTintStrength ("Sky Prison Occlusion Tint Strength", Range(0,1)) = 0
        [HideInInspector] _SkyPrison_FootWorldPos ("Foot World Pos - Legacy Unused", Vector) = (0,0,0,1)

        // Sky Prison alpha-edge cleanup for Spine atlas seams.
        _SkyPrison_AlphaCleanupCutoff ("Sky Prison Alpha Cleanup Cutoff", Range(0,0.2)) = 0.015
        _SkyPrison_AlphaCleanupFeather ("Sky Prison Alpha Cleanup Feather", Range(0.0001,0.2)) = 0.04
        _SkyPrison_AlphaCleanupPower ("Sky Prison Alpha Cleanup Power", Range(0.25,4)) = 1

        // 2026-07-18：手绘阴影遮罩——纯粹的"哪里该暗"遮罩图，按图集同像素布局手画
        // （黑=阴影，白=不变），乘上去就行，不算光照方向、不依赖法线贴图，完全由
        // 美术直接决定阴影形状，不会有方向性数据镜像出错那类问题。默认Strength=0，
        // 不画遮罩图（保持纯白）也完全不影响现有渲染。
        [NoScaleOffset] _SkyPrison_ShadowMask ("Sky Prison Shadow Mask (black=shadow)", 2D) = "white" {}
        _SkyPrison_ShadowMaskStrength ("Sky Prison Shadow Mask Strength", Range(0,1)) = 0

        // 2026-07-20：死亡溶解特效。世界空间噪波采样（不是图集UV），避免Spine
        // 图集分部件UV导致噪波在部件之间断裂不连续；也天然不受镜像负缩放影响
        // （见记忆feedback-spine-mirror-facing-directional-data，位置类数据没有
        // 方向性数据那种镜像读反问题）。DissolveAmount=0不生效，完全不影响现有渲染。
        _SkyPrison_DissolveAmount ("Sky Prison Dissolve Amount", Range(0,1)) = 0
        _SkyPrison_DissolveDarken ("Sky Prison Dissolve Darken", Range(0,1)) = 0
        [NoScaleOffset] _SkyPrison_DissolveNoiseTex ("Sky Prison Dissolve Noise", 2D) = "white" {}
        _SkyPrison_DissolveNoiseScale ("Sky Prison Dissolve Noise Scale", Float) = 0.6
        _SkyPrison_DissolveEdgeWidth ("Sky Prison Dissolve Edge Width", Range(0.001,1)) = 0.12
        [HDR] _SkyPrison_DissolveEdgeColor ("Sky Prison Dissolve Edge Color", Color) = (0.5,0.04,0.02,1)

        // 2026-07-20：状态描边（比如灼烧持续发光）。最初用清理过的alpha边缘做"内侧描边"，
        // 但Spine角色是多个部件各自独立贴图拼出来的，逐部件alpha边缘会把每个部件的
        // 轮廓都描一遍（头发/身体/武器各自一圈），不是角色整体外轮廓——实测直接暴露。
        // 改用屏幕空间轮廓蒙版找边缘，天然只会描出角色整体外轮廓，不会有部件接缝问题。
        // 2026-07-20 二次修正：一开始用的是 CharacterPresenceFeature 发布的全局
        // _SP_CharPresence（全场角色合并成一张，本来是给全息掉落物遮挡判定用的）——
        // 两个角色贴在一起/重叠时，合并蒙版会把两者的轮廓边界"焊"在一起，表现为
        // "重叠处描边缺口"。改用 UnitStatusOutlinePresenceFeature 每帧为每个开着
        // 状态描边的单位单独渲染的专属蒙版（只画这个单位自己的部件，跟别的单位
        // 完全隔离），由 UnitStatusOutlineEffect 通过 MaterialPropertyBlock 绑定。
        // Intensity=0完全不生效，不采样、不影响现有渲染。
        _SkyPrison_StatusOutlineIntensity ("Sky Prison Status Outline Intensity", Range(0,1)) = 0
        [HDR] _SkyPrison_StatusOutlineColor ("Sky Prison Status Outline Color", Color) = (2.2,0.7,0.05,1)
        _SkyPrison_StatusOutlineWidthPixels ("Sky Prison Status Outline Width Pixels", Range(1,12)) = 3
        // 粗细/明暗变化 + 流动：复用死亡溶解那张噪波贴图，世界空间采样随时间平移，
        // 拿噪波值同时调制描边宽度和亮度，做出"深浅粗细不均匀、沿轮廓流动"的效果，
        // 而不是等宽等亮的实线。WidthVariance=0时退化回原来的等宽描边。
        _SkyPrison_StatusOutlineWidthVariance ("Sky Prison Status Outline Width Variance", Range(0,1)) = 0.6
        _SkyPrison_StatusOutlineFlowSpeed ("Sky Prison Status Outline Flow Speed", Float) = 0.6
        _SkyPrison_StatusOutlineNoiseScale ("Sky Prison Status Outline Noise Scale", Float) = 1.2

        // 2026-07-20：状态效果响应闪烁（比如DOT真的跳了一下伤害时的即时反馈）。
        // 跟状态描边是两码事——不依赖描边开关，任何状态只要触发了一次"效果响应"
        // （目前是DOT tick）都能用。UnitStatusFlashEffect 驱动。
        // 中间试过环形波收缩、径向渐变两版，效果都不理想，改回最初的全身统一强度
        // 起伏——按sin曲线0→1→0一次呼吸，叠加式合成（不是lerp，避免颜色浑浊）。
        [Toggle] _SkyPrison_StatusFlashActive ("Sky Prison Status Flash Active", Float) = 0
        _SkyPrison_StatusFlashProgress ("Sky Prison Status Flash Progress", Range(0,1)) = 0
        [HDR] _SkyPrison_StatusFlashColor ("Sky Prison Status Flash Color", Color) = (1,1,1,1)
        _SkyPrison_StatusFlashAlphaDip ("Sky Prison Status Flash Alpha Dip", Range(0,1)) = 0.35

        // Outline properties are drawn via custom editor.
        [HideInInspector] _OutlineWidth("Outline Width", Range(0,8)) = 3.0
        [HideInInspector][MaterialToggle(_USE_SCREENSPACE_OUTLINE_WIDTH)] _UseScreenSpaceOutlineWidth("Width in Screen Space", Float) = 0
        [HideInInspector] _OutlineColor("Outline Color", Color) = (1,1,0,1)
        [HideInInspector][MaterialToggle(_OUTLINE_FILL_INSIDE)]_Fill("Fill", Float) = 0
        [HideInInspector] _OutlineReferenceTexWidth("Reference Texture Width", Int) = 1024
        [HideInInspector] _ThresholdEnd("Outline Threshold", Range(0,1)) = 0.25
        [HideInInspector] _OutlineSmoothness("Outline Smoothness", Range(0,1)) = 1.0
        [HideInInspector][MaterialToggle(_USE8NEIGHBOURHOOD_ON)] _Use8Neighbourhood("Sample 8 Neighbours", Float) = 1
        [HideInInspector] _OutlineOpaqueAlpha("Opaque Alpha", Range(0,1)) = 1.0
        [HideInInspector] _OutlineMipLevel("Outline Mip Level", Range(0,3)) = 0
    }

    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Fog { Mode Off }
        Cull Off
        ZWrite Off

        // Alpha production route:
        // The normal Spine body must not be directly eaten by arbitrary 3D depth.
        // Formal occlusion is handled by UnitOcclusionMaterialReceiver + Spine/SpineOcclusionComposite.
        ZTest Always

        Blend One OneMinusSrcAlpha
        Lighting Off

        Stencil {
            Ref[_StencilRef]
            Comp[_StencilComp]
            Pass Keep
        }

        Pass {
            Name "Normal"
            ZTest Always
            ZWrite Off

            CGPROGRAM
            #pragma shader_feature _ _STRAIGHT_ALPHA_INPUT
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "CGIncludes/Spine-Common.cginc"

            sampler2D _MainTex;

            sampler2D _SkyPrison_ShadowMask;
            float _SkyPrison_ShadowMaskStrength;

            fixed4 _SkyPrison_EnvTint;
            float _SkyPrison_EnvTintStrength;
            float _SkyPrison_EnvDarken;
            float _SkyPrison_EnvSaturation;
            float _SkyPrison_EnvContrast;
            float _SkyPrison_EnvExposure;
            float _SkyPrison_EnvShadowTintStrength;

            float _SkyPrison_OcclusionAlpha;
            fixed4 _SkyPrison_OcclusionTint;
            float _SkyPrison_OcclusionTintStrength;

            float _SkyPrison_AlphaCleanupCutoff;
            float _SkyPrison_AlphaCleanupFeather;
            float _SkyPrison_AlphaCleanupPower;

            sampler2D _SkyPrison_DissolveNoiseTex;
            float _SkyPrison_DissolveAmount;
            float _SkyPrison_DissolveDarken;
            float _SkyPrison_DissolveNoiseScale;
            float _SkyPrison_DissolveEdgeWidth;
            fixed4 _SkyPrison_DissolveEdgeColor;

            float _SkyPrison_StatusOutlineIntensity;
            fixed4 _SkyPrison_StatusOutlineColor;
            float _SkyPrison_StatusOutlineWidthPixels;
            float _SkyPrison_StatusOutlineWidthVariance;
            float _SkyPrison_StatusOutlineFlowSpeed;
            float _SkyPrison_StatusOutlineNoiseScale;

            float _SkyPrison_StatusFlashActive;
            float _SkyPrison_StatusFlashProgress;
            fixed4 _SkyPrison_StatusFlashColor;
            float _SkyPrison_StatusFlashAlphaDip;

            // 每单位专属轮廓蒙版（UnitStatusOutlinePresenceFeature 每帧只为这一个单位画，
            // 跟其他单位完全隔离，不会有重叠焊接缺口）。跟死亡溶解那类效果不共用。
            sampler2D _SkyPrison_StatusOutlinePresence;
            float4 _SkyPrison_StatusOutlinePresence_TexelSize;
            float _SkyPrison_StatusOutlinePresenceActive;

            struct VertexInput {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 vertexColor : COLOR;
            };

            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 vertexColor : COLOR;
                float2 worldPosXY : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            VertexOutput vert (VertexInput v) {
                VertexOutput o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.vertexColor = PMAGammaToTargetSpace(v.vertexColor);
                o.worldPosXY = mul(unity_ObjectToWorld, v.vertex).xy;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float SampleStatusOutlinePresenceRaw(float2 screenUV) {
                return tex2D(_SkyPrison_StatusOutlinePresence, screenUV).r;
            }

            // 跟 SpineOcclusionComposite.shader 的 GetCharSilhouetteEdge 同一套算法：
            // 屏幕空间8邻域采样，中心比邻域最小值多出来的部分就是"轮廓边缘"。这里采样
            // 的是每单位专属蒙版，不是全场合并蒙版，不会跟其他单位重叠焊接。
            float GetStatusOutlineSilhouetteEdge(float2 screenUV, float widthPixels) {
                if (_SkyPrison_StatusOutlinePresenceActive < 0.5)
                    return 0.0;

                float2 texel = abs(_SkyPrison_StatusOutlinePresence_TexelSize.xy) * max(1.0, widthPixels);

                float neighborMin = 1.0;
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2( texel.x, 0)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2(-texel.x, 0)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2(0,  texel.y)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2(0, -texel.y)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2( texel.x,  texel.y)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2(-texel.x,  texel.y)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2( texel.x, -texel.y)));
                neighborMin = min(neighborMin, SampleStatusOutlinePresenceRaw(screenUV + float2(-texel.x, -texel.y)));

                float center = SampleStatusOutlinePresenceRaw(screenUV);
                return saturate(center - neighborMin);
            }

            float3 SkyPrisonApplyOverlayEnvironment(float3 rgb) {
                float tintStrength = saturate(_SkyPrison_EnvTintStrength);
                float darken = saturate(_SkyPrison_EnvDarken);
                float saturation = max(0.0, _SkyPrison_EnvSaturation);
                float contrast = max(0.0, _SkyPrison_EnvContrast);
                float exposure = _SkyPrison_EnvExposure;
                float shadowTintStrength = saturate(_SkyPrison_EnvShadowTintStrength);

                rgb *= exp2(exposure);
                float luminance = dot(rgb, float3(0.299, 0.587, 0.114));
                rgb = lerp(luminance.xxx, rgb, saturation);
                rgb = (rgb - 0.5) * contrast + 0.5;

                float3 envTinted = rgb * _SkyPrison_EnvTint.rgb;
                rgb = lerp(rgb, envTinted, tintStrength);

                float shadowMask = saturate(1.0 - luminance);
                float3 shadowTinted = rgb * _SkyPrison_EnvTint.rgb;
                rgb = lerp(rgb, shadowTinted, shadowMask * shadowTintStrength);

                rgb *= (1.0 - darken);
                return saturate(rgb);
            }

            float4 frag (VertexOutput i) : SV_Target {
                float4 texColor = tex2D(_MainTex, i.uv);

                #if defined(_STRAIGHT_ALPHA_INPUT)
                texColor.rgb *= texColor.a;
                #endif

                float4 baseColor = texColor * i.vertexColor;
                float alpha = saturate(baseColor.a);

                float cleanupCutoff = saturate(_SkyPrison_AlphaCleanupCutoff);
                float cleanupFeather = max(_SkyPrison_AlphaCleanupFeather, 0.0001);
                float edgeKeep = smoothstep(cleanupCutoff, cleanupCutoff + cleanupFeather, alpha);
                edgeKeep = pow(saturate(edgeKeep), max(_SkyPrison_AlphaCleanupPower, 0.0001));

                alpha *= edgeKeep;
                baseColor.rgb *= edgeKeep;

                float3 straightRgb = alpha > 0.0001 ? baseColor.rgb / alpha : 0;
                straightRgb = SkyPrisonApplyOverlayEnvironment(straightRgb);

                // 手绘阴影遮罩：黑=阴影、白=不变，直接乘上去。用edgeKeep在图集
                // 部件轮廓边缘做淡出（跟颜色通道同一套规则——新加的逐像素效果都要
                // 接这个，否则边缘会重新出现"灰边"，见记忆feedback-spine-shader-
                // edgekeep-required）。
                float shadowMask = tex2D(_SkyPrison_ShadowMask, i.uv).r;
                float shadowStrength = saturate(_SkyPrison_ShadowMaskStrength) * edgeKeep;
                straightRgb *= lerp(1.0, shadowMask, shadowStrength);

                float occlusionAlpha = saturate(_SkyPrison_OcclusionAlpha);
                float occlusionTintStrength = saturate(_SkyPrison_OcclusionTintStrength);
                straightRgb = lerp(straightRgb, straightRgb * _SkyPrison_OcclusionTint.rgb, occlusionTintStrength);

                // 状态描边：用全局角色轮廓蒙版找屏幕空间边缘，只描角色整体外轮廓，
                // 不会像逐部件alpha边缘那样把头发/身体/武器各自描一圈。
                float statusOutlineIntensity = saturate(_SkyPrison_StatusOutlineIntensity);
                if (statusOutlineIntensity > 0.0001) {
                    float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);

                    // 流动噪波：世界空间UV随时间平移（复用死亡溶解噪波贴图），只调制亮度，
                    // 不再调制取样宽度——极细的发光线在Bloom降采样链路里会直接丢失、
                    // 怎么调Intensity都不发光，之前的"宽度也跟着变"就是这么把Bloom搞没的。
                    // 取样宽度固定用配置值，保证线一直够粗、Bloom稳定能抓住。
                    float2 flowUV = i.worldPosXY * _SkyPrison_StatusOutlineNoiseScale + float2(0, _Time.y * _SkyPrison_StatusOutlineFlowSpeed);
                    float flowNoise = tex2D(_SkyPrison_DissolveNoiseTex, flowUV).r;

                    float edge = GetStatusOutlineSilhouetteEdge(screenUV, _SkyPrison_StatusOutlineWidthPixels);
                    float widthVariance = saturate(_SkyPrison_StatusOutlineWidthVariance);
                    float flowBrightness = lerp(1.0 - widthVariance * 0.8, 1.0, flowNoise);
                    straightRgb += _SkyPrison_StatusOutlineColor.rgb * edge * flowBrightness * statusOutlineIntensity;
                }

                float finalAlpha = alpha * occlusionAlpha;

                // 状态效果响应闪烁：全身统一强度按sin曲线0→1→0起伏一次（触发DOT tick时
                // 亮一下再消失），不做空间上的渐变/移动。跟死亡溶解/状态描边都无关。
                // 2026-07-20 三次修正：加色叠加(+=)在角色本身较亮的底色上会泛白发脏
                // （加色混合天生盖不住底色，只是在亮色上再加一点，观感发粉）。改成
                // 乘色调(tint multiply)——直接对角色自己已经算好的明暗颜色做染色，
                // 保留阴影/细节，只是整体颜色倾向偏向闪烁色，比叠加干净得多。
                if (_SkyPrison_StatusFlashActive > 0.5) {
                    float progress = saturate(_SkyPrison_StatusFlashProgress);
                    float statusFlashIntensity = saturate(sin(progress * 3.14159265));
                    if (statusFlashIntensity > 0.0001) {
                        straightRgb = lerp(straightRgb, straightRgb * _SkyPrison_StatusFlashColor.rgb, statusFlashIntensity);
                        finalAlpha *= lerp(1.0, 1.0 - saturate(_SkyPrison_StatusFlashAlphaDip), statusFlashIntensity);
                    }
                }

                clip(finalAlpha - 0.001);

                // 死亡溶解第一阶段：本体先整体压黑（不裁剪任何像素），压黑压满后
                // 才进入下面的世界空间噪波裁剪阶段——两阶段的时间分配由
                // UnitDeathController 那边算好再喂DissolveAmount/DissolveDarken。
                float dissolveDarken = saturate(_SkyPrison_DissolveDarken);
                straightRgb *= (1.0 - dissolveDarken);

                // 死亡溶解第二阶段：世界空间噪波逐像素阈值裁剪 + 阈值附近发光描边。
                float dissolveAmount = saturate(_SkyPrison_DissolveAmount);
                if (dissolveAmount > 0.0001) {
                    float noiseValue = tex2D(_SkyPrison_DissolveNoiseTex, i.worldPosXY * _SkyPrison_DissolveNoiseScale).r;
                    clip(noiseValue - dissolveAmount);
                    float edgeWidth = max(_SkyPrison_DissolveEdgeWidth, 0.001);
                    float edgeGlow = 1.0 - saturate((noiseValue - dissolveAmount) / edgeWidth);
                    straightRgb = lerp(straightRgb, _SkyPrison_DissolveEdgeColor.rgb, edgeGlow);
                }

                return float4(straightRgb * finalAlpha, finalAlpha);
            }
            ENDCG
        }

        Pass {
            Name "Caster"
            Tags { "LightMode"="ShadowCaster" }
            Offset 1, 1
            ZWrite On
            ZTest LEqual

            Fog { Mode Off }
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma fragmentoption ARB_precision_hint_fastest
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed _Cutoff;

            struct VertexOutput {
                V2F_SHADOW_CASTER;
                float4 uvAndAlpha : TEXCOORD1;
            };

            VertexOutput vert (appdata_base v, float4 vertexColor : COLOR) {
                VertexOutput o;
                o.uvAndAlpha = v.texcoord;
                o.uvAndAlpha.a = vertexColor.a;
                TRANSFER_SHADOW_CASTER(o)
                return o;
            }

            float4 frag (VertexOutput i) : SV_Target {
                fixed4 texcol = tex2D(_MainTex, i.uvAndAlpha.xy);
                clip(texcol.a * i.uvAndAlpha.a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    CustomEditor "SpineShaderWithOutlineGUI"
}
