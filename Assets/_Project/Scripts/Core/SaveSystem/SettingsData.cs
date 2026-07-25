using System;

/// <summary>
/// 游戏设置，独立于存档槽位持久化。
/// 写入 settings.json，与 save_auto.json 分开。
/// </summary>
[Serializable]
public class SettingsData
{
    // ── 音频 ──────────────────────────────────────────────────────────────
    public float masterVolume = 1f;
    public float musicVolume  = 1f;
    public float sfxVolume    = 1f;
    public float voiceVolume  = 1f;

    // ── 画面 ──────────────────────────────────────────────────────────────
    public int   resolutionWidth  = 1920;
    public int   resolutionHeight = 1080;
    public bool  fullscreen       = true;
    public int   windowMode       = 1;   // 0=窗口 1=全屏 2=无边框全屏，跟 fullscreen 字段保持同步（历史字段，别的地方可能还在读）
    public int   qualityLevel     = 2;   // 对应 Unity QualitySettings index
    public bool  vsync            = true;
    public int   targetFrameRate  = 60;
    public float brightness       = 1f;
    public bool  motionBlur       = false;
    public bool  chromaticAberration = false;
    public bool  showFps          = false;
    public int   antialiasingMode = 1;   // 0=关 1=FXAA 2=TAA，对应 SkyPrisonAntialiasingApplier

    // ── 语言 ──────────────────────────────────────────────────────────────
    public string languageCode = "zh-CN";

    // ── 游戏玩法 ──────────────────────────────────────────────────────────
    public float mouseSensitivity   = 1f;
    public bool  showDamageNumbers  = true;
    public bool  screenShake        = true;
    public bool  harmonyMode        = false;   // 和谐模式：隐藏血腥特效
    public bool  autoSaveEnabled    = true;    // 关掉后 SaveManager.Save() 只在玩家手动存档时才落盘

    // ── 手柄 ──────────────────────────────────────────────────────────────
    public float gamepadDeadzone    = 0.25f;   // 同步到 PlayerAimFacingController.GamepadDeadZone
    public float vibrationStrength  = 1f;      // 同步到 SkyPrisonGamepadRumble.Strength，0=关震动
}
