using System;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 主界面控制器。
/// 放在 MainMenu 场景的任意 GameObject 上，运行时自动构建 UI。
/// 遵循项目 UI 规范：磨砂背景 / 细冷绿边框 / 透明按钮 / ButtonFeedback。
///
/// 阶段 1 — AnyKey：全屏"按任意键开始"提示，背后 LOGO/LIVE2D/AE 特效照常播放
/// 阶段 2 — Menu ：菜单按钮组出现；继续游戏无存档时灰色
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("场景配置")]
    [SerializeField] private string hubScene    = "Hub_Base";
    [SerializeField] private string steamAppId  = "";   // 填写则显示 Steam 商店按钮

    /// <summary>给 SkyPrisonSoakTestAutoStart 读——浸测模式跳过菜单点击直接复刻继续/新游戏
    /// 流程时，要用这里配置的真实场景名，不能自己硬编码一份容易跟这里失步。</summary>
    public string HubSceneName => hubScene;

    [Header("主界面配置（自动从 Resources 加载，也可手动指定）")]
    [SerializeField] private MainMenuSettings settings;

    private const string SettingsResourcePath = "MainMenuSettings";

    // ── 内部引用 ──────────────────────────────────────────────────────────
    private GameObject    _anyKeyRoot;
    private GameObject    _menuRoot;
    private Button        _continueBtn;
    private List<Button>  _menuButtons = new List<Button>();

    // 之前语言切换主菜单不会跟着刷新，得重启才看得到新语言——这里不像 Settings/Pause
    // 那样整个 Canvas 一炸重建（主菜单还挂着 BGM/Live2D/背景视频，重建代价太大、
    // 会闪一下），改成登记每个用得到本地化文字的 TMP_Text 组件+对应 key，语言变化
    // 时直接原地改 .text，不动其它任何东西。
    private readonly List<(TMP_Text text, string key, string fallback)> _localizedTexts = new();
    // 菜单按钮不是单个 TMP_Text——色收差效果额外挂了 Label_R/Label_B 两层同文字的
    // 副本（MenuButtonHoverFX 驱动，hover 时错位显示出色差）。之前只用
    // GetComponentInChildren<TMP_Text>() 取第一个（也就是主 Label）去更新，
    // Label_R/Label_B 这两层从来没跟着刷新，一直停在建按钮那一刻的旧语言文字——
    // 平时看不出来，一旦 hover 触发色收差错位，旧文字混着新文字露出来，就是
    // "游設定置" 这种乱码叠字。改成按 Button 整体注册，刷新时把这个按钮下所有
    // TMP_Text（包含 inactive 的 Label_R/Label_B）一起改掉。
    private readonly List<(Button button, string key, string fallback)> _localizedButtonTexts = new();

    private void RegisterLocalizedText(TMP_Text text, string key, string fallback) =>
        _localizedTexts.Add((text, key, fallback));

    private void RegisterLocalizedButtonText(Button button, string key, string fallback) =>
        _localizedButtonTexts.Add((button, key, fallback));

    private void OnLanguageChanged(string _)
    {
        foreach (var (text, key, fallback) in _localizedTexts)
            if (text != null) text.text = L(key, fallback);

        foreach (var (button, key, fallback) in _localizedButtonTexts)
        {
            if (button == null) continue;
            string resolved = L(key, fallback);
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true))
                t.text = resolved;
        }
    }

    private enum Phase { AnyKey, Menu }
    private Phase _phase = Phase.AnyKey;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;

        // 提前确保 Canvas 存在，避免其他系统在 Start 前访问时报错
        if (GetComponent<Canvas>() == null)
        {
            var c = gameObject.AddComponent<Canvas>();
            c.renderMode   = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 10;
        }
        if (GetComponent<CanvasScaler>() == null)
        {
            var s = gameObject.AddComponent<CanvasScaler>();
            s.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(3840f, 2160f);
            s.matchWidthOrHeight  = 0.5f;
        }
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
        // 提前创建 AudioSource，防止其他系统在 Start 前访问时抛 MissingComponentException
        if (GetComponent<AudioSource>() == null)
            gameObject.AddComponent<AudioSource>();

        // 读条揭幕保护：跟游戏内 HUD/背包等系统一样，创建根 CanvasGroup 后必须立即登记，
        // 否则读条还在等玩家按确认键那阶段，这个 Canvas 会先露出一帧、下一帧才被
        // SceneLoader 的兜底扫描追上盖住——表现为"返回主菜单时画面闪一下又被读条盖住"。
        var cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        SceneLoader.RegisterGameCanvasForReveal(cg);
    }

    private void Start()
    {
        if (settings == null)
            settings = Resources.Load<MainMenuSettings>(SettingsResourcePath);

        _inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        CleanupGameplaySystems();
        BuildUI();
        SetupBGM();
        SetupLive2D();
        SetupBackground();
        EnterAnyKeyPhase();
    }

    private void SetupBGM()
    {
        if (settings == null)
        {
            Debug.LogWarning("[MainMenu] settings 为 null，BGM 无法播放");
            return;
        }
        if (settings.bgm == null)
        {
            Debug.LogWarning("[MainMenu] settings.bgm 未设置，跳过 BGM 播放");
            return;
        }

        // 停掉场景里所有其他 AudioSource，防止残留音乐叠放
        foreach (var existing in FindObjectsOfType<AudioSource>())
        {
            if (existing.gameObject != gameObject)
                existing.Stop();
        }

        var src = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _bgmSource                 = src;
        src.clip                  = settings.bgm;
        src.volume                = ComputeBGMVolume();
        src.loop                  = settings.bgmLoop;
        src.playOnAwake           = false;
        src.outputAudioMixerGroup = null;   // 走默认通道，不受游戏混音器影响
        src.Play();
        Debug.Log($"[MainMenu] BGM 开始播放：{settings.bgm.name}，音量={src.volume}");
    }

    // 之前 src.volume 只在开始播放那一刻赋值一次——玩家在设置窗口里把音乐音量拖到
    // 0，正在播的这首 BGM 完全不会跟着变小，因为没有任何代码在音乐播放期间重新读取
    // 全局音量设置。改成每帧从 SkyPrisonAudioGlobalSettings（设置窗口滑块实时同步的
    // 那个对象）重新算一次，拖动滑块时立刻生效。
    private AudioSource _bgmSource;

    private float ComputeBGMVolume()
    {
        float baseVolume = settings != null ? settings.bgmVolume : 1f;
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        if (gs == null) return baseVolume;
        return baseVolume * Mathf.Max(0f, gs.masterVolume) * Mathf.Max(0f, gs.bgmVolume);
    }

    private void SetupLive2D()
    {
        if (settings?.live2dPrefab == null) return;
        var go = Instantiate(settings.live2dPrefab);
        // 挂到场景根节点，位置由 anchor 换算到世界坐标（Canvas 外的 3D/2D 对象）
        go.name = "[MainMenu_Live2D]";
        go.transform.localScale = Vector3.one * settings.live2dScale;
    }

    private void SetupBackground()
    {
        if (settings == null) return;

        // 优先使用 AE Prefab
        if (settings.aePrefab != null)
        {
            var go = Instantiate(settings.aePrefab);
            go.name = "[MainMenu_AE]";
            return;
        }

        // 其次使用背景视频
        if (settings.backgroundVideo != null)
        {
            var camGo = GameObject.FindWithTag("MainCamera");
            if (camGo == null) return;

            var vp = camGo.GetComponent<UnityEngine.Video.VideoPlayer>()
                     ?? camGo.AddComponent<UnityEngine.Video.VideoPlayer>();
            vp.clip        = settings.backgroundVideo;
            vp.isLooping   = settings.videoLoop;
            vp.renderMode  = UnityEngine.Video.VideoRenderMode.CameraFarPlane;
            vp.targetCamera = camGo.GetComponent<Camera>();
            vp.Play();
        }
    }

    private void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
    }

    private string L(string key, string fallback)
    {
        var table = settings?.localizationTable;
        if (table == null) return fallback;
        string result = table.Get(key, "");
        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    /// <summary>销毁从游戏场景遗留下来的 DontDestroyOnLoad 运行时系统，主菜单不需要它们。</summary>
    private static void CleanupGameplaySystems()
    {
        string[] rootNames =
        {
            "SkyPrisonRuntimeSystems",
            "SkyPrisonRuntimeUIRoot",
            "SkyPrisonBattleHUD_Runtime",
            "SkyPrisonPlayerAuthority",
        };

        foreach (var name in rootNames)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }

        // 销毁所有 __SkyPrisonHUDModule* 节点
        foreach (var go in FindObjectsOfType<GameObject>())
            if (go.name.StartsWith("__SkyPrisonHUD")) Destroy(go);
    }

    // 手柄导航状态
    private int   _cursorIndex    = 0;
    private float _navCooldown    = 0f;
    private const float NavDelay  = 0.18f;
    private const float NavRepeat = 0.12f;
    private bool  _axisHeld        = false;
    private float _prevAxisY       = 0f;
    private float _prevDPadY       = 0f;
    private float _menuInputBlock  = 0f;   // 进入菜单后屏蔽输入的剩余时间
    private SkyPrisonInputSettings _inputSettings;

    private void Update()
    {
        if (_bgmSource != null)
            _bgmSource.volume = ComputeBGMVolume();

        if (_phase == Phase.AnyKey && AnyKeyDown())
        {
            EnterMenuPhase();
            return;
        }

        if (_phase == Phase.Menu)
        {
            // 存档选择弹窗/设置窗口打开时，主菜单自己的 W/S/方向键导航要让路，
            // 否则背后的按钮光标会被同时移动，还会误触发切换音效——这个坑之前只堵了
            // 存档选择弹窗，设置窗口一直漏着：进设置窗口后手柄摇杆推到底，声音其实
            // 是这里、藏在设置窗口背后的主菜单自己在响，跟设置窗口/按键绑定弹窗
            // 本身的导航逻辑毫无关系。
            if (SaveSlotSelectorUI.IsOpen || SettingsWindowUI.IsOpen)
                return;
            HandleMenuNavigation();
        }
    }

    private void HandleMenuNavigation()
    {
        if (_menuButtons == null || _menuButtons.Count == 0) return;

        if (_menuInputBlock > 0f)
        {
            _menuInputBlock -= Time.unscaledDeltaTime;
            // 冷却期间也要消耗轴状态，防止冷却结束瞬间触发
            _prevAxisY = Input.GetAxisRaw("Vertical");
            _prevDPadY = Input.GetAxisRaw("DPadVertical");
            return;
        }

        const float Threshold = 0.6f;
        float axisY  = Input.GetAxisRaw("Vertical");
        float dpadY  = Input.GetAxisRaw("DPadVertical");

        // 边缘检测：只在轴值从中立区跨越阈值那一帧触发
        bool axisUp   = (axisY >  Threshold && _prevAxisY <=  Threshold)
                     || (dpadY >  Threshold && _prevDPadY <=  Threshold);
        bool axisDown = (axisY < -Threshold && _prevAxisY >= -Threshold)
                     || (dpadY < -Threshold && _prevDPadY >= -Threshold);
        _prevAxisY = axisY;
        _prevDPadY = dpadY;

        // 键盘方向键：用 GetKey 配合 _axisHeld 做连按
        bool keyUp   = _inputSettings != null ? _inputSettings.GetAction(SkyPrisonInputAction.MoveUp)   : Input.GetKey(KeyCode.UpArrow);
        bool keyDown = _inputSettings != null ? _inputSettings.GetAction(SkyPrisonInputAction.MoveDown) : Input.GetKey(KeyCode.DownArrow);

        // 合并：轴边缘触发 或 键盘持续
        bool up   = axisUp   || keyUp;
        bool down = axisDown || keyDown;

        // 轴触发是边缘检测，不走 held 逻辑；只有键盘走 held 重复
        bool useHeld = keyUp || keyDown;
        float v = up ? 1f : (down ? -1f : 0f);

        // 键盘 W/S/方向键默认同时也驱动 Unity 内置的 "Vertical" 轴，会让 axisUp/axisDown
        // 和下面 keyUp/keyDown 的 held 分支在同一次按键里都判定为"触发"，两边各移动一次，
        // 同一帧就连着走两格，表现为"跳过一个选项"。轴边缘触发时，把 held 状态也一并
        // 标记成"已经动过一次、进入冷却"，held 分支这一帧就不再重复移动，只有真正超过
        // NavDelay 冷却后才会继续走第二步——这样不管走哪条判定路径，实际效果都是每次
        // 按键只移动一格。
        bool axisHandledThisFrame = axisUp || axisDown;
        if (axisUp)   { MoveMenuCursor(-1); _axisHeld = true; _navCooldown = NavDelay; }
        if (axisDown) { MoveMenuCursor( 1); _axisHeld = true; _navCooldown = NavDelay; }

        // 键盘/按钮：走 held 连按逻辑
        if (useHeld && Mathf.Abs(v) > 0f && !axisHandledThisFrame)
        {
            if (!_axisHeld)
            {
                MoveMenuCursor(v > 0 ? -1 : 1);
                _navCooldown = NavDelay;
                _axisHeld    = true;
            }
            else
            {
                _navCooldown -= Time.unscaledDeltaTime;
                if (_navCooldown <= 0f)
                {
                    MoveMenuCursor(v > 0 ? -1 : 1);
                    _navCooldown = NavRepeat;
                }
            }
        }
        else if (!useHeld)
        {
            _axisHeld    = false;
            _navCooldown = 0f;
        }

        // 确认：走玩家绑定的 Interact 键
        bool confirm = _inputSettings != null
            ? _inputSettings.GetActionDown(SkyPrisonInputAction.Interact)
            : (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return));
        if (confirm)
        {
            var btn = GetActiveButton(_cursorIndex);
            if (btn != null && btn.interactable)
                btn.onClick.Invoke();
        }
    }

    // 清除所有按钮的 hover 效果（切换光标 / 进入阶段时使用）
    private void ClearAllButtonFX()
    {
        foreach (var b in _menuButtons)
        {
            if (b == null) continue;
            var fx = b.GetComponent<MenuButtonHoverFX>();
            if (fx != null) fx.OnExit();
        }
    }

    private void MoveMenuCursor(int dir)
    {
        var active = GetActiveButtons();
        if (active.Count == 0) return;

        // 清除所有按钮（包括鼠标可能悬停的那个）
        ClearAllButtonFX();

        _cursorIndex = (_cursorIndex + dir + active.Count) % active.Count;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        // 触发新按钮 PointerEnter FX（不重复播SE，由FX内部播）
        var newBtn = active[_cursorIndex];
        var es     = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(newBtn.gameObject);
        var newFx = newBtn.GetComponent<MenuButtonHoverFX>();
        if (newFx != null) newFx.OnEnterSilent();   // 不播SE，避免双响
    }

    private List<Button> GetActiveButtons()
    {
        var result = new List<Button>();
        foreach (var b in _menuButtons)
            if (b != null && b.interactable) result.Add(b);
        return result;
    }

    private Button GetActiveButton(int index)
    {
        var active = GetActiveButtons();
        if (active.Count == 0) return null;
        return active[Mathf.Clamp(index, 0, active.Count - 1)];
    }

    // ── 阶段切换 ──────────────────────────────────────────────────────────

    private void EnterAnyKeyPhase()
    {
        _phase = Phase.AnyKey;
        SetActive(_anyKeyRoot, true);
        SetActive(_menuRoot,   false);
    }

    private void EnterMenuPhase()
    {
        _phase = Phase.Menu;
        _menuInputBlock = 0.4f;    // 屏蔽 0.4s，防止按任意键穿透到菜单操作
        // 清空轴历史，防止手柄轴残留在冷却结束后立刻触发
        _prevAxisY = Input.GetAxisRaw("Vertical");
        _prevDPadY = Input.GetAxisRaw("DPadVertical");
        _axisHeld  = false;
        SetActive(_anyKeyRoot, false);
        SetActive(_menuRoot,   true);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.TitlePressAnyKey);

        if (_continueBtn != null)
            _continueBtn.interactable = SaveManager.HasAnySave();

        // 初始化手柄光标到第一个可用按钮（先清除所有 hover 防止双亮）
        ClearAllButtonFX();
        var active = GetActiveButtons();
        _cursorIndex = 0;
        if (active.Count > 0)
        {
            var es = EventSystem.current;
            if (es != null) es.SetSelectedGameObject(active[0].gameObject);
            var fx = active[0].GetComponent<MenuButtonHoverFX>();
            if (fx != null) fx.OnEnterSilent();
        }
    }

    // 上下移动光标全部走 Update() 里的手动轮询（MoveMenuCursor），不用 Unity
    // EventSystem 自己那套 Navigation.Explicit——之前两套导航同时挂着，Unity 内置的
    // StandaloneInputModule 也在监听同一个 "Vertical" 轴去移动 currentSelectedGameObject，
    // 跟手动轮询各走各的，两边每次按键都各自挪一格，选中的高亮（_cursorIndex 驱动）
    // 和手柄确认键真正提交的对象（Unity 内置 Submit 直接打给 currentSelectedGameObject）
    // 就对不上号了——表现为"光标显示在设置，手柄按下去却退出游戏"。全部设 None，
    // 只用来禁用 Unity 自己的 Move，Submit 不受影响，仍然打给我们手动同步好的
    // currentSelectedGameObject。
    private void BuildMenuNavigation()
    {
        foreach (var b in _menuButtons)
        {
            if (b == null) continue;
            var n = b.navigation;
            n.mode = Navigation.Mode.None;
            b.navigation = n;
        }
    }

    // ── 按钮回调 ──────────────────────────────────────────────────────────

    private void OnContinue()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
        SaveSlotSelectorUI.Show(SaveSlotSelectorUI.Mode.Continue, slot =>
        {
            if (!SaveManager.Load(slot)) return;
            // 上次强退在章节内 → 回到基地，不自动进地图
            bool wasInChapter = SaveManager.Player?.IsInChapter == true;
            if (wasInChapter)
                SaveManager.EndChapter();
            // 存档提示 UI 要等揭幕结束、玩家已经站在基地里才触发，不能在读条期间就存；
            // 标记基地解锁不受这个限制，反正是幂等操作，进基地就该标记。
            SceneLoader.LoadScene(hubScene, () =>
            {
                SaveManager.MarkHubUnlocked();
                if (wasInChapter) SaveManager.AutoSave(false);
            });
        });
    }

    private void OnNewGame()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);

        string targetScene = hubScene;
        Vector3 spawnPos   = Vector3.zero;
        float   spawnRotY  = 0f;

        if (settings != null && !string.IsNullOrEmpty(settings.newGameScene))
        {
            targetScene = settings.newGameScene;
            spawnPos    = settings.newGameSpawnPosition;
            spawnRotY   = settings.newGameSpawnRotationY;
        }

        SaveManager.NewGame(0);
        SaveManager.SetSpawnOverride(targetScene, spawnPos, spawnRotY);
        // 新游戏首次落盘不受自动保存开关影响——不然关掉开关新建的角色永远不会出现在存档列表里
        SceneLoader.LoadScene(targetScene, () =>
        {
            SaveManager.ApplyPendingSpawnOverride();
            SaveManager.Save(false);
        });
    }

    private void OnSettings()
    {
        SettingsWindowUI.Show();
    }

    private void OnSteamStore()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
