#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器侧渲染质量桥接。
/// 只做极轻量状态切换，不做烘焙、不扫描场景、不 SaveAssets。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonRenderQualityEditorBridge
{
    private const string EditorTierPrefsKey = "SkyPrison.RenderQuality.EditorTier";
    private const string AutoUseRuntimePreviewOnPlayPrefsKey = "SkyPrison.RenderQuality.AutoUseRuntimePreviewOnPlay";

    static SkyPrisonRenderQualityEditorBridge()
    {
        SkyPrisonRenderQualityTier tier = (SkyPrisonRenderQualityTier)EditorPrefs.GetInt(
            EditorTierPrefsKey,
            (int)SkyPrisonRenderQualityTier.EditPreview);

        SkyPrisonRenderQualityContext.SetTier(tier);

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static bool AutoUseRuntimePreviewOnPlay
    {
        get => EditorPrefs.GetBool(AutoUseRuntimePreviewOnPlayPrefsKey, true);
        set => EditorPrefs.SetBool(AutoUseRuntimePreviewOnPlayPrefsKey, value);
    }

    public static void SetEditorTier(SkyPrisonRenderQualityTier tier)
    {
        SkyPrisonRenderQualityContext.SetTier(tier);
        EditorPrefs.SetInt(EditorTierPrefsKey, (int)tier);
        SceneView.RepaintAll();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // 注意：这里只切档，不允许做任何扫描、烘焙、保存。
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            if (AutoUseRuntimePreviewOnPlay && SkyPrisonRenderQualityContext.CurrentTier != SkyPrisonRenderQualityTier.Final)
                SkyPrisonRenderQualityContext.SetTier(SkyPrisonRenderQualityTier.RuntimePreview);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SkyPrisonRenderQualityTier tier = (SkyPrisonRenderQualityTier)EditorPrefs.GetInt(
                EditorTierPrefsKey,
                (int)SkyPrisonRenderQualityTier.EditPreview);

            SkyPrisonRenderQualityContext.SetTier(tier);
        }
    }
}
#endif
