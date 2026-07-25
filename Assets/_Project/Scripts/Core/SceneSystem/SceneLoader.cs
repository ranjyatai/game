using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局场景加载器。所有场景切换都走这里，统一触发加载界面。
/// 自动创建，DontDestroyOnLoad。
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    // ── 事件（加载界面监听这些来驱动 UI）────────────────────────────────
    public static event Action            OnLoadStart;
    public static event Action<float>     OnLoadProgress;   // 0~1
    /// <summary>进度到 100%，等待玩家输入。加载界面显示"按确定键继续"。</summary>
    public static event Action            OnLoadReady;
    /// <summary>玩家确认后、场景激活前，通知 UI 做淡出黑屏。</summary>
    public static event Action            OnLoadFadeOut;
    public static event Action            OnLoadComplete;
    /// <summary>LoadingScreenUI 的黑底揭幕动画（0.25s）真正播放完毕那一刻触发。
    /// LoadRoutine 的 onComplete 回调要等这个才执行，不能提前——不然存档快照会
    /// 拍到还没揭幕、纯黑或半透明黑的画面。</summary>
    public static event Action            OnRevealComplete;

    /// <summary>仅供 LoadingScreenUI 的揭幕协程调用，标记揭幕动画真正结束。</summary>
    public static void NotifyRevealComplete() => OnRevealComplete?.Invoke();
    /// <summary>玩家按确定、揭幕开始的那一刻触发。AI/状态 tick 必须等这个事件才打开，
    /// 绝不能在 BootCoordinator 跑完（SignalReady）时就打开，否则黑屏期间怪物已经在行动。</summary>
    public static event Action            OnGameplayResume;

    // ── 状态 ─────────────────────────────────────────────────────────────
    public static bool   IsLoading      { get; private set; }
    public static bool   IsWaitingInput { get; private set; }

    /// <summary>由 LoadingSceneController 读取：本次要加载的目标场景名。</summary>
    public static string PendingScene   { get; private set; }

    // 最短加载时间（秒），避免加载界面一闪而过
    private const float MinLoadSeconds = 0.8f;

    private bool   _inputConfirmed;
    private bool   _sceneReady;
    private static Action _pendingCallback;

    // 揭幕时需要同步恢复的游戏 canvas（key=CanvasGroup, value=隐藏前的原始alpha）
    private readonly List<(CanvasGroup cg, float origAlpha)> _gameCanvases = new();
    public static IReadOnlyList<(CanvasGroup cg, float origAlpha)> GameCanvases =>
        Instance != null ? Instance._gameCanvases : null;

    /// <summary>加载中（尚未揭幕）时为 true。游戏 UI 系统在自己创建根 CanvasGroup 时
    /// 应检查这个标记，如果为 true 就立刻把 alpha 设为 0，从源头杜绝"先出现再被隐藏"的一帧。</summary>
    public static bool IsAwaitingReveal { get; private set; }

    /// <summary>揭幕淡出动画正在播放（从黑屏到画面完全显示的这 0.2~0.3s 内）为 true。
    /// 任何"自己也会持续驱动同一个 CanvasGroup.alpha"的系统（比如战斗可见度自动淡入淡出）
    /// 都必须在这期间让路，否则两边同时写同一个 alpha 字段会互相打架，出现闪烁/回暗。</summary>
    public static bool IsRevealing { get; private set; }

    /// <summary>仅供 LoadingScreenUI 的揭幕协程调用，标记揭幕动画的起止。</summary>
    public static void SetRevealing(bool value) => IsRevealing = value;

    /// <summary>
    /// 游戏 UI 系统（HUD/背包/拾取提示等）在创建自己的根 CanvasGroup 后应立即调用本方法。
    /// 若当前处于加载未揭幕阶段，会同步把 alpha 设为 0 并记录原始值，揭幕时统一淡入。
    /// 可重复调用（同一个 CanvasGroup 不会被处理两次）。
    /// </summary>
    public static void RegisterGameCanvasForReveal(CanvasGroup cg)
    {
        if (cg == null || Instance == null) return;
        if (!IsAwaitingReveal) return; // 已经揭幕，不需要处理

        foreach (var entry in Instance._gameCanvases)
            if (entry.cg == cg) return; // 已注册过

        float orig = cg.alpha;
        cg.alpha = 0f;
        Instance._gameCanvases.Add((cg, orig));
    }

    /// <summary>
    /// 场景初始化系统（Bootstrapper）完成后调用，通知 SceneLoader 可以开放玩家输入。
    /// 若 10 秒内未收到信号，自动超时继续。
    /// </summary>
    public static void SignalReady()
    {
        if (Instance != null) Instance._sceneReady = true;
    }

    // 每次进入 Play Mode 都重置静态状态（防止 Editor 关闭 Domain Reload 时静态残留）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        IsLoading      = false;
        IsWaitingInput = false;
        PendingScene   = null;
        _pendingCallback = null;
        IsAwaitingReveal = false;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── 公开接口 ──────────────────────────────────────────────────────────

    /// <summary>加载场景：切到 LoadingScene，由 LoadingSceneController 接管异步加载。</summary>
    public static void LoadScene(string sceneName, Action onComplete = null)
    {
        if (IsLoading)
        {
            Debug.LogWarning("[SceneLoader] 正在加载中，忽略重复请求。");
            return;
        }

        // 检查 LoadingScene 是否已加入 Build Settings
        bool hasLoadingScene = false;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.IndexOf("LoadingScene", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hasLoadingScene = true;
                break;
            }
        }

        if (!hasLoadingScene)
        {
            // LoadingScene 未配置时直接跳目标场景，保证流程不中断
            Debug.LogWarning("[SceneLoader] LoadingScene 不在 Build Settings，直接加载目标场景。请运行 Tools → Sky Prison → 创建 LoadingScene 修复。");
            EnsureInstance();
            Instance.StartCoroutine(Instance.LoadRoutine(sceneName, onComplete));
            return;
        }

        PendingScene      = sceneName;
        _pendingCallback  = onComplete;
        SceneManager.LoadScene("LoadingScene");
    }

    /// <summary>由 LoadingSceneController 在加载场景 Start 时调用，开始异步加载 PendingScene。</summary>
    public static void BeginLoad()
    {
        if (string.IsNullOrEmpty(PendingScene))
        {
            Debug.LogWarning("[SceneLoader] BeginLoad called but PendingScene is empty.");
            return;
        }
        string  target = PendingScene;
        Action  cb     = _pendingCallback;
        PendingScene      = null;
        _pendingCallback  = null;
        EnsureInstance();
        Instance.StartCoroutine(Instance.LoadRoutine(target, cb));
    }

    /// <summary>玩家在加载完成后按确定键，由 LoadingScreenUI 调用。</summary>
    public static void ConfirmEnter()
    {
        if (Instance != null)
            Instance._inputConfirmed = true;
    }

    /// <summary>
    /// 进入章节：更新存档会话，然后加载对应场景。
    /// </summary>
    public static void EnterChapter(string chapterId, string sceneName, Vector3 startPos)
    {
        SaveManager.BeginChapter(chapterId, sceneName, startPos);
        LoadScene(sceneName);
    }

    /// <summary>
    /// 本内切换地图（同一章节内部的区域切换，比如同一层楼换房间）：
    /// 不走完整读条流程，只用一个很快的黑屏过渡（约 0.25s），避免章节内部
    /// 切图过于频繁地打断玩家。跨章节请用 EnterChapter/ExitChapter。
    /// </summary>
    public static void SwitchMap(string mapSceneName, Vector3 playerPos)
    {
        SaveManager.UpdateMapInChapter(mapSceneName, playerPos);
        QuickSwitchScene(mapSceneName);
    }

    // 章节内部快速切图用的黑屏淡入淡出时长（秒）
    private const float QuickSwitchFadeIn  = 0.12f;
    private const float QuickSwitchFadeOut = 0.15f;

    // 返回主菜单用的黑屏淡入淡出——比章节内切图更从容一点，主菜单 Start() 里要建
    // Live2D/背景视频/AE Prefab 这些，给足够帧数稳定下来再揭幕，不然容易在揭幕瞬间
    // 看到没加载完的半成品画面（这也是"稳健"的意思：宁可稍微多等一下，不要抢跑）。
    private const float MainMenuFadeIn      = 0.30f;
    private const float MainMenuFadeOut     = 0.35f;
    private const int   MainMenuSettleFrames = 6;

    private static void QuickSwitchScene(string sceneName)
    {
        if (IsLoading)
        {
            Debug.LogWarning("[SceneLoader] 正在加载中，忽略重复的章节内切图请求。");
            return;
        }
        EnsureInstance();
        Instance.StartCoroutine(Instance.QuickSwitchRoutine(sceneName, QuickSwitchFadeIn, QuickSwitchFadeOut, 1));
    }

    private static void QuickSwitchToMainMenu(string sceneName)
    {
        if (IsLoading)
        {
            Debug.LogWarning("[SceneLoader] 正在加载中，忽略重复的返回主菜单请求。");
            return;
        }
        EnsureInstance();
        Instance.StartCoroutine(Instance.QuickSwitchRoutine(sceneName, MainMenuFadeIn, MainMenuFadeOut, MainMenuSettleFrames));
    }

    private IEnumerator QuickSwitchRoutine(string sceneName, float fadeIn, float fadeOut, int settleFrames)
    {
        IsLoading = true;

        CanvasGroup overlay = CreateQuickSwitchOverlay();
        yield return FadeCanvasGroup(overlay, 0f, 1f, fadeIn);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;

        // 等几帧让新场景 Awake/Start 跑完、稳定下来，避免揭幕瞬间看到未初始化的画面
        for (int i = 0; i < settleFrames; i++) yield return null;

        yield return FadeCanvasGroup(overlay, 1f, 0f, fadeOut);
        Destroy(overlay.gameObject);

        IsLoading = false;
    }

    private CanvasGroup CreateQuickSwitchOverlay()
    {
        var go = new GameObject("[QuickSwitchFade]");
        DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // 盖过场景里的一切，但不需要像 LoadingScene 那样单独占一个场景

        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.color = Color.black;
        img.raycastTarget = true; // 过渡期间挡住底层输入，防止手速快的玩家在黑屏时戳到旧场景UI

        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        return cg;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        float t = 0f;
        cg.alpha = from;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        cg.alpha = to;
    }

    /// <summary>
    /// 正常出本：结算存档，加载基地场景。
    /// </summary>
    public static void ExitChapter(string baseSceneName)
    {
        SaveManager.EndChapter();
        // 存档（含自动保存提示 UI）要等揭幕结束、玩家已经站在地图里才触发，不能在读条期间就存
        LoadScene(baseSceneName, () =>
        {
            SaveManager.MarkHubUnlocked();
            SaveManager.AutoSave(false);
        });
    }

    /// <summary>
    /// 返回主界面：不走完整读条流程（进度条/按确认键揭幕那一套），只用黑屏淡入淡出——
    /// 但比章节内部切图更从容一点（更长的淡入淡出 + 多等几帧让主菜单自己的
    /// Live2D/背景/BGM 搭建完再揭幕），避免"稳健"不够、揭幕瞬间看到半成品画面。
    /// </summary>
    public static void LoadMainMenu(string mainMenuScene = "MainMenu")
    {
        QuickSwitchToMainMenu(mainMenuScene);
    }

    // ── 内部协程 ──────────────────────────────────────────────────────────

    private static void PreloadMapBGMClips(string sceneName)
    {
        MapBGMRegistry registry = MapBGMRegistry.LoadOrNull();
        MapBGMRegistry.Entry entry = registry?.GetForScene(sceneName, null);
        if (entry == null)
            return;

        PreloadClipList(entry.exploreClips);
        PreloadClipList(entry.combatClips);
    }

    private static void PreloadClipList(List<AudioClip> clips)
    {
        if (clips == null)
            return;

        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }
    }

    private IEnumerator LoadRoutine(string sceneName, Action onComplete)
    {
        IsLoading       = true;
        IsWaitingInput  = false;
        _inputConfirmed = false;
        _sceneReady     = false;
        OnLoadStart?.Invoke();

        yield return null;
        yield return null;

        // 地图BGM预载——趁读条阶段1这段本来就要等的时间，把目标地图配置的BGM曲目
        // 提前塞进内存（AudioClip.LoadAudioData是异步的，不用等它跑完，Phase1
        // 本来就有至少0.8秒的假进度动画时间），避免揭幕进地图那一刻MapBGMController
        // 第一次Play()时才现读文件卡一下。
        PreloadMapBGMClips(sceneName);

        // ── 阶段1：真实资产加载（0 → 70%），场景不激活 ──────────────────────
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);

        // 场景名没注册进 Build Settings（改名/打错字之类）时 LoadSceneAsync 直接返回
        // null——之前没查这个，下一行对 null 赋值 allowSceneActivation 直接崩溃，
        // 玩家除了卡在读条界面干瞪眼，什么提示都看不到。走统一报错系统，带错误编号，
        // 安全退回主菜单，不让玩家以为游戏死机了只能强制关掉重开。
        if (op == null)
        {
            IsLoading = false;
            SkyPrisonErrorReporter.ReportFatal(SkyPrisonErrorCodes.SceneNotInBuildSettings,
                $"场景 \"{sceneName}\" 无法加载（没有注册进 Build Settings，或者场景名不存在）。");
            yield break;
        }

        op.allowSceneActivation = false;

        const float realPhaseMin = 0.8f;
        float phase1Start = Time.realtimeSinceStartup;
        while (op.progress < 0.9f || Time.realtimeSinceStartup - phase1Start < realPhaseMin)
        {
            // 只用"按时间推算"的进度来驱动显示，保证视觉上平滑地从0爬到70%。
            // 之前这里取 Max(真实进度, 时间进度)：地图场景通常几帧内 op.progress 就冲到0.9，
            // 真实进度瞬间跳到100%，取max后进度条第一帧就跳到70%，然后卡住不动——
            // 看起来像“一上来就快满了”，而不是真实反映加载过程。
            // 循环退出条件仍然同时检查 op.progress，确保真实加载没完成时不会提前推进到下一阶段。
            float timeProgress = Mathf.Clamp01((Time.realtimeSinceStartup - phase1Start) / realPhaseMin);
            OnLoadProgress?.Invoke(timeProgress * 0.7f);
            yield return null;
        }
        OnLoadProgress?.Invoke(0.7f);

        // ── 阶段2：提前激活场景 + 冻结游戏时间，与假进度动画并行 ────────────
        // timeScale=0：FixedUpdate 停跑，角色无法移动；渲染和实时时间继续
        _sceneReady = false;
        _gameCanvases.Clear();
        IsAwaitingReveal = true; // 从此刻起，任何调用 RegisterGameCanvasForReveal 的系统在创建时就直接alpha=0
        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);
        Time.timeScale = 0f;

        // 等 1 帧让场景 Awake/Start 跑完，再做第一次 canvas 遮盖
        yield return null;
        HideGameCanvases();

        // ── 阶段3：70→100% 真实反映 BootCoordinator 的 Phase0~Phase3 完成度 ──
        // 不再用固定时长瞎猜：卡住时进度条会如实停住，不撒谎说"快好了"；
        // 哪个 Phase 真正跑完了，进度条才会往前跳一格。
        const float readyTimeout = 10f;
        float readyStart = Time.realtimeSinceStartup;
        while (!_sceneReady && Time.realtimeSinceStartup - readyStart < readyTimeout)
        {
            float bootProgress = SkyPrisonRuntimeBootCoordinator.BootProgress01;
            OnLoadProgress?.Invoke(Mathf.Lerp(0.7f, 1f, bootProgress));
            HideGameCanvases(); // 期间持续侦测新建 canvas（背包/拾取等系统可能延迟创建）
            yield return null;
        }
        OnLoadProgress?.Invoke(1f);

        if (!_sceneReady)
            Debug.LogWarning($"[SceneLoader] SignalReady timeout after {readyTimeout:0.0}s.");

        HideGameCanvases();

        // ── 阶段4：等玩家按确定键，期间持续遮盖新建 canvas ──────────────────
        IsWaitingInput = true;
        OnLoadReady?.Invoke();
        while (!_inputConfirmed)
        {
            HideGameCanvases();
            yield return null;
        }

        // ── 阶段5：揭幕 ───────────────────────────────────────────────────
        // 从此刻起新创建的 canvas 不再被强制置0——揭幕已经开始
        IsAwaitingReveal = false;

        // 玩家按 E 才打开 AI/状态 tick——绝不能提前，否则怪物在黑屏期间已经行动
        // 这一步会遍历场景 FindObjectsOfType<UnitStatusController> 等，可能是重操作，
        // 先在纯黑背景下跑完，等它稳定一帧后再恢复时间、开始可见的淡出动画，
        // 避免这次开销正好卡在淡出过程中造成视觉上的卡顿。
        OnGameplayResume?.Invoke();
        yield return null;

        // 恢复时间，给 RT/Chromatic 管线 2 帧稳定后再开始淡出
        Time.timeScale = 1f;
        yield return new WaitForEndOfFrame();
        yield return null;

        IsWaitingInput = false;
        OnLoadFadeOut?.Invoke();
        yield return null;

        IsLoading = false;
        OnLoadComplete?.Invoke();

        if (onComplete != null)
        {
            // 等 LoadingScreenUI 的揭幕动画真正播完（黑底完全透明）才执行回调——
            // 这是自动存档快照会不会拍到黑屏的关键。没有 LoadingScreenUI 时
            // （比如 LoadingScene 没接入 Build Settings 的兜底路径）不会有人调
            // NotifyRevealComplete，靠超时兜底，不会永久卡住。
            bool revealDone = false;
            Action markDone = () => revealDone = true;
            OnRevealComplete += markDone;
            float waitStart = Time.realtimeSinceStartup;
            const float revealTimeout = 0.6f;
            while (!revealDone && Time.realtimeSinceStartup - waitStart < revealTimeout)
                yield return null;
            OnRevealComplete -= markDone;

            onComplete.Invoke();
        }
    }

    /// <summary>
    /// 兜底轮询：主要遮盖机制是各 UI 系统自己在创建 CanvasGroup 时调用
    /// RegisterGameCanvasForReveal（零帧延迟，从源头杜绝闪烁）。
    /// 这个方法只用来捕获尚未主动注册、遗漏的根 Canvas，作为安全网。
    /// </summary>
    private void HideGameCanvases()
    {
        // 用 HashSet 防止重复处理同一个 CanvasGroup
        var alreadyHandled = new HashSet<CanvasGroup>();
        foreach (var entry in _gameCanvases) alreadyHandled.Add(entry.cg);

        var loadingUI = FindObjectOfType<LoadingScreenUI>();
        Canvas loadingCanvas = loadingUI != null ? loadingUI.GetComponent<Canvas>() : null;

        foreach (var canvas in FindObjectsOfType<Canvas>(includeInactive: true))
        {
            if (canvas == loadingCanvas) continue;
            if (!canvas.isRootCanvas) continue;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            var cg = canvas.GetComponent<CanvasGroup>();
            if (cg == null) cg = canvas.gameObject.AddComponent<CanvasGroup>();
            if (alreadyHandled.Contains(cg)) continue;

            float orig = cg.alpha;
            cg.alpha = 0f;
            _gameCanvases.Add((cg, orig));
            alreadyHandled.Add(cg);
        }
    }

    private static void EnsureInstance()
    {
        if (Instance != null) return;
        var go = new GameObject("[SceneLoader]");
        go.AddComponent<SceneLoader>();
    }

}
