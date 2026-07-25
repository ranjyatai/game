using System.Collections.Generic;
using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 玩家 HUD 左下角状态图标条。
/// 由 SkyPrisonRuntimeSystemsBootstrapper 自动生成，不需要手动挂载。
/// 复用 UnitStatusIconView 对象池，刷新逻辑与头顶 UI 保持一致。
///
/// 直接挂在真正的 PlayerStatusArea（HP/LP血条区域）底下当子物体，不再单独建一个
/// DontDestroyOnLoad的Canvas去用独立的CanvasScaler+锚点数值"对齐"另一个Canvas——
/// 两个Canvas各自的参考分辨率/缩放模式哪怕设成一样，Windows系统缩放不是100%时
/// Build里读到的Screen尺寸也可能跟Editor Game窗口模拟出来的不一致，两边各自算出
/// 来的锚点换算结果就会跑偏，这个坑今天踩了一整天。现在图标条是PlayerStatusArea
/// 的真实子物体，共用同一个Canvas、同一套缩放，不存在"两边对不齐"这件事了。
/// </summary>
[DisallowMultipleComponent]
public class PlayerHUDStatusIconBar : MonoBehaviour
{
    [Header("布局（相对 PlayerStatusArea 左上角）")]
    [SerializeField] private float iconSize    = 70f;
    [SerializeField] private float iconGap     = 6f;
    [SerializeField] private float marginLeft  = 210f;
    [SerializeField] private float marginTop   = -40f; // 离 PlayerStatusArea 顶边往下的距离，负数=往上
    [SerializeField] private int   maxIconPool = 24;

    [Header("调试")]
    [SerializeField] private bool debugLogs            = false;
    [SerializeField] private bool showTestPlaceholders = false;
    [SerializeField] private int  testPlaceholderCount = 3;

    // ── 内部 ──────────────────────────────────────────────────────────────

    private UnitStatusController          _statusController;
    private RectTransform                 _contentRoot;
    private RectTransform                 _playerStatusArea;
    private readonly List<UnitStatusIconView> _pool = new List<UnitStatusIconView>();
    private readonly List<StatusViewData>     _visibleBuffer = new List<StatusViewData>();

