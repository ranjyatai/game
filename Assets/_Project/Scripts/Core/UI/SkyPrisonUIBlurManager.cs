using UnityEngine;

/// <summary>
/// 仅持有模糊材质引用。模糊捕获由 SkyPrisonInventoryBlurBackground 在背包打开时独立完成。
/// </summary>
public static class SkyPrisonUIBlurManager
{
    // 模糊材质由 RendererFeature 在 Create() 时注册，供背包模糊捕获使用
    public static Material BlurMaterial { get; set; }

    public static void Release() { }
}
