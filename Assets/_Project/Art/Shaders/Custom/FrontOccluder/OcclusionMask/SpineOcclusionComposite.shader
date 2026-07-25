Shader "Spine/SpineOcclusionComposite"
{
    // V42 - CleanCharacterOutlineGate_OverlapSafe
    // Clean mainline:
    //   Pass 1 writes the real Spine alpha shape into CharacterMask.
    //   NormalBody samples precomputed HiddenMask from _OcclusionTex.
    //   Hidden outline is drawn inside this same Spine body pass, on the HiddenMask edge.
//   V39 final uses the faction color pushed by SkyPrisonMainCameraHiddenMaskOutlineFeature_V3_FactionColorFinal.
    //   V40 stabilizes hidden outline: edge requires stable exterior support, suppressing occluder rib/noise internal lines.
    //   No Canvas overlay. No fullscreen outline dependency.

    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (1,1,1,1)
        [Toggle] _StraightAlphaInput ("Straight Alpha Texture", Float) = 0

        _Cutoff ("Shadow alpha cutoff", Range(0,1)) = 0.1

        _OcclusionTex ("Occlusion Texture - Precomputed HiddenMask", 2D) = "black" {}
        _SkyPrison_CleanCharacterOutlineTex ("Clean Character Outline Texture", 2D) = "black" {}
        _SkyPrison_UseCleanCharacterOutlineTex ("Use Clean Character Outline Texture", Float) = 1
        _MaskThreshold ("Mask Threshold", Range(0,1)) = 0.5
        _MaskSoftness ("Mask Softness", Range(0.001,0.5)) = 0.001
        _FlipMaskY ("Flip Mask Y", Float) = 0
        _SampleBothY ("Sample Both Y Directions", Float) = 0
        _SkyPrison_EnableBodyClip ("Sky Prison Enable Body Clip", Float) = 1

        _SkyPrison_HiddenOutlineColor ("Hidden Outline Color", Color) = (1,0.83,0,1)
        _SkyPrison_EnableHiddenOutline ("Enable Hidden Outline", Float) = 1
        _SkyPrison_HiddenOutlineWidthPixels ("Hidden Outline Width Pixels", Range(1,12)) = 2
        _SkyPrison_HiddenOutlineAlpha ("Hidden Outline Alpha", Range(0,1)) = 1
        _SkyPrison_HiddenOutlineStableFilter ("Hidden Outline Stable Filter", Float) = 1
        _SkyPrison_HiddenOutlineFarSampleScale ("Hidden Outline Far Sample Scale", Range(1,4)) = 2.35
        _SkyPrison_HiddenOutlineMinStableVotes ("Hidden Outline Min Stable Votes", Range(1,8)) = 2
        _SkyPrison_HiddenOutlineNoiseThreshold ("Hidden Outline Noise Threshold", Range(0,1)) = 0.35

        // 2026-07-19：全息点阵填充——描边（找轮廓边缘）在多角色贴近交叉时有天花板
        // （逐网格画的线只能画在自己网格覆盖到的范围内，交叉处会被对方实体盖住）。
        // 填充式效果不需要找边缘，只要 hidden（已经很可靠，来自 _OcclusionTex）就行，
        // 天生没有交叉缺口问题——跟掉落物本来就有的全息效果（LootDropHologram.shader）
        // 同一套视觉语言，风格统一。开着时完全跳过上面的描边分支。
        [Toggle] _SkyPrison_UseHologramFill ("Use Hologram Fill Instead Of Outline", Float) = 0
        _SkyPrison_HologramFillColor ("Hologram Fill Color", Color) = (0.55, 0.95, 1.0, 1)
        // 轮廓填充强度（正常alpha混合，决定前后遮挡关系）；调高能缓解Spine分层重叠处
        // 的双层叠加发暗，但会牺牲一点通透感，是个需要肉眼判断的取舍值。
        _SkyPrison_HologramSilhouetteAlpha ("Hologram Silhouette Alpha (occludes, controls front/back)", Range(0,1)) = 0
        _SkyPrison_HologramAlpha ("Hologram Glow Alpha (grid/scan, additive, no occlusion)", Range(0,1)) = 0.25
        _SkyPrison_HologramGridDensity ("Hologram Grid Density", Range(1, 40)) = 14
        _SkyPrison_HologramGridLineWidth ("Hologram Grid Line Width", Range(0.01, 0.49)) = 0.08
        _SkyPrison_HologramGridBright ("Hologram Grid Brightness", Range(0, 3)) = 0.6
        _SkyPrison_HologramCycleLength ("Hologram Sweep Cycle Length (sec)", Range(1, 10)) = 4.0
        // 横带一个周期匀速爬升这么高就自然出了角色身体范围（没有几何体可画，视觉上就是
        // "扫完消失"），剩下的周期时间就是安静等待——不需要额外的时间闸门。这个值要留够
        // 余量、明显盖过角色实际身高，否则会看到"扫到一半凭空消失"（时间没走完但已经
        // 追不上人物只是因为爬升距离设小了会有类似症状，务必比角色身高留够冗余）。
        _SkyPrison_HologramSweepRangeY ("Hologram Sweep Height Range (world units)", Range(0.5, 6)) = 4.0
        _SkyPrison_HologramTrailLength ("Hologram Trail Fade Length (world units)", Range(0.05, 1.5)) = 0.4

        _SkyPrison_DebugBodyMaskMode ("Debug Body Mask Mode", Float) = 0
        _SkyPrison_DebugBodyMaskAlpha ("Debug Body Mask Alpha", Range(0,1)) = 0.75

        _SkyPrison_StencilRef ("Sky Prison Stencil Ref", Float) = 41
        _StencilRef ("Stencil Reference", Float) = 41
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _StencilWriteMask ("Stencil Write Mask", Float) = 255

        _SkyPrison_AlphaCleanupCutoff ("Sky Prison Alpha Cleanup Cutoff", Range(0,0.2)) = 0.015
        _SkyPrison_AlphaCleanupFeather ("Sky Prison Alpha Cleanup Feather", Range(0.0001,0.2)) = 0.04
        _SkyPrison_AlphaCleanupPower ("Sky Prison Alpha Cleanup Power", Range(0.25,4)) = 1

        // 2026-07-18：手绘阴影遮罩，跟 Spine-Skeleton.shader 那边同一张贴图/同一套规则——
        // 黑=阴影、白=不变，只在 NormalBody 这个Pass（真正决定"部分遮挡时角色可见部分
        // 显示什么颜色"）里生效，不碰其他只负责写遮罩/深度/模板的Pass。
        [NoScaleOffset] _SkyPrison_ShadowMask ("Sky Prison Shadow Mask (black=shadow)", 2D) = "white" {}
        _SkyPrison_ShadowMaskStrength ("Sky Prison Shadow Mask Strength", Range(0,1)) = 0

        // 2026-07-18：跟 Spine-Skeleton.shader 同一套"地图环境色调接收接口"，这个shader
        // 之前从来没有接过，导致SkyPrisonCharacterEnvironmentLightReceiver推过来的数据
        // 只有走 Spine-Skeleton.shader 那条渲染路径的角色才生效，遮挡合成这条路径完全
        // 读不到。属性名保持跟 Spine-Skeleton.shader 完全一致，方便同一个组件同时驱动
        // 两边。
        _SkyPrison_EnvTint ("Sky Prison Env Tint", Color) = (0.78,0.88,0.82,1)
        _SkyPrison_EnvTintStrength ("Sky Prison Env Tint Strength", Range(0,1)) = 0
        _SkyPrison_EnvDarken ("Sky Prison Env Darken", Range(0,1)) = 0
        _SkyPrison_EnvSaturation ("Sky Prison Env Saturation", Range(0,2)) = 1
        _SkyPrison_EnvContrast ("Sky Prison Env Contrast", Range(0,2)) = 1
        _SkyPrison_EnvExposure ("Sky Prison Env Exposure", Range(-2,2)) = 0
        _SkyPrison_EnvShadowTintStrength ("Sky Prison Shadow Tint Strength", Range(0,1)) = 0

        // 2026-07-12：遮挡描边反显，纯 shader 方案，不需要 RendererFeature。
        // 是否遮挡这件事不在这里算——由 SkyPrisonDepthRevealShaderToggle 从
        // UnitOcclusionMaterialReceiver.CurrentOccluded（SimpleDirectionalOccluder 世界坐标Z
        // 阈值+锚点判定，游戏本来就在用、已验证正确）读出来，写进 _SP_DepthRevealEnable。
        // 这里只管在被判定为遮挡时画一层填色，不做任何深度采样/比较。
        [Toggle] _SP_DepthRevealEnable ("SP Depth Reveal Enable", Float) = 0
        _SP_DepthRevealColor ("SP Depth Reveal Color", Color) = (1,0.83,0,1)
        _SP_DepthRevealAlpha ("SP Depth Reveal Alpha", Range(0,1)) = 0.6

        // 2026-07-20：死亡溶解 / 状态描边，跟 Spine-Skeleton.shader 用完全相同的属性名，
        // 这样同一个 MaterialPropertyBlock 能同时驱动两条渲染路径（正常可见 + 被遮挡时
        // 走这个 composite shader）。遮挡效果本身的优先级不变——全息点阵填充/描边照常
        // 画，溶解只是在遮挡结果上再叠一层裁剪+压黑+描边发光。
        _SkyPrison_DissolveAmount ("Sky Prison Dissolve Amount", Range(0,1)) = 0
        _SkyPrison_DissolveDarken ("Sky Prison Dissolve Darken", Range(0,1)) = 0
        [NoScaleOffset] _SkyPrison_DissolveNoiseTex ("Sky Prison Dissolve Noise", 2D) = "white" {}
        _SkyPrison_DissolveNoiseScale ("Sky Prison Dissolve Noise Scale", Float) = 0.6
        _SkyPrison_DissolveEdgeWidth ("Sky Prison Dissolve Edge Width", Range(0.001,1)) = 0.12
        [HDR] _SkyPrison_DissolveEdgeColor ("Sky Prison Dissolve Edge Color", Color) = (0.5,0.04,0.02,1)

        _SkyPrison_StatusOutlineIntensity ("Sky Prison Status Outline Intensity", Range(0,1)) = 0
        [HDR] _SkyPrison_StatusOutlineColor ("Sky Prison Status Outline Color", Color) = (2.2,0.7,0.05,1)
        _SkyPrison_StatusOutlineWidthPixels ("Sky Prison Status Outline Width Pixels", Range(1,12)) = 3
        // 2026-07-20 二次修正：状态描边不再借用全场合并的 _SP_CharPresence（多单位贴在
        // 一起时会把描边边界焊在一起，见 UnitStatusOutlinePresenceFeature 头部注释）。
        // 改用 UnitStatusOutlinePresenceFeature 每帧为每个开着状态描边的单位单独渲染的
        // 专属蒙版属性，见下面 _SkyPrison_StatusOutlinePresence。
        _SkyPrison_StatusOutlineWidthVariance ("Sky Prison Status Outline Width Variance", Range(0,1)) = 0.6
        _SkyPrison_StatusOutlineFlowSpeed ("Sky Prison Status Outline Flow Speed", Float) = 0.6
        _SkyPrison_StatusOutlineNoiseScale ("Sky Prison Status Outline Noise Scale", Float) = 1.2

        [Toggle] _SkyPrison_StatusFlashActive ("Sky Prison Status Flash Active", Float) = 0
        _SkyPrison_StatusFlashProgress ("Sky Prison Status Flash Progress", Range(0,1)) = 0
        [HDR] _SkyPrison_StatusFlashColor ("Sky Prison Status Flash Color", Color) = (1,1,1,1)
        _SkyPrison_StatusFlashAlphaDip ("Sky Prison Status Flash Alpha Dip", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+40"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha
        Fog { Mode Off }

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _OcclusionTex;
        sampler2D _SkyPrison_CleanCharacterOutlineTex;
        float4 _OcclusionTex_TexelSize;

        float4 _TintColor;
        float _StraightAlphaInput;

        float _MaskThreshold;
        float _MaskSoftness;
        float _FlipMaskY;
        float _SampleBothY;
        float _SkyPrison_EnableBodyClip;

        float4 _SkyPrison_HiddenOutlineColor;
        float _SkyPrison_EnableHiddenOutline;
        float _SkyPrison_UseCleanCharacterOutlineTex;
        float _SkyPrison_HiddenOutlineWidthPixels;
        float _SkyPrison_HiddenOutlineAlpha;
        float _SkyPrison_HiddenOutlineStableFilter;
        float _SkyPrison_HiddenOutlineFarSampleScale;
        float _SkyPrison_HiddenOutlineMinStableVotes;
        float _SkyPrison_HiddenOutlineNoiseThreshold;
        float _SkyPrison_DebugBodyMaskMode;
        float _SkyPrison_DebugBodyMaskAlpha;

        float _SkyPrison_UseHologramFill;
        float4 _SkyPrison_HologramFillColor;
        float _SkyPrison_HologramSilhouetteAlpha;
        float _SkyPrison_HologramAlpha;
        float _SkyPrison_HologramGridDensity;
        float _SkyPrison_HologramGridLineWidth;
        float _SkyPrison_HologramGridBright;
        float _SkyPrison_HologramCycleLength;
        float _SkyPrison_HologramSweepRangeY;
        float _SkyPrison_HologramTrailLength;

        float _SkyPrison_AlphaCleanupCutoff;
        float _SkyPrison_AlphaCleanupFeather;
        float _SkyPrison_AlphaCleanupPower;

        sampler2D _SkyPrison_ShadowMask;
        float _SkyPrison_ShadowMaskStrength;

        fixed4 _SkyPrison_EnvTint;
        float _SkyPrison_EnvTintStrength;
        float _SkyPrison_EnvDarken;
        float _SkyPrison_EnvSaturation;
        float _SkyPrison_EnvContrast;
        float _SkyPrison_EnvExposure;
        float _SkyPrison_EnvShadowTintStrength;

        // 跟 Spine-Skeleton.shader 里 SkyPrisonApplyOverlayEnvironment 完全一致的实现，
        // 保持两边行为统一——角色不管走哪条渲染路径，同样的环境参数应该出同样的效果。
        float3 SkyPrisonApplyOverlayEnvironment(float3 rgb)
        {
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

        // Reveal fill toggle - driven entirely by script (see property block comment above).
        float _SP_DepthRevealEnable;
        float4 _SP_DepthRevealColor;
        float _SP_DepthRevealAlpha;

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

        // 每单位专属轮廓蒙版，跟下面遮挡描边用的全场合并 _SP_CharPresence 是两码事，
        // 不要混用。
        sampler2D _SkyPrison_StatusOutlinePresence;
        float4 _SkyPrison_StatusOutlinePresence_TexelSize;
        float _SkyPrison_StatusOutlinePresenceActive;

        float SampleStatusOutlinePresenceRaw(float2 screenUV)
        {
            return tex2D(_SkyPrison_StatusOutlinePresence, screenUV).r;
        }

        float GetStatusOutlineSilhouetteEdge(float2 screenUV, float widthPixels)
        {
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

        // 2026-07-14：角色自身轮廓边缘 - 复用 CharacterPresenceFeature 每帧写好的全局贴图
        // (原本给全息掉落物遮挡用，Shader.SetGlobalTexture 发布，不需要新开 RT/RendererFeature)。
        // _OcclusionTex 是遮挡物形状，只能描出遮挡物边界；这张是角色真实轮廓形状，能描出
        // 角色被完全吞没那部分的真实身形边缘。两者在 NormalBody 里 max() 到一起用。
        sampler2D _SP_CharPresence;
        float4 _SP_CharPresence_TexelSize;
        float _SP_CharPresenceActive;

        float SampleCharPresenceRaw(float2 screenUV)
        {
            return tex2D(_SP_CharPresence, screenUV).r;
        }

        float GetCharSilhouetteEdgeWidth(float2 screenUV, float widthPixels)
        {
            if (_SP_CharPresenceActive < 0.5)
                return 0.0;

            float2 texel = abs(_SP_CharPresence_TexelSize.xy) * max(1.0, widthPixels);

            float neighborMin = 1.0;
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2( texel.x, 0)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2(-texel.x, 0)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2(0,  texel.y)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2(0, -texel.y)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2( texel.x,  texel.y)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2(-texel.x,  texel.y)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2( texel.x, -texel.y)));
            neighborMin = min(neighborMin, SampleCharPresenceRaw(screenUV + float2(-texel.x, -texel.y)));

            float center = SampleCharPresenceRaw(screenUV);
            return saturate(center - neighborMin);
        }

        float GetCharSilhouetteEdge(float2 screenUV)
        {
            return GetCharSilhouetteEdgeWidth(screenUV, _SkyPrison_HiddenOutlineWidthPixels);
        }

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            float4 screenPos : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
            float relativeY : TEXCOORD3;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            // 全息扫描线要相对角色自己的脚底/根节点位置扫，不能用绝对世界Y——否则角色
            // 站在不同楼层/高度时扫描线出现的世界坐标是固定的，跟角色身体对不上。
            // unity_ObjectToWorld._m13 就是根节点(通常是脚底)的世界Y，直接取平移分量，
            // 不用整个矩阵乘向量。
            o.relativeY = o.worldPos.y - unity_ObjectToWorld._m13;
            o.uv = v.uv;
            o.color = v.color;
            o.screenPos = ComputeScreenPos(o.pos);
            return o;
        }

        float CleanupAlpha(float a)
        {
            float cutoff = saturate(_SkyPrison_AlphaCleanupCutoff);
            float feather = max(_SkyPrison_AlphaCleanupFeather, 0.0001);
            float keep = smoothstep(cutoff, cutoff + feather, a);
            keep = pow(saturate(keep), max(_SkyPrison_AlphaCleanupPower, 0.0001));
            return saturate(a * keep);
        }

        float4 SamplePremulBody(v2f i, out float alpha)
        {
            float4 texColor = tex2D(_MainTex, i.uv);
            if (_StraightAlphaInput > 0.5)
                texColor.rgb *= texColor.a;

            float4 c = texColor * i.color;
            c.rgb *= _TintColor.rgb;

            alpha = CleanupAlpha(saturate(c.a));
            c.rgb *= (alpha > 0.0001 && c.a > 0.0001) ? (alpha / max(c.a, 0.0001)) : 0;
            c.a = alpha;
            return c;
        }

        float MaxRGBA(float4 v)
        {
            return max(max(v.r, v.g), max(v.b, v.a));
        }

        float2 ConvertMaskUV(float2 screenUV)
        {
            float2 uv = screenUV;
            if (_FlipMaskY > 0.5)
                uv.y = 1.0 - uv.y;
            return uv;
        }

        float SampleHiddenMaskRaw(float2 screenUV)
        {
            float2 uv = ConvertMaskUV(screenUV);
            float hidden = MaxRGBA(tex2D(_OcclusionTex, uv));

            if (_SampleBothY > 0.5)
            {
                float2 uv2 = screenUV;
                uv2.y = 1.0 - uv2.y;
                hidden = max(hidden, MaxRGBA(tex2D(_OcclusionTex, uv2)));
            }
            return saturate(hidden);
        }

        float GetHiddenFactor(float2 screenUV)
        {
            if (_SkyPrison_EnableBodyClip < 0.5)
                return 0.0;
            float raw = SampleHiddenMaskRaw(screenUV);
            return smoothstep(_MaskThreshold, _MaskThreshold + max(_MaskSoftness, 0.001), raw);
        }

        float StableExteriorVote(float2 screenUV, float2 dir, float2 texel, float farScale, float noiseThreshold)
        {
            // A true hidden silhouette edge has exterior space outside the mask not only at one texel,
            // but also farther away. Narrow occluder ribs / mask cracks often fail this far-sample test.
            float nearHidden = GetHiddenFactor(screenUV + dir * texel);
            float farHidden = GetHiddenFactor(screenUV + dir * texel * farScale);

            float nearOutside = 1.0 - nearHidden;
            float farOutside = 1.0 - farHidden;

            float nearOk = smoothstep(noiseThreshold, 1.0, nearOutside);
            float farOk = smoothstep(noiseThreshold, 1.0, farOutside);
            return saturate(nearOk * farOk);
        }

        float SampleCleanCharacterOutlineRaw(float2 screenUV)
        {
            float2 uv = ConvertMaskUV(screenUV);
            float outline = MaxRGBA(tex2D(_SkyPrison_CleanCharacterOutlineTex, uv));

            if (_SampleBothY > 0.5)
            {
                float2 uv2 = screenUV;
                uv2.y = 1.0 - uv2.y;
                outline = max(outline, MaxRGBA(tex2D(_SkyPrison_CleanCharacterOutlineTex, uv2)));
            }
            return saturate(outline);
        }

        float SampleCleanCharacterOutlineNeighborhood(float2 screenUV)
        {
            // V42: The hidden body pass only runs on character pixels.  A clean silhouette
            // outline can sit just outside the hidden pixels, so requiring same-pixel overlap
            // makes the outline disappear.  Sample a small neighborhood around the current
            // hidden pixel and use the clean outline only as a stable gate/shape source.
            float2 texel = abs(_OcclusionTex_TexelSize.xy) * max(1.0, _SkyPrison_HiddenOutlineWidthPixels);
            float outline = SampleCleanCharacterOutlineRaw(screenUV);

            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2( texel.x, 0)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(-texel.x, 0)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(0,  texel.y)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(0, -texel.y)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2( texel.x,  texel.y)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(-texel.x,  texel.y)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2( texel.x, -texel.y)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(-texel.x, -texel.y)));

            float farScale = max(1.0, _SkyPrison_HiddenOutlineFarSampleScale);
            float2 farTexel = texel * farScale;
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2( farTexel.x, 0)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(-farTexel.x, 0)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(0,  farTexel.y)));
            outline = max(outline, SampleCleanCharacterOutlineRaw(screenUV + float2(0, -farTexel.y)));

            return saturate(outline);
        }

        float GetCleanCharacterOutlineGate(float2 screenUV, float hidden)
        {
            if (_SkyPrison_EnableHiddenOutline < 0.5)
                return 0.0;
            if (hidden <= 0.001)
                return 0.0;

            // V42: hidden stays the visibility condition; clean outline is sampled nearby
            // to prevent occluder surface cracks from becoming strokes while avoiding the
            // strict same-pixel overlap that made V41 draw nothing.
            float outline = SampleCleanCharacterOutlineNeighborhood(screenUV);
            return saturate(hidden * outline);
        }

        float GetHiddenEdge(float2 screenUV, float hidden)
        {
            if (_SkyPrison_EnableHiddenOutline < 0.5)
                return 0.0;
            if (hidden <= 0.001)
                return 0.0;

            float2 texel = abs(_OcclusionTex_TexelSize.xy) * max(1.0, _SkyPrison_HiddenOutlineWidthPixels);

            // Legacy edge: any neighbor outside the HiddenMask becomes an edge.
            // Kept as a fallback through _SkyPrison_HiddenOutlineStableFilter = 0.
            float neighborMin = 1.0;
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2( texel.x, 0)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2(-texel.x, 0)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2(0,  texel.y)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2(0, -texel.y)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2( texel.x,  texel.y)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2(-texel.x,  texel.y)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2( texel.x, -texel.y)));
            neighborMin = min(neighborMin, GetHiddenFactor(screenUV + float2(-texel.x, -texel.y)));
            float legacyEdge = saturate(hidden * (1.0 - neighborMin));

            if (_SkyPrison_HiddenOutlineStableFilter < 0.5)
                return legacyEdge;

            float farScale = max(1.0, _SkyPrison_HiddenOutlineFarSampleScale);
            float noiseThreshold = saturate(_SkyPrison_HiddenOutlineNoiseThreshold);
            float minVotes = max(1.0, _SkyPrison_HiddenOutlineMinStableVotes);

            float votes = 0.0;
            votes += StableExteriorVote(screenUV, float2( 1,  0), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, float2(-1,  0), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, float2( 0,  1), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, float2( 0, -1), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, normalize(float2( 1,  1)), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, normalize(float2(-1,  1)), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, normalize(float2( 1, -1)), texel, farScale, noiseThreshold);
            votes += StableExteriorVote(screenUV, normalize(float2(-1, -1)), texel, farScale, noiseThreshold);

            // Vote confidence suppresses 1px/high-frequency internal cracks from corrugated occluders.
            float confidence = smoothstep(minVotes - 0.5, minVotes + 0.5, votes);
            return saturate(legacyEdge * confidence);
        }
        ENDCG

        Pass
        {
            Name "FullAlphaStencilOnly"
            Tags { "LightMode"="SkyPrisonFullAlphaStencilOnly" }
            ZTest Always
            ZWrite Off
            ColorMask 0

            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilReadMask]
                WriteMask [_StencilWriteMask]
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                float a;
                SamplePremulBody(i, a);
                clip(a - 0.001);
                return 0;
            }
            ENDCG
        }

        Pass
        {
            Name "FullAlphaMaskOnly"
            Tags { "LightMode"="SkyPrisonFullAlphaMaskOnly" }
            ZTest Always
            ZWrite Off
            Blend One Zero
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                float a;
                SamplePremulBody(i, a);
                clip(a - 0.001);
                return float4(1, 1, 1, a);
            }
            ENDCG
        }

        // 把角色世界 Z 编码到 R 通道，供全息掉落物逐像素比较深度关系
        Pass
        {
            Name "CharacterWorldZ"
            Tags { "LightMode"="SkyPrisonCharacterWorldZ" }
            ZTest Always
            ZWrite Off
            Blend One Zero
            ColorMask R

            CGPROGRAM
            #pragma vertex vertWZ
            #pragma fragment fragWZ
            #pragma target 3.0

            struct v2fWZ
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
                float  worldZ : TEXCOORD1;
            };

            v2fWZ vertWZ(appdata v)
            {
                v2fWZ o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                o.color  = v.color;
                // 世界空间 Z（相机轴方向的深度代理）
                o.worldZ = mul(unity_ObjectToWorld, v.vertex).z;
                return o;
            }

            float4 fragWZ(v2fWZ i) : SV_Target
            {
                float4 tex = tex2D(_MainTex, i.uv) * i.color;
                clip(tex.a - 0.001);
                // 归一化到 [0,1]，假设世界 Z 范围 [-200, 200]
                // 0.5 = Z 0，< 0.5 = 负 Z（更靠前），> 0.5 = 正 Z（更靠后）
                float normZ = saturate((i.worldZ + 200.0) / 400.0);
                return float4(normZ, 0, 0, 1);
            }
            ENDCG
        }

        // HLSL pass — 供 CharacterPresenceFeature 通过 DrawRenderer 写入 presence RT
        // 必须 HLSL（非 CG）才能在 URP RenderGraph UnsafePass 的 cmd.DrawRenderer 里生效
        Pass
        {
            Name "CharPresence"
            Tags { "LightMode"="SkyPrisonCharPresence" }
            ZTest Always
            ZWrite Off
            Blend One Zero
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertCP
            #pragma fragment fragCP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct AttributesCP { float4 posOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct VaryingsCP   { float4 posHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            VaryingsCP vertCP(AttributesCP IN)
            {
                VaryingsCP OUT;
                OUT.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv     = IN.uv;
                OUT.color  = IN.color;
                return OUT;
            }

            float4 fragCP(VaryingsCP IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                clip(tex.a - 0.01);
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "NormalBody"
            Tags { "LightMode"="UniversalForward" }
            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                float alpha;
                float4 c = SamplePremulBody(i, alpha);
                clip(alpha - 0.001);

                // 死亡溶解：跟 Spine-Skeleton.shader 同一套世界空间噪波规则。被遮挡时也要
                // 继续裁剪/压黑——角色在溶解消失这件事不应该因为躲在墙后面就停住。
                float dissolveAmount = saturate(_SkyPrison_DissolveAmount);
                float dissolveNoiseValue = 1.0;
                if (dissolveAmount > 0.0001)
                {
                    dissolveNoiseValue = tex2D(_SkyPrison_DissolveNoiseTex, i.worldPos.xy * _SkyPrison_DissolveNoiseScale).r;
                    clip(dissolveNoiseValue - dissolveAmount);
                }
                float dissolveDarken = saturate(_SkyPrison_DissolveDarken);
                c.rgb *= (1.0 - dissolveDarken);

                float dissolveEdgeGlowStrength = 0.0;
                if (dissolveAmount > 0.0001)
                {
                    float dissolveEdgeWidth = max(_SkyPrison_DissolveEdgeWidth, 0.001);
                    dissolveEdgeGlowStrength = 1.0 - saturate((dissolveNoiseValue - dissolveAmount) / dissolveEdgeWidth);
                }

                // 状态描边用每单位专属轮廓蒙版（_SkyPrison_StatusOutlinePresence，
                // UnitStatusOutlinePresenceFeature 只画这一个单位），跟遮挡描边用的全场
                // 合并 _SP_CharPresence 是两码事——避免多单位贴在一起时描边边界被焊住。
                float2 statusScreenUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);
                float statusOutlineIntensity = saturate(_SkyPrison_StatusOutlineIntensity);
                float statusOutlineGlowStrength = 0.0;
                if (statusOutlineIntensity > 0.0001)
                {
                    // 跟 Spine-Skeleton.shader 同一套流动噪波调制宽度/亮度的做法，两条渲染
                    // 路径（正常可见/被遮挡）视觉表现保持一致。
                    float2 statusFlowUV = i.worldPos.xy * _SkyPrison_StatusOutlineNoiseScale + float2(0, _Time.y * _SkyPrison_StatusOutlineFlowSpeed);
                    float statusFlowNoise = tex2D(_SkyPrison_DissolveNoiseTex, statusFlowUV).r;

                    // 取样宽度固定用配置值，不再跟着噪波变——极细的发光线会在Bloom降采样
                    // 链路里丢失，怎么调Intensity都不发光。流动只调制亮度。
                    float statusEdge = GetStatusOutlineSilhouetteEdge(statusScreenUV, _SkyPrison_StatusOutlineWidthPixels);
                    float statusWidthVariance = saturate(_SkyPrison_StatusOutlineWidthVariance);
                    float statusFlowBrightness = lerp(1.0 - statusWidthVariance * 0.8, 1.0, statusFlowNoise);
                    statusOutlineGlowStrength = statusEdge * statusFlowBrightness * statusOutlineIntensity;
                }

                // 溶解描边发光 + 状态描边发光，两者都是附加色，被遮挡（全息/描边分支）和
                // 正常可见都要叠上去——所以在这里算一次，两条路径各自用。
                float3 extraGlow = _SkyPrison_DissolveEdgeColor.rgb * dissolveEdgeGlowStrength
                                  + _SkyPrison_StatusOutlineColor.rgb * statusOutlineGlowStrength;

                // c.rgb 是预乘Alpha的颜色——环境色调那套（对比度/饱和度/明度）不是线性
                // 缩放，直接套在预乘颜色上在半透明边缘会算错，必须先还原成straight颜色
                // 处理完再乘回alpha，跟 Spine-Skeleton.shader 的处理顺序保持一致。
                float3 straightRgb = alpha > 0.0001 ? c.rgb / alpha : 0;
                straightRgb = SkyPrisonApplyOverlayEnvironment(straightRgb);

                // alpha 在这里已经是 CleanupAlpha() 处理过的结果（图集接缝边缘平滑过渡到0），
                // 直接拿来给阴影遮罩的生效强度做边缘淡出，跟 Spine-Skeleton.shader 那边用
                // edgeKeep 做的是同一件事——避免图集部件边缘的不可靠像素被阴影遮罩放大成
                // 新的灰边（见记忆 feedback-spine-shader-edgekeep-required）。
                float shadowMaskValue = tex2D(_SkyPrison_ShadowMask, i.uv).r;
                straightRgb *= lerp(1.0, shadowMaskValue, saturate(_SkyPrison_ShadowMaskStrength) * alpha);

                c.rgb = straightRgb * alpha;

                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);
                float hidden = GetHiddenFactor(screenUV);

                // Body-local debug. The full-screen debug view is handled by the RendererFeature.
                // 1 = show hidden area on the actual Spine mesh.
                if (_SkyPrison_DebugBodyMaskMode > 0.5)
                {
                    float debugA = saturate(hidden * _SkyPrison_DebugBodyMaskAlpha);
                    if (debugA > 0.001)
                        return float4(debugA, debugA, 0, debugA);
                }

                // 2026-07-19：全息点阵填充——不找边缘，只吃 hidden（已经很可靠），天生没有
                // "多个角色贴近交叉时一方实体盖住另一方描边线"这个找边缘方案的天花板。
                //
                // 混合方案（卡轮廓+叠加网格）：轮廓填充部分走正常预乘alpha混合（alpha>0，
                // 会按 sortingOrder 现有的前后顺序正确遮挡背后的东西——这个项目角色前后
                // 本来就是靠 sortingOrder 排序、不是硬件深度测试，材质换成这个不影响原有
                // 排序）；网格线/扫描横带这层叠加在轮廓之上，输出时不计入 alpha（相当于
                // OneMinusSrcAlpha 部分恒为1），只加亮不参与遮挡判断，不会在 Spine 图集
                // 分层重叠处（头发压头、衣服压身体）跟着轮廓一起层层加深。
                if (_SkyPrison_UseHologramFill > 0.5)
                {
                    if (hidden > 0.001)
                    {
                        // 世界空间网格，跟掉落物 LootDropHologram.shader 同一套视觉语言
                        float2 gridPos  = i.worldPos.xy * _SkyPrison_HologramGridDensity;
                        float2 cellFrac = frac(gridPos);
                        float2 toLine   = min(cellFrac, 1.0 - cellFrac);
                        float  aa       = fwidth(min(toLine.x, toLine.y));
                        float  gridMask = 1.0 - smoothstep(_SkyPrison_HologramGridLineWidth - aa,
                                                            _SkyPrison_HologramGridLineWidth + aa,
                                                            min(toLine.x, toLine.y));

                        // 扫描节奏：不是一直循环滚动，也不是突然开关的闪烁——是一条横带，
                        // 相对角色自己脚底的高度，平滑地从下往上移动一次，然后安静几秒，
                        // 再来一次。之前"到窗口时间就渐隐"跟"横带实际爬升距离"这两个参数
                        // 没配合好，横带还没走到头顶，时间闸门就先把它关掉了，看起来像扫到
                        // 一半凭空消失。改成横带自己走出角色身体范围（没有几何体可画）自然
                        // 消失，不再用时间硬性截断；只在每个周期刚开始的一瞬间做个短暂淡入，
                        // 避免从脚底突然冒出来。sweepRangeY 要留够余量，确保横带在下一个
                        // 周期开始前已经清过头顶。
                        float cycleT = frac(_Time.y / _SkyPrison_HologramCycleLength);
                        float sweepFade = smoothstep(0.0, 0.08, cycleT); // 只淡入，不再被时间窗强制淡出
                        float bandY = cycleT * _SkyPrison_HologramSweepRangeY; // 横带高度：整个周期匀速爬升
                        float dist  = i.relativeY - bandY; // >0=横带还没到这里，<0=已经扫过去了

                        // 前沿（dist 刚过0，即将到达处）收得利落；尾迹（dist 更负，已扫过的下方）
                        // 用更长的距离缓慢拖出去，不是硬切。
                        float front = 1.0 - smoothstep(0.0, 0.03, dist);
                        float trail = 1.0 - smoothstep(0.0, _SkyPrison_HologramTrailLength, -dist);
                        float waveMask = saturate(front * trail) * sweepFade;

                        // 轮廓填充：正常alpha混合，负责前后遮挡关系
                        float silA = saturate(hidden) * _SkyPrison_HologramSilhouetteAlpha * _SkyPrison_HologramFillColor.a;
                        // 网格+扫描：叠加发光，不参与遮挡
                        float glowAdd = saturate(gridMask * _SkyPrison_HologramGridBright * 0.6 + waveMask) * hidden
                                      * _SkyPrison_HologramAlpha * _SkyPrison_HologramFillColor.a;

                        if (silA <= 0.001 && glowAdd <= 0.001 && dissolveEdgeGlowStrength <= 0.001 && statusOutlineGlowStrength <= 0.001)
                            discard;

                        float3 outRgb = _SkyPrison_HologramFillColor.rgb * (silA + glowAdd * 0.6) + extraGlow * saturate(silA + glowAdd);
                        return float4(outRgb, silA);
                    }
                }
                else
                {
                    float edge = (_SkyPrison_UseCleanCharacterOutlineTex > 0.5) ? GetCleanCharacterOutlineGate(screenUV, hidden) : GetHiddenEdge(screenUV, hidden);
                    // GetCharSilhouetteEdge 读的是 CharacterPresenceFeature 发布的全局
                    // "全场角色合并"粗糙贴图（本来是给全息掉落物遮挡用的），不受
                    // _SkyPrison_EnableHiddenOutline 这个材质开关控制，这里补上同一个开关，
                    // 否则会一直叠加一圈跟角色真实网格边界脱节的、又粗又圆的描边。
                    if (_SkyPrison_EnableHiddenOutline > 0.5)
                        edge = max(edge, GetCharSilhouetteEdge(screenUV));
                    if (hidden > 0.001)
                    {
                        if (edge > 0.001)
                        {
                            float a = saturate(edge * _SkyPrison_HiddenOutlineAlpha * _SkyPrison_HiddenOutlineColor.a);
                            return float4(_SkyPrison_HiddenOutlineColor.rgb * a, a);
                        }
                        discard;
                    }
                }

                // Reveal fill (independent of the _OcclusionTex system above). No depth math at
                // all: _SP_DepthRevealEnable is pushed in from script, already computed by the
                // game's own SimpleDirectionalOccluder (world-space Z threshold + anchor points),
                // via UnitOcclusionMaterialReceiver.CurrentOccluded. The shader just draws the
                // fill wherever this unit is currently occluded - alpha-clipped to the character's
                // real silhouette by the clip() above, same as the rest of this pass.
                if (_SP_DepthRevealEnable > 0.5)
                {
                    float a = saturate(_SP_DepthRevealAlpha * _SP_DepthRevealColor.a);
                    return float4(_SP_DepthRevealColor.rgb * a, a);
                }

                // 状态效果响应闪烁：全身统一强度按sin曲线0→1→0起伏一次，被遮挡时也要闪，
                // 跟死亡溶解一样的"遮挡不该让效果停摆"原则。乘色调而不是加色——保留
                // 角色本身明暗细节，只是整体颜色倾向偏向闪烁色，比叠加干净。
                if (_SkyPrison_StatusFlashActive > 0.5)
                {
                    float flashProgress = saturate(_SkyPrison_StatusFlashProgress);
                    float statusFlashIntensity = saturate(sin(flashProgress * 3.14159265));
                    if (statusFlashIntensity > 0.0001)
                    {
                        float3 flashStraightRgb = c.a > 0.0001 ? c.rgb / c.a : 0;
                        flashStraightRgb = lerp(flashStraightRgb, flashStraightRgb * _SkyPrison_StatusFlashColor.rgb, statusFlashIntensity);
                        float flashAlpha = c.a * lerp(1.0, 1.0 - saturate(_SkyPrison_StatusFlashAlphaDip), statusFlashIntensity);
                        c.rgb = flashStraightRgb * flashAlpha;
                        c.a = flashAlpha;
                    }
                }

                c.rgb += extraGlow * alpha;
                return c;
            }
            ENDCG
        }

        Pass
        {
            Name "OccludedInsideMask"
            Tags { "LightMode"="SkyPrisonDisabledOccludedBody" }
            ZTest Always
            ZWrite Off
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                discard;
                return 0;
            }
            ENDCG
        }

        // 2026-07-11 讨论：深度失败遮挡描边原型（企业级做法，先在 Player 频道验证）。
        // 追加在文件末尾，不改动前面任何 pass 的顺序号，不影响现有四通道 RT 系统。
        // 两个 pass 配合使用：先用 SPDepthOnlyPrepass 把角色真实 alpha 轮廓写进深度缓冲，
        // 再用 SPDepthFailHiddenMask 以 ZTest Greater 重画一次——只有深度测试"失败"
        // （已经有更近的遮挡物）的像素才会通过，直接由硬件深度测试判定遮挡，
        // 不需要 CharacterMask×OccluderMask 相乘合成。
        Pass
        {
            Name "SPDepthOnlyPrepass"
            Tags { "LightMode"="SkyPrisonDepthOnlyPrepass" }
            ZTest LEqual
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                float a;
                SamplePremulBody(i, a);
                clip(a - 0.001);
                return 0;
            }
            ENDCG
        }

        Pass
        {
            Name "SPDepthFailHiddenMask"
            Tags { "LightMode"="SkyPrisonDepthFailHiddenMask" }
            ZTest Greater
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            float4 _SP_DepthFailTintColor;

            fixed4 frag(v2f i) : SV_Target
            {
                float a;
                SamplePremulBody(i, a);
                clip(a - 0.001);
                return _SP_DepthFailTintColor;
            }
            ENDCG
        }

        Pass
        {
            Name "Caster"
            Tags { "LightMode"="ShadowCaster" }
            Offset 1, 1
            ZWrite On
            ZTest LEqual
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            fixed _Cutoff;

            struct VertexOutput
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
                float alpha : TEXCOORD2;
            };

            VertexOutput vertShadow(appdata_base v, float4 vertexColor : COLOR)
            {
                VertexOutput o;
                o.uv = v.texcoord.xy;
                o.alpha = vertexColor.a;
                TRANSFER_SHADOW_CASTER(o)
                return o;
            }

            float4 fragShadow(VertexOutput i) : SV_Target
            {
                fixed4 texcol = tex2D(_MainTex, i.uv);
                clip(texcol.a * i.alpha - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    FallBack Off
}
