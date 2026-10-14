using UnityEngine;

/// <summary>
/// 夜视仪模式：世界画面变成高对比度的黄绿单色（自动增益的感觉），同时让战争迷雾暂时失效。
///
/// 画面调色由 <see cref="SkyPrisonNightVisionRendererFeature"/> 完成：它插在 Main Camera 画完之后、
/// Overlay 相机开始画之前，只处理"世界画面"，头顶 UI（名字、血条、任务图标）和战术识别框画在它后面，
/// 保持原色。这里只负责开关状态、淡入淡出的权重，以及调色参数。
/// 之前用过全局 Volume 做调色，Volume 的后处理在栈尾统一跑，会连头顶 UI 一起染掉，所以弃用。
///
/// 迷雾部分：<see cref="IsActive"/> 为真时，SkyPrisonFogMaskRenderer 隐藏迷雾遮罩，
/// SkyPrisonVisionManager.ShouldHideUnitFromPlayer 不再隐藏敌方单位。只改"玩家看到什么"，
/// 不改 AI 的视线判定（CanUnitSeeUnit），所以敌人不会因此变得看不见玩家或看得更清楚。
///
/// 测试键 F6（仅编辑器 / Development Build），走 <see cref="TryPlayerToggle"/>：忙的时候（对话 / 接任务 /
/// 购买等窗口开着）开关不了。正式入口（装备、技能、道具）接上后，玩家主动操作调 TryPlayerToggle，
/// 代码强制开关调 <see cref="SetActive"/>。
/// </summary>
public static class SkyPrisonNightVisionMode
{
    private const float FadeSpeed = 6f;

    // ---- 调色参数（给 SkyPrisonNightVisionRendererFeature 读）----
    // 真实夜视仪不是"整体变亮"，而是自动增益把对比度拉开：弱对比的细节被放大、亮部发绿发亮，暗部也不是死黑。

    /// <summary>曝光，EV（每 +1 亮一倍）。注意这条通道处理的是已经压成 8 位的画面，不是高动态范围，
    /// 所以数值比 Volume 里的 PostExposure 小得多（+1 以上整张图就糊成一片了）。</summary>
    public static float Exposure = 0.45f;

    /// <summary>以中灰为轴的幂函数对比度指数，1 = 不变，越大对比越强。</summary>
    public static float ContrastPower = 1.28f;

    /// <summary>对比度的轴：高于它的更亮、低于它的更暗。这条通道处理的是已经压成 8 位的画面，轴要放在场景常见亮度附近。</summary>
    public static float ContrastPivot = 0.25f;

    /// <summary>中间调抬亮，暗部细节不被对比度压没。</summary>
    public static float GammaLift = 0.18f;

    /// <summary>单色化程度，1 = 完全单色。世界画面可以满单色——UI 不在这条通道里，颜色不受影响。</summary>
    public static float MonoStrength = 1f;

    /// <summary>单色绿：R / G / B 输出增益。B 比 R 高一点，绿色就偏冷（偏青）；R 比 B 高则偏黄绿。</summary>
    public static Vector4 MonoTint = new Vector4(0.20f, 1.45f, 0.28f, 1f);

    public static float VignetteIntensity = 0.30f;
    public static float GrainIntensity = 0.35f;

    // ---- 电量 ----
    // 开着耗电，关着慢慢回充；耗尽自动关机，并强制冷却到 RelockCharge 才能再开。战略家科技树强化容量（超容电池）和回充速度。
    public const string UnlockNodeId = "strategist_night_vision";
    public const string CapacityNodeId = "strategist_nv_capacity";
    public const string RechargeNodeId = "strategist_nv_recharge";
    public const float BaseCapacitySeconds = 60f;
    public const float CapacityPerLevelSeconds = 30f;      // 超容电池每级 +30 秒
    public const float BaseRechargeRatio = 0.25f;          // 回充速度 = 耗电速度的 1/4（空电充满 = 续航的 4 倍）
    public static readonly float[] RechargeRatioByLevel = { 0.25f, 0.4f, 0.6f, 0.85f };
    public const float RechargeDelaySeconds = 1f;          // 关机后先等一秒再回充，防止狂按开关刷电
    public const float RelockCharge = 0.2f;                // 耗尽后要回到这个电量才能再开
    public const float LowCharge = 0.2f;

    /// <summary>测试期：没点科技树节点也能用夜视（编辑器 / Development Build）。正式发布前自然失效。</summary>
    public static bool DebugForceUnlock = false;

    private static bool _depletedShutdown;
    private static float _charge = 1f;
    private static bool _depleted;
    private static float _lastOffTime = -10f;

    /// <summary>电量 0~1。</summary>
    public static float Charge => _charge;
    /// <summary>耗尽后的强制冷却中（电量还没回到 RelockCharge）。</summary>
    public static bool Depleted => _depleted;

    /// <summary>夜视装置有没有搭载（科技树节点点了 / 测试期强制）。</summary>
    public static bool Unlocked =>
        (DebugForceUnlock && (Application.isEditor || Debug.isDebugBuild)) || TechTreeRuntime.SavedLevel(UnlockNodeId) >= 1;

    public static float CapacitySeconds => BaseCapacitySeconds + CapacityPerLevelSeconds * TechTreeRuntime.SavedLevel(CapacityNodeId);

