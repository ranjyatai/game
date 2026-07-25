using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(11200)]
public class SkyPrisonPlayerHUDRuntimeBinder : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V29 - 2026-06-11 - bind LP exhausted penalty state from UnitLoadRuntime";

    [Header("HUD View")]
    [SerializeField] private SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven hudView;
    [SerializeField] private Transform hudSearchRoot;
    [SerializeField] private bool autoResolveHudView = true;
    [SerializeField] private string playerStatusAreaName = "PlayerStatusArea";
    [SerializeField] private bool preferPlayerStatusAreaBridgeView = true;

    [Header("Current Player - Read Only")]
    [SerializeField] private SkyPrisonUnitRuntimeIdentity boundPlayerIdentity;
    [SerializeField] private GameObject boundPlayerObject;
    [SerializeField] private UnitHealthController boundHealth;
    [SerializeField] private UnitLoadRuntime boundLoad;
    [SerializeField] private UnitMovementController boundMovement;

    [Header("Refresh")]
    [SerializeField] private bool snapOnBind = true;
    [SerializeField] private bool pollRuntimeValues = true;
    [SerializeField] private float pollInterval = 0.05f;
    [SerializeField] private bool reacquireRuntimeComponentsEveryPoll = true;

    [Header("HP Source Diagnostic - Read Only")]
    [SerializeField] private string boundPlayerName = "";
    [SerializeField] private string boundHealthPath = "";
    [SerializeField] private float runtimeHpCurrent;
    [SerializeField] private float runtimeHpMax = 1f;
    [SerializeField] private float runtimeHpPercent01;
    [SerializeField] private bool multipleHealthControllersFound;

    [Header("HP Flow Diagnostic - Read Only")]
    [SerializeField] private string lastHpCommand = "";
    [SerializeField] private int hpSnapApplyCount;
    [SerializeField] private int hpSetApplyCount;
    [SerializeField] private int hpSkipApplyCount;
    [SerializeField] private int hpDamageObservedCount;
    [SerializeField] private int hpPreviewRuntimeCallCount;
    [SerializeField] private int hpSameFrameApplyCount;
    [SerializeField] private int hpLastApplyFrame = -1;

    [Header("HUD Target Diagnostic - Read Only")]
    [SerializeField] private string boundHudRootPath = "";
    [SerializeField] private string boundHudViewPath = "";
    [SerializeField] private bool hudViewResolvedInsideScope;
    [SerializeField] private bool boundHudViewIsPlayerStatusAreaBridge;
    [SerializeField] private string hudViewBindingMode = "";
    [SerializeField] private int forcedVisualApplyCount;

    [Header("LD / Burden")]
    [SerializeField] private bool useMovementBurdenForLd = true;
    [SerializeField] private bool useUnitLoadAsLdFallback = false;

    [Header("LP Flow Diagnostic - Read Only")]
    [SerializeField] private float runtimeLpCurrent;
    [SerializeField] private float runtimeLpMax = 1f;
    [SerializeField] private float runtimeLpPercent01;
    [SerializeField] private bool runtimeLpPenaltyActive;
    [SerializeField] private string lastLpCommand = "";
    [SerializeField] private int lpPenaltyApplyCount;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private float pollTimer;
    private float lastHp = float.NaN;
    private float lastMaxHp = float.NaN;
    private float lastLp = float.NaN;
    private float lastMaxLp = float.NaN;
    private float lastLd = float.NaN;
    private string lastLdStatusText;

    private Transform lastBindingRoot;
    private SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven lastResolvedHudView;
    private bool forceApplyOnNextRefresh;

    // Runtime HP is handled as two different flows:
    // 1) authoritative snapshot on bind / heal / max change: no damage lag;
    // 2) damage event: current fill moves to new HP, lag fill starts at old HP and catches up.
    private bool hasAuthoritativeHpSnapshot;
    private float lastAuthoritativeHp = float.NaN;
    private float lastAuthoritativeMaxHp = float.NaN;

    public string Version => scriptVersion;
    public SkyPrisonUnitRuntimeIdentity BoundPlayerIdentity => boundPlayerIdentity;
    public GameObject BoundPlayerObject => boundPlayerObject;
    public UnitHealthController BoundHealth => boundHealth;
    public UnitLoadRuntime BoundLoad => boundLoad;
    public UnitMovementController BoundMovement => boundMovement;

    private void Reset()
    {
        ResolveHudView();
    }

    private void Awake()
    {
        ResolveHudView();
    }

    private void OnEnable()
    {
        SkyPrisonPlayerAuthority.CurrentPlayerChangedGlobal += HandleCurrentPlayerChanged;
        BindToCurrentPlayer(true);
    }

    private void OnDisable()
    {
        SkyPrisonPlayerAuthority.CurrentPlayerChangedGlobal -= HandleCurrentPlayerChanged;
        UnsubscribeHealthEvents();
    }

    private void Update()
    {
        // HP/LP animation ticking belongs to SkyPrisonPlayerHUDView.Update, exactly like UnitOverheadUIView.
        // The binder is only a data bridge; it must not PreviewTick the view.
        if (!pollRuntimeValues)
            return;

        if (pollInterval > 0f)
        {
            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer > 0f)
                return;
            pollTimer = pollInterval;
        }

        RefreshFromBoundPlayer(false);
    }

    public void SetHudView(SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven view)
    {
        SetHudScope(view != null ? view.transform : null, view);
    }

    public void SetHudScope(Transform root, SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven view)
    {
        if (root != null)
            hudSearchRoot = root;

        if (view != null)
            hudView = view;

        // The target HUD instance changed. Force one full visual write so the visible HUD
        // cannot stay at PF default values while the binder already has runtime numbers.
        lastBindingRoot = null;
        lastResolvedHudView = null;
        forceApplyOnNextRefresh = true;
        ResetLastValues();
    }

    public void BindToCurrentPlayer(bool snap)
    {
        BindToPlayer(SkyPrisonPlayerAuthority.CurrentPlayerUnit, snap);
    }

    public void BindToPlayer(SkyPrisonUnitRuntimeIdentity identity, bool snap)
    {
        if (boundPlayerIdentity == identity && boundPlayerObject == (identity != null ? identity.gameObject : null))
        {
            RefreshFromBoundPlayer(snap);
            return;
        }

        UnsubscribeHealthEvents();

        boundPlayerIdentity = identity;
        boundPlayerObject = identity != null ? identity.gameObject : null;
        boundPlayerName = boundPlayerObject != null ? boundPlayerObject.name : "";
        boundHealth = null;
        boundLoad = null;
        boundMovement = null;
        ResetLastValues();
        ResetAuthoritativeHpSnapshot();
        ResetDebugFields();
        forceApplyOnNextRefresh = true;

        if (boundPlayerObject != null)
        {
            ResolveBoundRuntimeComponents();
            SubscribeHealthEvents();
        }

        ResolveHudView();
        RefreshFromBoundPlayer(snap || snapOnBind);

        if (debugLogs)
        {
            string unitName = boundPlayerObject != null ? boundPlayerObject.name : "<null>";
            Debug.Log($"[SkyPrisonPlayerHUDRuntimeBinder] Bind HUD to runtime Player: {unitName}", this);
        }
    }

    public bool ResolveHudView()
    {
        if (!autoResolveHudView && hudView != null)
            return EnsureHudViewBindingsIfNeeded();

        if (hudSearchRoot == null)
            hudSearchRoot = transform;

        // V28:
        // The accepted HUD chromatic route creates a bridge PlayerHUDView on PlayerStatusArea.
        // Runtime data must prefer that bridge. The original child view may have been moved to a hidden RT SourceRoot.
        SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven preferredBridge = ResolvePlayerStatusAreaBridgeView();

        if (preferredBridge != null && hudView != preferredBridge)
        {
            hudView = preferredBridge;
            lastBindingRoot = null;
            lastResolvedHudView = null;
            forceApplyOnNextRefresh = true;
            ResetLastValues();
        }

        if (hudView == null && hudSearchRoot != null)
            hudView = hudSearchRoot.GetComponentInChildren<SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven>(true);

        if (hudView == null)
            hudView = GetComponentInChildren<SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven>(true);

        // Important: do NOT use FindObjectOfType here.
        // The runtime binder must only drive the HUD instance created by the current runtime HUD root.
        // A global search can bind a hidden preview/old HUD and leave the visible HUD stuck at PF defaults.
        return EnsureHudViewBindingsIfNeeded();
    }

    private bool EnsureHudViewBindingsIfNeeded()
    {
        if (hudView == null)
        {
            boundHudViewPath = "<missing SkyPrisonPlayerHUDView>";
            hudViewResolvedInsideScope = false;
            boundHudViewIsPlayerStatusAreaBridge = false;
            hudViewBindingMode = "Missing";
            return false;
        }

        if (hudSearchRoot == null)
            hudSearchRoot = hudView.transform;

        boundHudRootPath = hudSearchRoot != null ? hudSearchRoot.name : "<null>";
        boundHudViewPath = GetRelativePath(hudSearchRoot, hudView.transform);
        hudViewResolvedInsideScope = IsChildOrSelf(hudSearchRoot, hudView.transform);

        SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven bridge = ResolvePlayerStatusAreaBridgeView();
        boundHudViewIsPlayerStatusAreaBridge = bridge != null && bridge == hudView;

        if (!hudViewResolvedInsideScope)
        {
            Debug.LogWarning($"[SkyPrisonPlayerHUDRuntimeBinder] HUD View is outside binder scope. Root={boundHudRootPath}, View={hudView.name}. Binding is rejected.", this);
            hudView = null;
            boundHudViewIsPlayerStatusAreaBridge = false;
            hudViewBindingMode = "Rejected: outside scope";
            return false;
        }

        if (lastResolvedHudView != hudView || lastBindingRoot != hudSearchRoot)
        {
            // V28 critical rule:
            // If hudView is the PlayerStatusArea bridge, do NOT call EnsurePreviewBindingsFrom(hudSearchRoot).
            // The module RT renderer owns that bridge and binds it to its hidden SourceRoot. Rebinding it here
            // with the visible HUD root can overwrite the correct RT-source references and make Build diverge.
            if (boundHudViewIsPlayerStatusAreaBridge)
            {
                hudViewBindingMode = "PlayerStatusArea bridge; preserve ModulePostProcessRenderer SourceRoot binding";
            }
            else
            {
                hudView.EnsurePreviewBindingsFrom(hudSearchRoot);
                hudViewBindingMode = "Fallback child view; bound from hudSearchRoot";
            }

            lastResolvedHudView = hudView;
            lastBindingRoot = hudSearchRoot;
            forceApplyOnNextRefresh = true;
            ResetLastValues();
            forcedVisualApplyCount++;
        }

        return true;
    }

    private SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven ResolvePlayerStatusAreaBridgeView()
    {
        if (!preferPlayerStatusAreaBridgeView || hudSearchRoot == null)
            return null;

        Transform playerStatusArea = FindTransformRecursive(hudSearchRoot, playerStatusAreaName);
        if (playerStatusArea == null)
            return null;

        return playerStatusArea.GetComponent<SkyPrison.Runtime.UI.SkyPrisonPlayerHUDView_V4_StyleDriven>();
    }

    private static Transform FindTransformRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
            return null;

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindTransformRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    public void RefreshFromBoundPlayer(bool snap)
    {
        if (!ResolveHudView())
            return;

        bool forceVisual = forceApplyOnNextRefresh;
        forceApplyOnNextRefresh = false;

        if (boundPlayerObject == null)
        {
            ApplyEmptyState();
            return;
        }

        ResolveBoundRuntimeComponents();

        ApplyHealth(snap || forceVisual);
        ApplyLp(snap || forceVisual);
        ApplyBurdenLd(snap || forceVisual);
    }

    private void ResolveBoundRuntimeComponents()
    {
        if (boundPlayerObject == null)
            return;

        UnitHealthController oldHealth = boundHealth;

        if (reacquireRuntimeComponentsEveryPoll || boundHealth == null)
            boundHealth = ResolveBestHealthController(boundPlayerObject);

        if (oldHealth != boundHealth)
        {
            if (oldHealth != null)
                UnsubscribeHealthEvents(oldHealth);
            SubscribeHealthEvents();
            ResetLastValues();
            forceApplyOnNextRefresh = true;
        }

        if (reacquireRuntimeComponentsEveryPoll || boundLoad == null)
            boundLoad = boundPlayerObject.GetComponentInChildren<UnitLoadRuntime>(true);

        if (reacquireRuntimeComponentsEveryPoll || boundMovement == null)
            boundMovement = boundPlayerObject.GetComponentInChildren<UnitMovementController>(true);

        UpdateHealthDebugFields();
    }

    private void ApplyEmptyState()
    {
        if (hudView == null)
            return;

        hudView.SnapHp(0f, 1f);
        hudView.SnapLp(0f, 1f);
        hudView.SetLoad(0f, "轻便");
        ResetLastValues();
    }

    private void ApplyHealth(bool snap)
    {
        if (hudView == null || boundHealth == null)
            return;

        float current = Mathf.Max(0f, boundHealth.CurrentHealth);
        float max = Mathf.Max(1f, boundHealth.MaxHealth);
        runtimeHpCurrent = current;
        runtimeHpMax = max;
        runtimeHpPercent01 = Mathf.Clamp01(current / max);

        bool firstSnapshot = float.IsNaN(lastHp) || float.IsNaN(lastMaxHp);
        bool maxChanged = !Approximately(max, lastMaxHp);
        bool hpChanged = firstSnapshot || !Approximately(current, lastHp) || maxChanged;

        if (!hpChanged && !snap)
        {
            hpSkipApplyCount++;
            lastHpCommand = $"Skip frame={Time.frameCount} hp={current:0.###}/{max:0.###}";
            return;
        }

        RegisterHpApplyFrame();

        if (snap || firstSnapshot || maxChanged)
        {
            hudView.SnapHp(current, max);
            hpSnapApplyCount++;
            lastHpCommand = $"SnapHp frame={Time.frameCount} snap={snap} first={firstSnapshot} maxChanged={maxChanged} hp={current:0.###}/{max:0.###}";
        }
        else
        {
            if (current < lastHp)
                hpDamageObservedCount++;

            // Single overhead-style flow:
            // Binder only pushes current/max. View.SetHp owns oldTarget/newTarget damage-lag state.
            hudView.SetHp(current, max);
            hpSetApplyCount++;
            lastHpCommand = $"SetHp frame={Time.frameCount} old={lastHp:0.###}/{lastMaxHp:0.###} new={current:0.###}/{max:0.###}";
        }

        hasAuthoritativeHpSnapshot = true;
        lastAuthoritativeHp = current;
        lastAuthoritativeMaxHp = max;

        lastHp = current;
        lastMaxHp = max;
    }

    private void ApplyLp(bool snap)
    {
        if (hudView == null || boundLoad == null)
            return;

        float current = Mathf.Max(0f, boundLoad.CurrentLoad);
        float max = Mathf.Max(1f, boundLoad.MaxLoad);
        bool penaltyActive = boundLoad.IsLoadExhaustedPenaltyActive;

        runtimeLpCurrent = current;
        runtimeLpMax = max;
        runtimeLpPercent01 = Mathf.Clamp01(current / max);
        runtimeLpPenaltyActive = penaltyActive;

        // V29:
        // LP red flashing must follow UnitLoadRuntime's exhausted penalty state, not only CurrentLoad == 0.
        // UnitLoadRuntime may recover currentLoad above 0 while exhaustedSprintLock remains active until the
        // resume threshold is reached. If HUD only uses the empty ratio fallback, the red flash appears for
        // one instant and then gets turned off as soon as recovery starts.
        hudView.SetLpPenaltyActive(penaltyActive);
        lpPenaltyApplyCount++;

        if (snap || !Approximately(current, lastLp) || !Approximately(max, lastMaxLp))
        {
            if (snap || float.IsNaN(lastLp))
            {
                hudView.SnapLp(current, max);
                lastLpCommand = $"SnapLp frame={Time.frameCount} lp={current:0.###}/{max:0.###} penalty={penaltyActive}";
            }
            else
            {
                hudView.SetLp(current, max);
                lastLpCommand = $"SetLp frame={Time.frameCount} old={lastLp:0.###}/{lastMaxLp:0.###} new={current:0.###}/{max:0.###} penalty={penaltyActive}";
            }

            lastLp = current;
            lastMaxLp = max;
        }
        else
        {
            lastLpCommand = $"PenaltyOnly frame={Time.frameCount} lp={current:0.###}/{max:0.###} penalty={penaltyActive}";
        }
    }

    private void ApplyBurdenLd(bool snap)
    {
        if (hudView == null)
            return;

        if (useMovementBurdenForLd && boundMovement != null)
        {
            float normalized = Mathf.Max(0f, boundMovement.BurdenPercent01);
            string statusText = GetBurdenStatusText(boundMovement.CurrentBurdenLevel);

            if (snap || !Approximately(normalized, lastLd) || lastLdStatusText != statusText)
            {
                hudView.SetLoad(normalized, statusText);
                lastLd = normalized;
                lastLdStatusText = statusText;
            }

            return;
        }

        if (useUnitLoadAsLdFallback && boundLoad != null)
        {
            float current = Mathf.Max(0f, boundLoad.CurrentLoad);
            float max = Mathf.Max(1f, boundLoad.MaxLoad);
            float normalized = max <= 0.0001f ? 0f : current / max;

            if (snap || !Approximately(normalized, lastLd))
            {
                hudView.SetLoad(normalized);
                lastLd = normalized;
                lastLdStatusText = null;
            }
        }
    }

    private static string GetBurdenStatusText(UnitMovementController.BurdenLevel level)
    {
        switch (level)
        {
            case UnitMovementController.BurdenLevel.Light:
                return "LIGHT";
            case UnitMovementController.BurdenLevel.Medium:
            case UnitMovementController.BurdenLevel.Heavy:
                return "HEAVY";
            case UnitMovementController.BurdenLevel.Overweight:
                return "OVERWEIGHT";
            default:
                return "LIGHT";
        }
    }

    private void RegisterHpApplyFrame()
    {
        int frame = Time.frameCount;
        if (hpLastApplyFrame == frame)
            hpSameFrameApplyCount++;
        hpLastApplyFrame = frame;
    }

    private void HandleCurrentPlayerChanged(SkyPrisonUnitRuntimeIdentity oldPlayer, SkyPrisonUnitRuntimeIdentity newPlayer)
    {
        BindToPlayer(newPlayer, true);
    }

    private void SubscribeHealthEvents()
    {
        if (boundHealth == null)
            return;

        boundHealth.OnDamaged += HandleHealthDamaged;
        boundHealth.OnHealed += HandleHealthHealed;
        boundHealth.OnFullHealed += HandleHealthFullHealed;
        boundHealth.OnHealthZero += HandleHealthZero;
    }

    private void UnsubscribeHealthEvents()
    {
        if (boundHealth == null)
            return;

        UnsubscribeHealthEvents(boundHealth);
    }

    private void UnsubscribeHealthEvents(UnitHealthController health)
    {
        if (health == null)
            return;

        health.OnDamaged -= HandleHealthDamaged;
        health.OnHealed -= HandleHealthHealed;
        health.OnFullHealed -= HandleHealthFullHealed;
        health.OnHealthZero -= HandleHealthZero;
    }

    private void HandleHealthDamaged(UnitHealthController source, float amount)
    {
        ApplyHealth(false);
    }

    private void HandleHealthHealed(UnitHealthController source, float amount)
    {
        ApplyHealth(false);
    }

    private void HandleHealthFullHealed(UnitHealthController source)
    {
        ApplyHealth(true);
    }

    private void HandleHealthZero(UnitHealthController source)
    {
        ApplyHealth(false);
    }

    private void ResetLastValues()
    {
        lastHp = float.NaN;
        lastMaxHp = float.NaN;
        lastLp = float.NaN;
        lastMaxLp = float.NaN;
        lastLd = float.NaN;
        lastLdStatusText = null;
        ResetAuthoritativeHpSnapshot();
    }

    private void ResetAuthoritativeHpSnapshot()
    {
        hasAuthoritativeHpSnapshot = false;
        lastAuthoritativeHp = float.NaN;
        lastAuthoritativeMaxHp = float.NaN;
    }

    private void ResetDebugFields()
    {
        boundHealthPath = "";
        runtimeHpCurrent = 0f;
        runtimeHpMax = 1f;
        runtimeHpPercent01 = 0f;
        runtimeLpCurrent = 0f;
        runtimeLpMax = 1f;
        runtimeLpPercent01 = 0f;
        runtimeLpPenaltyActive = false;
        lastLpCommand = "";
        lpPenaltyApplyCount = 0;
        multipleHealthControllersFound = false;
    }

    private void UpdateHealthDebugFields()
    {
        if (boundPlayerObject == null)
        {
            boundPlayerName = "";
            ResetDebugFields();
            return;
        }

        boundPlayerName = boundPlayerObject.name;
        boundHealthPath = boundHealth != null ? GetRelativePath(boundPlayerObject.transform, boundHealth.transform) : "<missing UnitHealthController>";

        if (boundHealth != null)
        {
            runtimeHpCurrent = Mathf.Max(0f, boundHealth.CurrentHealth);
            runtimeHpMax = Mathf.Max(1f, boundHealth.MaxHealth);
            runtimeHpPercent01 = Mathf.Clamp01(runtimeHpCurrent / runtimeHpMax);
        }
        else
        {
            runtimeHpCurrent = 0f;
            runtimeHpMax = 1f;
            runtimeHpPercent01 = 0f;
        }
    }

    private UnitHealthController ResolveBestHealthController(GameObject playerRoot)
    {
        multipleHealthControllersFound = false;

        if (playerRoot == null)
            return null;

        UnitHealthController direct = playerRoot.GetComponent<UnitHealthController>();
        if (direct != null)
            return direct;

        UnitHealthController[] all = playerRoot.GetComponentsInChildren<UnitHealthController>(true);
        if (all == null || all.Length == 0)
            return null;

        multipleHealthControllersFound = all.Length > 1;

        UnitHealthController best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            UnitHealthController candidate = all[i];
            if (candidate == null)
                continue;

            int score = 0;
            if (candidate.isActiveAndEnabled)
                score += 1000;
            if (candidate.gameObject.activeInHierarchy)
                score += 500;

            score -= GetHierarchyDepthFrom(playerRoot.transform, candidate.transform) * 10;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int GetHierarchyDepthFrom(Transform root, Transform target)
    {
        if (root == null || target == null)
            return 999;

        int depth = 0;
        Transform t = target;
        while (t != null && t != root)
        {
            depth++;
            t = t.parent;
        }

        return t == root ? depth : 999;
    }

    private static bool IsChildOrSelf(Transform root, Transform target)
    {
        if (root == null || target == null)
            return false;

        Transform t = target;
        while (t != null)
        {
            if (t == root)
                return true;
            t = t.parent;
        }

        return false;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return "";

        if (root == target)
            return root.name;

        System.Collections.Generic.Stack<string> stack = new System.Collections.Generic.Stack<string>();
        Transform t = target;
        while (t != null && t != root)
        {
            stack.Push(t.name);
            t = t.parent;
        }

        if (t != root)
            return target.name;

        return root.name + "/" + string.Join("/", stack.ToArray());
    }

    [ContextMenu("HUD Binder/Refresh From Current Player Now")]
    public void RefreshFromCurrentPlayerNow()
    {
        BindToCurrentPlayer(true);
    }

    private static bool Approximately(float a, float b)
    {
        if (float.IsNaN(a) || float.IsNaN(b))
            return false;

        return Mathf.Abs(a - b) <= 0.0001f;
    }
}
