#ifndef SKYPRISON_SIGHTLINE_FADE_INCLUDED
#define SKYPRISON_SIGHTLINE_FADE_INCLUDED

// 相机挡视线时淡出候选建筑——判定完全在GPU侧、O(1)、不随场景复杂度变化。
//
// 原理：不去反推"谁挡住了玩家"，而是让每个候选建筑的着色器自己判断
// "我这个像素是不是正好卡在相机和玩家连线中间、而且比玩家更靠近相机"。
// 满足就自己淡出，不满足什么都不做——不需要知道具体是哪个物体，
// 因为挡路的那个物体自己会知道是自己。
//
// CPU侧唯一的工作是每帧写一次玩家世界坐标（SkyPrisonPlayerPositionBroadcaster.cs），
// 常数开销，跟候选建筑有多少个完全无关。

// 只有玩家坐标是真正的全局值（所有候选建筑共用同一个玩家位置，理所当然）。
// 淡出半径/柔和度改成函数参数、由各自材质的CBUFFER传入——不声明成全局变量，
// 是因为调用方（各个候选建筑材质）也会在自己的CBUFFER里声明同名的可调参数，
// 两边都声明会导致HLSL重复定义报错；做成参数后每个候选建筑还能各自调不同的
// 淡出范围，比写死成全局共用更灵活。
float3 _SkyPrison_PlayerWorldPos;

/// 世界坐标 -> 眼深度。跟落地深度遮挡、距离模糊用的是同一套正交/透视分流公式，
/// 这个项目相机是正交，LinearEyeDepth那套透视公式在这里直接用会得到无意义的值
/// （今天已经在两个不同的地方踩过这个坑）。
float SkyPrisonSightlineEyeDepth(float3 worldPos)
{
    float3 viewPos = mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).xyz;
    return -viewPos.z;
}

/// 8x8 Bayer风格的简易屏幕空间抖动噪声，用来把"淡出程度"转成"部分像素直接消失"
/// 的抖动裁切图案，而不是真正的alpha混合——好处是候选建筑不需要切到透明队列，
/// 任何现有的不透明材质都能直接接入，不涉及透明排序问题。
float SkyPrisonDither(float2 screenPixelPos)
{
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(screenPixelPos, magic.xy)));
}

/// 返回0~1的"挡路强度"：0=完全没挡、不用淡出，越接近1挡得越实。
/// fragWorldPos：当前像素的世界坐标。screenUV：当前像素的屏幕UV(0~1，跟Unity一致)。
/// radius/softness：屏幕空间淡出范围。depthSoftness：深度关系翻转时的柔和过渡带宽度
/// （世界单位），越大越提前开始淡入，避免深度关系突然翻转时的硬切。
/// 均由调用方材质自己的CBUFFER传入，见文件顶部说明。
float SkyPrisonSightlineFadeAmount(float3 fragWorldPos, float2 screenUV, float radius, float softness, float depthSoftness)
{
    float4 playerClip = mul(UNITY_MATRIX_VP, float4(_SkyPrison_PlayerWorldPos, 1.0));
    if (playerClip.w <= 0.0001)
        return 0.0; // 玩家理论上不会跑到相机背后，防御性处理

    float2 playerScreenUV = (playerClip.xy / playerClip.w) * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    playerScreenUV.y = 1.0 - playerScreenUV.y;
#endif

    float screenDist = distance(screenUV, playerScreenUV);
    radius = max(0.001, radius);
    softness = max(0.001, softness);
    float proximity = 1.0 - smoothstep(radius, radius + softness, screenDist);

    if (proximity <= 0.0)
        return 0.0;

    // 必须比玩家更靠近相机才有资格"挡路"——否则玩家背后的墙也会跟着一起淡，
    // 那样反而会在玩家身后开一个不该有的透明洞。
    //
    // 这里原来是硬判断(fragEye >= playerEye就直接判定不挡)，深度关系一旦在某一帧
    // 翻转(比如玩家走位让墙从"比玩家远"变成"比玩家近")，效果会瞬间从0跳到当前
    // proximity值——没有过渡，容易看到突然出现/消失。改成跟深度差值本身挂钩的
    // 平滑过渡，深度差越大越接近1，跨过分界线时是渐变而不是硬切——跟落地深度
    // 遮挡那套"差值+柔和区间"是同一个思路。
    float depthDiff = SkyPrisonSightlineEyeDepth(_SkyPrison_PlayerWorldPos) - SkyPrisonSightlineEyeDepth(fragWorldPos);
    float depthFade = smoothstep(0.0, max(0.001, depthSoftness), depthDiff);

    return proximity * depthFade;
}

/// 候选建筑材质只需要在自己fragment末尾调这一行。screenPixelPos用input.positionCS.xy
/// （像素坐标，不是UV），抖动图案需要整数像素网格才不会随分辨率跑位。
void SkyPrisonApplySightlineClip(float3 fragWorldPos, float2 screenUV, float2 screenPixelPos, float radius, float softness, float depthSoftness)
{
    float fade = SkyPrisonSightlineFadeAmount(fragWorldPos, screenUV, radius, softness, depthSoftness);
    clip(SkyPrisonDither(screenPixelPos) - fade);
}

#endif
