using UnityEngine;

/// <summary>
/// 按 F9 手动触发一次致命报错弹窗，纯粹用来测试 SkyPrisonErrorReporter 的 UI 效果，
/// 不会真的写坏任何数据。只在编辑器/开发版里编译，正式发布版（非 Development Build）
/// 里这段代码根本不存在，不会被玩家意外按到。
/// </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
public static class SkyPrisonErrorReporterDebugHotkey
{
    private static bool _installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (_installed) return;
        _installed = true;

        var go = new GameObject("[ErrorReporterDebugHotkey]") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Ticker>();
    }

    private class Ticker : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                SkyPrisonErrorReporter.ReportFatal(
                    SkyPrisonErrorCodes.SceneNotInBuildSettings,
                    "这是 F9 手动触发的测试报错，不代表真的出了问题——纯粹用来检查报错弹窗长什么样。");
            }
        }
    }
}
#endif
