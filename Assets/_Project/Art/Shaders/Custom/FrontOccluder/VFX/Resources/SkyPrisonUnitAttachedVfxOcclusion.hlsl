#ifndef SKYPRISON_UNIT_ATTACHED_VFX_OCCLUSION_INCLUDED
#define SKYPRISON_UNIT_ATTACHED_VFX_OCCLUSION_INCLUDED

// 「挂在单位身上的特效」（枪口火光、弹道拖尾）的遮挡判定，跟角色 Spine 本体
// （SpineOcclusionComposite.shader 的 GetHiddenFactorFromFootprint）用同一条规则：
//
//   - ZTest Always，不拿特效自己的几何深度去跟场景比。45 度视角下角色侧身贴墙站，
//     枪口在几何深度上会插进身后的墙里，走普通 ZTest 就被墙吃掉——角色本体不会被
//     吃，是因为它判的是「脚底谁在前面」，特效没有走这套，两边就对不上。
//   - 判定深度取持有者 Spine 根节点（由 CPU 通过 MaterialPropertyBlock 传进
//     _SkyPrison_VfxAnchorWS），跟落地深度图比较。角色露着特效就露着，角色躲进
//     建筑后面特效一起被藏，不会出现「人藏住了、火光浮在墙面上」。
//
// _SkyPrison_VfxAnchorWS.w < 0.5 表示没绑定持有者，不做遮挡（完全可见）——
// 保证 Prefab 被别处直接拿去用时至少不会整个消失。

TEXTURE2D(_SkyPrison_OccluderFootprintDepth);
SAMPLER(sampler_SkyPrison_OccluderFootprintDepth);

float SkyPrisonVfxHiddenFactor(float2 screenUV, float4 anchorWS, float bias, float softness)
{
    if (anchorWS.w < 0.5)
        return 0.0;

    float anchorEye = -TransformWorldToView(anchorWS.xyz).z;
    float occluderEye = SAMPLE_TEXTURE2D(_SkyPrison_OccluderFootprintDepth,
                                         sampler_SkyPrison_OccluderFootprintDepth, screenUV).r;

    float diff = anchorEye - occluderEye;
    bias = max(bias, 0.0);
    softness = max(softness, 0.0001);
    return smoothstep(bias, bias + softness, diff);
}

// Shader Graph「Hue」节点 Degrees 模式的原样实现——两张 RVFX 原版 Shader Graph
// 都用它做色相偏移，这里照搬保证换着色器后颜色不变。
float3 SkyPrisonHueDegrees(float3 In, float Offset)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 P = lerp(float4(In.bg, K.wz), float4(In.gb, K.xy), step(In.b, In.g));
    float4 Q = lerp(float4(P.xyw, In.r), float4(In.r, P.yzx), step(P.x, In.r));
    float D = Q.x - min(Q.w, Q.y);
    float E = 1e-10;
    float3 hsv = float3(abs(Q.z + (Q.w - Q.y) / (6.0 * D + E)), D / (Q.x + E), Q.x);

    float hue = hsv.x + Offset / 360.0;
    hsv.x = (hue < 0.0) ? hue + 1.0 : (hue > 1.0) ? hue - 1.0 : hue;

    float4 K2 = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 P2 = abs(frac(hsv.xxx + K2.xyz) * 6.0 - K2.www);
    return hsv.z * lerp(K2.xxx, saturate(P2 - K2.xxx), hsv.y);
}

#endif