    private float _searchCooldown;
    private float _hudSearchCooldown;
    private const float SearchInterval = 1.5f;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        TryFindPlayer();
    }

    private void OnEnable()
    {
        if (_statusController != null)
            _statusController.StatusesChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (_statusController != null)
            _statusController.StatusesChanged -= Refresh;
    }

    private void Update()
    {
        if (_statusController == null)
        {
            _searchCooldown -= Time.unscaledDeltaTime;
            if (_searchCooldown <= 0f) { TryFindPlayer(); _searchCooldown = SearchInterval; }
        }

        if (_contentRoot == null)
        {
            _hudSearchCooldown -= Time.unscaledDeltaTime;
            if (_hudSearchCooldown <= 0f) { TryAttachToPlayerStatusArea(); _hudSearchCooldown = SearchInterval; }
        }
    }

    // ── 挂到真正的 PlayerStatusArea 底下 ─────────────────────────────────

    private void TryAttachToPlayerStatusArea()
    {
        var driver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
        GameObject hud = driver != null ? driver.HudInstance : null;
        if (hud == null)
        {
            if (debugLogs) Debug.Log("[HUDStatusBar] HudInstance 还没建好，继续等待重试。", this);
            return;
        }

        Transform t = FindChildRecursive(hud.transform, "PlayerStatusArea");
        if (t == null)
        {
            if (debugLogs) Debug.LogWarning("[HUDStatusBar] 找不到 PlayerStatusArea，继续等待重试。", this);
            return;
        }

        _playerStatusArea = t as RectTransform;
        if (_playerStatusArea == null) return;

        var containerGo = new GameObject("StatusIconContent", typeof(RectTransform));
        containerGo.transform.SetParent(_playerStatusArea, false);
        _contentRoot = containerGo.GetComponent<RectTransform>();
        // 直接贴 PlayerStatusArea 的左上角，两者是同一个Canvas下的父子关系，不用管
        // 缩放/参考分辨率，Unity自己会算对。
        _contentRoot.anchorMin        = new Vector2(0f, 1f);
        _contentRoot.anchorMax        = new Vector2(0f, 1f);
        _contentRoot.pivot            = new Vector2(0f, 1f);
        _contentRoot.anchoredPosition = new Vector2(marginLeft, -marginTop);
        _contentRoot.sizeDelta        = new Vector2(800f, iconSize);

        // 色收差：直接复用 PlayerStatusArea 自己所在模块已经有的渲染/后处理设置——
        // 既然现在是它的真实子物体，它父级模块上的 SkyPrisonHUDModulePostProcessRenderer_V87
        // （如果有）本来就会把子物体一起渲染进去，不用再单独 Attach 一份。

        if (debugLogs) Debug.Log("[HUDStatusBar] 已挂到 PlayerStatusArea 底下。", this);

        // 重新触发一次排布，把已经存在的图标摆到新建好的 _contentRoot 里。
        Refresh();
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    // ── 玩家查找 ──────────────────────────────────────────────────────────

    private void TryFindPlayer()
    {
        foreach (var id in FindObjectsOfType<SkyPrisonUnitRuntimeIdentity>())
        {
            if (!id.IsRuntimePlayer) continue;
            var sc = id.GetComponent<UnitStatusController>()
                  ?? id.GetComponentInChildren<UnitStatusController>();
            if (sc == null) continue;

            if (_statusController != null)
                _statusController.StatusesChanged -= Refresh;

            _statusController = sc;
            _statusController.StatusesChanged += Refresh;
            Refresh();

            if (debugLogs) Debug.Log($"[HUDStatusBar] 绑定玩家状态控制器: {sc.gameObject.name}", this);
            return;
        }
    }

    // ── 刷新（与头顶 UI 对齐的逻辑） ─────────────────────────────────────

    private void Refresh()
    {
        if (_contentRoot == null) return;
        if (showTestPlaceholders) { RefreshTest(); return; }
        if (_statusController == null) return;

        FillVisibleBuffer(_statusController.VisibleStatuses);

        int visibleCount = _visibleBuffer.Count;
        EnsurePool(visibleCount);

        // 全部先 Reset + 隐藏
        for (int i = 0; i < _pool.Count; i++)
        {
            _pool[i].ResetView();
            _pool[i].gameObject.SetActive(false);
        }

        if (visibleCount == 0) return;

        // 逐个 Apply
        for (int i = 0; i < visibleCount; i++)
        {
            UnitStatusIconView view = _pool[i];
            view.gameObject.SetActive(true);
            view.EnsureStructure();
            view.Apply(_visibleBuffer[i], StatusDisplayDirection.TopToBottom);
            PositionSlot(view.GetComponent<RectTransform>(), i);
        }

        if (debugLogs) Debug.Log($"[HUDStatusBar] 显示 {visibleCount} 个状态", this);
    }

    private void FillVisibleBuffer(IReadOnlyList<StatusViewData> statuses)
    {
        _visibleBuffer.Clear();
        if (statuses == null) return;
        int cap = maxIconPool > 0 ? maxIconPool : int.MaxValue;
        for (int i = 0; i < statuses.Count; i++)
        {
            StatusViewData d = statuses[i];
            if (!d.visible || d.icon == null) continue;
            if (!d.hideDurationMask && d.durationFillMode != StatusDurationFillMode.None && d.remainingNormalized <= 0.001f) continue;
            _visibleBuffer.Add(d);
            if (_visibleBuffer.Count >= cap) break;
        }
    }

    private void EnsurePool(int need)
    {
        if (_contentRoot == null) return;

        // 移除空引用
        for (int i = _pool.Count - 1; i >= 0; i--)
            if (_pool[i] == null) _pool.RemoveAt(i);

        int target = Mathf.Clamp(Mathf.Max(1, need), 1, Mathf.Max(1, maxIconPool));
        while (_pool.Count < target)
        {
            var go = new GameObject($"StatusIcon_{_pool.Count}", typeof(RectTransform), typeof(UnitStatusIconView));
            go.transform.SetParent(_contentRoot, false);
            var view = go.GetComponent<UnitStatusIconView>();
            view.EnsureStructure();
            // 固定尺寸
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(iconSize, iconSize);
            _pool.Add(view);
        }
    }

    private void PositionSlot(RectTransform rt, int index)
    {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta        = new Vector2(iconSize, iconSize);
        rt.anchoredPosition = new Vector2(index * (iconSize + iconGap), 0f);
    }

    // ── 测试占位 ──────────────────────────────────────────────────────────

    private void RefreshTest()
    {
        int count = Mathf.Clamp(testPlaceholderCount, 0, maxIconPool);
        EnsurePool(count);

        for (int i = 0; i < _pool.Count; i++)
        {
            bool show = i < count;
            _pool[i].gameObject.SetActive(show);
            if (show) PositionSlot(_pool[i].GetComponent<RectTransform>(), i);
        }
    }

    // ── 静态工厂 ─────────────────────────────────────────────────────────

    public static PlayerHUDStatusIconBar EnsureInScene()
    {
        if (FindObjectOfType<PlayerHUDStatusIconBar>() is { } existing) return existing;
        var go = new GameObject("[PlayerHUDStatusIconBar_Runtime]");
        DontDestroyOnLoad(go);
        return go.AddComponent<PlayerHUDStatusIconBar>();
    }
}
