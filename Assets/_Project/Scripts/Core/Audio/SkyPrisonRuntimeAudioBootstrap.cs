using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// V1 - 2026-05-29
/// Runtime audio bootstrap / build diagnosis for Sky Prison.
///
/// Purpose:
/// - Ensure Build has at least one enabled AudioListener after the scene is loaded.
/// - Warm up SkyPrisonRuntimeAudioCatalog so Resources references are touched at runtime.
/// - Optionally play a 2D diagnostic audio clip from the runtime catalog in Development Build.
///
/// This does not change footstep logic. It only closes the runtime audio environment side.
/// </summary>
public sealed class SkyPrisonRuntimeAudioBootstrap : MonoBehaviour
{
    private const string BootstrapObjectName = "SkyPrison_RuntimeAudioBootstrap";
    private const string FallbackListenerObjectName = "SkyPrison_RuntimeFallbackAudioListener";
    private const string ProbeSourceObjectName = "SkyPrison_RuntimeAudioProbe2D";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        InstallListenerWarningFilter();

        if (FindObjectOfType<SkyPrisonRuntimeAudioBootstrap>() != null)
            return;

        GameObject go = new GameObject(BootstrapObjectName);
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<SkyPrisonRuntimeAudioBootstrap>();
    }

    // "There are N audio listeners in the scene..."是Unity引擎自己在音频系统内部打的
    // 警告，不是我们代码里的Debug.Log——没法靠"降低检测频率/少调用几次"让它变少，
    // 因为它是引擎每帧自己判断自己打的。用户明确说了不是频率问题，就是不想看到它，
    // 那唯一办法就是在日志层拦截：包一层 ILogHandler，凡是内容匹配这条警告的直接吃掉
    // 不转发给默认输出，其它日志/警告原样放行，不会连累别的诊断信息一起消失。
    /// <summary>
    /// 找出 FindObjectsByType 看不见的那些 AudioListener。
    ///
    /// 现状矛盾：Unity 引擎报「场景里有 1126 个」且每帧 +1，而本类的 [ListenerHunt]
    /// 和独立的 SkyPrisonAudioListenerLeakProbe 两套扫描都只数到 1 个
    /// （AudioListenerRoot 上那个）。两套 FindObjectsByType 一致看不见，说明泄漏出来的
    /// 那些对它不可见——典型情况是挂在 HideFlags.HideAndDontSave 的对象上，
    /// 或者存在于预览场景（prefab 缩略图 / PreviewRenderUtility）里。
    /// Resources.FindObjectsOfTypeAll 两者都能看到。
    ///
    /// 放在 bootstrap 而不是探针里：探针靠 RuntimeInitializeOnLoadMethod 创建，
    /// Play 期间一旦触发重编译，Domain Reload 会销毁它且不再重建，观测就此失明——
    /// 这正是「只数到 1 个」这个假象的来源，探针自己的注释还警告过这件事。
    /// bootstrap 是场景里的真实对象，每帧都在跑，不会这样失明。
    ///
    /// 定性用，每 120 次调用报一次，且只在数量变化时输出。
    /// </summary>
    private static int _invisibleReportTick;
    private static int _lastInvisibleTotal = -1;

    private static void ReportInvisibleListeners(int visibleCount)
    {
        if (++_invisibleReportTick % 120 != 0)
            return;

        var all = Resources.FindObjectsOfTypeAll<AudioListener>();
        if (all.Length == _lastInvisibleTotal)
            return;
        _lastInvisibleTotal = all.Length;

        var byScene = new System.Collections.Generic.Dictionary<string, int>();
        var sampleHidden = new System.Collections.Generic.List<string>();

        foreach (AudioListener l in all)
        {
            if (l == null) continue;

            GameObject go = l.gameObject;
            string key = go.scene.IsValid()
                ? $"场景:{go.scene.name}"
                : "(无场景／资产或预览场景)";
            key += $" hideFlags={go.hideFlags}";

            byScene.TryGetValue(key, out int n);
            byScene[key] = n + 1;

            if (go.hideFlags != HideFlags.None && sampleHidden.Count < 5)
                sampleHidden.Add(GetHierarchyPath(go.transform));
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"[ListenerAll] FindObjectsByType 可见={visibleCount}，全量={all.Length}。分布：");
        foreach (var kv in byScene)
            sb.Append($"\n  {kv.Key} = {kv.Value}");
        if (sampleHidden.Count > 0)
            sb.Append($"\n  隐藏对象样例：{string.Join(" | ", sampleHidden)}");

        Debug.LogWarning(sb.ToString());
    }

    private static bool _filterInstalled;
    private static void InstallListenerWarningFilter()
    {
        if (_filterInstalled) return;
        _filterInstalled = true;
        Debug.unityLogger.logHandler = new AudioListenerWarningFilterLogHandler(Debug.unityLogger.logHandler);
    }

    private sealed class AudioListenerWarningFilterLogHandler : ILogHandler
    {
        private readonly ILogHandler _inner;
        public AudioListenerWarningFilterLogHandler(ILogHandler inner) => _inner = inner;

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            // 之前只查 format 参数本身，猜错了——Unity引擎内部这条警告很可能是拿
            // "{0}"这种占位符当format、真正的文字装在args里传进来的，直接查format
            // 永远查不到"audio listener"这几个字，等于这个过滤器从来没真正拦到过
            // 这条引擎警告。改成把format和整个args都拼成最终文字再判断，两边都不
            // 漏过。
            if (ContainsListenerText(format) || ContainsListenerText(args))
            {
                return;
            }
            _inner.LogFormat(logType, context, format, args);
        }

        private static bool ContainsListenerText(string s) =>
            s != null && s.IndexOf("audio listener", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool ContainsListenerText(object[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (ContainsListenerText(args[i] as string))
                    return true;
            return false;
        }

        public void LogException(System.Exception exception, UnityEngine.Object context) =>
            _inner.LogException(exception, context);
    }

    private IEnumerator Start()
    {
        // Wait one frame so scene cameras / runtime camera systems have a chance to create their AudioListener.
        yield return null;

        EnsureAudioListener();
        WarmupCatalogAndLog();

        // 2026-07-17：自检探针（PlayCatalogProbeClip2D）关掉了——它当初是用来验证
        // "Build里音频系统是不是真的活着"，实现方式是从 forceIncludedAudioClips
        // 清单里随手捞第一个不为空的Clip整首PlayOneShot放一次，不关心那具体是什么
        // 音效。这份清单里排第一个的碰巧是 bgm_oldFactory_01.wav（一首完整的BGM
        // 曲子），导致每次进任意非MainMenu场景都会莫名其妙响起一整首"老工厂"BGM，
        // 排查了很久才找到是这里——诊断作用已经达到过（确认过Build音频链路是通的），
        // 现在留着只会跟真正的地图BGM混在一起造成混淆，先关掉。方法本身保留，以后
        // 真要验证Build音频还活不活着可以手动调用 PlayCatalogProbeClip2D()。

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 场景加载那一刻查一次不够——Additive加载新场景、编辑器里同时开着多个场景一起
    // 进Play、敌人/单位陆续生成都可能让监听器数量在运行过程中变化，只在Start()查
    // 一次或者隔几秒查一次，都会留出一段"确实同时存在好几个"的窗口，期间Unity引擎
    // 自己那条"There are N audio listeners..."警告会每帧刷屏。改成每帧检查——
    // FindObjectsOfType<AudioListener>按类型扫描，这东西全场景本来就只有个位数，
    // 每帧扫一次的开销可以忽略，换来的是"最多一帧"就收敛回1个，不再有肉眼可见的
    // 刷屏窗口。额外加订阅 sceneLoaded，新场景一读完立刻查一次，不用等下一帧。
    private void Update() => EnsureAudioListener();

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureAudioListener();

    private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // 诊断用：记录已经见过的 AudioListener 实例ID，每出现一个之前没见过的新实例，
    // 打一条带完整层级路径的log——只在"第一次见到"这一帧打一次，不是每帧刷，
    // 用来在下一次测试时直接指认到底是谁在不停生成新的监听器。数量从6一路涨到96，
    // 说明不是"场景没清理"这种一次性状态，是有什么东西在运行过程中持续新建
    // AudioListener，静态读代码/prefab都没找到明确的调用点（唯一会AddComponent
    // 的三处——SkyPrisonPlayerAudioListenerAnchor/这里自己的兜底/主菜单——都有
    // null检查不会重复建），只能靠运行时实名揪出来。
    private static readonly System.Collections.Generic.HashSet<int> _seenListenerIds =
        new System.Collections.Generic.HashSet<int>();

    // 真凶找到了：场景里那个 AudioListenerRoot 已经挂着 SkyPrisonPlayerAudioListenerAnchor
    // 组件，它自己每帧/每秒都在做"禁用其它监听器、把自己这个扶正"这件事；而这里
    // （Update每帧）也在做完全同一件事，但选哪个当"要保留的那个"用的是
    // FindObjectsOfType返回的数组顺序（不稳定，谁在前全看Unity内部怎么排），
    // 两边经常选中不一样的那个当"正确答案"——于是同一帧内你方唱罢我登场，两个
    // 监听器的 enabled 被反复来回切（这帧我们把 AudioListenerRoot 关了留 Main Camera，
    // 下一秒锚点自己那套周期检查又把 AudioListenerRoot 掰回来、顺手关掉 Main Camera，
    // 如此反复）。真实存在的组件数量一直只有2个，但Unity引擎内部的监听器注册/
    // 反注册计数看起来无法承受这种同一批组件被高频反复启禁，累积出偏差，
    // 这才是数字一直往上滚雪球的根本原因，跟"场景没清理干净""Editor没重启"都无关。
    // 修法：这里不再跟锚点抢活干——场景里已经有 SkyPrisonPlayerAudioListenerAnchor
    // 的话，去重这件事完全交给它一个人做，这里只保留"一个都没有"时的兜底创建。
    private void EnsureAudioListener()
    {
        // 用 Resources.FindObjectsOfTypeAll 而不是 FindObjectsOfType：后者看不见带
        // HideFlags.DontSave 的对象，而「创建守卫看不见自己创建的东西」正是本文件
        // 造成 1126 个 listener 泄漏的机制。即使现在已经不再设 DontSave，扫描这一侧
        // 也要能看见，否则将来任何人再设一次就会静默重现同样的泄漏。
        //
        // 代价是它还会返回 prefab 资产里的组件，所以下面要过滤掉没有有效场景的对象，
        // 只处理真正存在于场景中的 listener。
        var allListeners = Resources.FindObjectsOfTypeAll<AudioListener>();
        var sceneListeners = new System.Collections.Generic.List<AudioListener>(allListeners.Length);
        for (int i = 0; i < allListeners.Length; i++)
        {
            AudioListener l = allListeners[i];
            if (l != null && l.gameObject.scene.IsValid())
                sceneListeners.Add(l);
        }
        AudioListener[] listeners = sceneListeners.ToArray();
        int enabledCount = 0;
        AudioListener firstEnabled = null;
        bool anchorPresent = FindObjectOfType<SkyPrisonPlayerAudioListenerAnchor>() != null;

        // 清掉历史遗留的 fallback listener。上面那个顺序 bug 会每帧新建一个，
        // 存档或场景里可能已经积累了成百上千个，光修顺序不会让它们消失。
        // 按名字识别，只清自己建的那种，不碰任何别的 listener。
        int fallbackCount = 0;
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] == null) continue;
            if (listeners[i].gameObject.name != FallbackListenerObjectName) continue;

            fallbackCount++;
            if (fallbackCount > 1 || anchorPresent)
            {
                Destroy(listeners[i].gameObject);
                listeners[i] = null;
            }
        }

        if (fallbackCount > 1)
            Debug.Log($"[SkyPrisonRuntimeAudioBootstrap] 清理了 {fallbackCount - (anchorPresent ? 0 : 1)} 个多余的 fallback AudioListener。");

        ReportInvisibleListeners(listeners.Length);

        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] == null) continue;

            int id = listeners[i].GetInstanceID();
            if (_seenListenerIds.Add(id))
            {
                Debug.LogError("[ListenerHunt] 新出现的AudioListener #" + _seenListenerIds.Count +
                    " 挂在: " + GetHierarchyPath(listeners[i].transform), listeners[i]);
            }

            if (listeners[i].enabled && listeners[i].gameObject.activeInHierarchy)
            {
                enabledCount++;
                if (firstEnabled == null)
                    firstEnabled = listeners[i];
            }
        }

        // 锚点存在时完全不插手，连"没有启用的 listener"这种情况也不补。
        //
        // 这个判断原来排在下面的创建分支之后，等于没起作用，两个系统会每帧互相拆台：
        //   Bootstrap 发现没有启用的 listener → 新建一个 fallback（启用）
        //   → Anchor 的 DisableOtherListenersIfNeeded 判定它是"其它"，禁用它
        //   → 下一帧 Bootstrap 又发现 enabledCount==0，再建一个
        // 每帧泄漏一个，实测累积到 1060 个。listener 数量一多，Unity 会随机挑一个用，
        // 3D 音效方位随机漂移，而且每个都参与音频计算，帧率也被拖下去。
        //
        // 注意 EnsureAudioListener 是在 Update 里每帧调用的，这里任何"补一个"的行为
        // 都必须先确认没有别的系统在管，否则就是每帧新建。
        if (anchorPresent)
            return;

        if (enabledCount <= 0)
        {
            GameObject listenerGo = new GameObject(FallbackListenerObjectName);
            DontDestroyOnLoad(listenerGo);
            // 这里绝对不能设 HideFlags.DontSave —— 那是 1126 个 listener 泄漏的根因。
            //
            // FindObjectsOfType/FindObjectsByType 不返回带 DontSave 的对象，于是上面
            // 那次扫描看不见自己上一帧刚创建的这个 fallback，判定 enabledCount==0，
            // 每帧再建一个，无限循环。同一个盲区也让 [ListenerHunt] 和独立探针都只
            // 数到 1 个，两套诊断结论一致反而让人确信「没有泄漏」。
            //
            // DontSave 还会让对象在退出 Play 时不被销毁，跨会话累积（实测残留 1125 个
            // 无场景的孤儿）。运行时对象已经有 DontDestroyOnLoad 保证跨场景存活，
            // DontSave 在这里没有任何好处。
            listenerGo.transform.position = Vector3.zero;
            listenerGo.AddComponent<AudioListener>();
            return;
        }

        if (enabledCount > 1)
        {
            // 直接销毁多余的组件，不只是禁用——跟 SkyPrisonPlayerAudioListenerAnchor
            // 那边同一个思路：怀疑只是禁用没能让Unity引擎内部的监听器登记表真正退出。
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null || listener == firstEnabled)
                    continue;
                Destroy(listener);
            }
        }
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t == null) return "(null)";
        string path = t.name;
        Transform current = t.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private void WarmupCatalogAndLog()
    {
        SkyPrisonRuntimeAudioCatalog catalog = SkyPrisonRuntimeAudioCatalog.Instance;
        if (catalog == null)
        {
            Debug.LogError("[SkyPrisonRuntimeAudioBootstrap] SkyPrisonRuntimeAudioCatalog not found in Resources. Run Tools/音声设置/校对并收集运行时音声资源 before Build, or check BuildPreprocessor.");
            return;
        }

        catalog.RuntimeWarmup(true);

        int clipCount = catalog.forceIncludedAudioClips != null ? catalog.forceIncludedAudioClips.Count : 0;
        int packageCount = catalog.audioPackages != null ? catalog.audioPackages.Count : 0;
        int surfaceCount = catalog.groundSurfaceMaterials != null ? catalog.groundSurfaceMaterials.Count : 0;

        Debug.Log("[SkyPrisonRuntimeAudioBootstrap] Runtime audio catalog available. packages=" + packageCount + ", surfaces=" + surfaceCount + ", clips=" + clipCount + ".");
    }

    private void PlayCatalogProbeClip2D()
    {
        SkyPrisonRuntimeAudioCatalog catalog = SkyPrisonRuntimeAudioCatalog.Instance;
        if (catalog == null || catalog.forceIncludedAudioClips == null || catalog.forceIncludedAudioClips.Count <= 0)
        {
            Debug.LogWarning("[SkyPrisonRuntimeAudioBootstrap] Probe skipped: catalog has no forceIncludedAudioClips.");
            return;
        }

        AudioClip clip = null;
        for (int i = 0; i < catalog.forceIncludedAudioClips.Count; i++)
        {
            AudioClip candidate = catalog.forceIncludedAudioClips[i];
            if (candidate != null)
            {
                clip = candidate;
                break;
            }
        }

        if (clip == null)
        {
            Debug.LogWarning("[SkyPrisonRuntimeAudioBootstrap] Probe skipped: all catalog AudioClip references are null.");
            return;
        }

        GameObject sourceGo = new GameObject(ProbeSourceObjectName);
        DontDestroyOnLoad(sourceGo);
        sourceGo.hideFlags = HideFlags.DontSave;

        AudioSource source = sourceGo.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 1f;
        source.mute = false;
        source.outputAudioMixerGroup = null;
        source.bypassListenerEffects = true;
        source.bypassReverbZones = true;
        source.PlayOneShot(clip, 1f);

        Debug.Log("[SkyPrisonRuntimeAudioBootstrap] Playing 2D probe clip from catalog: " + clip.name + ", length=" + clip.length.ToString("0.###") + "s. If this is audible, Build audio output and catalog clips are alive.", sourceGo);

        Destroy(sourceGo, Mathf.Max(2f, clip.length + 0.5f));
    }
}
