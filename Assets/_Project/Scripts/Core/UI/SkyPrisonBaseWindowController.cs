using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 所有游戏窗口的基类。
///
/// 子类只需：
///   1. 重写 WindowId    — 返回与 SkyPrisonUIPrefabMetadata_V1.uiId 一致的字符串
///   2. 重写 BuildHints  — 返回底部操作提示列表（可返回空数组）
///   3. 重写 OnWindowOpen / OnWindowClose — 窗口激活/关闭时的自定义逻辑（可选）
///   4. 在 Prefab 上把关闭按钮赋给 closeButton 字段（Inspector）
///
/// 不需要：自己找 Manager、注册提示条接口、写关闭逻辑。
/// </summary>
public abstract class SkyPrisonBaseWindowController : MonoBehaviour, ISkyPrisonWindowHintProvider
{
    [SerializeField] protected Button closeButton;

    // ── 子类必须实现 ──────────────────────────────────────────────────────

    /// <summary>与 SkyPrisonUIPrefabMetadata_V1.uiId 一致的唯一标识。</summary>
    protected abstract string WindowId { get; }

    /// <summary>底部操作提示列表。不需要提示时返回空数组即可。</summary>
    protected abstract IReadOnlyList<SkyPrisonWindowHint> BuildHints();

    // ── 子类可选重写 ──────────────────────────────────────────────────────

    /// <summary>窗口被打开、Awake 完成后调用。在这里初始化内容。</summary>
    protected virtual void OnWindowOpen() { }

    /// <summary>窗口关闭前调用（Close 发起时同步触发）。</summary>
    protected virtual void OnWindowClose() { }

    // ── ISkyPrisonWindowHintProvider ──────────────────────────────────────

    private IReadOnlyList<SkyPrisonWindowHint> _hints;
    public IReadOnlyList<SkyPrisonWindowHint> GetWindowHints()
        => _hints ??= BuildHints();

    // ── 生命周期 ──────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
            SkyPrisonUIButtonFeedback.Attach(closeButton.gameObject);
        }

        // 提示条内容如果用了 L(key, fallback) 查表本地化，语言切换后必须重建缓存，
        // 否则 _hints 一旦缓存过就再也不会刷新，永远停在打开那一刻的语言（背包
        // InventoryWindowController 之前自己重复实现过一遍这个逻辑，仓库
        // StashWindowController 完全没做，提示条常年停在中文——挪到基类统一做，
        // 以后所有继承这个基类的窗口都自动受益，不用每个子类各写一遍）。
        LocalizationRuntime.OnLanguageChanged += OnLanguageChangedInvalidateHints;

        OnWindowOpen();
    }

    protected virtual void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChangedInvalidateHints;

        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
    }

    private void OnLanguageChangedInvalidateHints(string _) => _hints = null;

    // ── 关闭 ──────────────────────────────────────────────────────────────

    public void Close()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        OnWindowClose();
        ResolveManager()?.Close(WindowId);
    }

    // ── 内部工具 ──────────────────────────────────────────────────────────

    private SkyPrisonWindowManager_V1 _manager;

    private SkyPrisonWindowManager_V1 ResolveManager()
    {
        if (_manager == null)
            _manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        return _manager;
    }
}
