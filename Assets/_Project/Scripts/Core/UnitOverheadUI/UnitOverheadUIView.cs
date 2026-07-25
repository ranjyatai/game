using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class UnitOverheadUIView : MonoBehaviour
{
    public UnitDefinition unitDefinition;
    public Component runtimeBinder;
    public bool autoResolveUnitDefinition = true;
    public bool autoEnsureStructure = true;
    public bool applyOnStart = true;
    public bool applyOnValidate = true;

    [Header("Layout Safety")]
    [Tooltip("开启后，运行时只刷新血量/状态图标/贴图，不改 UI 编辑器里已经摆好的 RectTransform 坐标。") ]
    public bool preserveAuthoredLayout = true;

    public Transform overheadAnchor;
    public RectTransform runtimeUiRoot;
    public RectTransform nameRoot;
    public TextMeshProUGUI nameText;
    public RectTransform slotRoot01;
    public RectTransform slotView01;
    public RectTransform bgLayerRoot;
    public RectTransform statusRoot;
    public RectTransform statusAreaRoot;
    public RectTransform statusContentRoot;
    public RectTransform damageNumberRoot;
    public RectTransform damageNumberContentRoot;
    public RectTransform damageRefMaskRoot;
    public RawImage damageReferenceImage;
    public RectTransform fillMaskRoot;
    public RawImage fillImage;

    public bool debugLogs = false;

    [Header("Billboard")]
    public bool faceCameraEveryFrame = true;
    public Camera targetCamera;

    private readonly List<RawImage> runtimeBgImages = new List<RawImage>();
    private readonly List<UnitStatusIconView> runtimeStatusIcons = new List<UnitStatusIconView>();
    private readonly List<DamageNumberView> runtimeDamageNumbers = new List<DamageNumberView>();
    private readonly List<StatusViewData> cachedStatuses = new List<StatusViewData>();
    private readonly List<StatusViewData> visibleStatusBuffer = new List<StatusViewData>();
    private readonly List<RectTransform> activeStatusRectBuffer = new List<RectTransform>();

    [Header("Status Icon Runtime Safety")]
    [Tooltip("状态图标池上限。防止异常状态列表或重复刷新把 UnitStatusIconView 无限堆起来。") ]
    [SerializeField] private int maxRuntimeStatusIconPool = 24;
    [Tooltip("运行时刷新状态时，清理旧版本遗留/未登记的状态图标子节点。") ]
    [SerializeField] private bool compactStrayStatusIconsOnRefresh = true;

    private OverheadBarStyleAsset appliedStyle;
    private StatusDisplayDefinition cachedStatusDisplayDefinition;

    private float targetPercent = 1f;
    private float currentPercent = 1f;
    private float damageReferencePercent = 1f;
    private float damageReferenceHoldTimer = 0f;
    private float currentDisplayAlpha = 1f;
    private float targetDisplayAlpha = 1f;
    private CanvasGroup slotCanvasGroup;

    private const string RuntimeRootName = "UnitOverheadUIRuntimeRoot";
    private const int OverheadUiLayer = 19;
    private static readonly Vector3 RuntimeRootScale = new Vector3(0.02f, 0.02f, 0.02f);

    private bool isInitializing;

    private void Awake() { SafeInitialize(); }
    private void Start() { SafeInitialize(); }

    private void OnEnable()
    {
        OverheadBarStyleAsset.OnStyleChanged += HandleStyleChanged;
    }

    private void OnDisable()
    {
        OverheadBarStyleAsset.OnStyleChanged -= HandleStyleChanged;
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (!applyOnValidate || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                SafeInitialize();
        };
#endif
    }

    private void Update()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;

        float fillSpeed = appliedStyle != null ? Mathf.Max(0.01f, appliedStyle.fillLerpSpeed) : 3f;
        currentPercent = Mathf.Lerp(currentPercent, targetPercent, 1f - Mathf.Exp(-fillSpeed * dt));
        if (Mathf.Abs(currentPercent - targetPercent) < 0.0005f)
            currentPercent = targetPercent;

        if (damageReferenceHoldTimer > 0f)
        {
            damageReferenceHoldTimer -= dt;
        }
        else
        {
            float refSpeed = appliedStyle != null ? Mathf.Max(0.01f, appliedStyle.damageReferenceFadeSpeed) : 2.5f;
            damageReferencePercent = Mathf.MoveTowards(damageReferencePercent, currentPercent, refSpeed * dt);
        }

        float fadeSpeed = appliedStyle != null ? Mathf.Max(0.01f, appliedStyle.fadeSpeed) : 8f;
        currentDisplayAlpha = Mathf.Lerp(currentDisplayAlpha, targetDisplayAlpha, 1f - Mathf.Exp(-fadeSpeed * dt));
        if (Mathf.Abs(currentDisplayAlpha - targetDisplayAlpha) < 0.0005f)
            currentDisplayAlpha = targetDisplayAlpha;

        RefreshBarVisuals();
    }

    private void LateUpdate()
    {
        if (!faceCameraEveryFrame)
            return;

        ApplyBillboardToCamera();
    }

    private void ApplyBillboardToCamera()
    {
        if (runtimeUiRoot == null)
            return;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
            return;

        runtimeUiRoot.rotation = cam.transform.rotation;
    }

    private void HandleStyleChanged(OverheadBarStyleAsset changedStyle)
    {
        if (changedStyle == null)
            return;

        OverheadBarStyleAsset currentStyle = unitDefinition != null
            ? unitDefinition.overheadHpBarStyle
            : appliedStyle;

        if (currentStyle == null || currentStyle != changedStyle)
            return;

        ApplyStyle(changedStyle);

        if (cachedStatuses.Count > 0 || cachedStatusDisplayDefinition != null)
            RefreshStatuses(cachedStatuses, cachedStatusDisplayDefinition);
    }

    private void SafeInitialize()
    {
        if (isInitializing)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(gameObject))
            return;
