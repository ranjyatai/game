using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DialogueSubtitleHUD 根物体用 HideFlags.HideAndDontSave（不能改——改成不含 DontSave
/// 的 flag 会连带打断"离开NPC自动淡出"那条逻辑，具体机制未查清，改回原样后淡出恢复
/// 正常）。副作用是这类物体不受 FindObjectsByType 影响，也不受 Play 模式退出的正常
/// 清理管理——一旦脚本重编译/Editor 崩溃/异常退出发生在 Play 期间，旧实例就会永久
/// 留在编辑器场景里，且没人能通过 Hierarchy 或去重逻辑找到它，表现为"游戏里台词怎么
/// 都不消失"（用户看到的其实是某个陈年孤儿，不是当前真正在跑的实例）。
///
/// Resources.FindObjectsOfTypeAll 不受 HideAndDontSave 影响，能扫到这些孤儿——编辑器
/// 一重编译、以及每次退出 Play，都清一遍。当前编辑器场景里此刻不应该存在任何
/// [DialogueSubtitleHUD]，这个物体只应该在 Play 期间由 AutoCreate() 现造。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonDialogueSubtitleOrphanCleanup
{
    static SkyPrisonDialogueSubtitleOrphanCleanup()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            Cleanup();

        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                Cleanup();
        };
    }

    private static void Cleanup()
    {
        var orphans = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.name == "[DialogueSubtitleHUD]" && go.scene.IsValid())
            .ToList();

        foreach (var go in orphans)
            Object.DestroyImmediate(go);

        if (orphans.Count > 0)
            Debug.Log($"[对话字幕孤儿清理] 清掉了 {orphans.Count} 个泄漏进编辑模式的 [DialogueSubtitleHUD] 残留物体。");
    }
}
