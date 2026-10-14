using UnityEngine;

/// <summary>
/// 遮挡物专用的 URP Rendering Layer 位。
///
/// 为什么用 Rendering Layer 而不是 GameObject Layer：
/// 遮挡物是运行时从地形装饰物定义生成的，视觉体和地形一样都在 World3D 层上，
/// 按 GameObject Layer 根本分不开。而改它们的 GameObject Layer 会直接影响
/// 相机 cullingMask（Main Camera 的 mask 是 384 = World3D + Character2D），
/// 挪到别的层就直接看不见了。
///
/// Rendering Layer 是 URP 里和 GameObject Layer 完全独立的一套机制，
/// 只影响「哪些 pass 会画到它」，不影响剔除和正常渲染，正是这个场景要的东西。
///
/// 用途：落地深度 pass 只画带这一位的 Renderer。
/// 2.5D 的排序语义是「谁的脚在前面」，不是「谁的表面离相机近」——高的物体
/// （比如叉车顶棚）在 45 度俯视下会朝相机倾过来，几何深度比站在它前面的角色
/// 还小，直接拿 _CameraDepthTexture 比较会误判成「叉车在角色前面」。
/// 落地深度图让每个遮挡物写自己根节点的深度（整个物体一个平坦值），
/// 比较的就是落地点而不是表面。
/// </summary>
public static class SkyPrisonOccluderRenderingLayer
{
    /// <summary>
    /// 第 2 位。0 位是 URP 的 Default，1 位常被其他功能占用，从 2 起比较安全。
    /// 改这个值时记得同步 Renderer Feature 上的过滤设置——它们必须一致，
    /// 否则表现是「落地深度图全空 = 所有遮挡物都不遮挡」，而不会有任何报错。
    /// </summary>
    public const uint Mask = 1u << 2;

    /// <summary>
    /// 给遮挡物视觉体打上标记。用「或」而不是直接赋值，避免抹掉别的系统
    /// 已经写在同一个 Renderer 上的 rendering layer 位。
    /// </summary>
    public static void Mark(Renderer renderer)
    {
        if (renderer == null)
            return;

        renderer.renderingLayerMask |= Mask;
    }
}
