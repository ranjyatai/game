using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// V2 - 2026-06-09: Auto ensures runtime identity for character units using definition holder.
// This does not modify UnitDefinition assets. It only guarantees the unit instance has the runtime authority layer.
[DisallowMultipleComponent]
public class UnitDefinitionHolder : MonoBehaviour
{
    [SerializeField] private UnitDefinition definition;

    [Header("Runtime Authority")]
    [Tooltip("当这个 Holder 指向角色单位定义时，自动补 SkyPrisonUnitRuntimeIdentity。")]
    [SerializeField] private bool autoEnsureRuntimeIdentityForCharacterUnits = true;

    public UnitDefinition Definition => definition;

    private void Reset()
    {
        EnsureRuntimeIdentityForCurrentDefinition();
    }

    private void Awake()
    {
        EnsureRuntimeIdentityForCurrentDefinition();
    }

    private void OnEnable()
    {
        EnsureRuntimeIdentityForCurrentDefinition();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureRuntimeIdentityForCurrentDefinition();
    }
#endif

    [ContextMenu("Runtime Authority/Ensure Runtime Identity")]
    public void EnsureRuntimeIdentityForCurrentDefinition()
    {
        if (!autoEnsureRuntimeIdentityForCharacterUnits)
            return;

        if (definition == null || definition.defineType != UnitDefineType.Character)
            return;

        SkyPrisonUnitRuntimeIdentity identity = GetComponent<SkyPrisonUnitRuntimeIdentity>();
        if (identity == null)
            identity = gameObject.AddComponent<SkyPrisonUnitRuntimeIdentity>();

        if (identity == null)
            return;

        identity.ResolveReferences();
        identity.ResolveDefinitionIdentity();

        if (Application.isPlaying)
        {
            identity.InitializeFromDefinitionIfNeeded();
            identity.ApplyRuntimeIdentitySideEffects();
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
#endif
    }
}
