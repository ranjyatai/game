using UnityEngine;

[DisallowMultipleComponent]
public class UnitOverheadHealthBridge : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private UnitHealthController healthController;
    [SerializeField] private UnitDeathController deathController;
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private UnitOverheadUIView overheadView;
    [SerializeField] private UnitStatusController statusController;
    [SerializeField] private BattleParameterDatabase battleParameterDatabase;
    [SerializeField] private bool autoFindRuntimeBattleDatabase = true;

    [Header("规则")]
    [SerializeField] private bool autoSetupOnAwake = true;
    [SerializeField] private bool pollHealthChanges = true;
    [SerializeField] private bool forceSyncStatuses = true;
    [SerializeField, Min(0.05f)] private float statusForceSyncInterval = 0.25f;
    [SerializeField] private bool hideBarOnDeath = true;
    [SerializeField] private bool hideNameOnDeath = true;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    private const string DefaultBattleParameterDatabaseResourcesPath = "BattleParameters/BattleParameterDatabase_001";

    private float lastCurrentHealth = -99999f;
    private float lastMaxHealth = -99999f;
    private float lastRegainPool = -99999f;
    private bool lastDeadLike = false;
    private float nextStatusForceSyncTime = 0f;

    private void Reset()
    {
        AutoSetup();
    }

    private void Awake()
    {
        // 玩家单位头顶 UI 透明度设为 0（不禁用，换阵营后可随时恢复）
        var identity = GetComponent<SkyPrisonUnitRuntimeIdentity>()
                    ?? GetComponentInParent<SkyPrisonUnitRuntimeIdentity>();
        if (identity != null && identity.IsRuntimePlayer && overheadView != null)
        {
            var cg = overheadView.GetComponent<CanvasGroup>();
            if (cg == null) cg = overheadView.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
        }

        if (autoSetupOnAwake)
            AutoSetup();

        RefreshNow();
    }

    private void OnEnable()
    {
        if (autoSetupOnAwake)
            AutoSetup();

        OverheadBarStyleAsset.OnStyleChanged += HandleStyleChanged;
        BindEvents();
        RefreshNow();
    }

    private void OnDisable()
    {
        OverheadBarStyleAsset.OnStyleChanged -= HandleStyleChanged;
        UnbindEvents();

        if (overheadView != null)
            overheadView.ClearStatuses();
    }

    private void LateUpdate()
    {
        // 20260509: 血条轮询只在数值真的变化时同步，避免每帧重刷头顶 UI。
        if (pollHealthChanges && HasHealthVisualChanged())
            SyncAllNow();

        // 状态栏只做低频兜底同步。状态增删仍然走事件即时刷新。
        if (forceSyncStatuses && Application.isPlaying && Time.unscaledTime >= nextStatusForceSyncTime)
        {
            nextStatusForceSyncTime = Time.unscaledTime + Mathf.Max(0.02f, statusForceSyncInterval);
            SyncStatusesNow();
        }
    }

    private bool HasHealthVisualChanged()
    {
        if (healthController == null)
            return false;

        bool deadLike = deathController != null && deathController.IsDeadLike;
        return !Mathf.Approximately(lastCurrentHealth, healthController.CurrentHealth)
            || !Mathf.Approximately(lastMaxHealth, healthController.MaxHealth)
            || !Mathf.Approximately(lastRegainPool, healthController.RegainPool)
            || lastDeadLike != deadLike;
    }


    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (healthController == null)
            healthController = GetComponent<UnitHealthController>();

        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();

        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (overheadView == null)
            overheadView = GetComponent<UnitOverheadUIView>();

        if (overheadView == null)
            overheadView = GetComponentInChildren<UnitOverheadUIView>(true);

        if (statusController == null)
            statusController = GetComponent<UnitStatusController>();

        EnsureBattleParameterDatabase();
    }

    [ContextMenu("Refresh Now")]
    public void RefreshNow()
    {
        SyncAllNow();
        SyncStatusesNow();
        nextStatusForceSyncTime = Time.unscaledTime + Mathf.Max(0.02f, statusForceSyncInterval);
    }


    private void EnsureDefinitionAndStyle()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (runtimeBinder == null || overheadView == null)
            return;

        UnitDefinition ud = runtimeBinder.UnitDefinitionAsset;
        if (ud == null)
        {
            overheadView.SetNameVisible(false);
            return;
        }

        bool definitionChanged = overheadView.unitDefinition != ud || overheadView.runtimeUiRoot == null;
        if (definitionChanged)
        {
            overheadView.unitDefinition = ud;
            overheadView.ApplyStyleFromDefinition();
        }

        overheadView.SetDisplayName(string.IsNullOrWhiteSpace(ud.displayName) ? ud.name : ud.displayName);
        overheadView.SetNameVisible(ud.autoOverheadNameVisibility ? (ud.characterIdentity == CharacterIdentity.Ally) : ud.manualShowOverheadName);
    }

    private void SyncAllNow()
    {
        if (healthController == null || overheadView == null)
            return;

        EnsureDefinitionAndStyle();
        overheadView.SetHp(healthController.CurrentHealth, healthController.MaxHealth);

        bool deadLike = deathController != null && deathController.IsDeadLike;
        ApplyDeathVisual(deadLike);
        ForceOverheadVisibility(deadLike);

        lastCurrentHealth = healthController.CurrentHealth;
        lastMaxHealth = healthController.MaxHealth;
        lastRegainPool = healthController.RegainPool;
        lastDeadLike = deadLike;

        if (debugLogs)
            Debug.Log($"[UnitOverheadHealthBridge] SyncAllNow -> hp={healthController.CurrentHealth}/{healthController.MaxHealth}, dead={deadLike}, def={(runtimeBinder != null && runtimeBinder.UnitDefinitionAsset != null ? runtimeBinder.UnitDefinitionAsset.name : "None")}", this);
    }


    private void ForceOverheadVisibility(bool deadLike)
    {
        if (overheadView == null)
            return;

        UnitDefinition ud = runtimeBinder != null ? runtimeBinder.UnitDefinitionAsset : overheadView.unitDefinition;
        bool showName = false;
        if (ud != null)
            showName = ud.autoOverheadNameVisibility ? (ud.characterIdentity == CharacterIdentity.Ally) : ud.manualShowOverheadName;

        if (overheadView.nameRoot != null)
            overheadView.nameRoot.gameObject.SetActive(!deadLike && showName);

        CanvasGroup cg = overheadView.slotRoot01 != null ? overheadView.slotRoot01.GetComponent<CanvasGroup>() : null;

        if (deadLike)
        {
            // 死亡期间：虚血还在衰减就保持显示（让血条空/虚血慢慢消退），
            // 虚血彻底归零后才隐藏整个 slotRoot01
            float regain = healthController != null ? healthController.RegainPool : 0f;
            bool hasRegain = regain > 0.01f;

            if (overheadView.slotRoot01 != null)
                overheadView.slotRoot01.gameObject.SetActive(hasRegain);

            if (cg != null)
                cg.alpha = hasRegain ? 1f : 0f;
        }
        else
        {
            if (overheadView.slotRoot01 != null)
                overheadView.slotRoot01.gameObject.SetActive(true);

            if (cg != null)
            {
                float max     = healthController != null ? healthController.MaxHealth : 0f;
                float current = healthController != null ? healthController.CurrentHealth : 0f;
                bool shouldShowBar = max > 0.0001f && current < max;
                cg.alpha = shouldShowBar ? 1f : 0f;
            }
        }
    }




    private void EnsureBattleParameterDatabase()
    {
        if (battleParameterDatabase != null || !autoFindRuntimeBattleDatabase)
            return;

        BattleParameterDatabase[] all = Resources.LoadAll<BattleParameterDatabase>("BattleParameters");
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].isRuntimeActive)
            {
                battleParameterDatabase = all[i];
                break;
            }
        }

        if (battleParameterDatabase == null)
            battleParameterDatabase = Resources.Load<BattleParameterDatabase>(DefaultBattleParameterDatabaseResourcesPath);

        if (debugLogs && battleParameterDatabase != null)
            Debug.Log($"[UnitOverheadHealthBridge] 自动加载战斗参数库成功: {battleParameterDatabase.name}", this);
    }

    private void SyncStatusesNow()
    {
        if (overheadView == null)
            return;

        if (statusController == null)
            statusController = GetComponent<UnitStatusController>();

        EnsureBattleParameterDatabase();

        OverheadBarStyleAsset style = runtimeBinder != null && runtimeBinder.UnitDefinitionAsset != null ? runtimeBinder.UnitDefinitionAsset.overheadHpBarStyle : null;
        string displayKey = style != null ? style.statusDisplayKey : string.Empty;
        StatusDisplayDefinition displayDefinition = battleParameterDatabase != null ? battleParameterDatabase.FindStatusDisplay(displayKey) : null;
        overheadView.RefreshStatuses(statusController != null ? statusController.VisibleStatuses : null, displayDefinition);
    }


    private void HandleDotDamageApplied(RuntimeStatusEntry entry, float amount)
    {
        if (entry == null || amount <= 0f || overheadView == null)
            return;

        string key = entry.definition != null ? entry.definition.dotDamageTypeKey : string.Empty;
        ShowDamageNumberByKey(key, amount, false);
    }

    public void ShowDamageNumberByKey(string damageTypeKey, float amount, bool isHeal = false,
        bool isPositiveCrit = false, bool isNegativeCrit = false)
    {
        if (overheadView == null || amount <= 0f)
            return;

        EnsureBattleParameterDatabase();
        Color color = ResolveDamageNumberColor(damageTypeKey, isHeal);
        overheadView.ShowDamageNumber(amount, color, isHeal, isPositiveCrit, isNegativeCrit);
    }

    private Color ResolveDamageNumberColor(string damageTypeKey, bool isHeal)
    {
        if (isHeal)
            return new Color(0.72f, 0.96f, 0.72f, 1f);

        // 统一白字，暴击等特殊情况在调用侧单独传色。
        return Color.white;
    }

    private void HandleStyleChanged(OverheadBarStyleAsset changedStyle)
    {
        if (runtimeBinder == null || overheadView == null)
            return;

        UnitDefinition ud = runtimeBinder.UnitDefinitionAsset;
        if (ud == null || ud.overheadHpBarStyle != changedStyle)
            return;

        RefreshNow();
    }

    private void BindEvents()
    {
        if (statusController != null)
        {
            statusController.StatusesChanged -= HandleStatusesChanged;
            statusController.DotDamageApplied -= HandleDotDamageApplied;
            statusController.StatusesChanged += HandleStatusesChanged;
            statusController.DotDamageApplied += HandleDotDamageApplied;
        }

        if (healthController != null)
        {
            healthController.OnDamaged -= HandleHealthChanged;
            healthController.OnHealed -= HandleHealthChanged;
            healthController.OnFullHealed -= HandleFullHealed;
            healthController.OnHealthZero -= HandleHealthZero;

            healthController.OnDamaged += HandleHealthChanged;
            healthController.OnHealed += HandleHealthChanged;
            healthController.OnFullHealed += HandleFullHealed;
            healthController.OnHealthZero += HandleHealthZero;
        }

        if (deathController != null)
        {
            deathController.OnDeathStarted -= HandleDeathStarted;
            deathController.OnRespawned -= HandleRespawned;

            deathController.OnDeathStarted += HandleDeathStarted;
            deathController.OnRespawned += HandleRespawned;
        }
    }

    private void UnbindEvents()
    {
        if (statusController != null)
        {
            statusController.StatusesChanged -= HandleStatusesChanged;
            statusController.DotDamageApplied -= HandleDotDamageApplied;
        }

        if (healthController != null)
        {
            healthController.OnDamaged -= HandleHealthChanged;
            healthController.OnHealed -= HandleHealthChanged;
            healthController.OnFullHealed -= HandleFullHealed;
            healthController.OnHealthZero -= HandleHealthZero;
        }

        if (deathController != null)
        {
            deathController.OnDeathStarted -= HandleDeathStarted;
            deathController.OnRespawned -= HandleRespawned;
        }
    }

    private void HandleHealthChanged(UnitHealthController controller, float amount)
    {
        SyncAllNow();
    }

    private void HandleStatusesChanged()
    {
        SyncStatusesNow();
    }

    private void HandleFullHealed(UnitHealthController controller)
    {
        SyncAllNow();
    }

    private void HandleHealthZero(UnitHealthController controller)
    {
        SyncAllNow();
    }

    private void HandleDeathStarted(UnitDeathController controller)
    {
        ApplyDeathVisual(true);
        if (overheadView != null)
            overheadView.ClearStatuses();
        if (debugLogs)
            Debug.Log("[UnitOverheadHealthBridge] HandleDeathStarted -> hide overhead", this);
    }

    private void HandleRespawned(UnitDeathController controller)
    {
        ApplyDeathVisual(false);
        RefreshNow();
        if (debugLogs)
            Debug.Log("[UnitOverheadHealthBridge] HandleRespawned -> show overhead", this);
    }

    private void ApplyDeathVisual(bool deadLike)
    {
        if (overheadView == null)
            return;

        // slotRoot01 由 ForceOverheadVisibility 的虚血判断统一管理，这里只隐藏名字
        if (overheadView.nameRoot != null && hideNameOnDeath)
            overheadView.nameRoot.gameObject.SetActive(!deadLike);
    }
}
