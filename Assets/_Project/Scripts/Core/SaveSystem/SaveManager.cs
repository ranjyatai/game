using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 全局存档管理器。自动创建，DontDestroyOnLoad。
///
/// 文件布局：
///   persistentDataPath/save_auto.json   ← 玩家存档
///   persistentDataPath/settings.json    ← 设置（独立，不随存档走）
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────
    public static event Action OnSaveLoaded;
    public static event Action OnSaved;
    public static event Action OnNewGame;
    // 自动保存提示 UI（AutoSaveIndicatorUI）订阅这三个事件来播放"保存中/完成/失败"动画。
    // OnSaved 之前就有（存档选择列表刷新用），这里额外加开始和失败两个事件。
    public static event Action OnSaveStarted;
    public static event Action<string> OnSaveFailed;

    // ── 当前数据（运行时唯一副本）────────────────────────────────────────
    public static PlayerSaveData  Player   { get; private set; }
    public static SettingsData    Settings { get; private set; }

    // ── 新游戏出生点临时覆盖（不写入存档，仅在本次加载流程中有效）─────────
    public static bool    HasSpawnOverride    { get; private set; }
    public static string  SpawnOverrideScene  { get; private set; }
    public static Vector3 SpawnOverridePos    { get; private set; }
    public static float   SpawnOverrideRotY   { get; private set; }

    /// <summary>
    /// 设置新游戏出生点覆盖。场景加载完成后玩家出生系统读取并清除。
    /// </summary>
    public static void SetSpawnOverride(string scene, Vector3 pos, float rotY)
    {
        HasSpawnOverride   = true;
        SpawnOverrideScene = scene;
        SpawnOverridePos   = pos;
        SpawnOverrideRotY  = rotY;
    }

    /// <summary>出生系统读取完毕后调用，清除覆盖状态。</summary>
    public static void ConsumeSpawnOverride()
    {
        HasSpawnOverride   = false;
        SpawnOverrideScene = null;
        SpawnOverridePos   = Vector3.zero;
        SpawnOverrideRotY  = 0f;
    }

    /// <summary>
    /// 真正的"出生系统"——之前 SetSpawnOverride 只是把数据存起来，从来没有代码读它，
    /// 角色进场景后站在哪完全看场景里烘焙好的固定位置，跟这份数据毫无关系。
    /// 要在场景真正加载完成、玩家角色对象已经确定之后调用（比如 SceneLoader.LoadScene
    /// 的 onComplete 回调里），场景名核对一致才应用，避免残留数据把角色挪到不相干的
    /// 场景坐标上。
    /// </summary>
    public static void ApplyPendingSpawnOverride()
    {
        if (!HasSpawnOverride) return;

        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!string.IsNullOrEmpty(SpawnOverrideScene) && activeScene != SpawnOverrideScene)
        {
            ConsumeSpawnOverride();
            return;
        }

        var playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        if (playerGo != null)
        {
            playerGo.transform.position = SpawnOverridePos;
            playerGo.transform.rotation = Quaternion.Euler(0f, SpawnOverrideRotY, 0f);
        }

        ConsumeSpawnOverride();
    }

    // 剧情是否已经解锁到余穹基地——玩家第一次真正进入基地场景时标记，之后死亡结算等
    // 需要"回基地"的地方都靠这个旗标判断，没解锁就退回新游戏的初始出生点。
    private const string HubUnlockedFlag = "hub_unlocked";

    public static bool IsHubUnlocked => Player != null && Player.HasQuestFlag(HubUnlockedFlag);

    /// <summary>玩家进入基地场景时调用（跟 AutoSave 放在同一个 onComplete 回调里最合适）。</summary>
    public static void MarkHubUnlocked()
    {
        if (Player == null) return;
        Player.SetQuestFlag(HubUnlockedFlag);
    }

    // ── 当前加载的存档槽位（-1 表示未加载）──────────────────────────────
    public static int ActiveSlot { get; private set; } = -1;

    private static string SavePath     => ActiveSlot >= 0
                                          ? GamePaths.SaveSlot(ActiveSlot)
                                          : GamePaths.AutoSave;
    private static string SettingsPath => GamePaths.Settings;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 确保所有游戏目录存在
        GamePaths.EnsureDirectories();

        // 启动时自动加载设置；存档等到主界面主动调用 Load
        LoadSettings();
        SkyPrisonFPSCounterUI.SetVisible(Settings != null && Settings.showFps);
        SkyPrisonBrightnessManager.Apply(Settings != null ? Settings.brightness : 1f);
        SkyPrisonBrightnessManager.ApplyMotionBlur(Settings != null && Settings.motionBlur);
        SkyPrisonBrightnessManager.ApplyChromaticAberration(Settings != null && Settings.chromaticAberration);
        ApplyDisplaySettingsOnBoot();

        // BloodVFXManager.HarmonyMode 是个 static bool，默认 false——之前从没有任何
        // 地方把 Settings.harmonyMode 同步过去，开关本身在 UI 上勾了也不会有任何效果。
        BloodVFXManager.HarmonyMode = Settings != null && Settings.harmonyMode;

        // languageCode 存在 SettingsData 里，但 LocalizationRuntime 自己另外用
        // PlayerPrefs 记了一份当前语言，两边完全不同步——玩家在设置里选的语言，
        // 运行时实际生效的语言以 LocalizationRuntime 为准，SettingsData.languageCode
        // 之前只是存了个没人读的字段。开机时用存档里的值同步一次。
        if (Settings != null && !string.IsNullOrEmpty(Settings.languageCode) && LocalizationRuntime.Instance != null)
            LocalizationRuntime.Instance.SetLanguage(Settings.languageCode);

        // 设置窗口"音频"tab 的四条音量滑块之前只写 SettingsData 存档字段，从没同步到
        // 真正驱动音效播放音量的 SkyPrisonAudioGlobalSettings.Instance——滑块拖了跟没拖
        // 一样，游戏里实际播放音量从头到尾都是默认值 1。开机时同步一次。
        var audioSettings = SkyPrisonAudioGlobalSettings.Instance;
        if (Settings != null && audioSettings != null)
        {
            audioSettings.masterVolume = Settings.masterVolume;
            audioSettings.bgmVolume    = Settings.musicVolume;
            audioSettings.seVolume     = Settings.sfxVolume;
            audioSettings.voiceVolume  = Settings.voiceVolume;
        }

        // 手柄震动强度 + 摇杆死区同样只是存档字段，得同步到真正驱动这两样的地方：
        // 震动强度 -> SkyPrisonGamepadRumble.Strength（静态字段，运行时全局唯一入口）；
        // 摇杆死区 -> SkyPrisonInputSettings 这个共享 asset 的 gamepadDeadZone（Resources.Load
        // 拿到的是同一份内存实例，改了就是全局生效，不需要额外的运行时单例）。
        if (Settings != null)
        {
            SkyPrisonGamepadRumble.Strength = Settings.vibrationStrength;
            SkyPrisonScreenShake.Enabled    = Settings.screenShake;
            SkyPrisonAntialiasingApplier.Apply(Settings.antialiasingMode);

            var inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
            if (inputSettings != null)
                inputSettings.gamepadDeadZone = Settings.gamepadDeadzone;
        }

        if (Player == null)
            Player = new PlayerSaveData();
    }

    // ── 存档 ──────────────────────────────────────────────────────────────

    /// <summary>指定槽位是否有存档文件。</summary>
    public static bool HasSave(int slot = 0) => File.Exists(GamePaths.SaveSlot(slot));

    /// <summary>任意槽位中是否存在至少一个存档（用于主菜单"继续游戏"按钮判断）。</summary>
    public static bool HasAnySave()
    {
        for (int i = 0; i < GamePaths.SaveSlotCount; i++)
            if (HasSave(i)) return true;
        return false;
    }

    /// <summary>读取指定槽位的元数据（不加载完整存档，仅用于存档选择UI）。</summary>
    public static SaveSlotMeta ReadSlotMeta(int slot)
    {
        string path = GamePaths.SaveSlot(slot);
        if (!File.Exists(path))
            return new SaveSlotMeta { slot = slot, isEmpty = true };

        try
        {
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<PlayerSaveData>(json);
            return new SaveSlotMeta
            {
                slot         = slot,
                isEmpty      = false,
                lastSaved    = data?.lastSavedTime ?? "",
                playTimeText = data?.playTimeText  ?? "",
                chapterId    = data?.activeSession?.chapterId ?? "",
                mapId        = data?.activeSession?.mapId ?? "",
            };
        }
        catch { return new SaveSlotMeta { slot = slot, isEmpty = false, lastSaved = "（损坏）" }; }
    }

    /// <summary>新游戏：指定槽位，清空数据（下次 Save 才实际写文件）。</summary>
    public static void NewGame(int slot)
    {
        ActiveSlot = slot;
        Player = new PlayerSaveData();
        BeginPlayTimeSession();
        OnNewGame?.Invoke();
    }

    /// <summary>读取指定槽位的完整存档。返回 true = 成功。</summary>
    public static bool Load(int slot)
    {
        string path = GamePaths.SaveSlot(slot);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveManager] 槽位 {slot} 存档不存在。");
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            Player = JsonUtility.FromJson<PlayerSaveData>(json);
            if (Player == null) Player = new PlayerSaveData();
            ActiveSlot = slot;
            BeginPlayTimeSession();
            OnSaveLoaded?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            SkyPrisonErrorReporter.Report(SkyPrisonErrorCodes.SaveReadCorrupt, $"读取槽位 {slot} 失败（存档可能已损坏）：{e.Message}", e);
            Player = new PlayerSaveData();
            return false;
        }
    }

    // ── 游玩时长 ──────────────────────────────────────────────────────────
    // 不用逐帧 Update 累加（没必要为了这个再开一个每帧跑的组件）。做法是记录"这段会话
    // 从什么时候开始算"的时间戳（Load/NewGame 时打一次），每次 Save() 时把从上个检查点
    // 到现在经过的真实时间累加进存档里的 totalPlayTimeSeconds，然后把检查点往前移，避免
    // 同一段时间被下次 Save 重复计入。用 Time.realtimeSinceStartup（不受 timeScale 影响，
    // 暂停菜单/读条时间也会算进去，跟大多数游戏"游玩时长"口径一致，做不到逐帧扣除暂停时间
    // 这么精细，但足够给玩家一个直观参考数字）。
    private static float _playTimeSessionCheckpoint = -1f;

    private static void BeginPlayTimeSession()
    {
        _playTimeSessionCheckpoint = Time.realtimeSinceStartup;
    }

    private static void AccumulatePlayTime()
    {
        if (Player == null || _playTimeSessionCheckpoint < 0f)
            return;

        float now = Time.realtimeSinceStartup;
        float elapsed = now - _playTimeSessionCheckpoint;
        if (elapsed > 0f)
            Player.totalPlayTimeSeconds += elapsed;

        _playTimeSessionCheckpoint = now;
        Player.playTimeText = FormatPlayTime(Player.totalPlayTimeSeconds);
    }

    private static string FormatPlayTime(float totalSeconds)
    {
        int totalMinutes = Mathf.FloorToInt(Mathf.Max(0f, totalSeconds) / 60f);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return $"{hours}h {minutes}m";
    }

    /// <summary>章节结束/回基地等"自动保存"触发点专用：受设置里的自动保存开关控制，
    /// 关掉后直接跳过（不落盘、不弹存档提示 UI）。跟物品/科技树等实时数据落盘的 Save()
    /// 是两回事——那些是核心数据持久化，不受这个开关影响，永远照常写盘。</summary>
    public static void AutoSave()
    {
        AutoSave(true);
    }

    /// <summary>captureScreenshot=false 用于场景揭幕回调里那种"玩家刚站进地图"的自动保存——
    /// 见 Save(bool) 上关于截屏阻塞的说明。</summary>
    public static void AutoSave(bool captureScreenshot)
    {
        if (Settings != null && !Settings.autoSaveEnabled) return;
        Save(captureScreenshot);
    }

    /// <summary>写入当前 ActiveSlot。</summary>
    public static void Save()
    {
        Save(true);
    }

    /// <summary>写入当前 ActiveSlot。captureScreenshot=false 用于跳过存档缩略图截图——
    /// ScreenCapture.CaptureScreenshot 的 GPU 回读+编码是同步阻塞的，实测在这个项目里单次
    /// 能吃掉 500ms 以上主线程时间（真实 Profiler 采样确认，2026-07-15）。章节内切地图这种
    /// 自动保存刚好卡在读条揭幕、玩家已经能操作角色的那一刻触发，这个阻塞是玩家能直接感受到
    /// 的卡顿；而且切地图那一瞬间画面还没稳定，缩略图本身也没什么参考价值，所以切地图自动
    /// 保存跳过截图，缩略图沿用上一次手动/正常保存时拍的那张即可。</summary>
    public static void Save(bool captureScreenshot)
    {
        OnSaveStarted?.Invoke();

        if (Player == null) Player = new PlayerSaveData();
        Player.lastSavedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        AccumulatePlayTime();

        try
        {
            string json = JsonUtility.ToJson(Player, prettyPrint: true);
            File.WriteAllText(SavePath, json);
            if (captureScreenshot)
                CaptureSaveScreenshot(ActiveSlot >= 0 ? ActiveSlot : 0);
            OnSaved?.Invoke();
        }
        catch (Exception e)
        {
            // 非致命：AutoSaveIndicatorUI 已经会给玩家一个"自动保存失败"的提示，这里
            // 只走统一报错系统写一份带编号的日志，不再弹一个重复的阻断式报错框。
            SkyPrisonErrorReporter.Report(SkyPrisonErrorCodes.SaveWriteFailed, $"写入存档文件失败：{e.Message}", e);
            OnSaveFailed?.Invoke(e.Message);
        }
    }

    /// <summary>
    /// 存档同时拍一张当前画面当快照，跟存档 slot 一一对应，供存档选择窗口预览显示。
    /// ScreenCapture.CaptureScreenshot 是异步写盘（在接下来的一两帧完成），不需要协程等待。
    /// </summary>
    private static void CaptureSaveScreenshot(int slot)
    {
        try
        {
            ScreenCapture.CaptureScreenshot(GamePaths.SaveScreenshot(slot));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 存档截图失败：{e.Message}");
        }
    }

    /// <summary>将当前存档另存到指定槽位（覆盖）。</summary>
    public static void SaveToSlot(int slot)
    {
        ActiveSlot = slot;
        Save();
    }

    /// <summary>删除指定槽位存档文件（连同快照一起删）。</summary>
    public static void DeleteSave(int slot = 0)
    {
        string path = GamePaths.SaveSlot(slot);
        if (File.Exists(path)) File.Delete(path);

        string shotPath = GamePaths.SaveScreenshot(slot);
        if (File.Exists(shotPath)) File.Delete(shotPath);

        if (ActiveSlot == slot) Player = new PlayerSaveData();
    }

    // ── 设置 ──────────────────────────────────────────────────────────────

    public static void LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            Settings = new SettingsData();
            SaveSettings();
            return;
        }

        try
        {
            string json = File.ReadAllText(SettingsPath);
            Settings = JsonUtility.FromJson<SettingsData>(json) ?? new SettingsData();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 读取设置失败：{e.Message}");
            Settings = new SettingsData();
        }
    }

    public static void SaveSettings()
    {
        if (Settings == null) Settings = new SettingsData();
        try
        {
            string json = JsonUtility.ToJson(Settings, prettyPrint: true);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 写入设置失败：{e.Message}");
        }
    }

    // 游戏启动时把存档里的显示/画面设置真正应用到引擎——之前这些值只在用户手动去设置
    // 窗口拖一下对应控件时才会生效，重开游戏不会自动应用上次保存的设置。
    private static void ApplyDisplaySettingsOnBoot()
    {
        if (Settings == null) return;

        QualitySettings.vSyncCount = Settings.vsync ? 1 : 0;
        QualitySettings.SetQualityLevel(Settings.qualityLevel, applyExpensiveChanges: true);
        Application.targetFrameRate = Settings.targetFrameRate;

        // 不要先手动赋值 Screen.fullScreenMode 再调 ApplyClampedResolution（内部还会
        // 再调一次 Screen.SetResolution）——同一帧连下两次模式切换指令，在 D3D12 +
        // flip-model swapchain 下容易互相打架，只留一次真正生效的调用。
        FullScreenMode bootMode = Settings.windowMode switch
        {
            0 => FullScreenMode.Windowed,
            2 => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.ExclusiveFullScreen,
        };
        ApplyClampedResolution(Settings.resolutionWidth, Settings.resolutionHeight, bootMode);
    }

    // Windowed 模式下如果选的分辨率比物理显示器还大，Windows 会把窗口硬压缩到
    // 屏幕能显示的范围内，压缩时不保证等比例——UI 的 CanvasScaler 拿到的
    // Screen.width/height 就不再是标准 16:9，导致同一套等比缩放代码在窗口模式
    // 和全屏模式下算出完全不同的显示比例。这里在应用分辨率前按物理显示器尺寸
    // 等比收缩，保证窗口模式和全屏模式最终生效的宽高比永远一致。
    public static void ApplyClampedResolution(int width, int height, FullScreenMode mode)
    {
        if (mode == FullScreenMode.Windowed)
        {
            // 窗口尺寸留一点余量（标题栏 + 任务栏），不能等于整个物理屏幕——
            // 之前直接夹到 Display.main.systemWidth/systemHeight，请求的尺寸
            // 刚好等于屏幕整个可用区域时，Windows/Unity 会把这个"窗口"摆得跟无边框
            // 全屏没有任何视觉差异（贴边铺满、看不到能拖动的边框），窗口模式形同虚设。
            //
            // 踩坑记录：Display.main.systemWidth/systemHeight 在游戏刚启动那一刻
            // （SaveManager.Awake 跑得非常早）在 Windows build 上可能还没被引擎填好，
            // 读出来是 0——算出来 maxW/maxH 也是 0，宽高直接被夹成 1x1 的窗口，
            // 表现就是"全黑一直闪"（Unity 反复尝试呈现一个 1 像素的 swapchain）。
            // 这里用更早可用的 Screen.currentResolution 兜底，并在两个来源都读不到
            // 有效值时直接跳过夹取，保底至少不把窗口夹成异常尺寸。
            int dispW = Display.main.systemWidth;
            int dispH = Display.main.systemHeight;
            if (dispW <= 0 || dispH <= 0)
            {
                dispW = Screen.currentResolution.width;
                dispH = Screen.currentResolution.height;
            }

            if (dispW > 0 && dispH > 0)
            {
                int maxW = Mathf.RoundToInt(dispW * 0.92f);
                int maxH = Mathf.RoundToInt(dispH * 0.88f);
                if (width > maxW || height > maxH)
                {
                    float scale = Mathf.Min((float)maxW / width, (float)maxH / height);
                    width  = Mathf.Max(1, Mathf.RoundToInt(width  * scale));
                    height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                }
            }
        }
        Screen.SetResolution(width, height, mode);
    }

    // ── 章节会话 ──────────────────────────────────────────────────────────

    /// <summary>进本：创建章节会话并立即保存。</summary>
    public static void BeginChapter(string chapterId, string startMapId, Vector3 startPos)
    {
        if (Player == null) return;
        Player.activeSession = new ChapterSessionData
        {
            chapterId = chapterId,
            mapId     = startMapId,
        };
        Player.activeSession.PlayerPosition = startPos;
        Save(false);
    }

    /// <summary>本内切地图：更新当前地图和位置并保存。跳过存档缩略图截图——
    /// 见 Save(bool) 上的说明。</summary>
    public static void UpdateMapInChapter(string mapId, Vector3 pos)
    {
        if (Player?.activeSession == null) return;
        Player.activeSession.mapId = mapId;
        Player.activeSession.PlayerPosition = pos;
        Save(false);
    }

    /// <summary>正常出本：会话结束，背包保留。不在这里落盘——
    /// 真正的存档（触发存档提示 UI）要等读条揭幕结束、玩家已经站在地图里才做，
    /// 由调用方在场景切换的 onComplete 回调里另外调用 Save()。</summary>
    public static void EndChapter()
    {
        if (Player == null) return;
        Player.activeSession = null;
    }

    /// <summary>死亡：清空背包，销毁会话。不在这里落盘——真正的存档（触发存档提示 UI）
    /// 要等读条揭幕结束、玩家已经站在基地里才做，由调用方在场景切换的 onComplete 回调里另外调用 Save()。</summary>
    public static void ClearOnDeath()
    {
        if (Player == null) return;
        Player.backpack.Clear();
        Player.activeSession = null;
    }
}
