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

    /// <summary>语音语言——独立于文字语言的设置，允许文字看中文、语音听日文这种组合。</summary>
    public string voiceLanguageCode = "zh-CN";

    // ── 游戏玩法 ──────────────────────────────────────────────────────────
    public float mouseSensitivity   = 1f;
    public bool  showDamageNumbers  = true;
    public bool  screenShake        = true;
    public bool  harmonyMode        = false;   // 和谐模式：隐藏血腥特效
    public bool  autoSaveEnabled    = true;    // 关掉后 SaveManager.Save() 只在玩家手动存档时才落盘
    public int   sellConfirmRarityThreshold = 5; // 商店出售稀有物品二次确认阈值：itemLevel>=此值弹二次确认；-1=不提示

    /// <summary>台本对话自动播放。true=没有真正分支选项时自动按文字/语音时长推进；
    /// false(默认)=每句都要玩家手动点选项才推进。出现2个以上分支选项时无论如何都会
    /// 停下来等玩家手动选，不会自动帮玩家决定剧情走向。</summary>
    public bool  dialogueAutoPlay = false;

    // ── 手柄 ──────────────────────────────────────────────────────────────
    public float gamepadDeadzone    = 0.25f;   // 同步到 PlayerAimFacingController.GamepadDeadZone
    public float vibrationStrength  = 1f;      // 同步到 SkyPrisonGamepadRumble.Strength，0=关震动
}
