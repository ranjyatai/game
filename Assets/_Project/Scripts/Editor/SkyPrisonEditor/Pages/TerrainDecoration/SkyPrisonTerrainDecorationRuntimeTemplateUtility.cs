using UnityEditor;
using UnityEngine;

/// <summary>
/// Deprecated compatibility shell.
///
/// 新地形装饰物放置主线不再通过 PF_TD RuntimeTemplate。
/// 该类只保留旧代码编译兼容，不再创建、写回、重连模板。
/// </summary>
public static class SkyPrisonTerrainDecorationRuntimeTemplateUtility
{
    public const string DefaultRuntimePrefabFolder = "Assets/_Project/Prefabs/TerrainDecorations/Custom";

    [MenuItem("Tools/Sky Prison/Diagnostics/Terrain Decoration/说明：RuntimeTemplateUtility 已废弃")]
    public static void WriteSelectedInstanceManualRootsToTemplateMenu()
    {
        ShowDeprecatedNotice();
    }

    [MenuItem("Tools/Sky Prison/Diagnostics/Terrain Decoration/说明：RuntimeTemplateUtility 已废弃/重新连接")]
    public static void RelinkSelectedInstanceToTemplateMenu()
    {
        ShowDeprecatedNotice();
    }

    [MenuItem("Tools/Sky Prison/Diagnostics/Terrain Decoration/说明：RuntimeTemplateUtility 已废弃/写回并连接")]
    public static void WriteBackAndRelinkSelectedInstanceMenu()
    {
        ShowDeprecatedNotice();
    }

    public static GameObject InstantiateConnectedRuntimeTemplate(
        TerrainDecorationDefinition definition,
        Transform parent,
        string selectedVariantId)
    {
        Debug.LogWarning("[TD_RUNTIME_TEMPLATE_DEPRECATED] 新放置主线禁止从 PF_TD RuntimeTemplate 实例化。请使用 SkyPrisonTerrainDecorationInstanceBuilder。", definition);
        return null;
    }

    public static GameObject GetOrCreateRuntimeTemplatePrefab(
        TerrainDecorationDefinition definition,
        string preferredVariantId = null)
    {
        Debug.LogWarning("[TD_RUNTIME_TEMPLATE_DEPRECATED] 禁止创建 PF_TD RuntimeTemplate。定义页不再生成运行时模板。", definition);
        return null;
    }

    public static GameObject FindRuntimeTemplatePrefab(TerrainDecorationDefinition definition)
    {
        return null;
    }

    public static bool WriteInstanceManualRootsToTemplate(GameObject instanceRoot, bool showDialog)
    {
        if (showDialog)
            ShowDeprecatedNotice();
        else
            Debug.LogWarning("[TD_RUNTIME_TEMPLATE_DEPRECATED] 模板写回已禁用。", instanceRoot);
        return false;
    }

    public static GameObject RelinkInstanceToRuntimeTemplate(GameObject oldRoot, bool showDialog)
    {
        if (showDialog)
            ShowDeprecatedNotice();
        else
            Debug.LogWarning("[TD_RUNTIME_TEMPLATE_DEPRECATED] 模板重连已禁用。", oldRoot);
        return null;
    }

    public static GameObject ResolveRuntimeRoot(GameObject obj)
    {
        if (obj == null)
            return null;

        Transform t = obj.transform;
        while (t != null)
        {
            if (t.GetComponent<TerrainDecorationRuntimeBinder>() != null || t.GetComponent<TerrainDecorationRuntimeApplier>() != null)
                return t.gameObject;
            t = t.parent;
        }
        return obj;
    }

    private static void ShowDeprecatedNotice()
    {
        EditorUtility.DisplayDialog(
            "RuntimeTemplateUtility 已废弃",
            "新地形装饰物主线不再通过 PF_TD RuntimeTemplate。\n\n" +
            "新放置实例必须由 SkyPrisonTerrainDecorationInstanceBuilder 按定义直接生成 Scene 实例结构。\n" +
            "旧实例迁移请使用 Migration 工具，不要再写回模板。",
            "知道了");
    }
}
