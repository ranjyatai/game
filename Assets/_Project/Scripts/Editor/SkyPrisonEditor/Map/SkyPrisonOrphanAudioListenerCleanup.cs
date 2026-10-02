using UnityEditor;
using UnityEngine;

/// <summary>
/// 清掉 SkyPrisonRuntimeAudioBootstrap 泄漏出来的孤儿 fallback AudioListener。
///
/// 泄漏机制（已在 bootstrap 里修掉源头）：fallback listener 曾被设为
/// HideFlags.DontSave，而 FindObjectsOfType 不返回带 DontSave 的对象——
/// 创建守卫看不见自己上一帧创建的那个，于是每帧再建一个。DontSave 又让它们在
/// 退出 Play 时不被销毁，跨会话累积。实测残留 1125 个「无场景」的孤儿。
///
/// 源头修了也不会让已有的这些消失（正因为 DontSave 才留下来），需要手动清一次。
/// 清完 Unity 那条「There are N audio listeners」警告应该回到正常。
///
/// 只删名字完全匹配、且没有有效场景（＝孤儿）的那些；DontDestroyOnLoad 里和真实
/// 场景里的一概不碰，避免误删正在用的 listener。
/// </summary>
public static class SkyPrisonOrphanAudioListenerCleanup
{
    private const string LogPrefix = "[SkyPrison 孤儿 Listener 清理]";
    private const string FallbackName = "SkyPrison_RuntimeFallbackAudioListener";

    [MenuItem("天空囚笼/校验与清理/清理泄漏的孤儿 AudioListener", false, 216)]
    public static void Run()
    {
        var all = Resources.FindObjectsOfTypeAll<AudioListener>();
        int destroyed = 0;
        int kept = 0;

        foreach (AudioListener listener in all)
        {
            if (listener == null)
                continue;

            GameObject go = listener.gameObject;

            // 只清孤儿：名字匹配 + 没有有效场景。
            if (go.name != FallbackName || go.scene.IsValid())
            {
                kept++;
                continue;
            }

            Object.DestroyImmediate(go);
            destroyed++;
        }

        Debug.Log($"{LogPrefix} 已销毁 {destroyed} 个孤儿 fallback listener，保留 {kept} 个（在场景中或非 fallback）。");
    }
}
