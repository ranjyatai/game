using UnityEngine;

/// <summary>
/// 天空囚笼渲染质量预算配置。
/// 建议资产路径：Assets/_Project/Data/Settings/RenderQuality/SkyPrisonRenderQualitySettings.asset
/// </summary>
[CreateAssetMenu(
    fileName = "SkyPrisonRenderQualitySettings",
    menuName = "Sky Prison/Settings/Render Quality Settings",
    order = 1900)]
public class SkyPrisonRenderQualitySettings : ScriptableObject
{
    [Header("Default Tier")]
    public SkyPrisonRenderQualityTier defaultEditorTier = SkyPrisonRenderQualityTier.EditPreview;
    public SkyPrisonRenderQualityTier defaultPlayModeTier = SkyPrisonRenderQualityTier.RuntimePreview;

    [Header("Ground Preview Resolution")]
    public int safeGroundPreviewResolution = 512;
    public int editGroundPreviewResolution = 1024;
    public int runtimeGroundPreviewResolution = 1024;
    public int finalGroundBakeResolution = 2048;

    [Header("Editor Frame Budget")]
    [Tooltip("编辑器单帧超过这个毫秒数，可以认为需要降级。")]
    public float editorWarningFrameMs = 50f;

    [Tooltip("连续超过预算多少帧后，允许自动降级。")]
    public int autoDowngradeAfterFrames = 10;

    [Header("Paint Preview")]
    [Tooltip("编辑刷地时的最高预览提交帧率。")]
    public float paintPreviewMaxFps = 30f;

    [Tooltip("安全档刷地时的最高预览提交帧率。")]
    public float safePaintPreviewMaxFps = 12f;

    [Header("Rules")]
    [Tooltip("Play 模式前是否允许自动正式烘焙。建议开发期关闭，正式构建前再开。")]
    public bool allowAutoFinalBakeBeforePlay = false;

    [Tooltip("编辑器预览阶段是否允许 AssetDatabase.SaveAssets / Refresh。建议关闭。")]
    public bool allowAssetDatabaseSaveDuringPreview = false;

    public int GetGroundResolution(SkyPrisonRenderQualityTier tier)
    {
        switch (tier)
        {
            case SkyPrisonRenderQualityTier.Safe:
                return Mathf.Max(64, safeGroundPreviewResolution);
            case SkyPrisonRenderQualityTier.EditPreview:
                return Mathf.Max(64, editGroundPreviewResolution);
            case SkyPrisonRenderQualityTier.RuntimePreview:
                return Mathf.Max(64, runtimeGroundPreviewResolution);
            case SkyPrisonRenderQualityTier.Final:
                return Mathf.Max(64, finalGroundBakeResolution);
            default:
                return Mathf.Max(64, editGroundPreviewResolution);
        }
    }

    public float GetPaintPreviewMaxFps(SkyPrisonRenderQualityTier tier)
    {
        if (tier == SkyPrisonRenderQualityTier.Safe)
            return Mathf.Max(1f, safePaintPreviewMaxFps);

        if (tier == SkyPrisonRenderQualityTier.Final)
            return Mathf.Max(1f, paintPreviewMaxFps);

        return Mathf.Max(1f, paintPreviewMaxFps);
    }
}
