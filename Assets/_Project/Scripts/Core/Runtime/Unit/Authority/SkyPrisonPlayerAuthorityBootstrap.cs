using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[DefaultExecutionOrder(10800)]
public class SkyPrisonPlayerAuthorityBootstrap : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V2 - 2026-06-09 - auto attach runtime identity to units";

    [Header("Authority")]
    [SerializeField] private bool createAuthorityIfMissing = true;

    [Header("Auto Attach Runtime Identity")]
    [SerializeField] private bool autoAttachRuntimeIdentityToDefinedUnits = true;
    [SerializeField] private bool includeInactiveUnits = true;
    [SerializeField] private bool rescanAfterSceneLoaded = true;

    [Tooltip("运行时定期补扫后生成单位。0 表示不定期扫描，只在 Awake/场景加载时扫描。")]
    [SerializeField] private float runtimeRescanIntervalSeconds = 0.50f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    public string Version => scriptVersion;

    private float nextRuntimeRescanTime;

    private void Awake()
    {
        EnsureAuthorityExists();

        if (autoAttachRuntimeIdentityToDefinedUnits)
            EnsureRuntimeIdentityForDefinedUnits();
    }

    private void OnEnable()
    {
        if (rescanAfterSceneLoaded)
            SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (rescanAfterSceneLoaded)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        EnsureAuthorityExists();

        if (autoAttachRuntimeIdentityToDefinedUnits)
            EnsureRuntimeIdentityForDefinedUnits();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (!autoAttachRuntimeIdentityToDefinedUnits)
            return;

        if (runtimeRescanIntervalSeconds <= 0f)
            return;

        if (Time.unscaledTime < nextRuntimeRescanTime)
            return;

        nextRuntimeRescanTime = Time.unscaledTime + runtimeRescanIntervalSeconds;
        EnsureRuntimeIdentityForDefinedUnits();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureAuthorityExists();

        if (autoAttachRuntimeIdentityToDefinedUnits)
            EnsureRuntimeIdentityForDefinedUnits();
    }

    [ContextMenu("Bootstrap/Ensure Runtime Authority Exists")]
    public void EnsureAuthorityExists()
    {
        if (!createAuthorityIfMissing)
            return;

        if (FindObjectOfType<SkyPrisonPlayerAuthority>() != null)
            return;

        GameObject authorityObject = new GameObject("SkyPrisonPlayerAuthority");
        authorityObject.AddComponent<SkyPrisonPlayerAuthority>();

        if (debugLogs)
            Debug.Log("[SkyPrisonPlayerAuthorityBootstrap] Created SkyPrisonPlayerAuthority.", authorityObject);
    }

    [ContextMenu("Bootstrap/Ensure Runtime Identity For Defined Units")]
    public void EnsureRuntimeIdentityForDefinedUnits()
    {
        int addedOrTouched = 0;

        UnitDefinitionRuntimeBinder[] binders = FindObjectsOfType<UnitDefinitionRuntimeBinder>(includeInactiveUnits);
        for (int i = 0; i < binders.Length; i++)
        {
            UnitDefinitionRuntimeBinder binder = binders[i];
            if (binder == null || binder.UnitDefinitionAsset == null)
                continue;

            if (binder.UnitDefinitionAsset.defineType != UnitDefineType.Character)
                continue;

            if (EnsureRuntimeIdentityForUnitObject(binder.gameObject))
                addedOrTouched++;
        }

        UnitDefinitionHolder[] holders = FindObjectsOfType<UnitDefinitionHolder>(includeInactiveUnits);
        for (int i = 0; i < holders.Length; i++)
        {
            UnitDefinitionHolder holder = holders[i];
            if (holder == null || holder.Definition == null)
                continue;

            if (holder.Definition.defineType != UnitDefineType.Character)
                continue;

            if (EnsureRuntimeIdentityForUnitObject(holder.gameObject))
                addedOrTouched++;
        }

        if (debugLogs && addedOrTouched > 0)
            Debug.Log($"[SkyPrisonPlayerAuthorityBootstrap] Runtime identity ensured for {addedOrTouched} unit(s).", this);
    }

    public static bool EnsureRuntimeIdentityForUnitObject(GameObject unitObject)
    {
        if (unitObject == null)
            return false;

        SkyPrisonUnitRuntimeIdentity identity = unitObject.GetComponent<SkyPrisonUnitRuntimeIdentity>();
        bool added = false;

        if (identity == null)
        {
            identity = unitObject.AddComponent<SkyPrisonUnitRuntimeIdentity>();
            added = true;
        }

        if (identity == null)
            return false;

        identity.ResolveReferences();
        identity.ResolveDefinitionIdentity();

        if (Application.isPlaying)
        {
            identity.InitializeFromDefinitionIfNeeded();
            identity.ApplyRuntimeIdentitySideEffects();
            SkyPrisonPlayerAuthority.RegisterUnit(identity);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(unitObject);
#endif

        return added;
    }
}
