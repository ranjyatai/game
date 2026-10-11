using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器里清理「跨 Play 泄漏」的运行时单例。
///
/// 项目里很多运行时单例(系统音效播放器、字幕、自动保存提示、震屏、手柄震动、输入设备检测……)创建时带
/// HideFlags.HideAndDontSave——带这个标记的物体退出 Play 时编辑器不会销毁，下次进 Play 它们又继续跑 Update、
/// 继续渲染；而各自「清掉多余实例」的代码用的 FindObjectsByType 又找不到隐藏物体，所以每进一次 Play 就多一份
/// (10-12 实测各自累计到 66~67 份：音效被重复播、自动保存提示叠字、字幕孤儿一直挂在屏幕上)。
/// 打包后的游戏不受影响(每次启动都是新进程)，只是编辑器里的问题。
///
/// 做法：回到编辑模式时、以及进入 Play 之前，各清一次——只清「不在场景里、带 DontSave 标记、挂着项目自己脚本」的根物体；
/// 编辑器自己的隐藏物体(场景相机、场景灯光)只挂 Unity 内置组件，不会被误删。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonPlayModeLeakCleaner
{
    static SkyPrisonPlayModeLeakCleaner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingEditMode)
            Clean(false);
    }

    [MenuItem("天空囚笼/调试/清理跨 Play 泄漏的运行时物体", false, 9100)]
    static void CleanFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[LeakCleaner] Play 中不清理(会删掉正在用的单例)，退出 Play 再点。"); return; }
        Clean(true);
    }

    static void Clean(bool verbose)
    {
        int removed = 0;
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || EditorUtility.IsPersistent(go)) continue;                 // 资产 / 预制体
            if (go.transform.parent != null) continue;                                   // 只看根物体，子物体随根一起删
            if ((go.hideFlags & HideFlags.DontSave) == 0) continue;                      // 不带「不保存」标记的是场景里正常的东西
            if (go.scene.IsValid() && go.scene.isLoaded && go.scene.name != "DontDestroyOnLoad") continue;
            if (!HasProjectScript(go)) continue;                                         // 编辑器自己的隐藏物体不动

            counts.TryGetValue(go.name, out int c); counts[go.name] = c + 1;
            Object.DestroyImmediate(go);
            removed++;
        }
        if (removed > 0 || verbose)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in counts) sb.Append(kv.Key).Append('×').Append(kv.Value).Append("  ");
            Debug.Log("[LeakCleaner] 清理跨 Play 残留的运行时物体 " + removed + " 个：" + sb);
        }
    }

    static bool HasProjectScript(GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string asm = mb.GetType().Assembly.GetName().Name;
            if (asm.StartsWith("Assembly-CSharp")) return true;
        }
        return false;
    }
}
