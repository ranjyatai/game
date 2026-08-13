using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 关掉场景里所有前景遮挡触发器的 debugLogs。
///
/// 背景：为了查「草为什么不遮挡」，我给 18 个杂草的触发器打开了 debugLogs，
/// 用完忘了关，而且场景保存过、打进了 Build。逐帧 Debug.Log + 字符串插值
/// × 20 个实例，直接对应性能报告里的 1749 次 GC 和
/// ScriptRunBehaviourLateUpdate=40.4ms。
///
/// 打开它的那个临时脚本（SkyPrisonOccluderDebugToggle）是 [InitializeOnLoad]，
/// 每次脚本重载都会重新打开，所以已经连脚本一起删掉了。这里负责把已经写进
/// 场景的值清掉。
///
/// 这个类本身是幂等的、开销极小（只在真的为 true 时才改），留着当保险：
/// 以后再有人为了排查打开忘了关，下次重载会被清掉。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOccluderDebugLogsOff
{
    private const string LogPrefix = "[SkyPrison OccluderDebug]";

    static SkyPrisonOccluderDebugLogsOff()
    {
        EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/关闭所有遮挡调试日志")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        var triggers = Object.FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int turnedOff = 0;
        foreach (var t in triggers)
        {
            if (t == null || !t.debugLogs)
                continue;

            Undo.RecordObject(t, "Disable Occluder Debug Logs");
            t.debugLogs = false;
            EditorUtility.SetDirty(t);
            turnedOff++;
        }

        if (turnedOff == 0)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));

        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"{LogPrefix} 关掉了 {turnedOff} 个遮挡触发器的 debugLogs（共 {triggers.Length} 个）。" +
            $"场景保存：{(saved ? "成功" : "失败或被取消")}。");
    }
}
