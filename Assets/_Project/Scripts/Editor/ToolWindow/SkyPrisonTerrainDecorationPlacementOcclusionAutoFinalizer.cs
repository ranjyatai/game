using UnityEditor;
using UnityEngine;

/// <summary>
/// Deprecated compatibility shell.
///
/// 旧版这里会通过 InitializeOnLoad + hierarchyChanged + delayCall 自动扫描全场景，
/// 并偷偷终局矫正 FrontOccluderProxy。该行为已禁用。
///
/// 新规则：地形装饰物结构只能由明确入口生成：
/// - 新放置：SkyPrisonTerrainDecorationInstanceBuilder
/// - 重新应用当前实例：放置窗口按钮明确触发
/// - 旧数据迁移：Migration 工具明确触发
/// </summary>
public static class SkyPrisonTerrainDecorationPlacementOcclusionAutoFinalizer
{
    private const string LogPrefix = "[TD_OCCLUSION_AUTO_FINALIZER_DISABLED]";

    [MenuItem("Tools/Sky Prison/Diagnostics/Terrain Decoration/说明：自动遮挡终局器已禁用")]
    private static void ShowDisabledNotice()
    {
        EditorUtility.DisplayDialog(
            "自动遮挡终局器已禁用",
            "旧版 OcclusionAutoFinalizer 的 InitializeOnLoad / hierarchyChanged / delayCall 自动修复已经关闭。\n\n" +
            "新实例必须在放置瞬间由 SkyPrisonTerrainDecorationInstanceBuilder 按定义生成正确结构。\n" +
            "旧实例请使用 Migration 工具，不要再靠后台自动矫正。",
            "知道了");
        Debug.Log(LogPrefix + " 自动修复入口保持禁用。不会扫描场景，不会修改任何实例。");
    }
}