    public static float RechargeRatio
    {
        get
        {
            int lv = Mathf.Clamp(TechTreeRuntime.SavedLevel(RechargeNodeId), 0, RechargeRatioByLevel.Length - 1);
            return RechargeRatioByLevel[lv];
        }
    }

    /// <summary>电池图标要不要显示：已搭载，并且开着 / 没充满 / 在冷却。</summary>
    public static bool BatteryVisible => Unlocked && (_active || _charge < 0.999f || _depleted);

    private static bool _active;
    private static float _weight;
    private static GameObject _tickerObject;

    /// <summary>夜视仪是否开启（目标状态，淡出过程中已经是 false）。</summary>
    public static bool IsActive => _active;

    /// <summary>当前画面混合权重 0~1（含淡入淡出），调色通道和 HUD 用它跟着一起出现 / 消失。</summary>
    public static float Weight => _weight;

    /// <summary>
    /// 玩家能不能手动开关夜视仪。接任务、和 NPC 对话、购买、背包 / 仓库 / 角色面板 / 任务日志 /
    /// 暂停菜单 / 设置这类"在忙"的窗口开着时不允许开关——这些窗口都会让
    /// SkyPrisonWindowManager_V1.AnyBlockingWindowOpen 为真（含关窗动画期间），
    /// 全模态窗口（商店等）另外还会锁移动。已经开着的夜视仪不会被强制关掉，只是这段时间切不了。
    /// 代码驱动的 <see cref="SetActive"/>（装备卸下、电量耗尽等）不受这个闸限制。
    /// </summary>
    public static bool CanPlayerToggle =>
        !SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.AnyBlockingWindowOpen
        && !SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.AnyMovementLockingWindowOpen;

    /// <summary>玩家输入触发的开关（热键 / 将来的装备按钮）。被"在忙"闸挡住时返回 false 并响一声禁止音。</summary>
    public static bool TryPlayerToggle()
    {
        // 没搭载 / 冷却中 / 没电：开不了（关得了）
        if (!_active && (!Unlocked || _depleted || _charge <= 0.001f))
        {
            DenyBeep();
            return false;
        }

        if (!CanPlayerToggle)
        {
            DenyBeep();
            return false;
        }

        SetActive(!_active);
        return true;
    }

    private static void DenyBeep()
    {
        if (Time.unscaledTime - _lastDeniedSeTime > 0.4f)
        {
            _lastDeniedSeTime = Time.unscaledTime;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        }
    }

    public static void Toggle() => SetActive(!_active);

    private static float _lastDeniedSeTime = -10f;

    public static void SetActive(bool active)
    {
        if (_active == active)
            return;

        if (!active)
            _lastOffTime = Time.time;
        _active = active;
        EnsureRuntime();
        if (Application.isPlaying)
        {
            if (active) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.NightVisionOn);
            else if (!_depletedShutdown) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.NightVisionOff);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 关掉 Domain Reload 进 Play 时，静态字段会残留上一轮的值。
        _active = false;
        _weight = 0f;
        _charge = 1f;
        _depleted = false;
        _lastOffTime = -10f;

        // 常驻物体带 HideAndDontSave，会活过 Play 结束；静态引用清成 null 之后下一次 EnsureRuntime 又会再建一个，
        // 实测积了 20 多个，每个都有一份 F6 监听——按一次键被处理多次，开关互相抵消。这里先把旧的清掉。
        if (_tickerObject != null)
            Object.DestroyImmediate(_tickerObject);
        foreach (var t in Resources.FindObjectsOfTypeAll<Ticker>())
        {
            if (t != null)
                Object.DestroyImmediate(t.gameObject);
        }
        _tickerObject = null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallDebugHotkey() => EnsureRuntime();
#endif

    private static void EnsureRuntime()
    {
        if (_tickerObject != null)
            return;

        _tickerObject = new GameObject("[SkyPrisonNightVision]") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(_tickerObject);
        _tickerObject.AddComponent<Ticker>();
        _tickerObject.AddComponent<SkyPrisonNightVisionHud>();
        _tickerObject.AddComponent<SkyPrisonNightVisionBatteryHud>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedRefill;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedRefill;
    }

    private static void UpdateBattery(float dt)
    {
        if (dt <= 0f)
            return;

        float cap = Mathf.Max(1f, CapacitySeconds);
        if (_active)
        {
            _charge -= dt / cap;
            if (_charge <= 0f)
            {
                _charge = 0f;
                _depleted = true;
                _depletedShutdown = true;
                SetActive(false);          // 电量耗尽：自动关机(这一下播「耗尽」，不播普通的「关闭」)
                _depletedShutdown = false;
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.NightVisionDepleted);
            }
        }
        else if (_charge < 1f && Time.time - _lastOffTime >= RechargeDelaySeconds)
        {
            _charge = Mathf.Min(1f, _charge + dt * RechargeRatio / cap);
        }

        if (_depleted && _charge >= RelockCharge)
            _depleted = false;
    }

    private static void OnSceneLoadedRefill(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        _charge = 1f;          // 每次进图满电出发，不跨局保留
        _depleted = false;
    }

    private class Ticker : MonoBehaviour
    {
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F6))
                TryPlayerToggle();
#endif
            _weight = Mathf.MoveTowards(_weight, _active ? 1f : 0f, FadeSpeed * Time.unscaledDeltaTime);
            UpdateBattery(Time.deltaTime);
        }
    }
}
