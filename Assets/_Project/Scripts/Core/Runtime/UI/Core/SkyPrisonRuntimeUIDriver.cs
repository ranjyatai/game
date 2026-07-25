using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(11100)]
public class SkyPrisonRuntimeUIDriver : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V10 - 2026-06-11 - safe offset chromatic overlay reads workbench GlobalStyleSettings";

    [Header("HUD Prefab")]
    [SerializeField] private GameObject battleHudPrefab;
#if UNITY_EDITOR
    [SerializeField] private string editorFallbackHudPrefabPath = "Assets/_Project/Prefabs/UI/HUD/PF_SkyPrisonBattleHUD.prefab";
#endif
    [SerializeField] private string resourcesFallbackHudPrefabPath = "UI/HUD/PF_SkyPrisonBattleHUD";

    [Header("UI Root")]
    [SerializeField] private Canvas uiRootCanvas;
    [SerializeField] private string uiRootName = "SkyPrisonRuntimeUIRoot";
    [SerializeField] private bool createUiRootIfMissing = true;
    [SerializeField] private bool dontDestroyUiRootOnLoad = false;
    [SerializeField] private int sortingOrder = 500;

    [Header("HUD Instance")]
    [SerializeField] private GameObject hudInstance;
    [SerializeField] private string hudInstanceName = "SkyPrisonBattleHUD_Runtime";
    [SerializeField] private bool instantiateHudOnStart = true;
    [SerializeField] private bool preventDuplicateHudInstance = true;

    [Header("Binding")]
    [SerializeField] private SkyPrisonPlayerHUDRuntimeBinder hudBinder;
    [SerializeField] private bool bindToCurrentPlayerOnStart = true;

    [Header("Rules")]
    [SerializeField] private bool forceUiLayer = true;
    // 之前固定像素模式在小分辨率屏幕（比如 1080p）下会让同样大小的 HUD 元素占屏幕
    // 比例明显偏高，看起来比大分辨率屏幕拥挤——改成跟随分辨率等比缩放，画面观感在
    // 不同分辨率下保持一致比例。PlayerHUDStatusIconBar 的画布缩放模式必须跟这里
    // 保持一致，不然状态图标位置又会跟 PlayerStatusArea 对不上（同一个坑的另一种
    // 触发方式）。
    [SerializeField] private bool useConstantPixelSizeForRuntimeRoot = false;
    [SerializeField] private Vector2 referenceResolution = new Vector2(3840f, 2160f);

    [Header("Safe Chromatic Overlay")]
    [SerializeField] private bool enableSafeChromaticOverlayRuntime = true;
    [SerializeField] private string[] safeChromaticOverlayModuleNames = new string[] { "PlayerStatusArea" };
    [SerializeField] private SkyPrisonHUDChromaticOffsetOverlayPresenter chromaticOffsetOverlayPresenter;
    [SerializeField] private string runtimeChromaticOverlayDiagnostic = "Not initialized";

    [Header("Boot Coordinator Integration")]
    [SerializeField] private bool presentationEffectsAllowedByBoot = false;
    [SerializeField] private string presentationEffectsBootReason = "Not opened";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    public string Version => scriptVersion;
    public Canvas UIRootCanvas => uiRootCanvas;
    public GameObject HudInstance => hudInstance;
    public SkyPrisonPlayerHUDRuntimeBinder HudBinder => hudBinder;
    public bool IsHudReadyForBoot => hudInstance != null && hudBinder != null && hudBinder.BoundPlayerIdentity != null;

    private void Awake()
    {
        EnsureUIRoot();
        EnsureInputDeviceTracker();

        if (instantiateHudOnStart)
            EnsureBattleHUDInstance();
    }

    private static void EnsureInputDeviceTracker()
    {
        if (SkyPrisonInputDeviceTracker.Instance != null) return;
        var go = new GameObject("[InputDeviceTracker]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<SkyPrisonInputDeviceTracker>();
    }

    private void OnEnable()
    {
        SkyPrisonPlayerAuthority.CurrentPlayerChangedGlobal += HandleCurrentPlayerChanged;
    }

    private void Start()
    {
        EnsureUIRoot();
        EnsureBattleHUDInstance();

        if (bindToCurrentPlayerOnStart && hudBinder != null)
            hudBinder.BindToCurrentPlayer(true);

        EnsureSafeChromaticOverlay();
        EnsureItemPickupToast();
    }

    private void OnDisable()
    {
        SkyPrisonPlayerAuthority.CurrentPlayerChangedGlobal -= HandleCurrentPlayerChanged;
    }

    [ContextMenu("UI Driver/Ensure UI Root")]
    public void EnsureUIRoot()
    {
        if (uiRootCanvas == null)
        {
            GameObject existing = GameObject.Find(uiRootName);
            if (existing != null)
                uiRootCanvas = existing.GetComponent<Canvas>();
        }

        if (uiRootCanvas == null && createUiRootIfMissing)
        {
            GameObject root = new GameObject(uiRootName);
            uiRootCanvas = root.AddComponent<Canvas>();
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
        }

        if (uiRootCanvas == null)
            return;

        // 在设置任何可见属性之前先确保 CanvasGroup 存在并注册进 SceneLoader：
        // 若当前处于"加载未揭幕"阶段，会被同步设为 alpha=0，
        // HUD/背包/拾取提示等作为其子物体天生继承这个透明度，不会有"先出现再被隐藏"的一帧。
        CanvasGroup rootGroup = uiRootCanvas.GetComponent<CanvasGroup>();
        if (rootGroup == null)
            rootGroup = uiRootCanvas.gameObject.AddComponent<CanvasGroup>();
        SceneLoader.RegisterGameCanvasForReveal(rootGroup);

        uiRootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        uiRootCanvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = uiRootCanvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = uiRootCanvas.gameObject.AddComponent<CanvasScaler>();

        if (useConstantPixelSizeForRuntimeRoot)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
        }
        else
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (uiRootCanvas.GetComponent<GraphicRaycaster>() == null)
            uiRootCanvas.gameObject.AddComponent<GraphicRaycaster>();

        if (forceUiLayer)
            SetLayerRecursively(uiRootCanvas.gameObject, LayerMask.NameToLayer("UI"));

        if (dontDestroyUiRootOnLoad && Application.isPlaying)
            DontDestroyOnLoad(uiRootCanvas.gameObject);
    }

    [ContextMenu("UI Driver/Ensure Battle HUD Instance")]
    public void EnsureBattleHUDInstance()
    {
        if (preventDuplicateHudInstance && hudInstance == null)
        {
            GameObject existing = GameObject.Find(hudInstanceName);
            if (existing != null)
                hudInstance = existing;
        }

        if (hudInstance == null)
        {
            GameObject prefab = ResolveBattleHudPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[SkyPrisonRuntimeUIDriver] Battle HUD prefab is missing. Assign PF_SkyPrisonBattleHUD or keep editor fallback path valid.", this);
                return;
            }

            bool prefabHasCanvas = prefab.GetComponent<Canvas>() != null;
            Transform parent = prefabHasCanvas ? null : (uiRootCanvas != null ? uiRootCanvas.transform : null);
            hudInstance = Instantiate(prefab, parent);
            hudInstance.name = hudInstanceName;

            RectTransform rect = hudInstance.transform as RectTransform;
            if (rect != null && parent != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            // HUD 有自己独立的根 Canvas（不挂在 uiRootCanvas 下），必须单独注册，
            // 否则揭幕前会有一帧以全 alpha 渲染出来。
            if (prefabHasCanvas)
            {
                CanvasGroup hudGroup = hudInstance.GetComponent<CanvasGroup>();
                if (hudGroup == null)
                    hudGroup = hudInstance.AddComponent<CanvasGroup>();
                SceneLoader.RegisterGameCanvasForReveal(hudGroup);
            }

            if (debugLogs)
                Debug.Log("[SkyPrisonRuntimeUIDriver] Created Battle HUD runtime instance.", hudInstance);
        }

        if (forceUiLayer)
            SetLayerRecursively(hudInstance, LayerMask.NameToLayer("UI"));

        ApplyScaleModeToHudInstance();
        EnsureHudBinder();
        EnsureSafeChromaticOverlay();

        // 新场景/新HUD实例建好之后，把已经绑定的快捷物品图标同步过去——不然读档
        // 或者切场景重建HUD之后，快捷槽绑定还在（QuickSlotRuntime没变），但HUD上的
        // 图标是全新实例、默认空白。
        QuickSlotRuntime.Instance?.PushAllToHud();
    }

    // PF_SkyPrisonBattleHUD 是独立根 Canvas（不挂在 uiRootCanvas 下），
    // 自带一份烘焙进预制体的 CanvasScaler（ConstantPixelSize）。
    // 只改 uiRootCanvas 的缩放模式不会影响它，这里必须单独同步。
    private void ApplyScaleModeToHudInstance()
    {
        if (hudInstance == null)
            return;

        Canvas hudCanvas = hudInstance.GetComponent<Canvas>();
        if (hudCanvas == null)
            return;

        CanvasScaler hudScaler = hudInstance.GetComponent<CanvasScaler>();
        if (hudScaler == null)
            hudScaler = hudInstance.AddComponent<CanvasScaler>();

        if (useConstantPixelSizeForRuntimeRoot)
        {
            hudScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            hudScaler.scaleFactor = 1f;
            hudScaler.referencePixelsPerUnit = 100f;
        }
        else
        {
            hudScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            hudScaler.referenceResolution = referenceResolution;
            hudScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            hudScaler.matchWidthOrHeight = 0.5f;
        }
    }

    public SkyPrisonPlayerHUDRuntimeBinder EnsureHudBinder()
    {
        if (hudInstance == null)
            return null;

        if (hudBinder == null)
            hudBinder = hudInstance.GetComponentInChildren<SkyPrisonPlayerHUDRuntimeBinder>(true);

        if (hudBinder == null)
            hudBinder = hudInstance.AddComponent<SkyPrisonPlayerHUDRuntimeBinder>();

        SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven view =
            hudInstance.GetComponentInChildren<SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven>(true);
        hudBinder.SetHudScope(hudInstance.transform, view);
        hudBinder.ResolveHudView();
        return hudBinder;
    }


    public void EnsureSafeChromaticOverlay()
    {
        if (!Application.isPlaying)
            return;

        if (hudInstance == null)
        {
            runtimeChromaticOverlayDiagnostic = "No HUD instance";
            return;
        }

        if (!presentationEffectsAllowedByBoot)
        {
            if (chromaticOffsetOverlayPresenter != null)
                chromaticOffsetOverlayPresenter.Configure(hudInstance.transform, safeChromaticOverlayModuleNames, false, 0f, 0f);
            runtimeChromaticOverlayDiagnostic = "Disabled by boot: " + presentationEffectsBootReason;
            return;
        }

        if (!enableSafeChromaticOverlayRuntime)
        {
            if (chromaticOffsetOverlayPresenter != null)
                chromaticOffsetOverlayPresenter.Configure(hudInstance.transform, safeChromaticOverlayModuleNames, false, 0f, 0f);
            runtimeChromaticOverlayDiagnostic = "Disabled by RuntimeUIDriver";
            return;
        }

        Component global = FindGlobalStyleSettingsComponent(hudInstance);
        if (global == null)
        {
            runtimeChromaticOverlayDiagnostic = "Missing GlobalStyleSettings";
            return;
        }

        bool enableChromatic = GetBoolFieldOrProperty(global, "enableChromaticByDefault", false);
        float intensity = Mathf.Clamp01(GetFloatFieldOrProperty(global, "chromaticIntensity", 0f));
        float softness  = Mathf.Clamp01(GetFloatFieldOrProperty(global, "chromaticSoftness", 0f));

        DisableLegacyGraphicOverlayPresenter();

        if (chromaticOffsetOverlayPresenter == null)
            chromaticOffsetOverlayPresenter = hudInstance.GetComponent<SkyPrisonHUDChromaticOffsetOverlayPresenter>()
                                           ?? hudInstance.AddComponent<SkyPrisonHUDChromaticOffsetOverlayPresenter>();

        bool active = enableChromatic && intensity > 0.001f;
        chromaticOffsetOverlayPresenter.Configure(hudInstance.transform, safeChromaticOverlayModuleNames, active, intensity, softness);
        runtimeChromaticOverlayDiagnostic = active
            ? "Offset Overlay requested from Workbench/PF GlobalStyleSettings intensity=" + intensity.ToString("0.###") + " soft=" + softness.ToString("0.###") + " presenter=" + chromaticOffsetOverlayPresenter.RuntimeDiagnostic
            : "Disabled by Workbench/PF GlobalStyleSettings";
    }

    private void DisableLegacyGraphicOverlayPresenter()
    {
        if (hudInstance == null)
            return;

        // The old Graphic Overlay presenter is an obsolete implementation and may have been deleted.
        // Do not reference its type directly, otherwise deleting the old route breaks compilation.
        Component[] components = hudInstance.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component c = components[i];
            if (c == null)
                continue;

            string typeName = c.GetType().Name;
            if (typeName != "SkyPrisonHUDChromaticOverlayPresenter" &&
                typeName != "SkyPrisonHUDChromaticRTOverlayPresenter")
                continue;

            Behaviour b = c as Behaviour;
            if (b != null)
                b.enabled = false;
        }
    }

    private static Component FindGlobalStyleSettingsComponent(GameObject root)
    {
        if (root == null)
            return null;

        Component[] components = root.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component c = components[i];
            if (c == null)
                continue;
            if (c.GetType().Name == "SkyPrisonUIGlobalStyleSettings_V1")
                return c;
        }

        return null;
    }

    private static bool GetBoolFieldOrProperty(Component component, string memberName, bool fallback)
    {
        if (component == null || string.IsNullOrEmpty(memberName))
            return fallback;

        System.Type type = component.GetType();
        System.Reflection.FieldInfo field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(component);

        System.Reflection.PropertyInfo property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
            return (bool)property.GetValue(component, null);

        return fallback;
    }

    private static float GetFloatFieldOrProperty(Component component, string memberName, float fallback)
    {
        if (component == null || string.IsNullOrEmpty(memberName))
            return fallback;

        System.Type type = component.GetType();
        System.Reflection.FieldInfo field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null)
        {
            object value = field.GetValue(component);
            if (value is float) return (float)value;
            if (value is int) return (int)value;
        }

        System.Reflection.PropertyInfo property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property != null && property.CanRead)
        {
            object value = property.GetValue(component, null);
            if (value is float) return (float)value;
            if (value is int) return (int)value;
        }

        return fallback;
    }

    private GameObject ResolveBattleHudPrefab()
    {
        if (battleHudPrefab != null)
            return battleHudPrefab;

        if (!string.IsNullOrWhiteSpace(resourcesFallbackHudPrefabPath))
        {
            GameObject resourcePrefab = Resources.Load<GameObject>(resourcesFallbackHudPrefabPath);
            if (resourcePrefab != null)
            {
                battleHudPrefab = resourcePrefab;
                return battleHudPrefab;
            }
        }

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(editorFallbackHudPrefabPath))
        {
            battleHudPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(editorFallbackHudPrefabPath);
            if (battleHudPrefab != null)
                return battleHudPrefab;
        }
