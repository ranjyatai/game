#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 天空囚笼渲染质量面板。
/// 菜单：Tools / Sky Prison / Render Quality / Open Render Quality Panel
/// </summary>
public class SkyPrisonRenderQualityPanel : EditorWindow
{
    [MenuItem("天空囚笼/设置/渲染质量面板", false, 105)]
    public static void Open()
    {
        GetWindow<SkyPrisonRenderQualityPanel>("渲染质量");
    }

    [MenuItem("天空囚笼/设置/渲染质量预设/Safe", false, 109)]
    public static void SetSafe()
    {
        SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.Safe);
    }

    [MenuItem("天空囚笼/设置/渲染质量预设/Edit Preview", false, 106)]
    public static void SetEditPreview()
    {
        SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.EditPreview);
    }

    [MenuItem("天空囚笼/设置/渲染质量预设/Runtime Preview", false, 107)]
    public static void SetRuntimePreview()
    {
        SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.RuntimePreview);
    }

    [MenuItem("天空囚笼/设置/渲染质量预设/Final", false, 108)]
    public static void SetFinal()
    {
        bool ok = EditorUtility.DisplayDialog(
            "切换到正式发布档",
            "正式发布档允许完整烘焙和重任务。日常编辑不建议长期停留在这一档。",
            "切换",
            "取消");

        if (ok)
            SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.Final);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("天空囚笼渲染质量协议", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        DrawCurrentTier();

        EditorGUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "这不是单纯玩家画质，而是制作阶段协议：\n" +
            "安全档：救场不卡。\n" +
            "编辑预览档：刷地、摆放、编辑默认。\n" +
            "运行预览档：Play 测试默认。\n" +
            "正式发布档：出包、最终烘焙、宣传录制。",
            MessageType.Info);

        EditorGUILayout.Space(8f);

        DrawTierButtons();

        EditorGUILayout.Space(12f);

        bool autoRuntime = SkyPrisonRenderQualityEditorBridge.AutoUseRuntimePreviewOnPlay;
        bool newAutoRuntime = EditorGUILayout.ToggleLeft("进入 Play 时自动切到运行预览档", autoRuntime);
        if (newAutoRuntime != autoRuntime)
            SkyPrisonRenderQualityEditorBridge.AutoUseRuntimePreviewOnPlay = newAutoRuntime;

        EditorGUILayout.Space(12f);

        DrawRulePreview();
    }

    private void DrawCurrentTier()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("当前档位", EditorStyles.miniBoldLabel);

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            normal = { textColor = GetTierColor(SkyPrisonRenderQualityContext.CurrentTier) }
        };

        EditorGUILayout.LabelField(GetTierDisplayName(SkyPrisonRenderQualityContext.CurrentTier), style);
        EditorGUILayout.EndVertical();
    }

    private void DrawTierButtons()
    {
        EditorGUILayout.LabelField("手动切换", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("L0 安全档", GUILayout.Height(30f)))
            SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.Safe);

        if (GUILayout.Button("L1 编辑预览档", GUILayout.Height(30f)))
            SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.EditPreview);

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("L2 运行预览档", GUILayout.Height(30f)))
            SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.RuntimePreview);

        if (GUILayout.Button("L3 正式发布档", GUILayout.Height(30f)))
        {
            bool ok = EditorUtility.DisplayDialog(
                "切换到正式发布档",
                "正式发布档允许完整烘焙和重任务。日常编辑不建议长期停留在这一档。",
                "切换",
                "取消");

            if (ok)
                SkyPrisonRenderQualityEditorBridge.SetEditorTier(SkyPrisonRenderQualityTier.Final);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRulePreview()
    {
        EditorGUILayout.LabelField("当前规则", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginVertical("box");
        RuleRow("允许正式运行烘焙", SkyPrisonRenderQualityContext.AllowRuntimeBake);
        RuleRow("允许 Play 前自动烘焙", SkyPrisonRenderQualityContext.AllowPlayModeAutoBake);
        RuleRow("允许预览阶段 AssetDatabase", SkyPrisonRenderQualityContext.AllowAssetDatabaseDuringPreview);
        RuleRow("允许昂贵编辑器刷新", SkyPrisonRenderQualityContext.AllowExpensiveEditorRefresh);
        RuleRow("允许草浪", SkyPrisonRenderQualityContext.AllowGrassWave);
        RuleRow("允许高质量后处理", SkyPrisonRenderQualityContext.AllowHighQualityPostProcess);
        RuleRow("允许阻塞长任务", SkyPrisonRenderQualityContext.AllowBlockingLongTask);
        EditorGUILayout.EndVertical();
    }

    private void RuleRow(string label, bool enabled)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label);
        GUILayout.FlexibleSpace();
        GUILayout.Label(enabled ? "允许" : "禁止", enabled ? EditorStyles.boldLabel : EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private string GetTierDisplayName(SkyPrisonRenderQualityTier tier)
    {
        switch (tier)
        {
            case SkyPrisonRenderQualityTier.Safe:
                return "L0 安全档";
            case SkyPrisonRenderQualityTier.EditPreview:
                return "L1 编辑预览档";
            case SkyPrisonRenderQualityTier.RuntimePreview:
                return "L2 运行预览档";
            case SkyPrisonRenderQualityTier.Final:
                return "L3 正式发布档";
            default:
                return tier.ToString();
        }
    }

    private Color GetTierColor(SkyPrisonRenderQualityTier tier)
    {
        switch (tier)
        {
            case SkyPrisonRenderQualityTier.Safe:
                return new Color(0.80f, 0.95f, 1f, 1f);
            case SkyPrisonRenderQualityTier.EditPreview:
                return new Color(0.60f, 1f, 0.70f, 1f);
            case SkyPrisonRenderQualityTier.RuntimePreview:
                return new Color(1f, 0.85f, 0.45f, 1f);
            case SkyPrisonRenderQualityTier.Final:
                return new Color(1f, 0.55f, 0.45f, 1f);
            default:
                return Color.white;
        }
    }
}
#endif
