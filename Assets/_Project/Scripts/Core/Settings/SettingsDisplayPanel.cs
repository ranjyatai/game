using UnityEngine;

/// <summary>
/// 画面设置子面板。
/// 读写 SaveManager.Settings 中画面相关字段。
/// 视觉控件（Dropdown/Toggle/Slider）等做 UI 时再绑定。
/// </summary>
public class SettingsDisplayPanel : MonoBehaviour
{
    // ── 运行时暂存（未点确认前不写入 Settings）────────────────────────────
    private int    _qualityLevel;
    private bool   _fullscreen;
    private bool   _vsync;
    private int    _targetFrameRate;
    private float  _brightness;

    // ── 后处理开关（游戏特有，可按需增删）────────────────────────────────
    private bool   _chromaticAberration;
    private bool   _screenShake;        // 重复存在 Gameplay，这里只管画面震动
    private bool   _motionBlur;

    public void SetVisible(bool v) => gameObject.SetActive(v);

    /// <summary>从 Settings 刷新本地暂存值。UI 控件绑定后在这里更新显示。</summary>
    public void Refresh()
    {
        var s = SaveManager.Settings;
        if (s == null) return;

        _qualityLevel    = s.qualityLevel;
        _fullscreen      = s.fullscreen;
        _vsync           = s.vsync;
        _targetFrameRate = s.targetFrameRate;
        _brightness      = s.brightness;
        _motionBlur      = s.motionBlur;

        RefreshUI();
    }

    /// <summary>将暂存值写入 Settings（不写磁盘，由 SettingsPanel.ApplyAndClose 统一写）。</summary>
    public void Apply()
    {
        var s = SaveManager.Settings;
        if (s == null) return;

        s.qualityLevel    = _qualityLevel;
        s.fullscreen      = _fullscreen;
        s.vsync           = s.vsync;
        s.targetFrameRate = _targetFrameRate;
        s.brightness      = _brightness;
        s.motionBlur      = _motionBlur;

        ApplyToEngine();
    }

    // ── 供 UI 控件调用的 Setter ────────────────────────────────────────────

    public void SetQualityLevel(int level)    => _qualityLevel    = level;
    public void SetFullscreen(bool v)         => _fullscreen      = v;
    public void SetVsync(bool v)              => _vsync           = v;
    public void SetTargetFrameRate(int fps)   => _targetFrameRate = fps;
    public void SetBrightness(float v)        => _brightness      = Mathf.Clamp01(v);
    public void SetMotionBlur(bool v)         => _motionBlur      = v;

    // ── 内部 ──────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        // TODO: 画面 Tab 做好后，把 Dropdown/Toggle/Slider 的值同步到这里
    }

    private void ApplyToEngine()
    {
        QualitySettings.SetQualityLevel(_qualityLevel, applyExpensiveChanges: true);
        Screen.fullScreen = _fullscreen;
        QualitySettings.vSyncCount = _vsync ? 1 : 0;
        Application.targetFrameRate = _targetFrameRate;
        // brightness / motionBlur 需要接后处理 Volume，等渲染管线接入时补
    }
}
