#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// AudioListener 泄漏探针——独立于 SkyPrisonRuntimeAudioBootstrap 的观测。
///
/// 现状：Unity 报「场景里有 338 个 audio listener」，而 bootstrap 自带的
/// [ListenerHunt] 诊断只记录到 9 个（7 个在 AudioListenerRoot、2 个在 Main Camera）。
/// 差了 329 个——说明那个诊断的观测点选错了：它挂在 bootstrap 的 Update 里，
/// bootstrap 一旦停止运行或看不到某些对象，诊断就跟着失明，而泄漏照常继续。
///
/// 用一个失灵的诊断去查问题，比没有诊断更糟——它会让人以为「只有 9 个」。
///
/// 这个探针的取证方式：
///   - 独立 GameObject，不依赖任何现有系统是否存活
///   - FindObjectsOfType(includeInactive: true)，禁用的也要数
///   - 只在数量变化时输出，并打印新增的完整层级路径
///   - 同一个 GameObject 上挂了多个 listener 时单独指出——那是 AddComponent
///     守卫失效的直接证据（AudioListenerRoot 上有 7 个就是这种情况）
///
/// 不留任何序列化字段，只在 Development Build / 编辑器下编译。
/// </summary>
public sealed class SkyPrisonAudioListenerLeakProbe : MonoBehaviour
{
    private const float CheckInterval = 0.5f;
    private const int MaxPathsPerReport = 6;

    private readonly HashSet<int> known = new HashSet<int>();
    private float nextCheck;
    private int lastCount = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("~AudioListenerLeakProbe");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<SkyPrisonAudioListenerLeakProbe>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck)
            return;
        nextCheck = Time.unscaledTime + CheckInterval;

        AudioListener[] all = FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (all.Length == lastCount)
            return;

        int previous = lastCount;
        lastCount = all.Length;

        // 首次只记录基线，不报警——开局本来就会有 1 个。
        if (previous < 0)
        {
            foreach (AudioListener l in all)
            {
                if (l != null)
                    known.Add(l.GetInstanceID());
            }
            Debug.Log($"[ListenerProbe] 基线：{all.Length} 个 AudioListener。");
            return;
        }

        var newPaths = new List<string>();
        foreach (AudioListener l in all)
        {
            if (l == null || !known.Add(l.GetInstanceID()))
                continue;
            newPaths.Add(Describe(l));
        }

        // 同一个对象上挂多个 listener：AddComponent 的守卫失效了。
        var perObject = new Dictionary<int, int>();
        foreach (AudioListener l in all)
        {
            if (l == null)
                continue;
            int id = l.gameObject.GetInstanceID();
            perObject.TryGetValue(id, out int n);
            perObject[id] = n + 1;
        }

        var duplicated = new List<string>();
        foreach (AudioListener l in all)
        {
            if (l == null)
                continue;
            int id = l.gameObject.GetInstanceID();
            if (perObject.TryGetValue(id, out int n) && n > 1)
            {
                string path = Path(l.transform) + $"（同一对象上 {n} 个）";
                if (!duplicated.Contains(path))
                    duplicated.Add(path);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[ListenerProbe] 数量 {previous} -> {all.Length}（正常应为 1）");

        if (newPaths.Count > 0)
        {
            sb.AppendLine($"  本次新增 {newPaths.Count} 个：");
            for (int i = 0; i < newPaths.Count && i < MaxPathsPerReport; i++)
                sb.AppendLine($"    {newPaths[i]}");
            if (newPaths.Count > MaxPathsPerReport)
                sb.AppendLine($"    …… 其余 {newPaths.Count - MaxPathsPerReport} 个");
        }

        if (duplicated.Count > 0)
        {
            sb.AppendLine("  同一对象上挂了多个（AddComponent 守卫失效）：");
            foreach (string d in duplicated)
                sb.AppendLine($"    {d}");
        }

        Debug.LogWarning(sb.ToString());
    }

    private static string Describe(AudioListener l)
    {
        string path = Path(l.transform);
        var tags = new List<string>();

        if (l.gameObject.scene.name == "DontDestroyOnLoad" || !l.gameObject.scene.IsValid())
            tags.Add("DontDestroyOnLoad");
        if (l.GetComponent<SkyPrisonPlayerAudioListenerAnchor>() != null)
            tags.Add("带锚点组件");
        if (!l.enabled)
            tags.Add("已禁用");
        if (!l.gameObject.activeInHierarchy)
            tags.Add("对象未激活");

        return tags.Count > 0 ? $"{path}  [{string.Join(" / ", tags)}]" : path;
    }

    private static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        int guard = 0;
        while (t.parent != null && guard++ < 10)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }
        return sb.ToString();
    }
}
#endif
