using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 主界面可配置参数。
/// 创建方式：Project 面板右键 → Sky Prison → Main Menu Settings
/// 路径约定：Assets/_Project/Resources/MainMenuSettings.asset
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Main Menu Settings", fileName = "MainMenuSettings")]
public class MainMenuSettings : ScriptableObject
{
    [Header("LOGO")]
    [Tooltip("主界面 LOGO 图片（直接拖 PNG 即可）。留空则隐藏。")]
    public Texture2D logoTexture;

    [Tooltip("LOGO 距屏幕中心的垂直偏移（正值往上）")]
    public float logoOffsetY = 580f;

    [Header("背景音乐")]
    [Tooltip("主界面 BGM（AudioClip）。留空则无音乐。")]
    public AudioClip bgm;

    [Tooltip("BGM 音量（0~1）")]
    [Range(0f, 1f)]
    public float bgmVolume = 0.8f;

    [Tooltip("BGM 是否循环")]
    public bool bgmLoop = true;

    [Header("LIVE2D 角色")]
    [Tooltip("主界面 LIVE2D 角色 Prefab。留空则不显示。")]
    public GameObject live2dPrefab;

    [Tooltip("LIVE2D 在屏幕上的锚点位置（0~1，左下角为原点）")]
    public Vector2 live2dAnchor = new Vector2(0.5f, 0f);

    [Tooltip("LIVE2D 缩放倍数")]
    public float live2dScale = 1f;

    [Header("AE 特效 / 背景动画")]
    [Tooltip("AE 导出的动画 Prefab（如 Bodymovin/Lottie 或自定义序列帧 Prefab）。留空则不显示。")]
    public GameObject aePrefab;

    [Tooltip("背景视频文件（VideoClip）。与 AE Prefab 二选一，优先使用 Prefab。")]
    public VideoClip backgroundVideo;

    [Tooltip("背景视频是否循环")]
    public bool videoLoop = true;

    [Header("版本号")]
    [Tooltip("留空则自动使用 Application.version")]
    public string versionOverride = "";

    [Header("版权文字")]
    [Multiline(2)]
    public string copyrightText = "© 2025 SkyPrison Dev. All rights reserved.";

    // 由 MainMenuSettingsWindow 专属区块编辑，Inspector 不重复显示
    [HideInInspector] public string  newGameScene         = "Still Vault";
    [HideInInspector] public Vector3 newGameSpawnPosition = Vector3.zero;
    [HideInInspector] public float   newGameSpawnRotationY = 0f;

    [Header("鼠标图标")]
    [Tooltip("自定义鼠标图标——留空则用系统默认光标(会有CursorLockMode.Confined只卡住\n" +
             "热点、图标本身超出屏幕边缘会被裁切/露出的问题)。填了这个之后系统光标会被\n" +
             "隐藏，改成用这张图跟着鼠标位置走的UI图标，整张图的范围会被卡在屏幕内，\n" +
             "不会露出/裁切。")]
    public Sprite cursorIconSprite;

    [Tooltip("光标图标大小——固定像素，不跟着分辨率/UI缩放等比放大（跟系统鼠标一样，\n" +
             "任何分辨率下看起来都是同样大小）。")]
    public float cursorIconSize = 32f;

    [Tooltip("光标热点在图标里的位置(0,0)=左上角 (1,1)=右下角。大多数指针类图标热点在\n" +
             "尖端，通常是左上角附近。")]
    public Vector2 cursorHotspot01 = new Vector2(0.0938f, 0.1953f); // 128图，尖端在(12,25)像素

    [Header("本地化表引用")]
    [Tooltip("主菜单按钮文字的本地化表。留空则使用代码内置中文。")]
    public UILocalizationTable localizationTable;

    // ── 便捷属性 ─────────────────────────────────────────────────────────

    public string ResolvedVersion =>
        string.IsNullOrEmpty(versionOverride)
            ? $"Ver. {Application.version}"
            : $"Ver. {versionOverride}";
}