#endif

        isInitializing = true;
        try
        {
            if (autoEnsureStructure)
                EnsureStructure();

            if (autoResolveUnitDefinition)
                TryAutoResolveUnitDefinition();

            if (applyOnStart)
            {
                ApplyStyleFromDefinition();
                ApplyNameVisibilityFromDefinition();
            }
        }
        finally
        {
            isInitializing = false;
        }
    }


    [Header("Damage Number Anchor")]
    [SerializeField] private Transform damageNumberAnchor;
    [SerializeField] private Vector3 damageNumberAnchorOffset = new Vector3(0f, 0.15f, 0f);
    public void SetName(string value) { SetDisplayName(value); }

    public void SetNameVisible(bool visible)
    {
        if (nameRoot != null)
            nameRoot.gameObject.SetActive(visible);
    }

    public void SetDisplayName(string value)
    {
        EnsureNameHierarchyCorrect();
        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(value) ? "NPC Name" : value;
    }

    public void ApplyStyleFromDefinition()
    {
        OverheadBarStyleAsset style = unitDefinition != null ? unitDefinition.overheadHpBarStyle : null;
        ApplyStyle(style);
    }

    public void ApplyStyle(OverheadBarStyleAsset style)
    {
        appliedStyle = style;

        if (autoEnsureStructure)
            EnsureStructure();

        if (!preserveAuthoredLayout)
            ApplyUnitDefinitionTransformOverrides();

        ApplyNameStyle(style);

        if (preserveAuthoredLayout)
        {
            ApplyBarVisualStyleOnly(style);
            ApplyStatusAreaVisibilityOnly(style);
        }
        else
        {
            ApplyBarStyle(style);
            ApplyStatusAreaStyle(style);
        }
        ApplyNameVisibilityFromDefinition();
        ApplyBillboardToCamera();
        UpdateDisplayAlphaTarget();

        if (!Application.isPlaying)
            currentDisplayAlpha = targetDisplayAlpha;

        RefreshBarVisuals();
        DebugDumpOffsets("ApplyStyle");
    }

    public void SetHp(float current, float max)
    {
        float normalized = max > 0.0001f ? Mathf.Clamp01(current / max) : 0f;
        float oldTarget = targetPercent;
        targetPercent = normalized;

        if (targetPercent < oldTarget)
        {
            damageReferencePercent = Mathf.Max(damageReferencePercent, oldTarget);
            damageReferenceHoldTimer = appliedStyle != null
                ? Mathf.Max(0f, appliedStyle.damageReferenceHoldTime)
                : 0.35f;
        }

        UpdateDisplayAlphaTarget();

        if (!Application.isPlaying)
        {
            currentPercent = targetPercent;
            damageReferencePercent = Mathf.Max(damageReferencePercent, currentPercent);
            currentDisplayAlpha = targetDisplayAlpha;
            RefreshBarVisuals();
        }
    }

    public void EnsureStructure()
    {
        overheadAnchor = EnsureTransformChild(transform, "OverheadAnchor");
        runtimeUiRoot = EnsureSingleRuntimeRoot(overheadAnchor);
        EnsureCanvasComponents(runtimeUiRoot);
        EnsureNameHierarchyCorrect();

        slotRoot01 = EnsureRectChild(runtimeUiRoot, "SlotRoot_01");
        slotCanvasGroup = EnsureCanvasGroup(slotRoot01);
        slotView01 = EnsureRectChild(slotRoot01, "SlotView_01");
        bgLayerRoot = EnsureRectChild(slotView01, "BgLayerRoot");
        damageRefMaskRoot = EnsureMaskRoot(slotView01, "DamageRefMaskRoot");
        damageReferenceImage = EnsureRawImageChild(damageRefMaskRoot, "DamageRef");
        fillMaskRoot = EnsureMaskRoot(slotView01, "FillMaskRoot");
        fillImage = EnsureRawImageChild(fillMaskRoot, "Fill");

        statusRoot = EnsureRectChild(runtimeUiRoot, "StatusRoot");
        statusAreaRoot = EnsureRectChild(statusRoot, "StatusAreaRoot");
        statusContentRoot = EnsureRectChild(statusAreaRoot, "StatusContentRoot");

        damageNumberRoot = EnsureRectChild(runtimeUiRoot, "DamageNumberRoot");
        damageNumberContentRoot = EnsureRectChild(damageNumberRoot, "DamageNumberContentRoot");

        if (!preserveAuthoredLayout)
        {
            ConfigureDefaultAnchors();
            ApplyUnitDefinitionTransformOverrides();
        }

        SetLayerRecursively(runtimeUiRoot.gameObject, OverheadUiLayer);
    }

    private RectTransform EnsureSingleRuntimeRoot(Transform parent)
    {
        List<Transform> matches = new List<Transform>();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == RuntimeRootName)
                matches.Add(child);
        }

        RectTransform keep = null;
        for (int i = 0; i < matches.Count; i++)
        {
            RectTransform rect = matches[i] as RectTransform;
            if (keep == null && rect != null)
            {
                keep = rect;
                continue;
            }

            SafeDestroy(matches[i].gameObject);
        }

        if (keep != null)
            return keep;

        GameObject go = new GameObject(RuntimeRootName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private void ApplyNameVisibilityFromDefinition()
    {
        if (unitDefinition == null)
        {
            SetNameVisible(false);
            return;
        }

        bool visible = unitDefinition.autoOverheadNameVisibility
            ? ShouldShowNameAutomatically(unitDefinition)
            : unitDefinition.manualShowOverheadName;

        SetNameVisible(visible);
    }

    private bool ShouldShowNameAutomatically(UnitDefinition definition)
    {
        return definition != null && definition.characterIdentity == CharacterIdentity.Ally;
    }

    private void EnsureNameHierarchyCorrect()
    {
        if (runtimeUiRoot == null)
            return;

        TextMeshProUGUI strayRootText = runtimeUiRoot.GetComponent<TextMeshProUGUI>();
        if (strayRootText != null)
            SafeDestroyComponent(strayRootText);

        nameRoot = EnsureRectChild(runtimeUiRoot, "NameRoot");

        for (int i = runtimeUiRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = runtimeUiRoot.GetChild(i);
            if (child == null || child == nameRoot)
                continue;

            if (child.name == "NameText")
                SafeDestroy(child.gameObject);
        }

        TextMeshProUGUI wrongTextOnRoot = nameRoot.GetComponent<TextMeshProUGUI>();
        if (wrongTextOnRoot != null)
            SafeDestroyComponent(wrongTextOnRoot);

        nameText = EnsureTMPChild(nameRoot, "NameText", "NPC Name");
        nameRoot.gameObject.SetActive(false);
    }

    private void ApplyNameStyle(OverheadBarStyleAsset style)
    {
        EnsureNameHierarchyCorrect();
        if (nameText == null || nameRoot == null)
            return;

        if (style != null)
        {
            if (style.nameFontAsset != null)
                nameText.font = style.nameFontAsset;

            nameText.fontSize = Mathf.Max(8, style.nameFontSize);
            nameText.color = style.nameColor;
            SetRectCenter(nameRoot, new Vector2(style.nameOffset.x, style.nameOffset.y));
            nameRoot.sizeDelta = new Vector2(240f, 24f);
        }
        else
        {
            nameText.fontSize = 13f;
            nameText.color = Color.white;
            SetRectCenter(nameRoot, new Vector2(0f, 16f));
            nameRoot.sizeDelta = new Vector2(240f, 24f);
        }

        nameText.alignment = TextAlignmentOptions.Center;
        nameText.rectTransform.anchorMin = Vector2.zero;
        nameText.rectTransform.anchorMax = Vector2.one;
        nameText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        nameText.rectTransform.anchoredPosition = Vector2.zero;
        nameText.rectTransform.sizeDelta = Vector2.zero;
    }

    private void ApplyBarStyle(OverheadBarStyleAsset style)
    {
        if (slotRoot01 == null || slotView01 == null || bgLayerRoot == null ||
            damageRefMaskRoot == null || fillMaskRoot == null ||
            damageReferenceImage == null || fillImage == null)
            return;

        UnitOverheadLayoutUtility.LayoutResult layout = UnitOverheadLayoutUtility.BuildLayout(style);

        slotRoot01.anchorMin = new Vector2(0.5f, 0.5f);
        slotRoot01.anchorMax = new Vector2(0.5f, 0.5f);
        slotRoot01.pivot = new Vector2(0.5f, 0.5f);
        slotRoot01.anchoredPosition = layout.barRootPosition;

        SetRectCenter(slotView01, Vector2.zero);
        slotView01.sizeDelta = layout.barSize;
        slotView01.localRotation = Quaternion.Euler(0f, 0f, style != null ? style.rotationZ : 0f);

        ConfigureMaskRoot(damageRefMaskRoot, layout.barSize);
        ConfigureMaskRoot(fillMaskRoot, layout.barSize);
        ConfigureBarImage(damageReferenceImage.rectTransform, layout.barSize);
        ConfigureBarImage(fillImage.rectTransform, layout.barSize);

        if (style != null)
        {
            fillImage.texture = style.fillTexture;
            fillImage.color = style.fillColor;
            damageReferenceImage.texture = style.damageReferenceTexture;
            damageReferenceImage.color = style.damageReferenceColor;
            RebuildBgLayers(style);
        }
        else
        {
            fillImage.texture = null;
            fillImage.color = Color.white;
            damageReferenceImage.texture = null;
            damageReferenceImage.color = new Color(0.42f, 0.12f, 0.12f, 1f);
            ClearBgLayers();
        }

        if (debugLogs)
            Log($"ApplyBarStyle | barRoot={layout.barRootPosition} | barSize={layout.barSize}");
    }

    private void ApplyBarVisualStyleOnly(OverheadBarStyleAsset style)
    {
        if (damageReferenceImage == null || fillImage == null)
            return;

        appliedStyle = style;

        if (style != null)
        {
            fillImage.texture = style.fillTexture;
            fillImage.color = style.fillColor;
            damageReferenceImage.texture = style.damageReferenceTexture;
            damageReferenceImage.color = style.damageReferenceColor;
            RebuildBgLayers(style);
        }
        else
        {
            fillImage.texture = null;
            fillImage.color = Color.white;
            damageReferenceImage.texture = null;
            damageReferenceImage.color = new Color(0.42f, 0.12f, 0.12f, 1f);
            ClearBgLayers();
        }
    }

    private void ApplyStatusAreaVisibilityOnly(OverheadBarStyleAsset style)
    {
        if (statusRoot == null)
            return;

        bool enable = style == null || style.enableStatusArea;
        statusRoot.gameObject.SetActive(enable);
        if (!enable)
            ClearStatusIconViews();
    }

    public void ApplyStatusAreaStyle(OverheadBarStyleAsset style)
    {
        if (autoEnsureStructure)
            EnsureStructure();

        if (statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
            return;

        bool enable = style == null || style.enableStatusArea;
        statusRoot.gameObject.SetActive(enable);
        if (!enable)
            return;

        UnitOverheadLayoutUtility.LayoutResult layout = UnitOverheadLayoutUtility.BuildLayout(style);

        statusRoot.anchorMin = new Vector2(0.5f, 0.5f);
        statusRoot.anchorMax = new Vector2(0.5f, 0.5f);
        statusRoot.pivot = new Vector2(0.5f, 0.5f);
        statusRoot.anchoredPosition = layout.statusRootPosition;

        statusAreaRoot.anchorMin = new Vector2(0.5f, 0.5f);
        statusAreaRoot.anchorMax = new Vector2(0.5f, 0.5f);
        statusAreaRoot.pivot = new Vector2(0.5f, 0.5f);
        statusAreaRoot.anchoredPosition = layout.statusAreaPosition;
        statusAreaRoot.sizeDelta = layout.statusAreaSize;

        statusContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
        statusContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
        statusContentRoot.pivot = new Vector2(0.5f, 0.5f);
        statusContentRoot.anchoredPosition = layout.statusContentPosition;
        statusContentRoot.sizeDelta = layout.statusContentSize;

        if (debugLogs)
        {
            Log($"ApplyStatusAreaStyle | statusRoot={layout.statusRootPosition} | statusArea={layout.statusAreaPosition} | statusAreaSize={layout.statusAreaSize}");
        }
    }

    public void RefreshStatuses(IReadOnlyList<StatusViewData> statuses, StatusDisplayDefinition displayDefinition)
    {
        cachedStatuses.Clear();
        if (statuses != null)
        {
            for (int i = 0; i < statuses.Count; i++)
                cachedStatuses.Add(statuses[i]);
        }
        cachedStatusDisplayDefinition = displayDefinition;

        // 重要：状态刷新只刷新“状态图标”，不重套头顶 UI 坐标。
        // 之前每次状态同步都 EnsureStructure + ApplyStatusAreaStyle，会把血条/状态区域反复拉回默认布局，造成上下抖动和血条坐标下移。
        if (statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
        {
            if (autoEnsureStructure)
                EnsureStructure();

            if (statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
                return;
        }

        bool enableStatusArea = appliedStyle == null || appliedStyle.enableStatusArea;
        statusRoot.gameObject.SetActive(enableStatusArea);
        if (!enableStatusArea)
        {
            ClearStatusIconViews();
            return;
        }

        FillVisibleStatusesBuffer(statuses, visibleStatusBuffer);
        if (maxRuntimeStatusIconPool > 0 && visibleStatusBuffer.Count > maxRuntimeStatusIconPool)
            visibleStatusBuffer.RemoveRange(maxRuntimeStatusIconPool, visibleStatusBuffer.Count - maxRuntimeStatusIconPool);

        int visibleCount = visibleStatusBuffer.Count;
        EnsureStatusIconPool(visibleCount);

        if (compactStrayStatusIconsOnRefresh)
            CompactStatusIconChildren();

        // 先把池里旧图标清干净，但不动 statusRoot/statusAreaRoot/slotRoot01 的坐标。
        for (int i = 0; i < runtimeStatusIcons.Count; i++)
        {
            UnitStatusIconView view = runtimeStatusIcons[i];
            if (view == null)
                continue;

            view.ResetView();
            view.gameObject.SetActive(false);
        }

        if (visibleCount <= 0)
            return;

        Vector2 iconSize = UnitOverheadLayoutUtility.ResolveStatusIconSize(appliedStyle);
        Vector2 spacing = UnitOverheadLayoutUtility.ResolveStatusSpacing(appliedStyle);
        TextAnchor alignment = UnitOverheadLayoutUtility.ResolveStatusAlignment(appliedStyle);
        int maxLines = UnitOverheadLayoutUtility.ResolveStatusMaxLines(displayDefinition);
        StatusDisplayDirection direction = UnitOverheadLayoutUtility.ResolveStatusDirection(displayDefinition);

        activeStatusRectBuffer.Clear();

        for (int i = 0; i < visibleStatusBuffer.Count; i++)
        {
            UnitStatusIconView view = runtimeStatusIcons[i];
            if (view == null)
                continue;

            view.gameObject.SetActive(true);
            view.EnsureStructure();
            view.Apply(visibleStatusBuffer[i], direction);

            activeStatusRectBuffer.Add(view.root);
        }

        UnitOverheadLayoutUtility.LayoutStatusIcons(
            activeStatusRectBuffer,
            statusAreaRoot.sizeDelta,
            iconSize,
            spacing,
            maxLines,
            direction,
            alignment);

        AlignStatusContentToBarLeft(iconSize);

        if (debugLogs)
        {
            Log($"RefreshStatuses | visible={visibleStatusBuffer.Count} | pool={runtimeStatusIcons.Count} | iconSize={iconSize} | spacing={spacing} | alignment={alignment} | maxLines={maxLines} | direction={direction}");
        }
    }

    // 把状态图标整体偏移，使第一个图标的左端对齐血条的左端。
    // 只改 statusContentRoot.anchoredPosition.x，不碰 statusAreaRoot 的布局坐标。
    private void AlignStatusContentToBarLeft(Vector2 iconSize)
    {
        if (slotRoot01 == null || statusRoot == null || statusAreaRoot == null || statusContentRoot == null)
            return;

        if (activeStatusRectBuffer.Count == 0)
            return;

        float barHalfW = appliedStyle != null ? appliedStyle.barSize.x * 0.5f : 75f;

        // 血条左端在 runtimeUiRoot 空间的 X
        float barLeftX = slotRoot01.anchoredPosition.x - barHalfW;

        // statusAreaRoot 中心在 runtimeUiRoot 空间的 X
        float statusAreaCenterX = statusRoot.anchoredPosition.x + statusAreaRoot.anchoredPosition.x;

        // LayoutStatusIcons 完成后，第一个图标中心在 statusContentRoot 空间的 X
        float firstIconCenterX = activeStatusRectBuffer[0].anchoredPosition.x;

        // 直接计算 statusContentRoot.anchoredPosition.x 的目标值，
        // 使"statusAreaCenter + contentOffset + iconCenter - halfIcon == barLeftX"成立。
        // 注意：不依赖 contentPos 的当前值，直接设为结果，避免累加误差。
        float targetContentX = barLeftX - statusAreaCenterX - firstIconCenterX + iconSize.x * 0.5f;

        Vector2 contentPos = statusContentRoot.anchoredPosition;
        contentPos.x = targetContentX;
        statusContentRoot.anchoredPosition = contentPos;
    }

    public void ClearStatuses()
    {
        cachedStatuses.Clear();
        cachedStatusDisplayDefinition = null;
        ClearStatusIconViews();
    }

    private void ClearStatusIconViews()
    {
        for (int i = 0; i < runtimeStatusIcons.Count; i++)
        {
            UnitStatusIconView view = runtimeStatusIcons[i];
            if (view == null)
                continue;

            view.ResetView();
            view.gameObject.SetActive(false);
        }

        // 保险：旧版本/回档前生成的图标可能没有登记进 runtimeStatusIcons，直接扫容器，但仍然只清图标，不碰布局根坐标。
        if (statusContentRoot == null)
            return;

        for (int i = 0; i < statusContentRoot.childCount; i++)
        {
            Transform child = statusContentRoot.GetChild(i);
            if (child == null)
                continue;

            UnitStatusIconView view = child.GetComponent<UnitStatusIconView>();
            if (view != null)
                view.ResetView();

            child.gameObject.SetActive(false);
        }
    }


    public void ShowDamageNumber(float amount, Color color, bool isHeal, bool isPositiveCrit = false, bool isNegativeCrit = false)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning($"[DmgNum] amount<=0，跳过 amount={amount}", this);
            return;
        }

        if (autoEnsureStructure)
            EnsureStructure();

        EnsureDamageNumberRootLayout();
        DamageNumberView view = GetOrCreateDamageNumberView();
        if (view == null)
        {
            Debug.LogWarning($"[DmgNum] GetOrCreateDamageNumberView 返回 null，damageNumberContentRoot={damageNumberContentRoot}", this);
            return;
        }

        ApplyDamageNumberStyle(view);

        Vector2 bodyLocalPos = GetRandomBodyDamageNumberPosition();
        bool preferSoft = false;
        view.Play(amount, color, isHeal, bodyLocalPos, preferSoft, isPositiveCrit, isNegativeCrit);
        view.transform.SetAsLastSibling();
    }

    private void EnsureDamageNumberRootLayout()
    {
        if (damageNumberRoot == null || damageNumberContentRoot == null)
            return;

        damageNumberRoot.anchorMin = new Vector2(0.5f, 0.5f);
        damageNumberRoot.anchorMax = new Vector2(0.5f, 0.5f);
        damageNumberRoot.pivot = new Vector2(0.5f, 0.5f);
        damageNumberRoot.anchoredPosition = Vector2.zero;
        damageNumberRoot.sizeDelta = new Vector2(220f, 220f);

        damageNumberContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
        damageNumberContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
        damageNumberContentRoot.pivot = new Vector2(0.5f, 0.5f);
        damageNumberContentRoot.anchoredPosition = Vector2.zero;
        damageNumberContentRoot.sizeDelta = damageNumberRoot.sizeDelta;
    }

    private Vector2 GetRandomBodyDamageNumberPosition()
    {
        if (damageNumberContentRoot == null)
            return Vector2.zero;

        Canvas canvas = damageNumberContentRoot.GetComponentInParent<Canvas>();
        Camera cam = null;

        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        if (cam == null)
            cam = Camera.main;

        // 1. 优先取专用锚点（推荐挂在胸口/上半身）
        Vector3 baseWorld;
        if (damageNumberAnchor != null)
        {
            baseWorld = damageNumberAnchor.position + damageNumberAnchorOffset;
        }
        else
        {
            // 2. 没锚点时，退回到所有 Renderer 的包围盒中上部
            baseWorld = transform.position;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                // 取更靠近胸口的位置，而不是正中心或脚底
                baseWorld = new Vector3(
                    bounds.center.x,
                    bounds.min.y + bounds.size.y * 0.72f,
                    bounds.center.z);
            }
            else
            {
                // 最后兜底，也直接抬高到上半身
                baseWorld = transform.position + Vector3.up * 1.35f;
            }
        }

        // 3. 围绕基准点做“偏上半身”的随机角度 + 多档距离分布
        float angleDeg;
        float anglePick = UnityEngine.Random.value;

        // 主要分布：左上 / 正上 / 右上
        if (anglePick < 0.30f)
        {
            angleDeg = UnityEngine.Random.Range(120f, 155f);   // 左上
        }
        else if (anglePick < 0.70f)
        {
            angleDeg = UnityEngine.Random.Range(72f, 108f);    // 正上附近
        }
        else
        {
            angleDeg = UnityEngine.Random.Range(25f, 60f);     // 右上
        }

        // 少量扩展到两侧偏上，增强分散感，但不往脚下掉
        if (UnityEngine.Random.value < 0.22f)
        {
            angleDeg = UnityEngine.Random.value < 0.5f
                ? UnityEngine.Random.Range(155f, 192f)         // 左侧偏上
                : UnityEngine.Random.Range(-12f, 25f);         // 右侧偏上
        }

        float angle = angleDeg * Mathf.Deg2Rad;

        // 距离做成三档，让分布更散，不全挤在一圈
        float radiusPick = UnityEngine.Random.value;
        float radius;
        if (radiusPick < 0.38f)
        {
            radius = UnityEngine.Random.Range(0.10f, 0.16f);   // 近圈
        }
        else if (radiusPick < 0.80f)
        {
            radius = UnityEngine.Random.Range(0.16f, 0.26f);   // 中圈
        }
        else
        {
            radius = UnityEngine.Random.Range(0.26f, 0.36f);   // 外圈
        }

        // 椭圆分布：横向略宽，纵向略窄，更贴近身体范围
        float offsetX = Mathf.Cos(angle) * radius * 1.35f;
        float offsetY = Mathf.Sin(angle) * radius * 1.00f;

        Vector3 worldPoint =
            baseWorld +
            transform.right * offsetX +
            Vector3.up * offsetY;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPoint);

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            damageNumberContentRoot,
            screenPoint,
            canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
            out localPoint);

        localPoint.x += UnityEngine.Random.Range(-8f, 8f);
        localPoint.y += UnityEngine.Random.Range(-5f, 5f);

        return localPoint;
    }

    private DamageNumberView GetOrCreateDamageNumberView()
    {
        if (damageNumberContentRoot == null)
            return null;

        for (int i = 0; i < runtimeDamageNumbers.Count; i++)
        {
            DamageNumberView item = runtimeDamageNumbers[i];
            if (item != null && !item.IsPlaying)
                return item;
        }

        GameObject go = new GameObject($"DamageNumber_{runtimeDamageNumbers.Count}", typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(DamageNumberView));
        go.transform.SetParent(damageNumberContentRoot, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(128f, 32f);

        DamageNumberView view = go.GetComponent<DamageNumberView>();
        view.EnsureStructure();
        ApplyDamageNumberStyle(view);
        go.layer = runtimeUiRoot != null ? runtimeUiRoot.gameObject.layer : go.layer;
        runtimeDamageNumbers.Add(view);
        return view;
    }

    private void ApplyDamageNumberStyle(DamageNumberView view)
    {
        if (view == null)
            return;

        if (appliedStyle != null && appliedStyle.damageNumberFontAsset != null)
            view.ApplyFontAsset(appliedStyle.damageNumberFontAsset);
    }

    private void RebuildBgLayers(OverheadBarStyleAsset style)
    {
        runtimeBgImages.Clear();

        if (bgLayerRoot == null || style == null)
            return;

        HashSet<string> usedNames = new HashSet<string>();

        for (int i = 0; i < style.backgroundLayers.Count; i++)
        {
            OverheadBarBackgroundLayer layer = style.backgroundLayers[i];
            if (layer == null)
                continue;

            string layerName = string.IsNullOrWhiteSpace(layer.layerName) ? $"BgLayer_{i}" : layer.layerName;
            string uniqueName = layerName;
            int suffix = 1;
            while (!usedNames.Add(uniqueName))
            {
                uniqueName = $"{layerName}_{suffix}";
                suffix++;
            }

            Transform existing = bgLayerRoot.Find(uniqueName);
            RectTransform rt;
            RawImage image;

            if (existing != null)
            {
                rt = existing as RectTransform;
                image = existing.GetComponent<RawImage>();

                if (rt == null)
                    rt = existing.gameObject.GetComponent<RectTransform>() ?? existing.gameObject.AddComponent<RectTransform>();

                if (image == null)
                    image = existing.gameObject.AddComponent<RawImage>();
            }
            else
            {
                GameObject go = new GameObject(uniqueName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(bgLayerRoot, false);
                rt = go.GetComponent<RectTransform>();
                image = go.GetComponent<RawImage>();
            }

            Vector2 size = layer.sizeOverride == Vector2.zero ? appliedStyle.barSize : layer.sizeOverride;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(layer.offset.x, -layer.offset.y);
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, layer.rotationZ);
            rt.localScale = Vector3.one;

            image.texture = layer.texture;
            image.color = layer.useTint ? layer.tint : Color.white;
            image.raycastTarget = false;

            runtimeBgImages.Add(image);
            rt.SetSiblingIndex(i);
        }

        for (int i = bgLayerRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = bgLayerRoot.GetChild(i);
            if (!usedNames.Contains(child.name))
                SafeDestroy(child.gameObject);
        }

        bgLayerRoot.SetAsFirstSibling();
    }

    private void ClearBgLayers()
    {
        runtimeBgImages.Clear();

        if (bgLayerRoot == null)
            return;

        for (int i = bgLayerRoot.childCount - 1; i >= 0; i--)
            SafeDestroy(bgLayerRoot.GetChild(i).gameObject);
    }

    private void RefreshBarVisuals()
    {
        if (slotView01 == null || fillMaskRoot == null || damageRefMaskRoot == null ||
            fillImage == null || damageReferenceImage == null)
            return;

        Vector2 total = slotView01.sizeDelta;
        float left = appliedStyle != null ? appliedStyle.paddingLeft : 0f;
        float right = appliedStyle != null ? appliedStyle.paddingRight : 0f;
        float top = appliedStyle != null ? appliedStyle.paddingTop : 0f;
        float bottom = appliedStyle != null ? appliedStyle.paddingBottom : 0f;

        float innerWidth = Mathf.Max(0f, total.x - left - right);
        float innerHeight = Mathf.Max(0f, total.y - top - bottom);
        float verticalOffset = (bottom - top) * 0.5f;

        damageRefMaskRoot.anchoredPosition = new Vector2(left, verticalOffset);
        fillMaskRoot.anchoredPosition = new Vector2(left, verticalOffset);

        damageRefMaskRoot.sizeDelta = new Vector2(innerWidth * Mathf.Clamp01(damageReferencePercent), innerHeight);
        fillMaskRoot.sizeDelta = new Vector2(innerWidth * Mathf.Clamp01(currentPercent), innerHeight);

        ConfigureBarImage(damageReferenceImage.rectTransform, new Vector2(innerWidth, innerHeight));
        ConfigureBarImage(fillImage.rectTransform, new Vector2(innerWidth, innerHeight));

        if (slotCanvasGroup != null)
            slotCanvasGroup.alpha = currentDisplayAlpha;
        else if (slotRoot01 != null)
            slotRoot01.gameObject.SetActive(currentDisplayAlpha > 0.001f);
    }

    private void UpdateDisplayAlphaTarget()
    {
        bool hideWhenFull = appliedStyle != null && appliedStyle.hideWhenFull;
        float threshold = appliedStyle != null ? appliedStyle.fullHpThreshold : 0.999f;
        targetDisplayAlpha = (!hideWhenFull || targetPercent < threshold) ? 1f : 0f;
    }

    private void ConfigureDefaultAnchors()
    {
        if (runtimeUiRoot != null)
        {
            runtimeUiRoot.anchorMin = new Vector2(0.5f, 0.5f);
            runtimeUiRoot.anchorMax = new Vector2(0.5f, 0.5f);
            runtimeUiRoot.pivot = new Vector2(0.5f, 0.5f);
            runtimeUiRoot.anchoredPosition = Vector2.zero;
            runtimeUiRoot.sizeDelta = new Vector2(256f, 128f);
            runtimeUiRoot.localScale = RuntimeRootScale;
        }

        if (nameRoot != null)
            SetRectCenter(nameRoot, new Vector2(0f, 16f));

        if (slotRoot01 != null)
            SetRectCenter(slotRoot01, Vector2.zero);

        if (slotView01 != null)
        {
            SetRectCenter(slotView01, Vector2.zero);
            slotView01.sizeDelta = new Vector2(150f, 14f);
        }

        if (bgLayerRoot != null)
            SetRectCenter(bgLayerRoot, Vector2.zero);

        if (statusRoot != null)
            SetRectCenter(statusRoot, Vector2.zero);

        if (statusAreaRoot != null)
        {
            statusAreaRoot.anchorMin = new Vector2(0.5f, 0.5f);
            statusAreaRoot.anchorMax = new Vector2(0.5f, 0.5f);
            statusAreaRoot.pivot = new Vector2(0.5f, 0.5f);
            statusAreaRoot.anchoredPosition = Vector2.zero;
            statusAreaRoot.sizeDelta = new Vector2(120f, 28f);
        }

        if (statusContentRoot != null)
        {
            statusContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
            statusContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
            statusContentRoot.pivot = new Vector2(0.5f, 0.5f);
            statusContentRoot.anchoredPosition = Vector2.zero;
            statusContentRoot.sizeDelta = new Vector2(120f, 28f);
        }

        if (damageNumberRoot != null)
        {
            damageNumberRoot.anchorMin = new Vector2(0.5f, 0.5f);
            damageNumberRoot.anchorMax = new Vector2(0.5f, 0.5f);
            damageNumberRoot.pivot = new Vector2(0.5f, 0.5f);
            damageNumberRoot.anchoredPosition = Vector2.zero;
            damageNumberRoot.sizeDelta = new Vector2(180f, 80f);
        }

        if (damageNumberContentRoot != null)
        {
            damageNumberContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
            damageNumberContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
            damageNumberContentRoot.pivot = new Vector2(0.5f, 0.5f);
            damageNumberContentRoot.anchoredPosition = Vector2.zero;
            damageNumberContentRoot.sizeDelta = new Vector2(180f, 80f);
        }
    }

    private void ApplyUnitDefinitionTransformOverrides()
    {
        if (runtimeUiRoot == null)
            return;

        Vector3 localPosition = Vector3.zero;

        float offsetX = 0f;
        float offsetY = 0f;
        bool hasOffsetX = TryGetFloatFromUnitDefinition(new[] { "overheadUiOffsetX", "unitUiOffsetX" }, out offsetX);
        bool hasOffsetY = TryGetFloatFromUnitDefinition(new[] { "overheadUiOffsetY", "unitUiOffsetY" }, out offsetY);

        if (hasOffsetX)
            localPosition.x += offsetX;

        if (hasOffsetY)
            localPosition.y += offsetY;

        runtimeUiRoot.localPosition = localPosition;
        runtimeUiRoot.localEulerAngles = Vector3.zero;
        runtimeUiRoot.localScale = RuntimeRootScale;

        if (debugLogs)
        {
            string anchorInfo = overheadAnchor != null ? overheadAnchor.localPosition.ToString("F3") : "null";
            Log($"ApplyUnitDefinitionTransformOverrides | overheadAnchor.localPosition={anchorInfo} | defOffsetX={(hasOffsetX ? offsetX.ToString() : "none")} | defOffsetY={(hasOffsetY ? offsetY.ToString() : "none")} | runtimeUiRoot.localPosition={runtimeUiRoot.localPosition}");
        }
    }

    private bool TryGetFloatFromUnitDefinition(string[] names, out float value)
    {
        value = 0f;
        if (unitDefinition == null || names == null)
            return false;

        Type type = unitDefinition.GetType();
        for (int i = 0; i < names.Length; i++)
        {
            FieldInfo field = type.GetField(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                value = (float)field.GetValue(unitDefinition);
                return true;
            }

            PropertyInfo property = type.GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.PropertyType == typeof(float))
            {
                value = (float)property.GetValue(unitDefinition, null);
                return true;
            }
        }

        return false;
    }

    private int CountVisibleStatuses(IReadOnlyList<StatusViewData> statuses)
    {
        if (statuses == null)
            return 0;

        int count = 0;
        for (int i = 0; i < statuses.Count; i++)
        {
            if (IsRenderableStatus(statuses[i]))
                count++;
        }

        return count;
    }

    private void FillVisibleStatusesBuffer(IReadOnlyList<StatusViewData> statuses, List<StatusViewData> result)
    {
        result.Clear();
        if (statuses == null)
            return;

        int limit = maxRuntimeStatusIconPool > 0 ? maxRuntimeStatusIconPool : int.MaxValue;
        for (int i = 0; i < statuses.Count; i++)
        {
            if (!IsRenderableStatus(statuses[i]))
                continue;

            result.Add(statuses[i]);
            if (result.Count >= limit)
                break;
        }
    }

    private bool IsRenderableStatus(StatusViewData data)
    {
        if (!data.visible)
            return false;

        if (data.icon == null)
            return false;

        bool usesDurationMask = !data.hideDurationMask && data.durationFillMode != StatusDurationFillMode.None;
        if (usesDurationMask && data.remainingNormalized <= 0.001f)
            return false;

        return true;
    }

    private void EnsureStatusIconPool(int count)
    {
        if (statusContentRoot == null)
            return;

        int hardCap = Mathf.Max(1, maxRuntimeStatusIconPool);
        int target = Mathf.Clamp(Mathf.Max(1, count), 1, hardCap);

        for (int i = runtimeStatusIcons.Count - 1; i >= 0; i--)
        {
            if (runtimeStatusIcons[i] == null)
                runtimeStatusIcons.RemoveAt(i);
        }

        // Reuse existing children first. This is important after hot reloads / prefab revisions where icons already exist
        // under StatusContentRoot but the non-serialized runtime list has been rebuilt empty.
        for (int i = 0; i < statusContentRoot.childCount && runtimeStatusIcons.Count < target; i++)
        {
            UnitStatusIconView existing = statusContentRoot.GetChild(i).GetComponent<UnitStatusIconView>();
            if (existing != null && !runtimeStatusIcons.Contains(existing))
            {
                existing.EnsureStructure();
                runtimeStatusIcons.Add(existing);
            }
        }

        while (runtimeStatusIcons.Count < target)
        {
            GameObject go = new GameObject($"StatusIcon_{runtimeStatusIcons.Count}", typeof(RectTransform), typeof(UnitStatusIconView));
            go.transform.SetParent(statusContentRoot, false);

            UnitStatusIconView view = go.GetComponent<UnitStatusIconView>();
            view.EnsureStructure();

            runtimeStatusIcons.Add(view);
        }
    }

    private void CompactStatusIconChildren()
    {
        if (statusContentRoot == null)
            return;

        int hardCap = Mathf.Max(1, maxRuntimeStatusIconPool);
        int kept = 0;

        for (int i = statusContentRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = statusContentRoot.GetChild(i);
            if (child == null)
                continue;

            UnitStatusIconView view = child.GetComponent<UnitStatusIconView>();
            if (view == null)
                continue;

            kept++;
            if (kept > hardCap)
            {
                runtimeStatusIcons.Remove(view);
                SafeDestroy(child.gameObject);
            }
        }
    }

    private void EnsureCanvasComponents(RectTransform root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
            canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        if (root.GetComponent<CanvasScaler>() == null)
            root.gameObject.AddComponent<CanvasScaler>();

        if (root.GetComponent<GraphicRaycaster>() == null)
            root.gameObject.AddComponent<GraphicRaycaster>();
    }

    private CanvasGroup EnsureCanvasGroup(RectTransform target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
            group = target.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private Transform EnsureTransformChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private RectTransform EnsureRectChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        RectTransform rect = child as RectTransform;
        if (rect != null)
            return rect;

        string safeName = child != null ? (childName + "_RuntimeUI") : childName;
        Transform runtimeChild = parent.Find(safeName);
        RectTransform runtimeRect = runtimeChild as RectTransform;
        if (runtimeRect != null)
            return runtimeRect;

        GameObject go = new GameObject(safeName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private RectTransform EnsureMaskRoot(Transform parent, string childName)
    {
        RectTransform rect = EnsureRectChild(parent, childName);
        RectMask2D mask = rect.GetComponent<RectMask2D>();
        if (mask == null)
            mask = rect.gameObject.AddComponent<RectMask2D>();
        return rect;
    }

    private TextMeshProUGUI EnsureTMPChild(Transform parent, string childName, string defaultText)
    {
        RectTransform rect = EnsureRectChild(parent, childName);
        TextMeshProUGUI tmp = rect.GetComponent<TextMeshProUGUI>();
        if (tmp == null)
            tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();

        tmp.text = string.IsNullOrWhiteSpace(tmp.text) ? defaultText : tmp.text;
        tmp.raycastTarget = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 13f;
        tmp.color = Color.white;
        return tmp;
    }

    private RawImage EnsureRawImageChild(Transform parent, string childName)
    {
        RectTransform rect = EnsureRectChild(parent, childName);
        RawImage image = rect.GetComponent<RawImage>();
        if (image == null)
            image = rect.gameObject.AddComponent<RawImage>();

        image.raycastTarget = false;
        return image;
    }

    private void ConfigureMaskRoot(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private void ConfigureBarImage(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private void SetRectCenter(RectTransform rect, Vector2 anchored)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(anchored.x, -anchored.y);
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null)
            return;

        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    private void TryAutoResolveUnitDefinition()
    {
        if (unitDefinition != null)
            return;

        if (runtimeBinder == null)
            runtimeBinder = GetComponent("UnitDefinitionRuntimeBinder") ?? GetComponentInParent(typeof(MonoBehaviour), true);

        if (runtimeBinder != null)
        {
            if (TryExtractUnitDefinition(runtimeBinder, out UnitDefinition explicitDef))
            {
                unitDefinition = explicitDef;
                Log("Resolved UnitDefinition from runtimeBinder.");
                return;
            }
        }

        MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (TryExtractUnitDefinition(behaviour, out UnitDefinition value))
            {
                unitDefinition = value;
                Log("Auto resolved UnitDefinition from component: " + behaviour.GetType().Name);
                return;
            }
        }
    }

    private bool TryExtractUnitDefinition(object source, out UnitDefinition value)
    {
        value = null;
        if (source == null)
            return false;

        Type type = source.GetType();
        string[] preferredNames = { "unitDefinition", "definition", "currentDefinition", "runtimeDefinition", "resolvedDefinition", "UnitDefinition" };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            FieldInfo field = type.GetField(preferredNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && typeof(UnitDefinition).IsAssignableFrom(field.FieldType))
            {
                value = field.GetValue(source) as UnitDefinition;
                if (value != null)
                    return true;
            }

            PropertyInfo property = type.GetProperty(preferredNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && typeof(UnitDefinition).IsAssignableFrom(property.PropertyType))
            {
                value = property.GetValue(source, null) as UnitDefinition;
                if (value != null)
                    return true;
            }
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (typeof(UnitDefinition).IsAssignableFrom(field.FieldType))
            {
                value = field.GetValue(source) as UnitDefinition;
                if (value != null)
                    return true;
            }
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (!property.CanRead)
                continue;

            if (typeof(UnitDefinition).IsAssignableFrom(property.PropertyType))
            {
                try
                {
                    value = property.GetValue(source, null) as UnitDefinition;
                    if (value != null)
                        return true;
                }
                catch { }
            }
        }

        return false;
    }

    private void DebugDumpOffsets(string stage)
    {
        if (!debugLogs)
            return;

        Vector3 anchorLocal = overheadAnchor != null ? overheadAnchor.localPosition : Vector3.zero;
        Vector3 runtimeLocal = runtimeUiRoot != null ? runtimeUiRoot.localPosition : Vector3.zero;
        Vector2 barOffset = appliedStyle != null ? appliedStyle.hpBarOffset : Vector2.zero;
        Vector2 slotPos = slotRoot01 != null ? slotRoot01.anchoredPosition : Vector2.zero;
        Vector2 statusPos = statusAreaRoot != null ? statusAreaRoot.anchoredPosition : Vector2.zero;

        Log($"{stage} | anchorLocal={anchorLocal} | runtimeLocal={runtimeLocal} | hpBarOffset={barOffset} | slotRoot01={slotPos} | statusAreaRoot={statusPos}");
    }

    private void SafeDestroy(GameObject go)
    {
        if (go == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (go != null)
                    DestroyImmediate(go);
            };
            return;
        }
#endif
        Destroy(go);
    }

    private void SafeDestroyComponent(Component comp)
    {
        if (comp == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (comp != null)
                    DestroyImmediate(comp);
            };
            return;
        }
#endif
        Destroy(comp);
    }

    private void Log(string message)
    {
        if (!debugLogs)
            return;

        Debug.Log("[UnitOverheadUIView] " + message, this);
    }
}