using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 自动给场景里所有 TMP_Text 挂上 {A|B} 注音支持，不用每个显示名字/介绍的地方
/// 手动接一次。两部分都是运行时属性/组件，不会存进 Prefab，所以纯靠场景加载时
/// 扫一遍还不够——弹窗、提示框这类运行时动态生成的 TMP_Text 会漏掉，额外加一个
/// 低频轮询兜底新出现的实例；轮询同时也是"自愈"的机会——如果某个文本框后来又被
/// 别的系统重新赋值了 textPreprocessor（把我们包的这层顶掉），下一轮扫描会发现
/// 当前挂的已经不是我们的包装类，重新链式包一层上去，不需要判断"是不是我们先手
/// 挂的"这种脆弱的时序假设。
/// </summary>
public static class SkyPrisonRubyTextInstaller
{
    private const float RescanIntervalSeconds = 1f;

    private static GameObject _runnerGo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded += (_, __) => AttachToAll();
        AttachToAll();

        if (_runnerGo == null)
        {
            _runnerGo = new GameObject("[SkyPrisonRubyTextInstaller]");
            Object.DontDestroyOnLoad(_runnerGo);
            _runnerGo.hideFlags = HideFlags.HideAndDontSave;
            _runnerGo.AddComponent<Runner>();
        }
    }

    public static void AttachToAll()
    {
        foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            AttachTo(t);
    }

    public static void AttachTo(TMP_Text t)
    {
        if (t == null)
            return;

        SkyPrisonRubyTextPreprocessor preprocessor = t.textPreprocessor as SkyPrisonRubyTextPreprocessor;
        if (preprocessor == null)
        {
            preprocessor = new SkyPrisonRubyTextPreprocessor(t, t.textPreprocessor);
            t.textPreprocessor = preprocessor;

            // 2026-08-19：TMP 的 text setter 有"内容跟上次一样就跳过重新生成网格"的
            // 优化——如果这个文本框在挂上预处理器之前，已经被赋值过一次带 {A|B}
            // 标记的原始字符串（这一帧或更早），网格已经生成完、显示的是没处理过的
            // 原始文字。之后哪怕重新走一遍同样的赋值（比如再触发一次交互/对话），
            // 内容跟上次逐字节相同，TMP 判断"没变"直接跳过，连预处理器都不会再调用
            // 一次，表现为"这个文本框好像预处理器根本没接上"——其实接上了，只是卡在
            // 这层"内容没变不重新生成"的优化里出不来。这里强制重新解析一次，
            // 不管内容是否相同都真正跑一遍预处理器。
            if (t.gameObject.activeInHierarchy)
                t.ForceMeshUpdate(true, true);
        }

        // 真正定位注音要靠 characterInfo 里的实际渲染坐标，目前只支持
        // TextMeshProUGUI（Canvas UI）——项目里名字/介绍这类文本都是这个类型。
        if (t is TextMeshProUGUI tmpugui)
        {
            SkyPrisonRubyOverlayController overlay = t.GetComponent<SkyPrisonRubyOverlayController>();
            if (overlay == null)
                overlay = t.gameObject.AddComponent<SkyPrisonRubyOverlayController>();
            overlay.Bind(preprocessor);

            // 描边保底只给单位头顶 HUD 名字用——那里字号缩得小，材质自己的描边会跟着
            // 细到看不清；对话框这类字号本来就不小的地方，不强制加粗，用材质本来的
            // 描边就够，强行统一加粗反而变成了不该有的效果。
            UnitOverheadUIView overheadView = t.GetComponentInParent<UnitOverheadUIView>(true);
            overlay.SetOutlineCorrection(overheadView != null && overheadView.nameText == tmpugui ? 0.35f : 0f);
        }
    }

    private sealed class Runner : MonoBehaviour
    {
        private void OnEnable()
        {
            InvokeRepeating(nameof(Rescan), RescanIntervalSeconds, RescanIntervalSeconds);
        }

        private void Rescan() => AttachToAll();
    }
}
