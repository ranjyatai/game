using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class SkyPrisonSceneUnitMarker : MonoBehaviour
{
    [SerializeField] private string sceneUnitGuid;

    [Header("自动同步缓存（不要手填）")]
    [SerializeField] private string cachedUnitDefinitionId;
    [SerializeField] private string cachedDisplayName;

    public string SceneUnitGuid
    {
        get
        {
            EnsureGuidRuntime();
            return sceneUnitGuid;
        }
    }

    public string UnitDefinitionId
    {
        get
        {
            RefreshBindingCache();
            return cachedUnitDefinitionId;
        }
    }

    public string DisplayName
    {
        get
        {
            RefreshBindingCache();
            return string.IsNullOrWhiteSpace(cachedDisplayName) ? gameObject.name : cachedDisplayName;
        }
    }

    public string GetBestLabel()
    {
        RefreshBindingCache();

        if (!string.IsNullOrWhiteSpace(cachedUnitDefinitionId))
            return cachedUnitDefinitionId;

        if (!string.IsNullOrWhiteSpace(cachedDisplayName))
            return cachedDisplayName;

        return gameObject.name;
    }

    public bool IsValidUnit()
    {
        RefreshBindingCache();
        return !string.IsNullOrWhiteSpace(cachedUnitDefinitionId);
    }

    public void RefreshBindingCache()
    {
        UnitDefinitionRuntimeBinder binder = ResolveBinder();

        if (binder != null && binder.UnitDefinitionAsset != null)
        {
            cachedUnitDefinitionId = binder.UnitDefinitionAsset.unitId;
            cachedDisplayName = string.IsNullOrWhiteSpace(binder.UnitDefinitionAsset.displayName)
                ? binder.UnitDefinitionAsset.name
                : binder.UnitDefinitionAsset.displayName;
        }
        else
        {
            cachedUnitDefinitionId = "";
            cachedDisplayName = "";
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    private UnitDefinitionRuntimeBinder ResolveBinder()
    {
        // 先找自己
        UnitDefinitionRuntimeBinder binder = GetComponent<UnitDefinitionRuntimeBinder>();
        if (binder != null)
            return binder;

        // 再找父级（你的项目更像这种挂法）
        binder = GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
        if (binder != null)
            return binder;

        return null;
    }

    private void Awake()
    {
        EnsureGuidRuntime();
        RefreshBindingCache();
    }

    private void OnEnable()
    {
        EnsureGuidRuntime();
        RefreshBindingCache();
    }

    private void EnsureGuidRuntime()
    {
        if (string.IsNullOrWhiteSpace(sceneUnitGuid))
            sceneUnitGuid = System.Guid.NewGuid().ToString();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        EnsureGuidEditor();
        RefreshBindingCache();
        EditorUtility.SetDirty(this);
    }

    private void OnValidate()
    {
        EnsureGuidEditor();
        RefreshBindingCache();
        EditorUtility.SetDirty(this);
    }

    private void EnsureGuidEditor()
    {
        if (string.IsNullOrWhiteSpace(sceneUnitGuid))
            sceneUnitGuid = GUID.Generate().ToString();
    }
#endif
}
