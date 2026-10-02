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

        // 2026-08-15：这个着色器原本只给 Spine 2D 镂空精灵用——alpha 通道本来就是
        // "轮廓遮罩"语义，SamplePremulBody 里低alpha直接clip()掉整块像素。3D 通道的
        // 静态道具（比如破坏箱子）套这份材质做被遮挡全息时，它们的贴图alpha从来没被
        // 设计成透明度语义（可能是任意残留数据），被这条clip逻辑误当成"镂空区域"，
        // 表现为贴图上莫名其妙的黑洞。开这个开关强制alpha=1，绕开这整条判定，
        // 3D道具材质应该始终按不透明处理，不需要任何alpha裁剪。
        [Toggle] _SkyPrison_Force3DPropOpaqueAlpha ("Force Opaque Alpha (3D Prop Mode)", Float) = 0

        // 2026-08-15：3D道具（有实体体积，不是没有厚度的Spine精灵）需要真正的深度测试
        // 才能让自己网格内部交叉的结构（比如箱子的斜向撑木）正确前后排序——但
        // ZTest Always 又是"隔着遮挡物也要画出全息"这个效果本身的前提，两者在同一条
        // Pass 里互斥，调哪个参数都顾此失彼。拆成两条Pass解决：NormalBody这条按这几个
        // 属性切换成3D道具该有的正常深度测试渲染（自身排序完全正确，被真实遮挡物挡住
        // 的部分自然不画——这是期望行为，交给下面新增的HologramOverlay3D这条Pass补上
        // "被挡住时改画全息"）。默认值维持跟原来完全一致（Off/Off/Always），不影响任何
        // 现有Spine角色。
        [Enum(UnityEngine.Rendering.CullMode)] _SkyPrison_CullMode ("3D Prop Cull Mode", Float) = 0
        [Enum(Off,0,On,1)] _SkyPrison_ZWriteMode ("3D Prop ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _SkyPrison_ZTestMode ("3D Prop ZTest", Float) = 8

        _OcclusionTex ("Occlusion Texture - Precomputed HiddenMask", 2D) = "black" {}
        _SkyPrison_CleanCharacterOutlineTex ("Clean Character Outline Texture", 2D) = "black" {}
        _SkyPrison_UseCleanCharacterOutlineTex ("Use Clean Character Outline Texture", Float) = 1
        _MaskThreshold ("Mask Threshold", Range(0,1)) = 0.5
        _MaskSoftness ("Mask Softness", Range(0.001,0.5)) = 0.001
        _FlipMaskY ("Flip Mask Y", Float) = 0
        _SampleBothY ("Sample Both Y Directions", Float) = 0
        _SkyPrison_EnableBodyClip ("Sky Prison Enable Body Clip", Float) = 1

        // ---- 基于场景深度的遮挡判定 ----
        //
        // 现在是默认路径。开销与场景里有多少遮挡物完全无关，逐像素判定，
        // 而且不需要 CPU 侧的三角面射线求交，也不需要 RT 管线那套
        // 「每角色 2 次额外相机渲染 + 3 张全屏 RT + 若干 Blit」。
        //
        // 旧的 CPU 三角面路径保留在代码里，F9 可以临时切回去做对比，
        // 但不再是默认——它的开销随地图复杂度线性增长，扛不住内容量继续增加。
        [Toggle] _SkyPrison_UseSceneDepthOcclusion ("Use Scene Depth Occlusion", Float) = 1
        // 深度差要超过这个值才算被挡住。太小会让贴地的装饰物把角色脚部误判成遮挡。
        _SkyPrison_SceneDepthBias ("Scene Depth Bias", Range(0,2)) = 0.05
        // 判定的软过渡宽度，避免遮挡边界出现硬锯齿。
        _SkyPrison_SceneDepthSoftness ("Scene Depth Softness", Range(0.001,2)) = 0.15
        // 脚到头的深度补偿。深度路径下必须是 0——这一项是从 CPU 路径照搬来的，
        // 但两条路径的输入不同：CPU 路径从参考点发射线，射线不知道像素在角色身上多高，
        // 必须手工补偿；深度路径拿到的是像素真实的 worldPos，高度已经在里面，
        // 视矩阵会自动把「更高＝离俯视相机更近」算进去。再减一次就是同一个修正做两遍。
        //
        // 初版留了 0.7，后果是整个上半身的 charEye 被压到场景前面，diff 恒负，
        // 全场遮挡物一起失效——当时误以为是阈值或草的问题，其实和遮挡物毫无关系。
        // 诊断模式 4 下表现为「脚黑、越往上越红」，梯度形状就是这一项本身。
        _SkyPrison_SceneDepthFootScale ("Scene Depth Foot Scale", Range(0,2)) = 0

        // 深度诊断可视化。切到深度路径后场景里所有遮挡物一起失效，
        // 这种「全灭」不是参数没调好，是某个输入根本不对。靠读代码推理定位不了，
        // 直接把着色器实际读到的数画到屏幕上：
        //   1 = 场景深度（_CameraDepthTexture 采样并线性化）
        //   2 = 角色自身深度
        //   3 = 两者之差   绿=角色更远（应判定为被挡） 红=角色更近 黑=差值≈0
        // 若模式 1 全黑或全白，说明深度图压根没绑上，问题在管线不在阈值。
        _SkyPrison_SceneDepthDebug ("Scene Depth Debug View", Float) = 0

        // 整张精灵图共用脚底深度，而不是逐像素用自己的 worldPos。
        // 关掉会退回「billboard 几何体参与深度比较」，角色会被遮挡物从中间切开。
        [Toggle] _SkyPrison_UseRootAnchorDepth ("Use Root Anchor Depth", Float) = 1

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
        // 高度雾。必须和 Spine-Skeleton 接同一套，否则角色被遮挡换成这个材质的瞬间
        // 雾没了、色调就跳。注意路径是 Core/Includes/，不是插件注释里写的 Core/Library/。
        //
        // 雾的 include 用了 URP 的 _TimeParameters，而这里是内置管线的 CGPROGRAM，
        // 只有 UnityCG.cginc，不桥接会报 undeclared identifier。
        // 内置管线里 _Time.y 是秒数。必须排在 include 之前。
        #define _TimeParameters float4(_Time.y, sin(_Time.y), cos(_Time.y), unity_DeltaTime.x)
        #include "Assets/BOXOPHOBIC/Atmospheric Height Fog/Core/Includes/AtmosphericHeightFog.cginc"

        sampler2D _MainTex;
        sampler2D _OcclusionTex;
        sampler2D _SkyPrison_CleanCharacterOutlineTex;
        float4 _OcclusionTex_TexelSize;

        float4 _TintColor;
        float _StraightAlphaInput;
        float _SkyPrison_Force3DPropOpaqueAlpha;

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

            // 圆形 16 方向、内外两圈（宽度 ±0.5px）取邻域最小值再平均：原来方形 8 邻域 +
            // 二值结果会在转角处出锯齿台阶和缺口，两圈平均给内侧边缘一个像素的抗锯齿过渡。
            float2 texel = abs(_SkyPrison_StatusOutlinePresence_TexelSize.xy);
            float rIn = max(0.5, widthPixels - 0.5);
            float rOut = max(1.0, widthPixels + 0.5);
            float minIn = 1.0;
            float minOut = 1.0;
            [unroll] for (int k = 0; k < 16; k++)
            {
                float a = k * 0.39269908;
                float2 dir = float2(cos(a), sin(a)) * texel;
                minIn = min(minIn, SampleStatusOutlinePresenceRaw(screenUV + dir * rIn));
                minOut = min(minOut, SampleStatusOutlinePresenceRaw(screenUV + dir * rOut));
            }

            float center = SampleStatusOutlinePresenceRaw(screenUV);
            return saturate(center - 0.5 * (minIn + minOut));
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

            // 3D道具模式：源贴图的alpha通道不代表透明度，强制视为完全不透明，
            // 不参与下面的CleanupAlpha/clip镂空判定。
            if (_SkyPrison_Force3DPropOpaqueAlpha > 0.5)
                texColor.a = 1.0;

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

        // ================= 基于场景深度的遮挡判定（阶段一：与旧路径并存）=================
        //
        // 旧路径：CPU 每帧对每个遮挡物做逐三角面射线求交，决定哪些「授权」，再由
        // ScreenSpaceOutlineRTManager 把授权的渲染进 RT，求交得到 _OcclusionTex。
        // 开销 = 渲染体数 × 三角面数 × 采样点 × 每帧，随地图复杂度线性增长。
        // 实测一台叉车（6 个渲染体、包围盒 10.7x5.7x4.7）单帧吃掉 15.9ms，
        // 占 21 个遮挡物总开销的 90%。
        //
        // 新路径：直接采样 URP 已经在生成的 _CameraDepthTexture（项目 URP 资产里
        // m_RequireDepthTexture: 1），把「这个像素后面有没有更近的不透明几何体」交给
        // 深度比较。开销与场景里有多少遮挡物完全无关，而且是逐像素，比三角面更准。
        //
        // Spine 在透明队列（Transparent+40）不写深度，所以深度图里只有不透明几何体，
        // 不含角色自己——正是我们要比较的对象。
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

        float _SkyPrison_UseSceneDepthOcclusion;
        float _SkyPrison_SceneDepthBias;
        float _SkyPrison_SceneDepthSoftness;
        float _SkyPrison_SceneDepthFootScale;
        float _SkyPrison_SceneDepthDebug;
        float _SkyPrison_UseRootAnchorDepth;

        /// 把深度缓冲的原始值换算成「离相机多远」。
        /// 正交和透视的换算完全不同，必须分开——LinearEyeDepth 只对透视成立，
        /// 本项目相机是正交（orthographic size 12），用错会得到完全无意义的距离。
        float SkyPrisonSceneEyeDepth(float rawDepth)
        {
        #if defined(UNITY_REVERSED_Z)
            float d01 = 1.0 - rawDepth;
        #else
            float d01 = rawDepth;
        #endif
            float ortho = lerp(_ProjectionParams.y, _ProjectionParams.z, d01);
            float persp = LinearEyeDepth(rawDepth);
            return lerp(persp, ortho, unity_OrthoParams.w);
        }

        /// worldPos 处的片元离相机多远。视空间 z 取负即为眼深度，正交/透视都成立。
        float SkyPrisonFragmentEyeDepth(float3 worldPos)
        {
            float3 viewPos = mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).xyz;
            return -viewPos.z;
        }

        /// 与旧 GetHiddenFactor 输出同语义的 [0,1] 软值，便于 A/B 对比。
        ///
        /// relativeY 是当前像素相对角色脚底的高度。2.5D 里角色是竖直的 billboard，
        /// 但在世界里它是「站在地上」的——脚比头离相机更近。不做补偿的话，
        /// 头部像素会被判定成比实际更靠前，站在矮物件后面时头会穿出来。
        /// 这套补偿项目里本来就有（_SkyPrison_UseFootDepthCompensation 那一组），
        /// 这里复用同样的思路。
        /// 角色的判定深度 —— 只取脚底那一个点，整张精灵图共用。
        ///
        /// Spine 为了不被压缩必须垂直于视线（45 度对齐），于是 billboard 在世界里是一个
        /// 有延展的平面，会和叉车这类立体几何相交。一旦拿每个像素自己的 worldPos 去比深度，
        /// 交线以近的部分被挡、以远的不被挡，角色就从中间被切开 —— 表现为「脚在外面、
        /// 头插进模型里」。这是「用几何深度比较」在 2.5D 下的固有结果，调参数救不了。
        ///
        /// 2.5D 的排序语义本来就是「谁的脚在前面」，不是「谁的表面离相机近」。
        /// 所以判定深度只取脚底：unity_ObjectToWorld 的平移部分就是 Spine 根节点的世界
        /// 坐标，不需要 CPU 每帧传任何东西。整张图共用一个深度值，精灵图在深度上退化成
        /// 一个点，不再与任何几何体相交。
        float3 SkyPrisonRootWorldPos()
        {
            return float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
        }

        // 注意用 CG 风格声明，不能用 URP 的 TEXTURE2D_X / SAMPLER。
        // 这个着色器整体是 CG（UnityCG.cginc、tex2D、UNITY_DECLARE_DEPTH_TEXTURE），
        // 混进 URP ShaderLibrary 的宏会直接报 unrecognized identifier。
        sampler2D _SkyPrison_OccluderFootprintDepth;

        /// 用「落地深度图」判定，而不是 _CameraDepthTexture。
        ///
        /// _CameraDepthTexture 里是几何表面深度。45 度俯视下高的物体顶部会朝相机
        /// 倾过来——叉车顶棚的表面深度可以比站在叉车前面的角色脚底还小，于是同一台
        /// 叉车「顶部判在前、底部判在后」，角色被从中间切开（实测：头绿身红）。
        ///
        /// 落地深度图里每个遮挡物是一个平坦的常数（它自己根节点的深度），
        /// 比较的是落地点，符合 2.5D「谁的脚在前面」的排序语义。
        ///
        /// 没有遮挡物覆盖的像素保持清空值（极大），一定判为不遮挡——
        /// 所以角色露在遮挡物轮廓外面的部分正常显示，遮挡依然是逐像素的。
        float GetHiddenFactorFromFootprint(float2 screenUV, float3 worldPos)
        {
            // 单点根节点锚点是给没有厚度的 Spine 精灵设计的——落地深度图里每个遮挡物
            // 也是拍扁成常数（自己根节点深度），两边都用锚点比较，符合"谁的脚在前面"
            // 的 2.5D 排序语义。3D 通道场景物（比如箱子）是有实体体积的立方体，不同
            // 像素跟根节点锚点的真实深度差异很大，同一个锚点判定套到整个表面上必然有
            // 一部分像素判错——同时能解释"该显示时全灭"和"该藏时露出来"两个症状。
            // _SkyPrison_UseRootAnchorDepth=0 时改用这个像素自己的世界坐标，3D 通道
            // 场景物在 EnsureCompositeDefaults 里强制关掉这个开关。
            float3 anchorPos = _SkyPrison_UseRootAnchorDepth > 0.5
                ? SkyPrisonRootWorldPos()
                : worldPos;
            float charEye = SkyPrisonFragmentEyeDepth(anchorPos);

            float occluderEye = tex2D(_SkyPrison_OccluderFootprintDepth, screenUV).r;

            float diff = charEye - occluderEye;
            float bias = max(_SkyPrison_SceneDepthBias, 0.0);
            float softness = max(_SkyPrison_SceneDepthSoftness, 0.0001);
            return smoothstep(bias, bias + softness, diff);
        }

        float GetHiddenFactorFromSceneDepth(float2 screenUV, float3 worldPos, float relativeY)
        {
            float3 anchorPos = _SkyPrison_UseRootAnchorDepth > 0.5
                ? SkyPrisonRootWorldPos()
                : worldPos;

            float charEye = SkyPrisonFragmentEyeDepth(anchorPos)
                          - relativeY * _SkyPrison_SceneDepthFootScale;

            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
            float sceneEye = SkyPrisonSceneEyeDepth(rawDepth);

            // sceneEye 明显小于 charEye ＝ 前面有不透明几何体挡着。
            float diff = charEye - sceneEye;
            float bias = max(_SkyPrison_SceneDepthBias, 0.0);
            float softness = max(_SkyPrison_SceneDepthSoftness, 0.0001);
            return smoothstep(bias, bias + softness, diff);
        }

        /// 深度诊断可视化，只在 _SkyPrison_SceneDepthDebug > 0 时被调用。
        ///
        /// 存在的理由：判定「全灭」时，阈值、深度图绑定、正交换算、脚部补偿
        /// 这四者都可能是元凶，而它们在最终画面上的表现完全一样（都是不遮挡）。
        /// 把中间量画出来能一眼分开：深度图没绑 → 模式 1 全黑/全白；
        /// 换算错 → 模式 1 有图但灰度分布荒谬；纯阈值 → 模式 3 有绿色但画面没遮挡。
        float3 SkyPrisonSceneDepthDebugColor(float2 screenUV, float3 worldPos, float relativeY)
        {
            // 诊断必须和实际判定读同一份数据，否则画面会指向错误的结论。
            // 判定已经改用落地深度图，这里也跟着改——不再看 _CameraDepthTexture。
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
            float sceneEye = tex2D(_SkyPrison_OccluderFootprintDepth, screenUV).r;
            float charEye = SkyPrisonFragmentEyeDepth(SkyPrisonRootWorldPos());

            // 模式 1：深度图的原始采样值，不做任何换算。
            //
            // 这个模式是判定「深度图到底有没有内容」的唯一可信依据。经过线性化的
            // 模式 2/3 一旦归一化常数选错就会整片饱和，看起来和「没绑定」一模一样——
            // 上一版除以 50 就踩了这个坑，纯白既可能是没绑也可能只是相机远。
            // 原始值不受这个影响：均匀一致 = 没内容，有明暗变化 = 有内容。
            if (_SkyPrison_SceneDepthDebug < 1.5)
                return rawDepth.xxx;

            // 模式 2/3：对数刻度。相机到场景的实际距离未知（正交 size 12 只决定
            // 视野大小，不决定相机站多远），任何固定除数都可能整片饱和。
            // log 刻度在 1~1000 全程都有分辨力，不需要预先知道量级。
            if (_SkyPrison_SceneDepthDebug < 2.5)
                return saturate(log2(max(sceneEye, 1.0)) / 10.0).xxx;

            if (_SkyPrison_SceneDepthDebug < 3.5)
                return saturate(log2(max(charEye, 1.0)) / 10.0).xxx;

            // 模式 4：判定结果本身。阈值多大不重要，先看符号对不对——
            // 绿=角色比场景远（应被遮挡），红=角色在前面，越亮差值越大。
            // 用 log 压缩而不是线性放大后 saturate。
            //
            // 之前是 saturate(diff * 0.2)，只要 |diff| ≥ 5 就饱和 —— 于是「落地深度图是空的
            // （diff ≈ -1e9）」和「叉车只比角色远 10 个单位」画出来一模一样的纯红，
            // 看着像证据其实什么都没区分。和当初 /50 归一化整片全白是同一个错误。
            float diff = charEye - sceneEye;
            float mag = saturate(log2(abs(diff) + 1.0) / 10.0);
            return diff > 0.0 ? float3(0.0, mag, 0.0) : float3(mag, 0.0, 0.0);
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

        // ---- 全息去重 ----
        // Spine 角色是一片片部件分别画的，被挡住时每片各自叠一层全息，部件重叠处
        // （手压身体、头发压头）就会亮一倍。HologramCoverageFeature 在画透明物体之前，
        // 用下面的 HologramCoverage Pass 把每个像素上「被挡住的部件 alpha」累加进
        // _SP_HoloCoverage（R16F）。NormalBody 画全息时每片按 alpha / 总和 缩放，
        // 重叠几层加起来都正好是一层，整个人是一块均匀的填充。
        // 仍然在角色自己的绘制顺序里画，前后排序（sortingOrder）不受影响。
        sampler2D _SP_HoloCoverage;
        float _SP_HoloCoverageActive;

        float SkyPrisonHologramDedupWeight(float2 screenUV, float alpha)
        {
            if (_SP_HoloCoverageActive < 0.5)
                return 1.0; // Feature 没跑（场景视图、预览）：维持旧行为
            float sum = tex2D(_SP_HoloCoverage, screenUV).r;
            if (sum <= 0.0001)
                return alpha;
            return alpha * min(sum, 1.0) / sum;
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
            Cull [_SkyPrison_CullMode]
            // 3D道具模式下切成正常深度测试——保证自己网格内部的复杂结构排序正确，
            // 代价是被真实遮挡物挡住的部分这条Pass不会画出来（正常深度测试的题中之义），
            // 那部分交给下面 HologramOverlay3D 这条新Pass 单独补上全息。Spine默认值
            // （Off/Always）完全不变。
            ZTest [_SkyPrison_ZTestMode]
            ZWrite [_SkyPrison_ZWriteMode]
            Blend One OneMinusSrcAlpha
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                // 一次性诊断——放在函数最开头、任何 clip/discard 之前，排除"属性确实
                // 传到了材质、但被更早的 clip 提前干掉这个像素"这个可能性。品红=这条
                // Pass 真的被 URP 调度并执行到了这一行；如果连品红都看不到，说明问题
                // 出在这条 Pass 有没有被绘制，不是判定逻辑本身。
                if (_SkyPrison_SceneDepthDebug > 3.5)
                    return fixed4(1, 0, 1, 1);

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

                // 高度雾。位置和 Spine-Skeleton 里那一处对齐：接在环境色调+阴影遮罩
                // 之后、乘回 alpha 之前，只作用于身体本色，不碰后面叠加的发光。
                // 两个着色器的雾必须在同一个位置用同一份参数，否则换材质的瞬间会跳色。
                float4 skyPrisonFog = GetAtmosphericHeightFog(i.worldPos);
                straightRgb = ApplyAtmosphericHeightFog(straightRgb, skyPrisonFog);

                c.rgb = straightRgb * alpha;

                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);

                // 阶段一：两条路径并存，靠 _SkyPrison_UseSceneDepthOcclusion 切换。
                // 新路径逐像素比较场景深度，开销与遮挡物数量无关；旧路径靠 CPU
                // 逐三角面求交产出的 _OcclusionTex。两者输出同为 [0,1] 软值，可直接对比。
                float hidden = _SkyPrison_UseSceneDepthOcclusion > 0.5
                    ? (_SkyPrison_EnableBodyClip < 0.5
                        ? 0.0
                        : GetHiddenFactorFromFootprint(screenUV, i.worldPos))
                    : GetHiddenFactor(screenUV);

                // 深度诊断优先于一切后续处理返回：后面被挡的像素会 discard，
                // 而「什么都没被挡」正是要诊断的现象，放在后面就永远看不到。
                if (_SkyPrison_SceneDepthDebug > 0.5)
                    return float4(SkyPrisonSceneDepthDebugColor(screenUV, i.worldPos, i.relativeY), 1.0);

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

                        float holoW = SkyPrisonHologramDedupWeight(screenUV, alpha);
                        // 状态描边（灼烧等）被挡住就跟着淡掉，不再穿墙——它和 UnitStatusOutlineGlowFeature
                        // 的外圈辉光一起被遮挡（辉光那边只从没被挡住的身体部分往外发光）。
                        // 溶解边仍然跟着全息走：角色在墙后溶解消失也要看得见。
                        float3 dissolveGlowOnly = _SkyPrison_DissolveEdgeColor.rgb * dissolveEdgeGlowStrength;
                        float3 statusGlowVisible = _SkyPrison_StatusOutlineColor.rgb * statusOutlineGlowStrength * (1.0 - saturate(hidden));
                        float3 outRgb = _SkyPrison_HologramFillColor.rgb * (silA + glowAdd * 0.6)
                                      + dissolveGlowOnly * saturate(silA + glowAdd)
                                      + statusGlowVisible;
                        return float4(outRgb * holoW, silA * holoW);
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

        // 全息去重的计数 Pass：由 HologramCoverageFeature 用 DrawRenderer 显式调用
        // （LightMode 不是 UniversalForward，URP 不会自己调度）。判定条件必须和
        // NormalBody 里走全息分支的条件逐项一致，否则计数和实际绘制对不上。
        Pass
        {
            Name "HologramCoverage"
            Tags { "LightMode"="SkyPrisonHologramCoverage" }
            Cull [_SkyPrison_CullMode]
            ZTest Always
            ZWrite Off
            Blend One One
            ColorMask R

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragHoloCoverage
            #pragma target 3.0

            float4 fragHoloCoverage(v2f i) : SV_Target
            {
                clip(_SkyPrison_UseHologramFill - 0.5);
                clip(0.5 - _SkyPrison_Force3DPropOpaqueAlpha); // 3D道具走 HologramOverlay3D，不参与

                float alpha;
                SamplePremulBody(i, alpha);
                clip(alpha - 0.001);

                float dissolveAmount = saturate(_SkyPrison_DissolveAmount);
                if (dissolveAmount > 0.0001)
                {
                    float dissolveNoiseValue = tex2D(_SkyPrison_DissolveNoiseTex, i.worldPos.xy * _SkyPrison_DissolveNoiseScale).r;
                    clip(dissolveNoiseValue - dissolveAmount);
                }

                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);
                float hidden = _SkyPrison_UseSceneDepthOcclusion > 0.5
                    ? (_SkyPrison_EnableBodyClip < 0.5
                        ? 0.0
                        : GetHiddenFactorFromFootprint(screenUV, i.worldPos))
                    : GetHiddenFactor(screenUV);
                clip(hidden - 0.001);

                return float4(alpha, 0, 0, 0);
            }
            ENDCG
        }

        // 2026-08-15：3D道具专属——上面 NormalBody 那条Pass 为了让道具自己的立体结构
        // 正确排序，改成了正常深度测试，代价是真被遮挡物挡住的部分不会画出来。这条Pass
        // 单独补上"被挡住时改画全息"：ZTest Always（不管真实遮挡物挡没挡都画），
        // 但只在 hidden>0（我们自己那套逐像素场景深度判断认定"被挡住"）时才真正输出
        // 颜色，其余像素 discard——这样正常可见的部分完全交给 NormalBody 那条已经排序
        // 正确的Pass，这里只负责"露出来的隐藏轮廓"，两条Pass 画的像素不重叠。
        // 对 Spine 精灵完全无影响：_SkyPrison_Force3DPropOpaqueAlpha 默认0，最上面
        // 直接 clip 整条Pass 变成空Pass，Spine 的遮挡表现继续走 NormalBody 单条Pass
        // 里 ZTest Always 那套原有逻辑，不会被这条新Pass 干扰或重复绘制。
        Pass
        {
            Name "HologramOverlay3D"
            Tags { "LightMode"="UniversalForward" }
            Cull [_SkyPrison_CullMode]
            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOverlay3D
            #pragma target 3.0

            fixed4 fragOverlay3D(v2f i) : SV_Target
            {
                // 一次性诊断——同 NormalBody 那条，放在最开头、任何 clip 之前。青色=这条
                // Pass 真的被 URP 调度并执行到了这一行。
                if (_SkyPrison_SceneDepthDebug > 3.5)
                    return fixed4(0, 1, 1, 1);

                // 只给3D道具用——Spine精灵（Force3DPropOpaqueAlpha==0）这条Pass整个是
                // 空的，遮挡表现继续由 NormalBody 那条Pass 自己的 ZTest Always 处理，
                // 不会跟这里重复画。
                clip(_SkyPrison_Force3DPropOpaqueAlpha - 0.5);

                float alpha;
                SamplePremulBody(i, alpha);
                clip(alpha - 0.001);

                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);
                float hidden = _SkyPrison_UseSceneDepthOcclusion > 0.5
                    ? (_SkyPrison_EnableBodyClip < 0.5
                        ? 0.0
                        : GetHiddenFactorFromFootprint(screenUV, i.worldPos))
                    : GetHiddenFactor(screenUV);

                // 一次性诊断：3D道具被挡住时这条Pass完全没画出东西，怀疑hidden对3D网格
                // 一直算出0。开着诊断模式时跳过下面这行clip，不管hidden是多少都画，
                // 把hidden本身当亮度直接画出来（绿=判定为被挡住，越亮判定越强，
                // 全黑=完全没被判定为挡住）——NormalBody那条Pass的诊断分支因为现在
                // 走真实深度测试，箱子真被挡住时压根不会执行到那段诊断代码，只能在
                // 这条ZTest Always的Pass里才看得到"被挡住的那部分"到底算出了什么。
                if (_SkyPrison_SceneDepthDebug > 0.5)
                    return float4(0.0, saturate(hidden), 0.0, 1.0);

                // 没被挡住的部分交给 NormalBody 那条Pass（已经按正常深度测试画好了），
                // 这里只负责被挡住的那部分，避免两条Pass同一像素画两次。
                clip(hidden - 0.001);

                float2 gridPos  = i.worldPos.xy * _SkyPrison_HologramGridDensity;
                float2 cellFrac = frac(gridPos);
                float2 toLine   = min(cellFrac, 1.0 - cellFrac);
                float  aa       = fwidth(min(toLine.x, toLine.y));
                float  gridMask = 1.0 - smoothstep(_SkyPrison_HologramGridLineWidth - aa,
                                                    _SkyPrison_HologramGridLineWidth + aa,
                                                    min(toLine.x, toLine.y));

                float cycleT = frac(_Time.y / _SkyPrison_HologramCycleLength);
                float sweepFade = smoothstep(0.0, 0.08, cycleT);
                float bandY = cycleT * _SkyPrison_HologramSweepRangeY;
                float dist  = i.relativeY - bandY;
                float front = 1.0 - smoothstep(0.0, 0.03, dist);
                float trail = 1.0 - smoothstep(0.0, _SkyPrison_HologramTrailLength, -dist);
                float waveMask = saturate(front * trail) * sweepFade;

                float silA = saturate(hidden) * _SkyPrison_HologramSilhouetteAlpha * _SkyPrison_HologramFillColor.a;
                float glowAdd = saturate(gridMask * _SkyPrison_HologramGridBright * 0.6 + waveMask) * hidden
                              * _SkyPrison_HologramAlpha * _SkyPrison_HologramFillColor.a;

                if (silA <= 0.001 && glowAdd <= 0.001)
                    discard;

                float3 outRgb = _SkyPrison_HologramFillColor.rgb * (silA + glowAdd * 0.6);
                return float4(outRgb, silA);
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
