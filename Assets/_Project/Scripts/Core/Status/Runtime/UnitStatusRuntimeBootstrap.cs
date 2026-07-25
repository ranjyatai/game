using UnityEngine;

[DisallowMultipleComponent]
public class UnitStatusRuntimeBootstrap : MonoBehaviour
{
    [SerializeField] private bool autoSetupOnAwake = true;
    [SerializeField] private bool autoBindDefinition = true;
    [SerializeField] private bool debugLogs = false;

    [Header("缓存引用")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private UnitDefinitionRuntimeApplier runtimeApplier;
    [SerializeField] private UnitStatusController statusController;
    [SerializeField] private UnitAnomalyController anomalyController;
    [SerializeField] private UnitBattleStatRuntime battleStatRuntime;
    [SerializeField] private UnitOverheadHealthBridge overheadHealthBridge;
    [SerializeField] private UnitStatusOutlineEffect statusOutlineEffect;
    [SerializeField] private UnitStatusFlashEffect statusFlashEffect;

    private void Awake()
    {
        if (!autoSetupOnAwake)
            return;

        EnsureAll();
    }

    [ContextMenu("Ensure Status Runtime")]
    public void EnsureAll()
    {
        EnsureRootReferences();
        EnsureStatusController();
        EnsureAnomalyController();
        EnsureBattleStatRuntime();
        EnsureStatusOutlineEffect();
        EnsureStatusFlashEffect();
        EnsureStatusControllerInitialization();
        EnsureOverheadBridgeReference();

        if (debugLogs)
            Debug.Log($"[UnitStatusRuntimeBootstrap] Ensure 完成 -> {name}", this);
    }

    private void EnsureRootReferences()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (runtimeApplier == null)
            runtimeApplier = GetComponent<UnitDefinitionRuntimeApplier>();

        if (overheadHealthBridge == null)
            overheadHealthBridge = GetComponent<UnitOverheadHealthBridge>();
    }

    private void EnsureStatusController()
    {
        statusController = UnitStatusController.EnsureOnRoot(gameObject);
    }

    private void EnsureAnomalyController()
    {
        anomalyController = UnitAnomalyController.EnsureOnRoot(gameObject);
    }

    private void EnsureBattleStatRuntime()
    {
        battleStatRuntime = UnitBattleStatRuntime.EnsureOnRoot(gameObject);
    }

    private void EnsureStatusOutlineEffect()
    {
        statusOutlineEffect = UnitStatusOutlineEffect.EnsureOnRoot(gameObject);
    }

    private void EnsureStatusFlashEffect()
    {
        statusFlashEffect = UnitStatusFlashEffect.EnsureOnRoot(gameObject);
    }

    private void EnsureStatusControllerInitialization()
    {
        if (!autoBindDefinition)
            return;

        UnitDefinition definition = ResolveUnitDefinition();
        if (definition != null)
        {
            if (statusController != null)
                statusController.EnsureInitialized(definition);

            if (anomalyController != null)
                anomalyController.EnsureInitialized(definition);

            if (battleStatRuntime != null)
                battleStatRuntime.EnsureInitialized(definition);
        }
    }

    private void EnsureOverheadBridgeReference()
    {
        if (statusController == null || overheadHealthBridge == null)
            return;
    }

    private UnitDefinition ResolveUnitDefinition()
    {
        if (runtimeBinder != null)
        {
            var binderType = runtimeBinder.GetType();
            var field = binderType.GetField("unitDefinitionAsset");
            if (field != null)
            {
                UnitDefinition def = field.GetValue(runtimeBinder) as UnitDefinition;
                if (def != null)
                    return def;
            }

            var prop = binderType.GetProperty("UnitDefinitionAsset");
            if (prop != null)
            {
                UnitDefinition def = prop.GetValue(runtimeBinder, null) as UnitDefinition;
                if (def != null)
                    return def;
            }
        }

        if (runtimeApplier != null)
        {
            var applierType = runtimeApplier.GetType();
            var field = applierType.GetField("unitDefinitionAsset");
            if (field != null)
            {
                UnitDefinition def = field.GetValue(runtimeApplier) as UnitDefinition;
                if (def != null)
                    return def;
            }

            var prop = applierType.GetProperty("UnitDefinitionAsset");
            if (prop != null)
            {
                UnitDefinition def = prop.GetValue(runtimeApplier, null) as UnitDefinition;
                if (def != null)
                    return def;
            }
        }

        return null;
    }
}
