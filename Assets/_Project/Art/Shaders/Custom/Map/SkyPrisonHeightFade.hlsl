#ifndef SKYPRISON_HEIGHT_FADE_INCLUDED
#define SKYPRISON_HEIGHT_FADE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// 高层建筑"可显示高度"淡出——建筑自己底部以上超过 _HeightFadeThreshold 的部分，
// 在接下来 _HeightFadeDistance 这段距离内，逐渐淡出到全透明。用于避免高楼把镜头
// 和地图背景之间的视野挡得太死（参见 game-design-and-progress 记忆里
// "渲染/演出资源策略"一节，暗黑破坏神/都市天际线那类"屋顶淡出/建筑半透明"的做法）。
//
// _HeightFadeBaseY 是这栋建筑自己的地面高度（世界坐标Y），不是固定的世界Y=0——
// 地形有高低差时，每栋建筑各自的"从自己脚下往上数"才对，不能用同一个世界Y阈值套
// 所有建筑。由 SkyPrisonHeightFadeController.cs 在 Awake 时按 Renderer Bounds 算一次，
// 通过 MaterialPropertyBlock 传进来（建筑不会动，只需要算一次，不用每帧更新）。
float GetHeightFadeAlpha(float worldY, float baseY, float threshold, float fadeDistance)
{
    float heightAboveBase = worldY - baseY;
    float safeDistance = max(fadeDistance, 0.0001);
    return 1.0 - saturate((heightAboveBase - threshold) / safeDistance);
}

// 2026-07-17：改用抖动裁切（dithered clip）代替Alpha混合——材质切 Surface=Transparent
// 之后，贴图自己的Alpha通道会第一次真正参与混合，很多贴图的Alpha从来不是按"有意义的
// 透明度"画的，直接导致贴图看起来"破了洞"（真实踩过的Bug，见
// feedback-asset-store-integration-workflow 记忆）。抖动裁切走"保留/丢弃"的二值判断，
// 材质保持 Opaque，完全不会碰贴图自身alpha通道，也是Unity自己LOD_FADE_CROSSFADE
// （LODCrossFade.hlsl）同一套技术，游戏行业标准做法，不是自己发明的土办法。
//
// _DitheringTexture/_DitheringTextureInvSize 是URP管线每帧自动全局设置的抖动贴图
// （UniversalRenderPipeline.SetupPerFrameShaderConstants），不需要额外配置/挂载，
// 场景里有没有用到LOD Group都会被设置好。
TEXTURE2D(_DitheringTexture);
float _DitheringTextureInvSize;

// 用法：在 fragment shader 里，用 positionCS（SV_POSITION）和世界坐标 positionWS.y
// 调用这个函数——命中裁切阈值的像素会被 clip() 直接丢弃，不需要再手动处理alpha。
void ClipHeightFade(float4 positionCS, float worldY, float baseY, float threshold, float fadeDistance)
{
    float alpha = GetHeightFadeAlpha(worldY, baseY, threshold, fadeDistance);

    float2 uv = positionCS.xy * _DitheringTextureInvSize;
    float ditherValue = SAMPLE_TEXTURE2D(_DitheringTexture, sampler_PointRepeat, uv).a;

    clip(alpha - ditherValue);
}

#endif