#if STEAMWORKS_NET || FACEPUNCH_STEAMWORKS
        Steamworks.SteamFriends.ActivateGameOverlayToStore(
            new Steamworks.AppId_t(uint.Parse(steamAppId)),
            Steamworks.EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
#else
        Application.OpenURL($"https://store.steampowered.com/app/{steamAppId}");
#endif
    }

    private void OnQuit()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── UI 构建 ───────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // 字体加载：与 ItemPickupToastUI 完全相同的方式
        var style = FindObjectOfType<SkyPrison.Runtime.UI.SkyPrisonUIGlobalStyleSettings_V1>();
        _menuFont = style?.defaultTextFont;
        if (_menuFont == null) _menuFont = LoadTMPFont("ZhouFangRiMingTi-2 SDF");

        // 确保场景有 EventSystem（鼠标/手柄 UI 交互必须）
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // 确保场景有摄像机（MainMenu 场景纯 UI，只需基础摄像机）
        if (FindObjectOfType<Camera>() == null)
        {
            var camGo = new GameObject("Main Camera");
            var cam   = camGo.AddComponent<Camera>();
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.05f, 0.06f, 0.08f);
            cam.cullingMask      = 0; // 不渲染任何 3D 物体，只显示 ScreenSpaceOverlay UI
            camGo.tag            = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        // 主 Canvas
        var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight  = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        var root = (RectTransform)transform;

        // ── 全屏背景（两个阶段共用）────────────────────────────────────────
        MakeFullRect("Background", root, new Color(0.04f, 0.05f, 0.07f, 1f));

        // ── LOGO 占位（居中偏上，以后换成图片 Sprite）──────────────────────
        // 编辑器里选中 "LogoPlaceholder" 节点，把 Image.sprite 换成实际 LOGO 图即可
        var logoGo = new GameObject("LogoPlaceholder");
        logoGo.transform.SetParent(root, false);
        var logoRt = logoGo.AddComponent<RectTransform>();
        logoRt.anchorMin        = new Vector2(0.5f, 0.5f);
        logoRt.anchorMax        = new Vector2(0.5f, 0.5f);
        logoRt.pivot            = new Vector2(0.5f, 0.5f);
        float logoOffY = settings?.logoOffsetY ?? 580f;
        logoRt.anchoredPosition = new Vector2(0f, logoOffY);
        logoRt.sizeDelta        = new Vector2(960f, 320f); // 初始占位尺寸，加载纹理后会重算
        var logoImg = logoGo.AddComponent<Image>();
        if (settings?.logoTexture != null)
        {
            var tex = settings.logoTexture;
            var sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);

            logoImg.sprite         = sprite;
            logoImg.color          = Color.white;
            logoImg.preserveAspect = true;
            logoImg.type           = Image.Type.Simple;

            float nativeW = tex.width;
            float nativeH = tex.height;
            if (nativeW > 0 && nativeH > 0)
            {
                float maxW  = 3840f * 0.60f;
                float maxH  = 2160f * 0.40f;
                float scale = Mathf.Min(maxW / nativeW, maxH / nativeH, 1f);
                logoRt.sizeDelta = new Vector2(nativeW * scale, nativeH * scale);
            }
        }
        else
        {
            logoImg.color = new Color(1f, 1f, 1f, 0f);  // 透明占位
        }

        // ── 阶段1：任意键屏幕 ────────────────────────────────────────────
        _anyKeyRoot = new GameObject("AnyKeyScreen");
        _anyKeyRoot.transform.SetParent(root, false);
        var akRt = _anyKeyRoot.AddComponent<RectTransform>();
        akRt.anchorMin = Vector2.zero; akRt.anchorMax = Vector2.one;
        akRt.offsetMin = akRt.offsetMax = Vector2.zero;

        var akLabel = MakeText("AnyKeyLabel", akRt,
            L("ui_menu_press_any", "PRESS ANY BUTTON"), 44f, FontStyles.Normal);
        var akLabelRt = (RectTransform)akLabel.transform;
        akLabelRt.anchorMin        = new Vector2(0.5f, 0f);
        akLabelRt.anchorMax        = new Vector2(0.5f, 0f);
        akLabelRt.pivot            = new Vector2(0.5f, 0f);
        akLabelRt.anchoredPosition = new Vector2(0f, 120f);
        akLabelRt.sizeDelta        = new Vector2(1000f, 72f);
        akLabel.alignment          = TextAlignmentOptions.Center;
        akLabel.color              = new Color(1f, 1f, 1f, 0.85f);
        StartCoroutine(BreatheBlink(akLabel));
        RegisterLocalizedText(akLabel, "ui_menu_press_any", "PRESS ANY BUTTON");

        // ── 阶段2：菜单按钮组 ────────────────────────────────────────────
        _menuRoot = new GameObject("MenuRoot");
        _menuRoot.SetActive(false);   // 必须先隐藏；EnterMenuPhase 才激活
        _menuRoot.transform.SetParent(root, false);
        var menuRt = _menuRoot.AddComponent<RectTransform>();
        menuRt.anchorMin = Vector2.zero; menuRt.anchorMax = Vector2.one;
        menuRt.offsetMin = menuRt.offsetMax = Vector2.zero;

        // 按钮卡片：屏幕水平居中，垂直居下（pivot 底部）
        var card = MakeCard("MenuCard", menuRt,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 520f),      // 距底部 520px（4K 基准，原 1080p 是 260px）
            new Vector2(640f, 680f),
            transparent: true);         // 无背景填充

        // ── 按钮直接放在 card 下，不用 ScrollRect（避免拖拽手势吞掉 onClick）─
        const float btnStep = 116f; // 4K 基准（原 1080p 是 58px）
        const float cardW   = 560f;

        int btnIdx = 0;
        bool hasSave = SaveManager.HasAnySave();
        _menuButtons.Clear();

        // 用一个透明容器承载按钮，方便统一定位
        var contentGo = new GameObject("BtnContainer");
        contentGo.transform.SetParent(card, false);
        var contentRt = contentGo.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        contentRt.offsetMin = contentRt.offsetMax = Vector2.zero;

        _continueBtn = MakeMenuButton(L("ui_menu_continue", "继续游戏"), contentRt, -btnStep * btnIdx++, OnContinue);
        _continueBtn.interactable = hasSave;
        if (!hasSave)
        {
            var continueTxt = _continueBtn.GetComponentInChildren<TMP_Text>();
            if (continueTxt != null) continueTxt.color = new Color(1f, 1f, 1f, 0.3f);
        }
        RegisterLocalizedButtonText(_continueBtn, "ui_menu_continue", "继续游戏");
        _menuButtons.Add(_continueBtn);

        var newGameBtn = MakeMenuButton(L("ui_menu_new_game",  "新游戏"),   contentRt, -btnStep * btnIdx++, OnNewGame);
        RegisterLocalizedButtonText(newGameBtn, "ui_menu_new_game", "新游戏");
        _menuButtons.Add(newGameBtn);

        var settingsBtn = MakeMenuButton(L("ui_menu_settings",  "游戏设置"), contentRt, -btnStep * btnIdx++, OnSettings);
        RegisterLocalizedButtonText(settingsBtn, "ui_menu_settings", "游戏设置");
        _menuButtons.Add(settingsBtn);

        if (!string.IsNullOrEmpty(steamAppId))
        {
            var steamBtn = MakeMenuButton(L("ui_menu_steam_store", "Steam 商店"), contentRt, -btnStep * btnIdx++, OnSteamStore);
            RegisterLocalizedButtonText(steamBtn, "ui_menu_steam_store", "Steam 商店");
            _menuButtons.Add(steamBtn);
        }

        var quitBtn = MakeMenuButton(L("ui_menu_quit", "退出游戏"), contentRt, -btnStep * btnIdx++, OnQuit);
        RegisterLocalizedButtonText(quitBtn, "ui_menu_quit", "退出游戏");
        _menuButtons.Add(quitBtn);

        float totalH = btnIdx * btnStep;
        contentRt.sizeDelta = new Vector2(0f, totalH);

        // 手柄导航：环形链（上下循环）
        BuildMenuNavigation();

        var cardRt = (RectTransform)card;
        cardRt.sizeDelta = new Vector2(cardW + 80f, totalH + 32f);

        // 版本号（左下角）
        var ver = MakeText("Version", menuRt,
            settings?.ResolvedVersion ?? $"Ver. {Application.version}", 26f, FontStyles.Normal);
        var verRt = (RectTransform)ver.transform;
        verRt.anchorMin        = new Vector2(1f, 0f);
        verRt.anchorMax        = new Vector2(1f, 0f);
        verRt.pivot            = new Vector2(1f, 0f);
        verRt.anchoredPosition = new Vector2(-40f, 32f);
        verRt.sizeDelta        = new Vector2(440f, 48f);
        ver.alignment          = TextAlignmentOptions.Right;
        ver.color              = Color.white;

        // 版权文字（底部居中）—— 替换为实际公司名
        var copy = MakeText("Copyright", menuRt,
            settings?.copyrightText ?? "© 2025 SkyPrison Dev. All rights reserved.", 24f, FontStyles.Normal);
        var copyRt = (RectTransform)copy.transform;
        copyRt.anchorMin        = new Vector2(0.5f, 0f);
        copyRt.anchorMax        = new Vector2(0.5f, 0f);
        copyRt.pivot            = new Vector2(0.5f, 0f);
        copyRt.anchoredPosition = new Vector2(0f, 32f);
        copyRt.sizeDelta        = new Vector2(1200f, 44f);
        copy.alignment          = TextAlignmentOptions.Center;
        copy.color              = Color.white;

        TryBindFonts(root);
    }

    // ── UI 工具 ───────────────────────────────────────────────────────────

    private static void MakeFullRect(string n, Transform parent, Color color)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = color;
    }

    private static RectTransform MakeCard(string n, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size,
        bool transparent = false)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt       = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        if (!transparent)
            go.AddComponent<Image>().color = new Color(0.08f, 0.10f, 0.09f, 0.92f);

        return rt;
    }

    private static void MakeBorderLine(string n, Transform parent,
        Vector2 ancMin, Vector2 ancMax, Vector2 offsetMax, Color color)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt       = go.AddComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = offsetMax;
        go.AddComponent<Image>().color = color;
    }

    private static readonly Color ColdGreen = new Color(0.52f, 0.90f, 0.68f, 1f);

    private Button MakeMenuButton(string label, Transform parent,
        float anchoredY, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, anchoredY);
        rt.sizeDelta        = new Vector2(0f, 84f);

        var img   = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);   // 完全透明背景

        var btn    = go.AddComponent<Button>();
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor      = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor     = ColdGreen;
        colors.selectedColor    = Color.white;
        colors.disabledColor    = new Color(1f, 1f, 1f, 0.3f);
        colors.colorMultiplier  = 1f;
        btn.colors        = colors;
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        // 主文字层
        var txt = MakeText("Label", rt, label, 52f, FontStyles.Normal);
        txt.alignment = TextAlignmentOptions.Center;
        var txtRt = (RectTransform)txt.transform;
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;

        // 色收差偏移层（由 MenuButtonHoverFX 驱动动画）
        var txtR = MakeText("Label_R", rt, label, 52f, FontStyles.Normal);
        txtR.alignment = TextAlignmentOptions.Center;
        var rtR = (RectTransform)txtR.transform;
        rtR.anchorMin = Vector2.zero; rtR.anchorMax = Vector2.one;
        rtR.offsetMin = rtR.offsetMax = Vector2.zero;
        txtR.gameObject.SetActive(false);

        var txtB = MakeText("Label_B", rt, label, 52f, FontStyles.Normal);
        txtB.alignment = TextAlignmentOptions.Center;
        var rtB = (RectTransform)txtB.transform;
        rtB.anchorMin = Vector2.zero; rtB.anchorMax = Vector2.one;
        rtB.offsetMin = rtB.offsetMax = Vector2.zero;
        txtB.gameObject.SetActive(false);

        var fx = go.AddComponent<MenuButtonHoverFX>();
        fx.Init(btn, txt, txtR, rtR, txtB, rtB);

        // hover 事件 → 转交给 fx
        var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();

        var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ =>
        {
            // 鼠标进入时先清所有按钮，再激活本按钮，避免与手柄光标同时亮
            ClearAllButtonFX();
            var activeList = GetActiveButtons();
            int idx = activeList.IndexOf(btn);
            if (idx >= 0) _cursorIndex = idx;
            fx.OnEnter();
        });
        trigger.triggers.Add(enterEntry);

        var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => fx.OnExit());
        trigger.triggers.Add(exitEntry);

        var downEntry = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => { if (btn.interactable) txt.color = ColdGreen; });
        trigger.triggers.Add(downEntry);

        return btn;
    }

    // 由 BuildUI 初始化，供所有 MakeText 调用共享
    private static TMPro.TMP_FontAsset _menuFont;

    private static TMP_Text MakeText(string n, Transform parent,
        string text, float size, FontStyles style)
    {
        var go  = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp      = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        if (_menuFont != null) tmp.font = _menuFont;
        return tmp;
    }

    private static void TryBindFonts(Transform root)
    {
        var style = FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
        TMP_FontAsset font = style?.defaultTextFont;
#if UNITY_EDITOR
        if (font == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("ZhouFangRiMingTi-2 SDF t:TMP_FontAsset");
            if (guids.Length > 0)
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/ZhouFangRiMingTi-2 SDF");
        if (font == null) return;

        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            t.font = font;
    }

    // 呼吸闪烁：alpha 在 alphaMin~1 之间用正弦波缓慢往返
    private System.Collections.IEnumerator BreatheBlink(TMP_Text label,
        float period = 2.4f, float alphaMin = 0.25f)
    {
        float t = 0f;
        while (label != null)
        {
            t += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(alphaMin, 1f,
                (Mathf.Sin(t / period * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) * 0.5f);
            var c = label.color;
            c.a = alpha;
            label.color = c;
            yield return null;
        }
    }

    private static TMPro.TMP_FontAsset LoadTMPFont(string assetName)
    {
#if UNITY_EDITOR
        string path = $"Assets/_Project/UIUX/Fonts/TMP/{assetName}.asset";
        var fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
        if (fa != null) return fa;
        string[] guids = UnityEditor.AssetDatabase.FindAssets(assetName + " t:TMP_FontAsset");
        if (guids.Length > 0)
        {
            fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            if (fa != null) return fa;
        }
#endif
        return Resources.Load<TMPro.TMP_FontAsset>($"Fonts & Materials/{assetName}");
    }

    private float _anyKeyDelay = 0.6f;  // 启动后等待时间，防止手柄连接时初始输入误触
    // 之前延迟窗口内直接 return false、什么反馈都没有——GetKeyDown 只在按下的那一帧
    // 触发一次，玩家如果在这 0.6s 窗口内按了一下（构建后场景加载比编辑器 Play 慢，
    // 玩家很容易一进画面就立刻按），这次按键就直接被吃掉，必须再按第二次才有反应。
    // 改成延迟期间照常检测，命中了先记下来，延迟一到就直接放行，不用玩家重新按。
    private bool _bufferedAnyKeyDuringDelay;

    private bool AnyKeyDown()
    {
        bool qualifies = DetectQualifyingKeyDown();

        if (_anyKeyDelay > 0f)
        {
            _anyKeyDelay -= Time.unscaledDeltaTime;
            if (qualifies) _bufferedAnyKeyDuringDelay = true;
            return false;
        }

        if (_bufferedAnyKeyDuringDelay)
        {
            _bufferedAnyKeyDuringDelay = false;
            return true;
        }

        return qualifies;
    }

    private bool DetectQualifyingKeyDown()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) return false;

        // 键盘任意键（包含鼠标按钮）
        if (Input.anyKeyDown)
        {
            // 排除纯鼠标移动（没有按钮按下时 anyKeyDown 也可能因轴变化为 true）
            // 用玩家输入绑定检测手柄按钮，其他走 anyKeyDown
            if (_inputSettings != null)
            {
                // 先检测绑定的确认键
                if (_inputSettings.GetActionDown(SkyPrisonInputAction.Interact)) return true;
                if (_inputSettings.GetActionDown(SkyPrisonInputAction.Menu))     return true;
            }

            // 键盘/鼠标按键
            for (int i = (int)KeyCode.Backspace; i < (int)KeyCode.Mouse0; i++)
                if (Input.GetKeyDown((KeyCode)i)) return true;
            for (int i = (int)KeyCode.Mouse0; i <= (int)KeyCode.Mouse6; i++)
                if (Input.GetKeyDown((KeyCode)i)) return true;

            // 手柄按键——之前这段只扫了键盘/鼠标区间，JoystickButton0~19 完全没被
            // 检测过，即便 Input.anyKeyDown 因为手柄按键变成了 true，下面也永远找不到
            // 对应的键，导致手柄按下"任意键"毫无反应。
            for (int i = (int)KeyCode.JoystickButton0; i <= (int)KeyCode.JoystickButton19; i++)
                if (Input.GetKeyDown((KeyCode)i)) return true;
        }
        return false;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
