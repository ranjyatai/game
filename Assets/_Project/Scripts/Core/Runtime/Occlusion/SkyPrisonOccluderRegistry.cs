using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景里所有遮挡物视觉体的注册表，供落地深度 pass 使用。
///
/// 为什么用注册表而不是按图层过滤：遮挡物是运行时从地形装饰物定义生成的，
/// 视觉体和地形一样都在 World3D 层，按 GameObject Layer 分不开；而改它们的
/// 图层会直接影响相机 cullingMask（Main Camera 是 384 = World3D + Character2D），
/// 挪走就看不见了。触发器本来就已经把这些 Renderer 收集在
/// currentOccluderRenderers 里，直接复用是最省的做法——注册只在
/// OnEnable / 结构重建时发生，不是每帧扫场景。
///
/// 注意这里存的是「哪些东西算遮挡物」，不参与任何判定。判定完全在 GPU：
/// pass 把每个遮挡物根节点的深度画进一张图，角色着色器逐像素采样比较。
/// CPU 不读回任何结果。
/// </summary>
public static class SkyPrisonOccluderRegistry
{
    private static readonly List<Renderer> renderers = new List<Renderer>(128);

    public static IReadOnlyList<Renderer> Renderers => renderers;

    public static void Register(Renderer renderer)
    {
        if (renderer == null || renderers.Contains(renderer))
            return;

        renderers.Add(renderer);
    }

    public static void Unregister(Renderer renderer)
    {
        if (renderer == null)
            return;

        renderers.Remove(renderer);
    }

    /// <summary>
    /// 清掉已经被销毁的条目。换场景时装饰物整批销毁，留着空引用会让 pass
    /// 每帧做无谓的判空；但也不能在 pass 里边遍历边删，所以单独提供。
    /// </summary>
    public static void Compact()
    {
        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            if (renderers[i] == null)
                renderers.RemoveAt(i);
        }
    }
}
