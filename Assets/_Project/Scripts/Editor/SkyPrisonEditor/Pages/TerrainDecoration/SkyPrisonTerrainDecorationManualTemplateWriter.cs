using UnityEditor;
using UnityEngine;

/// <summary>
/// Deprecated compatibility shell.
/// 旧版手动代理写回模板已禁用，避免新主线再次绕回 PF_TD RuntimeTemplate。
/// </summary>
public static class SkyPrisonTerrainDecorationManualTemplateWriter
{
    [MenuItem("Tools/Sky Prison/Diagnostics/Terrain Decoration/说明：手动模板写回已废弃")]
    public static void WriteSelectedInstanceToTemplateMenu()
    {
        EditorUtility.DisplayDialog(
            "手动模板写回已废弃",
            "新地形装饰物主线不再把当前实例写回 PF_TD 模板。\n\n" +
            "结构应在放置瞬间由 Builder 按定义生成；旧数据请走 Migration。",
            "知道了");
    }

    public static bool WriteInstanceToTemplate(GameObject selectedOrRoot, bool showDialog = false, bool pingTemplate = false)
    {
        if (showDialog)
            WriteSelectedInstanceToTemplateMenu();
        else
            Debug.LogWarning("[TD_MANUAL_TEMPLATE_WRITER_DEPRECATED] 手动模板写回已禁用。", selectedOrRoot);
        return false;
    }
}
