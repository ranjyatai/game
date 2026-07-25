using UnityEngine;

/// <summary>
/// 游戏玩法设置子面板。
/// 读写 SaveManager.Settings 中玩法/系统相关字段。
/// </summary>
public class SettingsGameplayPanel : MonoBehaviour
{
    private string _languageCode;
    private bool   _showDamageNumbers;
    private bool   _screenShake;
    private bool   _harmonyMode;
    private float  _mouseSensitivity;

    public void SetVisible(bool v) => gameObject.SetActive(v);

    public void Refresh()
    {
        var s = SaveManager.Settings;
        if (s == null) return;

        _languageCode      = s.languageCode;
        _showDamageNumbers = s.showDamageNumbers;
        _screenShake       = s.screenShake;
        _harmonyMode       = s.harmonyMode;
        _mouseSensitivity  = s.mouseSensitivity;

        RefreshUI();
    }

    public void Apply()
    {
        var s = SaveManager.Settings;
        if (s == null) return;

        s.languageCode      = _languageCode;
        s.showDamageNumbers = _showDamageNumbers;
        s.screenShake       = _screenShake;
        s.harmonyMode       = _harmonyMode;
        s.mouseSensitivity  = _mouseSensitivity;

        ApplyToRuntime();
    }

    // ── 供 UI 调用 ────────────────────────────────────────────────────────

    public void SetLanguage(string code)         => _languageCode      = code;
    public void SetShowDamageNumbers(bool v)     => _showDamageNumbers = v;
    public void SetScreenShake(bool v)           => _screenShake       = v;
    public void SetHarmonyMode(bool v)           => _harmonyMode       = v;
    public void SetMouseSensitivity(float v)     => _mouseSensitivity  = Mathf.Clamp(v, 0.1f, 10f);

    /// <summary>清除游戏缓存（截图、地图缓存等），供按钮调用。</summary>
    public void ClearCache()
    {
        GamePaths.ClearCache();
        OnCacheCleared();
    }

    // ── 内部 ──────────────────────────────────────────────────────────────

    private void ApplyToRuntime()
    {
        // 语言切换通知 LocalizationRuntime（如果已接入）
        // LocalizationRuntime.SetLanguage(_languageCode);

        // 鼠标灵敏度由 InputSettings 使用，这里只存值
        // harmonyMode 由血液特效系统读取
    }

    private void RefreshUI()
    {
        // TODO: Toggle/Slider 绑定后在这里同步控件显示
    }

    private void OnCacheCleared()
    {
        // TODO: 弹出「缓存已清除」提示，显示释放了多少空间
        string summary = GamePaths.GetStorageSummary();
        Debug.Log($"[Settings] 缓存已清除。{summary}");
    }
}
