using System;
using UnityEngine;

/// <summary>
/// 全项目统一渲染质量上下文。
/// 所有系统都应该问这里，而不是各自散落一堆局部开关。
/// </summary>
public static class SkyPrisonRenderQualityContext
{
    public static event Action<SkyPrisonRenderQualityTier> TierChanged;

    private static SkyPrisonRenderQualityTier currentTier = SkyPrisonRenderQualityTier.EditPreview;

    public static SkyPrisonRenderQualityTier CurrentTier
    {
        get => currentTier;
        set => SetTier(value);
    }

    public static bool IsSafe => currentTier == SkyPrisonRenderQualityTier.Safe;
    public static bool IsEditPreview => currentTier == SkyPrisonRenderQualityTier.EditPreview;
    public static bool IsRuntimePreview => currentTier == SkyPrisonRenderQualityTier.RuntimePreview;
    public static bool IsFinal => currentTier == SkyPrisonRenderQualityTier.Final;

    /// <summary>
    /// 是否允许正式运行贴图烘焙。
    /// 原则：只有 Final 档允许。开发期 Play 不允许。
    /// </summary>
    public static bool AllowRuntimeBake => IsFinal;

    /// <summary>
    /// 是否允许在 Play 前自动正式烘焙。
    /// 原则：默认不允许，正式构建流程另行处理。
    /// </summary>
    public static bool AllowPlayModeAutoBake => IsFinal;

    /// <summary>
    /// 是否允许预览阶段调用 AssetDatabase.SaveAssets / Refresh / Import。
    /// 原则：只有 Final 档允许。
    /// </summary>
    public static bool AllowAssetDatabaseDuringPreview => IsFinal;

    /// <summary>
    /// 是否允许昂贵的编辑器全图刷新。
    /// </summary>
    public static bool AllowExpensiveEditorRefresh => IsFinal;

    /// <summary>
    /// 是否允许编辑器绘制时做局部贴图预览提交。
    /// </summary>
    public static bool AllowGroundPaintLivePreview => currentTier != SkyPrisonRenderQualityTier.Safe || Application.isPlaying == false;

    /// <summary>
    /// 是否允许高质量草浪 / 风场。
    /// </summary>
    public static bool AllowGrassWave => currentTier >= SkyPrisonRenderQualityTier.RuntimePreview;

    /// <summary>
    /// 是否允许高质量后处理。
    /// </summary>
    public static bool AllowHighQualityPostProcess => IsFinal;

    /// <summary>
    /// 是否允许高质量动态阴影。
    /// </summary>
    public static bool AllowHighQualityShadow => currentTier >= SkyPrisonRenderQualityTier.RuntimePreview;

    /// <summary>
    /// 是否允许低频/简化遮挡刷新。Safe 档也要保留基础遮挡判断，但不做昂贵视觉效果。
    /// </summary>
    public static bool AllowOcclusionLogic => true;

    /// <summary>
    /// 当前档位是否允许阻塞式同步长任务。
    /// </summary>
    public static bool AllowBlockingLongTask => IsFinal;

    public static void SetTier(SkyPrisonRenderQualityTier tier)
    {
        if (currentTier == tier)
            return;

        currentTier = tier;
        TierChanged?.Invoke(currentTier);
    }

    public static int GetFallbackGroundPreviewResolution()
    {
        switch (currentTier)
        {
            case SkyPrisonRenderQualityTier.Safe:
                return 512;
            case SkyPrisonRenderQualityTier.EditPreview:
                return 1024;
            case SkyPrisonRenderQualityTier.RuntimePreview:
                return 1024;
            case SkyPrisonRenderQualityTier.Final:
                return 2048;
            default:
                return 1024;
        }
    }

    public static float GetGroundPaintPreviewIntervalSeconds()
    {
        switch (currentTier)
        {
            case SkyPrisonRenderQualityTier.Safe:
                return 1f / 12f;
            case SkyPrisonRenderQualityTier.EditPreview:
                return 1f / 30f;
            case SkyPrisonRenderQualityTier.RuntimePreview:
                return 1f / 30f;
            case SkyPrisonRenderQualityTier.Final:
                return 1f / 30f;
            default:
                return 1f / 30f;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RuntimeDefault()
    {
        if (currentTier != SkyPrisonRenderQualityTier.Final)
            currentTier = SkyPrisonRenderQualityTier.RuntimePreview;
    }
}
