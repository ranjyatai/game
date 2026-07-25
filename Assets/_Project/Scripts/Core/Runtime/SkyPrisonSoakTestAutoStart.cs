using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 浸测模式专用——不需要人手动点主菜单的"继续游戏"/"新游戏"按钮，自动复刻这两条
/// 真实流程（逻辑照抄 MainMenuController.OnContinue/OnNewGame，只是跳过槽位选择UI，
/// 固定用槽位0），让 -soaktest 启动后自动跳过菜单进正式场景，配合
/// SkyPrisonSoakTestDriver（自动走位/跳跃/攻击）才能真正做到整晚无人值守。
///
/// 进场景后还会定时自动存档：
///   1) 存档时长统计（SaveManager.totalPlayTimeSeconds）要真的落盘才能累积
///   2) 中途崩溃/被杀不会让这一整晚测试白测——下次 -soaktest 启动能 Continue 接着上次的存档继续跑
///
/// 只在命令行带 -soaktest 时生效，不会出现在正常发布的Build里。
/// </summary>
public class SkyPrisonSoakTestAutoStart : MonoBehaviour
{
    [SerializeField] private string commandLineArg = "-soaktest";
    [SerializeField] private float delayBeforeAutoStart = 1.5f;
    [SerializeField] private float autoSaveIntervalSeconds = 300f;
    [SerializeField] private string mainMenuSceneNameContains = "MainMenu";

    private static bool _triggeredThisProcess;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isPlaying)
            return;

        if (FindObjectOfType<SkyPrisonSoakTestAutoStart>() != null)
            return;

        var go = new GameObject("[SkyPrisonSoakTestAutoStart]");
        go.AddComponent<SkyPrisonSoakTestAutoStart>();
    }

    private void Awake()
    {
        if (!HasCommandLineArg())
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        // 2026-07-15：曾经在这里强制切过 FullScreenWindow 想解决失焦检测问题，实测
        // 没解决问题、反而带来了副作用（触发亮度被重置到最暗）——已撤销，别再加回来。
        // 焦点检测本身的问题后来定位到是命令行后台启动方式导致的，不是代码问题，见
        // SkyPrisonFocusMuteController 相关排查记录，跟这里无关。

        // 窗口失焦挂后台时，F10报告反复出现 WaitForTargetFPS 飙到上千毫秒（PlayerLoop≈
        // WaitForTargetFPS≈整帧耗时）——VSync 是"等下一次显示器刷新"，窗口不在前台没有
        // 真正合成到屏幕上时，这个等待经常会被系统无限拖长，是这个异常最可能的直接机制。
        // 浸测场景本来就要长时间挂后台，关掉VSync、给个不设硬顶的目标帧率，避免卡在这个
        // 等待上；同时把进程调度优先级提一档，减轻（不能完全消除，这是Windows系统级的
        // 后台进程限流策略）窗口失焦时被系统降权调度的影响。只在 -soaktest 时生效，不碰
        // 玩家自己在设置里调好的 VSync/帧率偏好。
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SkyPrisonSoakTestAutoStart] 提升进程优先级失败（不影响其他功能）：{e.Message}");
        }

        SceneManager.sceneLoaded += OnSceneLoaded;

        // SceneLoader.LoadRoutine 阶段4会卡住等玩家在读条界面按确定键才揭幕继续
        // （SceneLoader.cs:374-381，正常游戏体验需要这个停顿）——机器人按不了这个键，
        // 不订阅这个事件的话每次切场景都会永远卡在这一步。收到"准备好了、等确认"信号
        // 就直接自动确认，不限于开局这一次，之后游戏内任何场景切换都会自动确认过去。
        SceneLoader.OnLoadReady += AutoConfirmSceneLoad;

        // 万一这个组件是在主菜单场景加载之后才创建的（理论上 AfterSceneLoad 时机应该
        // 跟场景加载同批，但留个保险），补一次当前场景检查。
        CheckSceneAndMaybeTrigger(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneLoader.OnLoadReady -= AutoConfirmSceneLoad;
    }

    private void AutoConfirmSceneLoad()
    {
        Debug.Log("[SkyPrisonSoakTestAutoStart] 读条界面等待确认，自动按确定继续。");
        SceneLoader.ConfirmEnter();
    }

    private bool HasCommandLineArg()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], commandLineArg, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CheckSceneAndMaybeTrigger(scene);
    }

    private void CheckSceneAndMaybeTrigger(Scene scene)
    {
        if (_triggeredThisProcess)
            return;

        if (string.IsNullOrEmpty(scene.name) || !scene.name.Contains(mainMenuSceneNameContains))
            return;

        _triggeredThisProcess = true;
        Debug.Log($"[SkyPrisonSoakTestAutoStart] 检测到场景 '{scene.name}'，{delayBeforeAutoStart}秒后自动跳过菜单进入游戏。");
        Invoke(nameof(TriggerAutoStart), delayBeforeAutoStart);
    }

    private void TriggerAutoStart()
    {
        MainMenuController mainMenuController = FindObjectOfType<MainMenuController>();
        string hubScene = mainMenuController != null ? mainMenuController.HubSceneName : "Hub_Base";
        Debug.Log($"[SkyPrisonSoakTestAutoStart] TriggerAutoStart 开始。mainMenuController={(mainMenuController != null ? "找到" : "没找到,用默认值")}, hubScene={hubScene}, hasSave(0)={SaveManager.HasSave(0)}");

        if (SaveManager.HasSave(0))
        {
            if (!SaveManager.Load(0))
            {
                Debug.LogWarning("[SkyPrisonSoakTestAutoStart] SaveManager.Load(0) 失败，改走新游戏流程。");
                StartNewGame(hubScene);
                return;
            }

            bool wasInChapter = SaveManager.Player != null && SaveManager.Player.IsInChapter;
            if (wasInChapter)
                SaveManager.EndChapter();

            Debug.Log($"[SkyPrisonSoakTestAutoStart] 继续存档流程，SceneLoader.LoadScene({hubScene}) 调用中...");
            SceneLoader.LoadScene(hubScene, () =>
            {
                Debug.Log("[SkyPrisonSoakTestAutoStart] 继续存档流程完成，场景已切换。");
                SaveManager.MarkHubUnlocked();
                if (wasInChapter)
                    SaveManager.AutoSave(false);
                BeginPeriodicAutoSave();
            });
        }
        else
        {
            StartNewGame(hubScene);
        }
    }

    private void StartNewGame(string hubScene)
    {
        Debug.Log($"[SkyPrisonSoakTestAutoStart] 新游戏流程，SceneLoader.LoadScene({hubScene}) 调用中...");
        SaveManager.NewGame(0);
        SceneLoader.LoadScene(hubScene, () =>
        {
            Debug.Log("[SkyPrisonSoakTestAutoStart] 新游戏流程完成，场景已切换。");
            SaveManager.Save(false);
            BeginPeriodicAutoSave();
        });
    }

    private void BeginPeriodicAutoSave()
    {
        CancelInvoke(nameof(PeriodicAutoSave));
        InvokeRepeating(nameof(PeriodicAutoSave), autoSaveIntervalSeconds, autoSaveIntervalSeconds);
    }

    private void PeriodicAutoSave()
    {
        SaveManager.AutoSave();
    }
}
