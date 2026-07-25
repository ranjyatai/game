#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Deprecated compatibility shell.
/// 旧版 FinalCleaner 会直接改写源码文件，已经禁止常驻执行。
/// </summary>
public static class SkyPrisonEditorRepaintLoopFinalCleaner
{
    [MenuItem("Tools/Sky Prison/Diagnostics/Repaint/说明：FinalCleaner 已禁用")]
    private static void ShowDisabledNotice()
    {
        EditorUtility.DisplayDialog(
            "FinalCleaner 已禁用",
            "旧版 RepaintLoopFinalCleaner 会直接读写源码文件。该行为已禁用。\n\n" +
            "如需排查刷新循环，请写只读 Scanner，不要再用源码改写器。",
            "知道了");
        Debug.Log("[SkyPrisonEditorRepaintLoopFinalCleaner] Disabled compatibility shell. No files were modified.");
    }
}
#endif