#endif

        return null;
    }

    /// <summary>
    /// Called by SkyPrisonRuntimeBootCoordinator during Phase3.
    /// Keeps HUD creation/binding build-safe while leaving presentation effects gated until Phase5.
    /// </summary>
    public bool EnsureHudReadyForBoot(bool allowPresentationEffects)
    {
        presentationEffectsAllowedByBoot = allowPresentationEffects;
        presentationEffectsBootReason = allowPresentationEffects ? "Boot EnsureHudReadyForBoot allow" : "Boot EnsureHudReadyForBoot phase3 hold";

        EnsureUIRoot();
        EnsureBattleHUDInstance();

        if (hudBinder == null)
            EnsureHudBinder();

        if (hudBinder != null)
            hudBinder.BindToCurrentPlayer(true);

        EnsureSafeChromaticOverlay();
        return IsHudReadyForBoot;
    }

    /// <summary>
    /// Called by SkyPrisonRuntimeBootCoordinator during Phase5.
    /// Only enables/disables the external overlay presenter. It never touches PlayerStatusArea internals.
    /// </summary>
    public void SetPresentationEffectsEnabled(bool enabled, string reason = "")
    {
        presentationEffectsAllowedByBoot = enabled;
        presentationEffectsBootReason = string.IsNullOrWhiteSpace(reason) ? (enabled ? "Opened" : "Closed") : reason;
        EnsureSafeChromaticOverlay();

        if (debugLogs)
            Debug.Log($"[SkyPrisonRuntimeUIDriver] Presentation effects {(enabled ? "enabled" : "disabled")} reason={presentationEffectsBootReason}", this);
    }

    private void HandleCurrentPlayerChanged(SkyPrisonUnitRuntimeIdentity oldPlayer, SkyPrisonUnitRuntimeIdentity newPlayer)
    {
        EnsureBattleHUDInstance();
        if (hudBinder != null)
            hudBinder.BindToPlayer(newPlayer, true);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
            return;

        root.layer = layer;
        Transform t = root.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }

    private void EnsureItemPickupToast()
    {
        if (!Application.isPlaying) return;
        if (uiRootCanvas == null) return;
        if (uiRootCanvas.GetComponentInChildren<ItemPickupToastUI>(true) != null) return;

        var go = new GameObject("[ItemPickupToast]");
        go.transform.SetParent(uiRootCanvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.AddComponent<ItemPickupToastUI>();
    }

}
