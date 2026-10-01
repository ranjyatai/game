#ifndef SKYPRISON_TERRAIN_GRATE_INCLUDED
#define SKYPRISON_TERRAIN_GRATE_INCLUDED

// 地形「格栅」地表：被刷成格栅层的地方，按格栅图案丢弃像素，透出地形下方的建筑群。
//
// 只丢视觉像素，不碰 TerrainCollider——角色照常站在格栅上走。这也是不用 Unity 自带
// 地形挖洞（Paint Holes）的原因：挖洞会连碰撞一起挖掉。
//
// 所有 pass（主绘制 / 追加层 / 阴影 / 深度 / 深度法线）都在原版 ClipHoles 旁边调用
// 这一个函数，保证 12 个地形层分三次绘制时每次镂空位置一致，阴影也从格栅漏下去。
//
// 参数都是全局变量，由 SkyPrisonTerrainGrateBinder 按场景里的 Terrain 写入：
//   _SkyPrisonGrateSplat    格栅层所在的那张 splat（alphamapTextures[layer / 4]）
//   _SkyPrisonGrateChannel  格栅层在那张 splat 里的通道（one-hot；全 0 = 场景里没有格栅层）
//   _SkyPrisonGrateParams   x,y = 地形尺寸（米），z = 格子间距（米），w = 栅条宽度（米）
//
// uv 是覆盖整块地形的 0~1 控制贴图坐标，乘地形尺寸就是相对地形原点的米数，
// 所以栅格固定按米对齐，不受分块/实例化影响。

TEXTURE2D(_SkyPrisonGrateSplat);
SAMPLER(sampler_SkyPrisonGrateSplat);
float4 _SkyPrisonGrateChannel;
float4 _SkyPrisonGrateParams;

// 纹理遮罩（优先于程序化格子）：哪里镂空由贴图决定，跟贴图上画的栅条逐像素对齐。
//   _SkyPrisonGrateMaskST     与这个地形层自己的 _SplatN_ST 相同的平铺（由 TerrainLayer 的
//                             tileSize/tileOffset 换算），保证遮罩和地形层贴图同步平铺
//   _SkyPrisonGrateMaskParams x: 1 = 用纹理遮罩，y: 1 = 读 Alpha / 0 = 读 R（灰度），z: 阈值
TEXTURE2D(_SkyPrisonGrateMask);
SAMPLER(sampler_SkyPrisonGrateMask);
float4 _SkyPrisonGrateMaskST;
float4 _SkyPrisonGrateMaskParams;

// 「虚空」地表：刷到哪里地面整块消失。和格栅同一套机制，各占一个通道，可以同时存在。
//   _SkyPrisonVoidSplat / _SkyPrisonVoidChannel 同格栅的 Splat / Channel（全 0 = 没有虚空层）
//
// 边缘干净的原因：splat 权重在纹素之间是双线性插值的，按 0.5 裁剪得到的是一条平滑等值线，
// 不像 Terrain 自带挖洞那样按格子「有/无」出锯齿台阶。权重 < 0.5 的过渡带会混进虚空层的
// 颜色（黑），在边缘形成一圈自然的暗边。
TEXTURE2D(_SkyPrisonVoidSplat);
SAMPLER(sampler_SkyPrisonVoidSplat);
float4 _SkyPrisonVoidChannel;

void SkyPrisonClipGrate(float2 uv)
{
    if (dot(_SkyPrisonVoidChannel, float4(1, 1, 1, 1)) > 0.5)
    {
        float voidWeight = dot(SAMPLE_TEXTURE2D(_SkyPrisonVoidSplat, sampler_SkyPrisonVoidSplat, uv), _SkyPrisonVoidChannel);
        clip(0.5 - voidWeight);
    }

    if (dot(_SkyPrisonGrateChannel, float4(1, 1, 1, 1)) < 0.5)
        return;

    float weight = dot(SAMPLE_TEXTURE2D(_SkyPrisonGrateSplat, sampler_SkyPrisonGrateSplat, uv), _SkyPrisonGrateChannel);
    if (weight < 0.5)
        return;

    if (_SkyPrisonGrateMaskParams.x > 0.5)
    {
        float2 maskUV = uv * _SkyPrisonGrateMaskST.xy + _SkyPrisonGrateMaskST.zw;
        float4 m = SAMPLE_TEXTURE2D(_SkyPrisonGrateMask, sampler_SkyPrisonGrateMask, maskUV);
        float solid = _SkyPrisonGrateMaskParams.y > 0.5 ? m.a : m.r;
        clip(solid - _SkyPrisonGrateMaskParams.z);
        return;
    }

    float cell = max(_SkyPrisonGrateParams.z, 0.01);
    float bar = clamp(_SkyPrisonGrateParams.w, 0.0, cell);

    // 到格子中心的距离（米）；两个方向都落在开口里才丢弃，留下的就是十字交叉的栅条。
    float2 meters = uv * _SkyPrisonGrateParams.xy;
    float2 fromCenter = abs(frac(meters / cell) - 0.5) * cell;
    float halfOpen = 0.5 * (cell - bar);

    clip((fromCenter.x < halfOpen && fromCenter.y < halfOpen) ? -1.0 : 1.0);
}

#endif
