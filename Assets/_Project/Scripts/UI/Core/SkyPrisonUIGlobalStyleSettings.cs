using TMPro;
using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Global UI style settings stored directly on the UI prefab.
    /// This component is not a profile source and does not generate UI by itself.
    /// The workbench edits it, and runtime/window drivers may read it after instantiating the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonUIGlobalStyleSettings_V1 : MonoBehaviour
    {
        [Header("Fonts")]
        public TMP_FontAsset defaultTextFont;
        public TMP_FontAsset defaultNumberFont;

        [Header("Text Colors")]
        public Color defaultTextColor = Color.white;
        public Color defaultNumberColor = Color.white;
        public Color defaultLabelColor = Color.white;

        [Header("Materials")]
        public Material defaultUIMaterial;
        public Material defaultTMPMaterial;
        public SkyPrisonHUDBlendModeV1 globalBlendMode = SkyPrisonHUDBlendModeV1.Normal;
        public bool applyMaterialToTMPText = true;

        [Header("Module Post Process")]
        public bool autoResolveMaterials = true;
        public bool enableModulePostProcessByDefault = true;
        public bool enableChromaticByDefault = false;
        [Tooltip("用户侧归一化强度。工作台 / Driver 会把 0..1 映射为 Shader 像素偏移。")]
        [Range(0f, 1f)] public float chromaticIntensity = 0.045f;
        [Range(0f, 1f)] public float chromaticSoftness = 0.18f;

        [HideInInspector] public Material modulePostProcessMaterial;
        [HideInInspector] public Material chromaticMaterial;
        [HideInInspector] public Material windowModulePostProcessMaterial;

        [Header("Inventory Slots / 背包格子")]
        [Tooltip("背包格子底图。留空则用内置程序生成的圆角矩形。")]
        public Sprite inventorySlotSprite;
        [Tooltip("背包格子底图叠加色（半透明让模糊背景透出）。")]
        public Color inventorySlotColor = new Color(0.12f, 0.12f, 0.12f, 0.55f);
        [Tooltip("自定义底图按九宫格(Sliced)拉伸，需要 sprite 带 border。关闭则 Simple 拉伸。")]
        public bool inventorySlotSliced = true;

        [Header("Window Skin / 窗口皮肤")]
        [Tooltip("窗口面板底图(整窗背景)。留空用内置圆角矩形。建议九宫格。")]
        public Sprite windowPanelSprite;
        [Tooltip("各横条底图(标题/筛选/排序/负重/货币栏)。留空用纯色。建议九宫格。")]
        public Sprite windowBarSprite;
        [Tooltip("各按钮底图(关闭/升降序/整理/排序下拉)。留空用纯色。建议九宫格。")]
        public Sprite windowButtonSprite;
        [Tooltip("关闭按钮的图标(替换默认的×文字)。留空则显示×。普通 sprite，非九宫格。")]
        public Sprite windowCloseIconSprite;

        [Header("Workbench Preview")]
        [Tooltip("模拟窗口背景色，用于体现半透明 UI 在实际游戏场景上的效果。调深调亮都可以，不影响运行时。")]
        public Color previewBackgroundColor = new Color(0.10f, 0.12f, 0.10f, 1f);

        [TextArea(2, 4)]
        public string note = "";
    }
}
