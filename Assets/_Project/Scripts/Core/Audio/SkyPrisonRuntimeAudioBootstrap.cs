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
        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);
        int enabledCount = 0;
        AudioListener firstEnabled = null;
        bool anchorPresent = FindObjectOfType<SkyPrisonPlayerAudioListenerAnchor>() != null;

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

        if (enabledCount <= 0)
        {
            GameObject listenerGo = new GameObject(FallbackListenerObjectName);
            DontDestroyOnLoad(listenerGo);
            listenerGo.hideFlags = HideFlags.DontSave;
            listenerGo.transform.position = Vector3.zero;
            listenerGo.AddComponent<AudioListener>();
            return;
        }

        if (anchorPresent)
        {
            // 场景里已经有专门的锚点组件在管这件事，这里完全不插手（连"禁用多余的"
            // 都不做），避免跟它打架。
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
