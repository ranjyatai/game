using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    public enum SkyPrisonHUDBlendModeV1
    {
        Normal = 0,
        Multiply = 1,
        Add = 2,
        Subtract = 3,
        SoftLight = 4,
        HardLight = 5,
    }

    [CreateAssetMenu(fileName = "SkyPrisonHUDStyleProfile_V1", menuName = "Sky Prison/UI/HUD Style Profile V1", order = 2201)]
    public sealed class SkyPrisonHUDStyleProfile_V1 : ScriptableObject
    {
        [Header("Sprites - Base")]
        public Sprite basePlateSprite;
        public Sprite scanLineSprite;
        public Sprite noiseSprite;
        public Sprite accentLineSprite;

        [Header("Sprites - Quick Slot")]
        public Sprite quickSlotBackSprite;
        public Sprite quickSlotFrameSprite;
        public Sprite quickSlotSelectedFrameSprite;
        public Sprite quickSlotCooldownMaskSprite;

        [Header("Sprites - HP Bar Resource Split")]
        [Tooltip("HP 空槽 / 底色。固定显示，不参与扣血裁切。")]
        public Sprite hpBarBackSprite;
        [Tooltip("HP 外框。固定显示，不参与扣血裁切。")]
        public Sprite hpBarFrameSprite;
        [Tooltip("HP 当前血量填充图。图片本体保持完整，由 HpFillMaskRoot 裁切显示。")]
        public Sprite hpBarFillSprite;
        [Tooltip("HP 虚血 / 延迟损失血量填充图。图片本体保持完整，由 HpDamageLagMaskRoot 裁切显示。")]
        public Sprite hpDamageLagFillSprite;
        [Tooltip("可选：HP 当前血量末端高光 / 端点。第一阶段可为空。")]
        public Sprite hpFillEndCapSprite;

        [Header("Sprites - LP Bar Resource Split")]
        [Tooltip("LP 空槽 / 底色。固定显示。")]
        public Sprite lpBarBackSprite;
        [Tooltip("LP 外框。固定显示。")]
        public Sprite lpBarFrameSprite;
        [Tooltip("LP 当前负荷填充图。图片本体保持完整，由 LpFillMaskRoot 裁切显示。")]
        public Sprite lpBarFillSprite;
        [Tooltip("可选：LP 残影 / Ghost 填充图。默认不启用。")]
        public Sprite lpGhostFillSprite;

        [Header("Materials - Legacy Slots")]
        [Tooltip("默认 UI 材质。用于未指定模块材质时的回退。")]
        public Material defaultUIMaterial;
        public Material glassPanelMaterial;
        public Material slotFrameMaterial;
        public Material barFrameMaterial;
        public Material hpFillMaterial;
        public Material hpDamageLagMaterial;
        public Material lpFillMaterial;
        public Material scanlineMaterial;
        public Material warningFlashMaterial;

        [Header("Materials - Global / Module Blend V54")]
        [Tooltip("全局 UI 合成方式。会作为未指定模块合成方式时的回退。SoftLight / HardLight 已接入 V54 的 UGUI 稳定近似合成。真正 CSP 级背景采样版后续走模块 RT 合成。")]
        public SkyPrisonHUDBlendModeV1 globalUIBlendMode = SkyPrisonHUDBlendModeV1.Normal;
        [Tooltip("全局 UI 材质。留空时使用 defaultUIMaterial；再留空则使用 Unity 默认 UI 材质。")]
        public Material globalUIMaterial;
        [Tooltip("玩家状态是否覆盖全局合成方式。关闭时使用全局 UI 合成方式。")]
        public bool playerStatusOverrideGlobalBlend = false;
        [Tooltip("玩家状态模块合成方式。只有开启覆盖全局合成方式时才使用。")]
        public SkyPrisonHUDBlendModeV1 playerStatusBlendMode = SkyPrisonHUDBlendModeV1.Normal;
        [Tooltip("玩家状态是否覆盖全局材质。关闭时使用全局 UI 材质。")]
        public bool playerStatusOverrideGlobalMaterial = false;
        [Tooltip("玩家状态模块材质。只有开启覆盖全局材质时才使用；留空时回退 defaultUIMaterial。")]
        public Material playerStatusModuleMaterial;
        [Tooltip("快捷栏是否覆盖全局合成方式。关闭时使用全局 UI 合成方式。")]
        public bool quickSlotsOverrideGlobalBlend = false;
        [Tooltip("快捷栏模块合成方式。只有开启覆盖全局合成方式时才使用。")]
        public SkyPrisonHUDBlendModeV1 quickSlotsBlendMode = SkyPrisonHUDBlendModeV1.Normal;
        [Tooltip("快捷栏是否覆盖全局材质。关闭时使用全局 UI 材质。")]
        public bool quickSlotsOverrideGlobalMaterial = false;
        [Tooltip("快捷栏模块材质。只有开启覆盖全局材质时才使用；留空时回退 defaultUIMaterial。")]
        public Material quickSlotsModuleMaterial;
        [Tooltip("是否把模块材质也套到 TMP 文本。V54 起：非 Normal 合成方式会自动作用到 TMP；此开关用于 Normal 状态下也强制套模块材质。")]
        public bool applyModuleMaterialToTMPText = true;

        [Header("Global Fonts")]
        [Tooltip("全局汉字 / 普通文字字体。局部未指定时使用。")]
        public TMP_FontAsset mainFont;
        [Tooltip("全局英文 / 数字字体。局部未指定时使用。")]
        public TMP_FontAsset numberFont;
        [Tooltip("全局标签字体。局部未指定时使用。")]
        public TMP_FontAsset labelFont;

        [Header("Global Text Colors")]
        [Tooltip("全局汉字 / 普通文字颜色。")]
        public Color globalTextColor = Color.white;
        [Tooltip("全局英文 / 数字颜色。")]
        public Color globalNumberColor = Color.white;
        [Tooltip("全局标签颜色。")]
        public Color globalLabelColor = Color.white;

        [Header("Global Text Aspect")]
        [Tooltip("全局汉字 / 普通文字横向比例。小于 1 为窄字，大于 1 为宽字。局部未覆盖时使用。")]
        [Range(0.55f, 3.00f)] public float globalTextWidthScale = 1.0f;
        [Tooltip("全局汉字 / 普通文字纵向比例。小于 1 为扁字，大于 1 为高字。局部未覆盖时使用。")]
        [Range(0.55f, 3.00f)] public float globalTextHeightScale = 1.0f;
        [Tooltip("全局英文 / 数字横向比例。小于 1 为窄数字。")]
        [Range(0.55f, 3.00f)] public float globalNumberWidthScale = 1.0f;
        [Tooltip("全局英文 / 数字纵向比例。小于 1 为扁数字。")]
        [Range(0.55f, 3.00f)] public float globalNumberHeightScale = 1.0f;
        [Tooltip("全局标签横向比例。小于 1 为窄标签字。")]
        [Range(0.55f, 3.00f)] public float globalLabelWidthScale = 1.0f;
        [Tooltip("全局标签纵向比例。小于 1 为扁标签字。")]
        [Range(0.55f, 3.00f)] public float globalLabelHeightScale = 1.0f;

        [Header("Base Colors")]
        public Color basePlateColor = new Color(1f, 1f, 1f, 0f);
        public Color baseFadeColor = new Color(1f, 1f, 1f, 0f);
        public Color edgeColor = new Color(1f, 1f, 1f, 0.18f);
        public Color accentColor = new Color(0.53f, 1.00f, 0.72f, 0.42f);

        [Header("Slot Colors")]
        public Color slotBackColor = new Color(1f, 1f, 1f, 0.055f);
        public Color slotFrameColor = new Color(1f, 1f, 1f, 0.26f);
        public Color slotSelectedColor = new Color(0.53f, 1.00f, 0.72f, 0.42f);
        public Color slotNumberColor = new Color(1f, 1f, 1f, 0.32f);

        [Header("Bar Colors")]
        public Color hpColor = new Color(0.53f, 1.00f, 0.72f, 1f);
        public Color hpDamageLagColor = new Color(1.00f, 0.49019608f, 0.45098040f, 0.69803923f);
        public Color lpColor = Color.white;
        public Color lpGhostColor = new Color(1f, 1f, 1f, 0.30f);
        public Color barBackColor = new Color(1f, 1f, 1f, 0.16f);
        public Color barFrameColor = new Color(1f, 1f, 1f, 0.35f);

        [Header("Legacy Text Colors")]
        [Tooltip("兼容旧字段。新显示优先使用 Global / Player Status Text Style。")]
        public Color textColor = Color.white;
        [Tooltip("兼容旧字段。新显示优先使用 Global / Player Status Text Style。")]
        public Color dimTextColor = new Color(1f, 1f, 1f, 0.55f);
        public Color warningTextColor = new Color(1.00f, 0.70f, 0.38f, 0.95f);
        public Color dangerTextColor = new Color(1.00f, 0.35f, 0.33f, 0.95f);
        public Color tinyHintColor = new Color(1f, 1f, 1f, 0.82f);

        [Header("Player Status Text Style - Local Overrides")]
        [Tooltip("HP 标签显示文字。为空时显示 HP。")]
        public string hpLabelText = "HP";
        [Tooltip("LP 标签显示文字。为空时显示 LP。")]
        public string lpLabelText = "LP";
        [Tooltip("HP 标签字体。为空时使用全局标签字体。")]
        public TMP_FontAsset playerHpLabelFont;
        [Tooltip("LP 标签字体。为空时使用全局标签字体。")]
        public TMP_FontAsset playerLpLabelFont;
        [Tooltip("LD 标签字体。为空时使用全局标签字体。")]
        public TMP_FontAsset playerLoadLabelFont;
        [Tooltip("LD 数字字体。为空时使用全局数字字体。")]
        public TMP_FontAsset playerLoadNumberFont;
        [Tooltip("LD 状态文字字体。为空时使用全局汉字字体。")]
        public TMP_FontAsset playerLoadStatusFont;
        [Tooltip("HP 标签颜色。")]
        public Color playerHpLabelColor = Color.white;
        [Tooltip("LP 标签颜色。")]
        public Color playerLpLabelColor = Color.white;
        [Tooltip("LD 标签颜色。")]
        public Color playerLoadLabelColor = Color.white;
        [Tooltip("LD 数字颜色。")]
        public Color playerLoadNumberColor = Color.white;
        [Tooltip("LD 百分号颜色。")]
        public Color playerLoadPercentColor = Color.white;

        [Header("Player Status Text Aspect - Local Overrides")]
        [Tooltip("HP 标签横向比例。0 表示使用全局标签横向比例。")]
        [Range(0f, 3.00f)] public float playerHpLabelWidthScale = 0f;
        [Tooltip("HP 标签纵向比例。0 表示使用全局标签纵向比例。")]
        [Range(0f, 3.00f)] public float playerHpLabelHeightScale = 0f;
        [Tooltip("LP 标签横向比例。0 表示使用全局标签横向比例。")]
        [Range(0f, 3.00f)] public float playerLpLabelWidthScale = 0f;
        [Tooltip("LP 标签纵向比例。0 表示使用全局标签纵向比例。")]
        [Range(0f, 3.00f)] public float playerLpLabelHeightScale = 0f;
        [Tooltip("LD 标签横向比例。0 表示使用全局标签横向比例。")]
        [Range(0f, 3.00f)] public float playerLoadLabelWidthScale = 0f;
        [Tooltip("LD 标签纵向比例。0 表示使用全局标签纵向比例。")]
        [Range(0f, 3.00f)] public float playerLoadLabelHeightScale = 0f;
        [Tooltip("LD 数字横向比例。0 表示使用全局数字横向比例。")]
        [Range(0f, 3.00f)] public float playerLoadNumberWidthScale = 0f;
        [Tooltip("LD 数字纵向比例。0 表示使用全局数字纵向比例。")]
        [Range(0f, 3.00f)] public float playerLoadNumberHeightScale = 0f;
        [Tooltip("LD 状态文字横向比例。0 表示使用全局普通文字横向比例。")]
        [Range(0f, 3.00f)] public float playerLoadStatusWidthScale = 0f;
        [Tooltip("LD 状态文字纵向比例。0 表示使用全局普通文字纵向比例。")]
        [Range(0f, 3.00f)] public float playerLoadStatusHeightScale = 0f;

        [Header("Load Status Text")]
        [Tooltip("LD 标签文字。")]
        public string loadPrefixText = "LD";
        [Tooltip("百分号文字。")]
        public string loadPercentSymbolText = "%";
        [Tooltip("LD 低负重状态显示文字。默认 0% - 29%。")]
        public string loadLightStatusText = "轻便";
        [Tooltip("LD 中负重状态显示文字。默认 30% - 89%。")]
        public string loadNormalStatusText = "负重";
        [Tooltip("LD 超重状态显示文字。默认 90% 以上。")]
        public string loadOverweightStatusText = "超重";
        [Tooltip("轻便状态颜色。")]
        public Color loadLightStatusColor = new Color(0.64f, 1.00f, 0.78f, 0.95f);
        [Tooltip("负重状态颜色。")]
        public Color loadNormalStatusColor = new Color(1.00f, 0.94f, 0.55f, 0.95f);
        [Tooltip("超重状态颜色。")]
        public Color loadOverweightStatusColor = new Color(1.00f, 0.62f, 0.58f, 0.95f);

        [Header("Load Segment Layout")]
        [Tooltip("LD 标签段宽度。")]
        [Min(0f)] public float loadLabelSegmentWidth = 44f;
        [Tooltip("LD 数字段宽度。数值右对齐，个位/十位/百位不会推动后面的百分号和状态文字。")]
        [Min(0f)] public float loadNumberSegmentWidth = 76f;
        [Tooltip("百分号段宽度。")]
        [Min(0f)] public float loadPercentSegmentWidth = 24f;
        [Tooltip("状态文字段起始附加间距。")]
        [Min(0f)] public float loadStatusSegmentGap = 18f;

        [Header("Effects")]
        [Range(0f, 1f)] public float overallAlpha = 1.0f;
        [Range(0f, 1f)] public float scanLineAlpha = 0.022f;
        [Range(0f, 1f)] public float noiseAlpha = 0.035f;
        [Range(0f, 2f)] public float hpGlowStrength = 0.45f;
        [Range(0f, 2f)] public float lpGlowStrength = 0.28f;
        [Range(0.5f, 1.5f)] public float fontScale = 1.0f;

        [Header("Bar Animation")]
        [Tooltip("HP 当前血条从旧值连贯变化到新值所需时间。0 表示瞬间。")]
        [Min(0f)] public float hpFillChangeDuration = 0.16f;
        [Tooltip("HP 受伤后，虚血条开始追随前的停顿时间。")]
        [Min(0f)] public float hpDamageLagHoldDuration = 2.35f;
        [Tooltip("HP 虚血条追赶速度。与单位头顶 UI 的 扣血参考追赶速度 一致，单位为比例/秒。")]
        [Min(0f)] public float hpDamageLagCatchupDuration = 2.5f;
        [Tooltip("是否启用 HP 虚血条。")]
        public bool enableHpDamageLag = true;
        [Tooltip("LP 当前条变化所需时间。LP 高频变化，默认较短。")]
        [Min(0f)] public float lpFillChangeDuration = 0.08f;
        [Tooltip("LP 残影层预留。第一阶段默认关闭。")]
        public bool enableLpGhostFill = false;
        [Tooltip("UI 动画是否使用 unscaledDeltaTime。暂停/菜单中仍希望 UI 动画跑完时可开启。")]
        public bool useUnscaledTimeForBars = false;

        [Header("Image Rules")]
        public Image.Type basePlateImageType = Image.Type.Sliced;
        public Image.Type slotImageType = Image.Type.Sliced;
        public Image.Type barFrameImageType = Image.Type.Sliced;
        [Tooltip("填充图自身不再被压缩扣血；此处只控制 Fill 图片类型，实际显示量由 MaskRoot 裁切。")]
        public Image.Type barFillImageType = Image.Type.Simple;

        public Material ResolveGlobalUIMaterial() => globalUIMaterial != null ? globalUIMaterial : defaultUIMaterial;
        public Material ResolvePlayerStatusModuleMaterial() => playerStatusOverrideGlobalMaterial ? (playerStatusModuleMaterial != null ? playerStatusModuleMaterial : defaultUIMaterial) : ResolveGlobalUIMaterial();
        public Material ResolveQuickSlotsModuleMaterial() => quickSlotsOverrideGlobalMaterial ? (quickSlotsModuleMaterial != null ? quickSlotsModuleMaterial : defaultUIMaterial) : ResolveGlobalUIMaterial();
        public SkyPrisonHUDBlendModeV1 ResolvePlayerStatusBlendMode() => playerStatusOverrideGlobalBlend ? playerStatusBlendMode : globalUIBlendMode;
        public SkyPrisonHUDBlendModeV1 ResolveQuickSlotsBlendMode() => quickSlotsOverrideGlobalBlend ? quickSlotsBlendMode : globalUIBlendMode;

        public TMP_FontAsset ResolveMainFont() => mainFont;
        public TMP_FontAsset ResolveNumberFont() => numberFont != null ? numberFont : mainFont;
        public TMP_FontAsset ResolveLabelFont() => labelFont != null ? labelFont : mainFont;
        public TMP_FontAsset ResolveHpLabelFont() => playerHpLabelFont != null ? playerHpLabelFont : ResolveLabelFont();
        public TMP_FontAsset ResolveLpLabelFont() => playerLpLabelFont != null ? playerLpLabelFont : ResolveLabelFont();
        public string ResolveHpLabelText() => string.IsNullOrWhiteSpace(hpLabelText) ? "HP" : hpLabelText;
        public string ResolveLpLabelText() => string.IsNullOrWhiteSpace(lpLabelText) ? "LP" : lpLabelText;
        public TMP_FontAsset ResolveLoadLabelFont() => playerLoadLabelFont != null ? playerLoadLabelFont : ResolveLabelFont();
        public TMP_FontAsset ResolveLoadNumberFont() => playerLoadNumberFont != null ? playerLoadNumberFont : ResolveNumberFont();
        public TMP_FontAsset ResolveLoadStatusFont() => playerLoadStatusFont != null ? playerLoadStatusFont : ResolveMainFont();

        public string ResolveLoadPrefixText() => string.IsNullOrWhiteSpace(loadPrefixText) ? "LD" : loadPrefixText;
        public string ResolveLoadPercentSymbolText() => string.IsNullOrWhiteSpace(loadPercentSymbolText) ? "%" : loadPercentSymbolText;
        public string ResolveLoadLightText() => string.IsNullOrWhiteSpace(loadLightStatusText) ? "轻便" : loadLightStatusText;
        public string ResolveLoadNormalText() => string.IsNullOrWhiteSpace(loadNormalStatusText) ? "负重" : loadNormalStatusText;
        public string ResolveLoadOverweightText() => string.IsNullOrWhiteSpace(loadOverweightStatusText) ? "超重" : loadOverweightStatusText;

        public Vector2 ResolveGlobalTextAspect() => new Vector2(globalTextWidthScale, globalTextHeightScale);
        public Vector2 ResolveGlobalNumberAspect() => new Vector2(globalNumberWidthScale, globalNumberHeightScale);
        public Vector2 ResolveGlobalLabelAspect() => new Vector2(globalLabelWidthScale, globalLabelHeightScale);
        public Vector2 ResolveHpLabelAspect() => new Vector2(ResolveLocalAspect(playerHpLabelWidthScale, globalLabelWidthScale), ResolveLocalAspect(playerHpLabelHeightScale, globalLabelHeightScale));
        public Vector2 ResolveLpLabelAspect() => new Vector2(ResolveLocalAspect(playerLpLabelWidthScale, globalLabelWidthScale), ResolveLocalAspect(playerLpLabelHeightScale, globalLabelHeightScale));
        public Vector2 ResolveLoadLabelAspect() => new Vector2(ResolveLocalAspect(playerLoadLabelWidthScale, globalLabelWidthScale), ResolveLocalAspect(playerLoadLabelHeightScale, globalLabelHeightScale));
        public Vector2 ResolveLoadNumberAspect() => new Vector2(ResolveLocalAspect(playerLoadNumberWidthScale, globalNumberWidthScale), ResolveLocalAspect(playerLoadNumberHeightScale, globalNumberHeightScale));
        public Vector2 ResolveLoadStatusAspect() => new Vector2(ResolveLocalAspect(playerLoadStatusWidthScale, globalTextWidthScale), ResolveLocalAspect(playerLoadStatusHeightScale, globalTextHeightScale));

        private static float ResolveLocalAspect(float localValue, float fallback) => localValue > 0.0001f ? localValue : fallback;

        private void OnValidate()
        {
            overallAlpha = Mathf.Clamp01(overallAlpha);
            scanLineAlpha = Mathf.Clamp01(scanLineAlpha);
            noiseAlpha = Mathf.Clamp01(noiseAlpha);
            hpGlowStrength = Mathf.Clamp(hpGlowStrength, 0f, 2f);
            lpGlowStrength = Mathf.Clamp(lpGlowStrength, 0f, 2f);
            fontScale = Mathf.Clamp(fontScale, 0.5f, 1.5f);
            globalTextWidthScale = Mathf.Clamp(globalTextWidthScale, 0.55f, 3.00f);
            globalTextHeightScale = Mathf.Clamp(globalTextHeightScale, 0.55f, 3.00f);
            globalNumberWidthScale = Mathf.Clamp(globalNumberWidthScale, 0.55f, 3.00f);
            globalNumberHeightScale = Mathf.Clamp(globalNumberHeightScale, 0.55f, 3.00f);
            globalLabelWidthScale = Mathf.Clamp(globalLabelWidthScale, 0.55f, 3.00f);
            globalLabelHeightScale = Mathf.Clamp(globalLabelHeightScale, 0.55f, 3.00f);
            playerHpLabelWidthScale = Mathf.Clamp(playerHpLabelWidthScale, 0f, 3.00f);
            playerHpLabelHeightScale = Mathf.Clamp(playerHpLabelHeightScale, 0f, 3.00f);
            playerLpLabelWidthScale = Mathf.Clamp(playerLpLabelWidthScale, 0f, 3.00f);
            playerLpLabelHeightScale = Mathf.Clamp(playerLpLabelHeightScale, 0f, 3.00f);
            playerLoadLabelWidthScale = Mathf.Clamp(playerLoadLabelWidthScale, 0f, 3.00f);
            playerLoadLabelHeightScale = Mathf.Clamp(playerLoadLabelHeightScale, 0f, 3.00f);
            playerLoadNumberWidthScale = Mathf.Clamp(playerLoadNumberWidthScale, 0f, 3.00f);
            playerLoadNumberHeightScale = Mathf.Clamp(playerLoadNumberHeightScale, 0f, 3.00f);
            playerLoadStatusWidthScale = Mathf.Clamp(playerLoadStatusWidthScale, 0f, 3.00f);
            playerLoadStatusHeightScale = Mathf.Clamp(playerLoadStatusHeightScale, 0f, 3.00f);
            hpFillChangeDuration = Mathf.Max(0f, hpFillChangeDuration);
            hpDamageLagHoldDuration = hpDamageLagHoldDuration < 1.0f ? 2.35f : Mathf.Max(0f, hpDamageLagHoldDuration);
            hpDamageLagCatchupDuration = hpDamageLagCatchupDuration < 1.0f ? 2.5f : Mathf.Max(0f, hpDamageLagCatchupDuration);
            lpFillChangeDuration = Mathf.Max(0f, lpFillChangeDuration);
            loadLabelSegmentWidth = Mathf.Max(0f, loadLabelSegmentWidth);
            loadNumberSegmentWidth = Mathf.Max(0f, loadNumberSegmentWidth);
            loadPercentSegmentWidth = Mathf.Max(0f, loadPercentSegmentWidth);
            loadStatusSegmentGap = Mathf.Max(0f, loadStatusSegmentGap);
        }
    }
}
