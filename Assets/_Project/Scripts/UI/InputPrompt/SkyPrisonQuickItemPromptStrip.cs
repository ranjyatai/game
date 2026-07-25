using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Displays current input binding icons under the four quick item slots.
///
/// V33 rule:
/// - The prompt overlay is no longer positioned in global CleanOverlayRoot space.
/// - It is re-homed under the visible QuickSlotsArea as a module-local overlay.
/// - It is NOT placed under the module post-process SourceRoot, so it is not captured by the RT/chromatic shader.
/// - It uses Slot_01~04 bounds relative to QuickSlotsContent, then draws those same local positions in the visible module overlay.
/// - No hard-coded screen offsets and no GameView/Workbench screen-coordinate conversion.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SkyPrisonQuickItemPromptStrip : MonoBehaviour
{
    private const string PromptOverlayName = "QuickItemPromptOverlay";
    private const string PromptPrefix = "QuickItemPrompt_";

    [Header("Visibility")]
    [SerializeField] private bool showKeyPrompts = true;

    [Header("Target - Workbench Bound")]
    [SerializeField] private RectTransform quickSlotsRoot;
    [SerializeField] private RectTransform[] slotRects = new RectTransform[4];

    [Header("Data")]
    [SerializeField] private SkyPrisonInputSettings inputSettings;
    [SerializeField] private SkyPrisonInputPromptIconDatabase iconDatabase;

    // 运行时缓存：其它 UI（如底部窗口操作提示条）借用同一份键帽数据库 / 输入设置。
    public static SkyPrisonInputPromptIconDatabase RuntimeDatabase { get; private set; }
    public static SkyPrisonInputSettings RuntimeInputSettings { get; private set; }

    [Header("Device Priority")]
    [SerializeField] private bool preferGamepadWhenConnected = true;
    [SerializeField] private SkyPrisonInputPromptDeviceStyle gamepadStyle = SkyPrisonInputPromptDeviceStyle.GamepadXbox;
    [SerializeField, Min(0.05f)] private float devicePollInterval = 0.5f;

    [Header("Layout")]
    [SerializeField] private Vector2 iconSize = new Vector2(32f, 32f);
    [SerializeField] private float horizontalOffset = 0f;
    [SerializeField] private float bottomYOffset = -18f;
    [SerializeField] private Color iconTint = Color.white;

    [Header("Actions")]
    [SerializeField] private SkyPrisonInputAction[] quickItemActions = new SkyPrisonInputAction[4]
    {
        SkyPrisonInputAction.QuickItem1,
        SkyPrisonInputAction.QuickItem2,
        SkyPrisonInputAction.QuickItem3,
        SkyPrisonInputAction.QuickItem4,
    };

    [Header("Runtime Diagnostic - Read Only")]
    [SerializeField] private bool runtimeHasGamepad;
    [SerializeField] private string runtimeJoystickSignature = "";
    [SerializeField] private string[] runtimeResolvedIconKeys = new string[4];
    [SerializeField] private KeyCode[] runtimeResolvedKeys = new KeyCode[4];
    [SerializeField] private string placementStatus = "V33 module display-side clean overlay";

    private readonly Image[] promptImages = new Image[4];
    private float nextDevicePollTime;

    public bool ShowKeyPrompts
    {
        get => showKeyPrompts;
        set
        {
            if (showKeyPrompts == value)
                return;
            showKeyPrompts = value;
            RefreshNow();
        }
    }

    public RectTransform QuickSlotsRoot => quickSlotsRoot;

    public void SetQuickSlotsRoot(RectTransform root)
    {
        quickSlotsRoot = root;
        RefreshNow();
    }

    public void BindQuickSlots(RectTransform root, RectTransform[] slots)
    {
        quickSlotsRoot = root;
        EnsureSlotArray();
        for (int i = 0; i < 4; i++)
            slotRects[i] = slots != null && i < slots.Length ? slots[i] : null;
        RefreshNow();
    }

    public bool RebindFromCurrentHudHierarchy()
    {
        EnsureSlotArray();

        RectTransform safeArea = FindSafeAreaFromAnyKnownPoint();
        if (safeArea == null)
            return false;

        RectTransform content = ResolveQuickSlotsContent(safeArea);
        if (content == null)
            return false;

        bool any = false;
        for (int i = 0; i < 4; i++)
        {
            RectTransform slot = ResolveSlotUnderKnownQuickSlotsBranch(content, i);
            slotRects[i] = slot;
            if (slot != null)
                any = true;
        }

        if (!any)
            return false;

        quickSlotsRoot = content;
        placementStatus = "V26 rebound slots from current HUD QuickSlotsContent.";
        return true;
    }

    private void Reset()
    {
        AutoAssignAssetsIfPossible();
        EnsureSlotArray();
        RefreshNow();
    }

    private void OnEnable()
    {
        AutoAssignAssetsIfPossible();
        if (iconDatabase != null) RuntimeDatabase = iconDatabase;
        if (inputSettings != null) RuntimeInputSettings = inputSettings;
        EnsureSlotArray();
#if UNITY_EDITOR
        if (!Application.isPlaying) { SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged += HandleDeviceFamilyChanged; return; }
#endif
        RefreshNow();
        SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged += HandleDeviceFamilyChanged;
    }

    private void OnDisable()
    {
        SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged -= HandleDeviceFamilyChanged;
    }

    private void HandleDeviceFamilyChanged(SkyPrisonInputDeviceTracker.DeviceFamily _, SkyPrisonInputDeviceTracker.DeviceFamily __)
        => RefreshPromptSprites();

    private void OnValidate()
    {
        if (iconSize.x < 1f) iconSize.x = 1f;
        if (iconSize.y < 1f) iconSize.y = 1f;
        if (devicePollInterval < 0.05f) devicePollInterval = 0.05f;

        AutoAssignAssetsIfPossible();
        EnsureSlotArray();
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        RefreshNow();
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        EnsurePromptOverlayIsModuleLocal();
        EnsurePromptImages();
        EnsureCurrentHudSlotRefs();
        RefreshPromptLayout();

        float now = Application.isPlaying ? Time.unscaledTime : (float)GetEditorTime();
        if (now < nextDevicePollTime)
            return;
        nextDevicePollTime = now + devicePollInterval;

        string signature = BuildJoystickSignature();
        if (!string.Equals(signature, runtimeJoystickSignature, System.StringComparison.Ordinal))
        {
            runtimeJoystickSignature = signature;
            runtimeHasGamepad = HasConnectedGamepadFromSignature(signature);
            RefreshPromptSprites();
        }
    }

    public void RefreshNow()
    {
        AutoAssignAssetsIfPossible();
        EnsureSlotArray();
        EnsurePromptOverlayIsModuleLocal();
        EnsurePromptImages();
        EnsureCurrentHudSlotRefs();
        RefreshPromptSprites();
        RefreshPromptLayout();
    }

    private void EnsurePromptOverlayIsModuleLocal()
    {
        RectTransform visibleQuickSlotsArea = FindVisibleQuickSlotsArea();
        RectTransform self = transform as RectTransform;
        if (visibleQuickSlotsArea == null || self == null)
            return;

        // V26: The overlay must be a singleton direct child of the visible QuickSlotsArea.
        // Workbench repaints/saves repeatedly, and older versions created one overlay per repaint.
        // If duplicates exist, keep exactly one strip-bearing overlay and destroy the rest.
        RectTransform singleton = ResolveOrCreateModuleOverlaySingleton(visibleQuickSlotsArea, self);
        if (singleton == null)
            return;

        if (singleton != self)
        {
            // Another overlay is now the legal owner.  Destroy this duplicate component object so it
            // cannot keep spawning prompt children every ExecuteAlways tick.
            DestroyObject(gameObject);
            return;
        }

        if (self.parent != visibleQuickSlotsArea)
            self.SetParent(visibleQuickSlotsArea, false);

        gameObject.name = PromptOverlayName;

        // Full module-local overlay.  Positioning is driven by QuickSlotsContent-relative bounds below.
        self.anchorMin = new Vector2(0.5f, 0.5f);
        self.anchorMax = new Vector2(0.5f, 0.5f);
        self.pivot = new Vector2(0.5f, 0.5f);
        self.anchoredPosition = Vector2.zero;
        self.sizeDelta = GetUsableOverlaySize(visibleQuickSlotsArea);
        self.localScale = Vector3.one;
        self.localRotation = Quaternion.identity;
        self.SetAsLastSibling();

        // Nested canvas only.  It stays in the module coordinate space, but renders over the module RT output.
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler != null)
            DestroyObject(scaler);

        GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            DestroyObject(raycaster);

        CanvasGroup group = GetComponent<CanvasGroup>();
        if (group == null)
            group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        CleanupDuplicateModuleOverlays(visibleQuickSlotsArea, self);
        CleanupOldCleanOverlayPromptCopies(visibleQuickSlotsArea);
    }

    private RectTransform ResolveOrCreateModuleOverlaySingleton(RectTransform visibleQuickSlotsArea, RectTransform self)
    {
        if (visibleQuickSlotsArea == null || self == null)
            return null;

        RectTransform firstStripOverlay = null;
        RectTransform firstEmptyOverlay = null;

        for (int i = 0; i < visibleQuickSlotsArea.childCount; i++)
        {
            Transform child = visibleQuickSlotsArea.GetChild(i);
            if (child == null || !string.Equals(child.name, PromptOverlayName, System.StringComparison.Ordinal))
                continue;

            RectTransform childRect = child as RectTransform;
            if (childRect == null)
                continue;

            if (child.GetComponent<SkyPrisonQuickItemPromptStrip>() != null)
            {
                if (firstStripOverlay == null)
                    firstStripOverlay = childRect;
            }
            else if (firstEmptyOverlay == null)
            {
                firstEmptyOverlay = childRect;
            }
        }

        if (self.parent == visibleQuickSlotsArea)
        {
            if (firstStripOverlay == null || firstStripOverlay == self)
                return self;
        }

        if (firstStripOverlay != null)
            return firstStripOverlay;

        if (firstEmptyOverlay != null && firstEmptyOverlay != self)
            DestroyObject(firstEmptyOverlay.gameObject);

        return self;
    }

    private void CleanupDuplicateModuleOverlays(RectTransform visibleQuickSlotsArea, RectTransform validOverlay)
    {
        if (visibleQuickSlotsArea == null || validOverlay == null)
            return;

        for (int i = visibleQuickSlotsArea.childCount - 1; i >= 0; i--)
        {
            Transform child = visibleQuickSlotsArea.GetChild(i);
            if (child == null || child == validOverlay)
                continue;

            if (string.Equals(child.name, PromptOverlayName, System.StringComparison.Ordinal))
                DestroyObject(child.gameObject);
        }
    }

    private Vector2 GetUsableOverlaySize(RectTransform visibleQuickSlotsArea)
    {
        Vector2 size = visibleQuickSlotsArea != null ? visibleQuickSlotsArea.rect.size : Vector2.zero;
        if (size.x < 1f || size.y < 1f)
        {
            RectTransform root = quickSlotsRoot;
            if (root != null && root.rect.width > 1f && root.rect.height > 1f)
                size = root.rect.size;
        }
        if (size.x < 1f) size.x = 660f;
        if (size.y < 1f) size.y = 120f;
        return size;
    }

    private void CleanupOldCleanOverlayPromptCopies(RectTransform visibleQuickSlotsArea)
    {
        RectTransform safeArea = FindSafeAreaFromAnyKnownPoint();
        if (safeArea == null)
            return;

        Transform oldCleanOverlay = safeArea.Find("CleanOverlayRoot/" + PromptOverlayName);
        if (oldCleanOverlay != null && oldCleanOverlay != transform)
            DestroyObject(oldCleanOverlay.gameObject);

        Transform hudOldCleanOverlay = safeArea.Find("HUDRoot/CleanOverlayRoot/" + PromptOverlayName);
        if (hudOldCleanOverlay != null && hudOldCleanOverlay != transform)
            DestroyObject(hudOldCleanOverlay.gameObject);

        // Remove prompt children accidentally left directly under QuickSlotsContent/Area by old versions.
        Transform hudRoot = safeArea.Find("HUDRoot");
        if (hudRoot == null)
            return;

        Transform quickSlotsArea = hudRoot.Find("QuickSlotsArea");
        if (quickSlotsArea == null)
            return;

        for (int i = quickSlotsArea.childCount - 1; i >= 0; i--)
        {
            Transform child = quickSlotsArea.GetChild(i);
            if (child == null || child == transform)
                continue;
            if (child.name.StartsWith(PromptPrefix, System.StringComparison.Ordinal))
                DestroyObject(child.gameObject);
        }

        Transform content = quickSlotsArea.Find("QuickSlotsContent");
        if (content == null)
            return;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform child = content.GetChild(i);
            if (child != null && child.name.StartsWith(PromptPrefix, System.StringComparison.Ordinal))
                DestroyObject(child.gameObject);
        }
    }

    private void EnsureCurrentHudSlotRefs()
    {
        EnsureSlotArray();

        RectTransform safeArea = FindSafeAreaFromAnyKnownPoint();
        if (safeArea == null)
            return;

        bool needsRebind = quickSlotsRoot == null;
        if (!needsRebind)
        {
            for (int i = 0; i < 4; i++)
            {
                if (slotRects[i] == null)
                {
                    needsRebind = true;
                    break;
                }
            }
        }

        // Do not reject valid workbench-bound refs just because a post-process SourceRoot changed world space.
        // Rebind only when something is actually missing.
        if (needsRebind)
            RebindFromCurrentHudHierarchy();
    }

    private void EnsurePromptImages()
    {
        RectTransform parentRect = transform as RectTransform;
        if (parentRect == null)
            return;

        for (int i = 0; i < 4; i++)
        {
            string childName = PromptPrefix + (i + 1).ToString("00");
            Transform existing = transform.Find(childName);
            Image image = existing != null ? existing.GetComponent<Image>() : null;

            if (image == null)
            {
                GameObject go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(transform, false);
                image = go.GetComponent<Image>();
            }

            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = iconSize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            image.raycastTarget = false;
            image.preserveAspect = true;
            image.color = iconTint;
            // V33: prompt icons are clean display-side overlay graphics; never let the HUD chromatic
            // material stick to these Images from a global style pass.
            image.material = null;
            promptImages[i] = image;
        }

        for (int childIndex = transform.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = transform.GetChild(childIndex);
            if (child == null || !child.name.StartsWith(PromptPrefix, System.StringComparison.Ordinal))
                continue;

            bool legal = false;
            for (int i = 0; i < 4; i++)
            {
                if (promptImages[i] != null && promptImages[i].transform == child)
                {
                    legal = true;
                    break;
                }
            }
            if (!legal)
                DestroyObject(child.gameObject);
        }
    }

    private void RefreshPromptSprites()
    {
        // 以 DeviceTracker 的实际最近使用设备为准（接了手柄但在用键盘 → 显示键盘图标）
        bool hasGamepad = preferGamepadWhenConnected &&
                          SkyPrisonInputDeviceTracker.Current == SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;
        runtimeHasGamepad = hasGamepad;
        runtimeJoystickSignature = BuildJoystickSignature();
        EnsureDiagnosticsArrays();

        for (int i = 0; i < 4; i++)
        {
            Image image = promptImages[i];
            if (image == null)
                continue;

            if (!showKeyPrompts || inputSettings == null || iconDatabase == null || quickItemActions == null || i >= quickItemActions.Length)
            {
                image.enabled = false;
                runtimeResolvedIconKeys[i] = "";
                runtimeResolvedKeys[i] = KeyCode.None;
                continue;
            }

            Sprite sprite;
            string iconKey;
            KeyCode key;
            bool ok = SkyPrisonInputPromptResolver.TryResolveActionIcon(
                inputSettings,
                iconDatabase,
                quickItemActions[i],
                preferGamepadWhenConnected,
                gamepadStyle,
                out sprite,
                out iconKey,
                out key);

            image.sprite = ok ? sprite : null;
            image.color = iconTint;
            image.enabled = ok && sprite != null && showKeyPrompts;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.material = null;

            runtimeResolvedIconKeys[i] = ok ? iconKey : "";
            runtimeResolvedKeys[i] = ok ? key : KeyCode.None;
        }
    }

    private void RefreshPromptLayout()
    {
        RectTransform overlayRect = transform as RectTransform;
        if (overlayRect == null)
            return;

        EnsureSlotArray();
        EnsurePromptImages();
        Canvas.ForceUpdateCanvases();

        RectTransform contentRoot = quickSlotsRoot;
        if (contentRoot == null)
        {
            placementStatus = "V26 missing QuickSlotsContent. Prompt hidden.";
            SetPromptImagesVisible(false);
            return;
        }

        bool placedAny = false;
        int invalidCount = 0;
        string lastDebug = "";

        for (int i = 0; i < 4; i++)
        {
            Image image = promptImages[i];
            if (image == null)
            {
                invalidCount++;
                continue;
            }

            RectTransform slot = slotRects != null && i < slotRects.Length ? slotRects[i] : null;
            if (slot == null)
            {
                image.enabled = false;
                invalidCount++;
                lastDebug = $"slot[{i}]=null";
                continue;
            }

            Vector2 pos;
            string debug;
            if (!TryCalculateModuleLocalPromptPosition(contentRoot, slot, out pos, out debug))
            {
                image.enabled = false;
                invalidCount++;
                lastDebug = debug;
                continue;
            }

            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = iconSize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = pos;
            rect.SetAsLastSibling();

            image.gameObject.SetActive(showKeyPrompts);
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.color = iconTint;
            if (showKeyPrompts && image.sprite != null)
                image.enabled = true;

            placedAny = true;
            lastDebug = debug;
            placementStatus = $"V26 module-local placed. slot={slot.name}, pos={pos}, {debug}";
        }

        if (!placedAny)
            placementStatus = $"V26 no prompt placed. invalid={invalidCount}. root={(contentRoot != null ? contentRoot.name : "null")}. refs=({FormatSlotRef(0)},{FormatSlotRef(1)},{FormatSlotRef(2)},{FormatSlotRef(3)}). last={lastDebug}";
    }

    private bool TryCalculateModuleLocalPromptPosition(
        RectTransform contentRoot,
        RectTransform slot,
        out Vector2 pos,
        out string debug)
    {
        pos = Vector2.zero;
        debug = "";

        if (contentRoot == null || slot == null)
        {
            debug = "missing contentRoot/slot";
            return false;
        }

        // Compute slot bounds relative to QuickSlotsContent.  Because both transforms are in the same
        // post-process source tree, huge offscreen world positions cancel out here.
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(contentRoot, slot);
        Vector3 center = bounds.center;
        Vector3 min = bounds.min;

        pos = new Vector2(center.x + horizontalOffset, min.y + bottomYOffset);

        if (!IsFiniteVector2(pos) || IsExtremePromptPosition(pos))
        {
            debug = $"invalid module-local pos={pos}, center={center}, min={min}, contentSize={contentRoot.rect.size}";
            return false;
        }

        debug = $"content-local bounds. center={center}, min={min}, contentSize={contentRoot.rect.size}";
        return true;
    }

    private RectTransform FindVisibleQuickSlotsArea()
    {
        RectTransform safeArea = FindSafeAreaFromAnyKnownPoint();
        if (safeArea == null)
            return null;

        Transform direct = safeArea.Find("HUDRoot/QuickSlotsArea");
        if (direct is RectTransform directRect)
            return directRect;

        Transform hudRoot = safeArea.Find("HUDRoot");
        Transform shallow = hudRoot != null ? FindDirectOrShallowChild(hudRoot, "QuickSlotsArea", 3) : null;
        return shallow as RectTransform;
    }

    private RectTransform FindSafeAreaFromAnyKnownPoint()
    {
        RectTransform fromSelf = FindAncestorRect(transform, "SafeAreaRoot");
        if (fromSelf != null)
            return fromSelf;

        if (quickSlotsRoot != null)
        {
            RectTransform fromRoot = FindAncestorRect(quickSlotsRoot, "SafeAreaRoot");
            if (fromRoot != null)
                return fromRoot;
        }

        EnsureSlotArray();
        for (int i = 0; i < 4; i++)
        {
            if (slotRects[i] == null)
                continue;
            RectTransform fromSlot = FindAncestorRect(slotRects[i], "SafeAreaRoot");
            if (fromSlot != null)
                return fromSlot;
        }

        return null;
    }

    private static RectTransform ResolveQuickSlotsContent(RectTransform safeArea)
    {
        if (safeArea == null)
            return null;

        Transform content = safeArea.Find("HUDRoot/QuickSlotsArea/QuickSlotsContent");
        if (content is RectTransform contentRect)
            return contentRect;

        Transform area = safeArea.Find("HUDRoot/QuickSlotsArea");
        if (area == null)
        {
            Transform hudRoot = safeArea.Find("HUDRoot");
            area = hudRoot != null ? FindDirectOrShallowChild(hudRoot, "QuickSlotsArea", 3) : null;
        }

        if (area == null)
            return null;

        content = area.Find("QuickSlotsContent");
        if (content is RectTransform nestedContent)
            return nestedContent;

        return area as RectTransform;
    }

    private void SetPromptImagesVisible(bool visible)
    {
        for (int i = 0; i < promptImages.Length; i++)
        {
            if (promptImages[i] != null)
                promptImages[i].enabled = visible;
        }
    }

    private void EnsureSlotArray()
    {
        if (slotRects == null || slotRects.Length != 4)
        {
            RectTransform[] next = new RectTransform[4];
            if (slotRects != null)
            {
                for (int i = 0; i < Mathf.Min(slotRects.Length, next.Length); i++)
                    next[i] = slotRects[i];
            }
            slotRects = next;
        }
    }

    private static bool IsFiniteVector2(Vector2 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
               !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
    }

    private static bool IsExtremePromptPosition(Vector2 pos)
    {
        if (!IsFiniteVector2(pos))
            return true;
        return Mathf.Abs(pos.x) > 100000f || Mathf.Abs(pos.y) > 100000f;
    }

    private void EnsureDiagnosticsArrays()
    {
        if (runtimeResolvedIconKeys == null || runtimeResolvedIconKeys.Length != 4)
            runtimeResolvedIconKeys = new string[4];
        if (runtimeResolvedKeys == null || runtimeResolvedKeys.Length != 4)
            runtimeResolvedKeys = new KeyCode[4];
    }

    private static string BuildJoystickSignature()
    {
        string[] names = Input.GetJoystickNames();
        if (names == null || names.Length == 0)
            return "";
        return string.Join("|", names);
    }

    private static bool HasConnectedGamepadFromSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;
        string[] parts = signature.Split('|');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
                return true;
        }
        return false;
    }

    private string FormatSlotRef(int index)
    {
        if (slotRects == null || index < 0 || index >= slotRects.Length || slotRects[index] == null)
            return "null";
        return slotRects[index].name;
    }

    private static RectTransform ResolveSlotUnderKnownQuickSlotsBranch(Transform searchRoot, int index)
    {
        if (searchRoot == null)
            return null;
        string n2 = (index + 1).ToString("00");
        string n1 = (index + 1).ToString();
        string[] names =
        {
            "Slot_" + n2,
            "Slot_" + n1,
            "QuickSlot_" + n2,
            "QuickSlot_" + n1,
            "ItemSlot_" + n2,
            "ItemSlot_" + n1,
        };
        for (int i = 0; i < names.Length; i++)
        {
            Transform direct = searchRoot.Find(names[i]);
            if (direct is RectTransform rt)
                return rt;
        }
        for (int i = 0; i < names.Length; i++)
        {
            Transform nested = FindDirectOrShallowChild(searchRoot, names[i], 3);
            if (nested is RectTransform rt)
                return rt;
        }
        return null;
    }

    private static Transform FindDirectOrShallowChild(Transform root, string targetName, int depth)
    {
        if (root == null || depth < 0 || string.IsNullOrEmpty(targetName))
            return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;
            if (string.Equals(child.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                return child;
            Transform nested = FindDirectOrShallowChild(child, targetName, depth - 1);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static RectTransform FindAncestorRect(Transform start, string ancestorName)
    {
        Transform t = start;
        while (t != null)
        {
            if (string.Equals(t.name, ancestorName, System.StringComparison.OrdinalIgnoreCase))
                return t as RectTransform;
            t = t.parent;
        }
        return null;
    }

    private static double GetEditorTime()
    {
#if UNITY_EDITOR
        return EditorApplication.timeSinceStartup;
#else
        return Time.realtimeSinceStartupAsDouble;
#endif
    }

    private void AutoAssignAssetsIfPossible()
    {
#if UNITY_EDITOR
        if (inputSettings == null)
            inputSettings = AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(SkyPrisonInputSettings.DefaultAssetPath);
        if (iconDatabase == null)
            iconDatabase = AssetDatabase.LoadAssetAtPath<SkyPrisonInputPromptIconDatabase>(SkyPrisonInputPromptIconDatabase.DefaultAssetPath);
#endif
    }

    private static void DestroyObject(Object obj)
    {
        if (obj == null)
            return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // V27:
            // This strip is ExecuteAlways and can run during workbench repaint / prefab serialization.
            // Do not call DestroyImmediate here.  Mark stale objects inactive instead; the workbench
            // explicit save/cleanup path owns structural deletion when Unity is in a safe state.
            if (obj is GameObject go)
            {
                go.SetActive(false);
                if (!go.name.StartsWith("__DisabledStale_", System.StringComparison.Ordinal))
                    go.name = "__DisabledStale_" + go.name;
            }
            else if (obj is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }
            else if (obj is Component component && component.gameObject != null)
            {
                component.gameObject.SetActive(false);
                if (!component.gameObject.name.StartsWith("__DisabledStale_", System.StringComparison.Ordinal))
                    component.gameObject.name = "__DisabledStale_" + component.gameObject.name;
            }
            return;
        }
        else
            Object.Destroy(obj);
#else
        Object.Destroy(obj);
#endif
    }
}
