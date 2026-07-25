using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Spine.Unity;

// V26 - 2026-06-09: Runtime PlayerAuthority integration.
// Auto ensures SkyPrisonUnitRuntimeIdentity on character units at the definition binding source.
// Keeps PF/UnitDefinition assets untouched; runtime identity is a separate override layer.
// V25 - 2026-06-05: KeepOccludedProxyOnly. Bind real Spine source plus only OutlineProxy_*_Occluded required by current occlusion mask chain. Removed old normal OutlineProxy_* and old camera route.

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class UnitDefinitionRuntimeBinder : MonoBehaviour
{
    [Header("Runtime Binding")]
    [SerializeField] private UnitDefinition unitDefinitionAsset;

    [Header("Options")]
    [SerializeField] private bool applyDefinitionOnAwake = true;
    [SerializeField] private bool applyDefinitionOnEnable = false;
    [SerializeField] private bool debugLogs = false;

    [Header("Runtime Authority")]
    [Tooltip("角色单位绑定定义后，自动补 SkyPrisonUnitRuntimeIdentity。这里是运行时身份层，不修改 UnitDefinition 资产。")]
    [SerializeField] private bool autoEnsureRuntimeIdentityForCharacterUnits = true;

    [Header("Spine 4.3 Source Binding / V25 Keep Occluded Proxy Only")]
    [SerializeField] private bool autoBindSpine43Renderers = true;
    [SerializeField] private string spineRootName = "SpineRoot";
    [SerializeField] private string spineSourceNameContains = "Spine GameObject";

    [Header("Occluded Proxy Binding - Required Current Baseline")]
    [SerializeField] private bool bindOccludedOutlineProxies = true;
    [SerializeField] private bool createMissingOccludedProxyComponents = true;
    [SerializeField] private string outlineProxyRootName = "OutlineProxyRoot";

    private static readonly string[] RequiredOccludedProxyNames =
    {
        "OutlineProxy_Player_Occluded",
        "OutlineProxy_Enemy_Occluded",
        "OutlineProxy_Item_Occluded",
        "OutlineProxy_Ally_Occluded",
    };

    public UnitDefinition UnitDefinitionAsset => unitDefinitionAsset;

    private void Awake()
    {
        RefreshSceneMarkerCache();
        EnsureRuntimeIdentityForCurrentDefinition();

        if (applyDefinitionOnAwake)
            ApplyDefinitionIfPossible();
        else
            RefreshSpine43RendererBindingsIfPossible();
    }

    private void OnEnable()
    {
        RefreshSceneMarkerCache();
        EnsureRuntimeIdentityForCurrentDefinition();

        if (applyDefinitionOnEnable)
            ApplyDefinitionIfPossible();
        else
            RefreshSpine43RendererBindingsIfPossible();

        if (Application.isPlaying)
            SkyPrisonVisionManager.Instance?.RegisterUnit(this);
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            SkyPrisonVisionManager.Instance?.UnregisterUnit(this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshSceneMarkerCache();
        EnsureRuntimeIdentityForCurrentDefinition();

        if (!Application.isPlaying)
            RefreshSpine43RendererBindingsIfPossible();
    }
#endif

    [ContextMenu("Apply Definition If Possible")]
    public void ApplyDefinitionIfPossible()
    {
        RefreshSceneMarkerCache();

        if (unitDefinitionAsset == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: UnitDefinition is null.", this);

            RefreshSpine43RendererBindingsIfPossible();
            return;
        }

        UnitDefinitionRuntimeApplier applier = GetComponent<UnitDefinitionRuntimeApplier>();
        if (applier != null)
        {
            applier.ApplyDefinition();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Applied '{unitDefinitionAsset.name}'.", this);
        }
        else if (debugLogs)
        {
            Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: No UnitDefinitionRuntimeApplier found.", this);
        }

        EnsureRuntimeIdentityForCurrentDefinition();
        RefreshSpine43RendererBindingsIfPossible();
    }

    [ContextMenu("Refresh Spine 4.3 Source Binding")]
    public void RefreshSpine43RendererBindingsIfPossible()
    {
        if (!autoBindSpine43Renderers)
            return;

        SkeletonAnimation sourceAnimation = FindMainSpineAnimation();
        if (sourceAnimation == null)
        {
            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: No main Spine SkeletonAnimation found. Skip Spine 4.3 binding.", this);
            return;
        }

        GameObject sourceObject = sourceAnimation.gameObject;
        EnsureSourceSpine43Components(sourceObject);

        SkeletonRenderer sourceRenderer = sourceObject.GetComponent<SkeletonRenderer>();

        // Spine 4.3 升级后，运行时 Prefab 上的 SkeletonRenderer / SkeletonAnimation
        // 可能已经存在，但 skeletonDataAsset 会丢失。这里必须以 UnitDefinition
        // 当前选择的 Spine .asset 为第一来源，再回退到场景对象自身。
        SkeletonDataAsset sourceDataAsset = ResolveSkeletonDataAssetFromUnitDefinition(unitDefinitionAsset)
            ?? ResolveSkeletonDataAsset(sourceAnimation, sourceRenderer);

        Material[] sourceMaterials = ResolveSourceMaterials(sourceObject, sourceDataAsset);

        if (sourceDataAsset == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: Source Spine object has no SkeletonDataAsset.", sourceObject);
            return;
        }

        BindSourceSpineObject(sourceObject, sourceAnimation, sourceRenderer, sourceDataAsset, sourceMaterials);

        int boundOccludedProxyCount = RefreshRequiredOccludedProxyBindings(sourceAnimation, sourceDataAsset, sourceMaterials);

        if (debugLogs)
            Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Spine source binding refreshed. Source='{sourceObject.name}', skeleton='{sourceDataAsset.name}', occludedProxies={boundOccludedProxyCount}.", this);
    }

    public void SetUnitDefinitionAsset(UnitDefinition definitionAsset, bool applyNow = true)
    {
        unitDefinitionAsset = definitionAsset;
        RefreshSceneMarkerCache();

        if (debugLogs)
        {
            string assetName = unitDefinitionAsset != null ? unitDefinitionAsset.name : "None";
            Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Set UnitDefinition -> {assetName}", this);
        }

        if (applyNow)
            ApplyDefinitionIfPossible();
        else
            RefreshSpine43RendererBindingsIfPossible();

        EnsureRuntimeIdentityForCurrentDefinition();
    }

    public void ClearUnitDefinitionAsset()
    {
        unitDefinitionAsset = null;
        RefreshSceneMarkerCache();

        if (debugLogs)
            Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Cleared UnitDefinition.", this);
    }

    public bool HasDefinition()
    {
        return unitDefinitionAsset != null;
    }

    [ContextMenu("Runtime Authority/Ensure Runtime Identity")]
    public void EnsureRuntimeIdentityForCurrentDefinition()
    {
        if (!autoEnsureRuntimeIdentityForCharacterUnits)
            return;

        UnitDefinition definition = unitDefinitionAsset;
        if (definition == null || definition.defineType != UnitDefineType.Character)
            return;

        SkyPrisonUnitRuntimeIdentity identity = GetComponent<SkyPrisonUnitRuntimeIdentity>();
        if (identity == null)
        {
            identity = gameObject.AddComponent<SkyPrisonUnitRuntimeIdentity>();

            if (debugLogs)
                Debug.Log($"[UnitDefinitionRuntimeBinder] {name}: Auto added SkyPrisonUnitRuntimeIdentity.", this);
        }

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

    private void EnsureSourceSpine43Components(GameObject sourceObject)
    {
        if (sourceObject == null)
            return;

        if (sourceObject.GetComponent<MeshFilter>() == null)
            sourceObject.AddComponent<MeshFilter>();

        if (sourceObject.GetComponent<MeshRenderer>() == null)
            sourceObject.AddComponent<MeshRenderer>();

        if (sourceObject.GetComponent<SkeletonRenderer>() == null)
            sourceObject.AddComponent<SkeletonRenderer>();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(sourceObject);
#endif
    }

    private void BindSourceSpineObject(GameObject sourceObject, SkeletonAnimation sourceAnimation, SkeletonRenderer sourceRenderer, SkeletonDataAsset sourceDataAsset, Material[] sourceMaterials)
    {
        if (sourceObject == null || sourceDataAsset == null)
            return;

        MeshRenderer meshRenderer = sourceObject.GetComponent<MeshRenderer>();
        MeshFilter meshFilter = sourceObject.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "Skeleton Mesh" };

        if (sourceAnimation != null)
            SetSkeletonDataAsset(sourceAnimation, sourceDataAsset);
        if (sourceRenderer != null)
            SetSkeletonDataAsset(sourceRenderer, sourceDataAsset);

        if (meshRenderer != null && sourceMaterials != null && sourceMaterials.Length > 0)
            meshRenderer.sharedMaterials = sourceMaterials;

        TryInvokeInitialize(sourceRenderer);
        TryInvokeInitialize(sourceAnimation);

        MarkDirty(sourceObject, sourceAnimation, sourceRenderer, meshRenderer, meshFilter);
    }

    private int RefreshRequiredOccludedProxyBindings(SkeletonAnimation sourceAnimation, SkeletonDataAsset sourceDataAsset, Material[] sourceMaterials)
    {
        if (!bindOccludedOutlineProxies || sourceAnimation == null || sourceDataAsset == null)
            return 0;

        Transform outlineRoot = FindDeepChild(transform, outlineProxyRootName);
        Transform searchRoot = outlineRoot != null ? outlineRoot : transform;

        int boundCount = 0;
        for (int i = 0; i < RequiredOccludedProxyNames.Length; i++)
        {
            Transform proxy = FindDeepChild(searchRoot, RequiredOccludedProxyNames[i]);
            if (proxy == null)
                continue;

            if (BindOneOccludedSpineProxy(proxy.gameObject, sourceAnimation, sourceDataAsset, sourceMaterials))
                boundCount++;
        }

        return boundCount;
    }

    private bool BindOneOccludedSpineProxy(GameObject proxyObject, SkeletonAnimation sourceAnimation, SkeletonDataAsset sourceDataAsset, Material[] sourceMaterials)
    {
        if (proxyObject == null || sourceAnimation == null || sourceDataAsset == null)
            return false;

        MeshFilter meshFilter = proxyObject.GetComponent<MeshFilter>();
        if (meshFilter == null && createMissingOccludedProxyComponents)
            meshFilter = proxyObject.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer = proxyObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null && createMissingOccludedProxyComponents)
            meshRenderer = proxyObject.AddComponent<MeshRenderer>();

        SkeletonAnimation targetAnimation = proxyObject.GetComponent<SkeletonAnimation>();
        if (targetAnimation == null && createMissingOccludedProxyComponents)
            targetAnimation = proxyObject.AddComponent<SkeletonAnimation>();

        SkeletonRenderer targetRenderer = proxyObject.GetComponent<SkeletonRenderer>();
        if (targetRenderer == null && createMissingOccludedProxyComponents)
            targetRenderer = proxyObject.GetComponent<SkeletonRenderer>() ?? proxyObject.AddComponent<SkeletonRenderer>();

        if (meshFilter != null && meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "Skeleton Mesh" };

        if (meshRenderer != null && sourceMaterials != null && sourceMaterials.Length > 0)
            meshRenderer.sharedMaterials = sourceMaterials;

        if (targetAnimation == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[UnitDefinitionRuntimeBinder] {name}: Occluded proxy '{proxyObject.name}' has no SkeletonAnimation.", proxyObject);
            return false;
        }

        SetSkeletonDataAsset(targetAnimation, sourceDataAsset);
        if (targetRenderer != null)
            SetSkeletonDataAsset(targetRenderer, sourceDataAsset);

        TryInvokeInitialize(targetRenderer);
        TryInvokeInitialize(targetAnimation);

        SpineOutlineFollower follower = proxyObject.GetComponent<SpineOutlineFollower>();
        if (follower == null && createMissingOccludedProxyComponents)
            follower = proxyObject.AddComponent<SpineOutlineFollower>();

        if (follower != null)
        {
            SetPrivateField(follower, "source", sourceAnimation);
            SetPrivateField(follower, "target", targetAnimation);
            MarkDirty(follower);
        }

        ForceSilhouetteMaterial forceMaterial = proxyObject.GetComponent<ForceSilhouetteMaterial>();
        if (forceMaterial != null && meshRenderer != null)
        {
            SetPrivateField(forceMaterial, "targetRenderer", meshRenderer);
            MarkDirty(forceMaterial);
        }

        MarkDirty(proxyObject, meshFilter, meshRenderer, targetAnimation, targetRenderer);
        return true;
    }


    private SkeletonAnimation FindMainSpineAnimation()
    {
        Transform spineRoot = FindDeepChild(transform, spineRootName);
        SkeletonAnimation[] animations = spineRoot != null
            ? spineRoot.GetComponentsInChildren<SkeletonAnimation>(true)
            : GetComponentsInChildren<SkeletonAnimation>(true);

        if (animations == null || animations.Length == 0)
            return null;

        SkeletonAnimation fallback = null;

        for (int i = 0; i < animations.Length; i++)
        {
            SkeletonAnimation candidate = animations[i];
            if (candidate == null)
                continue;

            if (IsOutlineProxyTransform(candidate.transform))
                continue;

            if (fallback == null)
                fallback = candidate;

            if (!string.IsNullOrWhiteSpace(spineSourceNameContains) &&
                candidate.name.Contains(spineSourceNameContains))
            {
                return candidate;
            }
        }

        return fallback;
    }

    private bool IsOutlineProxyTransform(Transform t)
    {
        while (t != null && t != transform)
        {
            if (t.name.StartsWith("OutlineProxy_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(t.name, outlineProxyRootName, StringComparison.OrdinalIgnoreCase))
                return true;

            t = t.parent;
        }

        return false;
    }


    private static SkeletonDataAsset ResolveSkeletonDataAssetFromUnitDefinition(UnitDefinition definition)
    {
        if (definition == null)
            return null;

        HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        SkeletonDataAsset best = null;
        int bestScore = int.MinValue;
        ScanObjectForSkeletonDataAsset(definition, "UnitDefinition", 0, visited, ref best, ref bestScore);
        return best;
    }

    private static void ScanObjectForSkeletonDataAsset(object obj, string path, int depth, HashSet<object> visited, ref SkeletonDataAsset best, ref int bestScore)
    {
        if (obj == null || depth > 4)
            return;

        if (obj is SkeletonDataAsset directAsset)
        {
            int directScore = ScoreSkeletonDataPath(path);
            if (directScore > bestScore)
            {
                best = directAsset;
                bestScore = directScore;
            }
            return;
        }

        Type type = obj.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return;

        // 不深入 Unity 资源内部，避免扫进 Material / Texture / GameObject 的大量字段。
        if (obj is UnityEngine.Object && !(obj is UnitDefinition))
            return;

        if (!type.IsValueType)
        {
            if (visited.Contains(obj))
                return;
            visited.Add(obj);
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field == null || field.IsStatic)
                continue;

            object value = null;
            try { value = field.GetValue(obj); }
            catch { continue; }

            string childPath = path + "." + field.Name;
            if (value is SkeletonDataAsset asset)
            {
                int score = ScoreSkeletonDataPath(childPath);
                if (score > bestScore)
                {
                    best = asset;
                    bestScore = score;
                }
                continue;
            }

            if (ShouldScanNestedValue(value, field.FieldType))
                ScanObjectForSkeletonDataAsset(value, childPath, depth + 1, visited, ref best, ref bestScore);
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            object value = null;
            try { value = property.GetValue(obj, null); }
            catch { continue; }

            string childPath = path + "." + property.Name;
            if (value is SkeletonDataAsset asset)
            {
                int score = ScoreSkeletonDataPath(childPath);
                if (score > bestScore)
                {
                    best = asset;
                    bestScore = score;
                }
                continue;
            }

            if (ShouldScanNestedValue(value, property.PropertyType))
                ScanObjectForSkeletonDataAsset(value, childPath, depth + 1, visited, ref best, ref bestScore);
        }
    }

    private static bool ShouldScanNestedValue(object value, Type declaredType)
    {
        if (value == null || declaredType == null)
            return false;

        if (value is SkeletonDataAsset)
            return true;

        if (value is UnityEngine.Object)
            return false;

        if (value is IEnumerable && !(value is string))
            return false;

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return false;

        string fullName = type.FullName ?? string.Empty;
        if (fullName.StartsWith("System."))
            return false;

        return true;
    }

    private static int ScoreSkeletonDataPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;

        string lower = path.ToLowerInvariant();
        int score = 0;
        if (lower.Contains("spine")) score += 100;
        if (lower.Contains("skeleton")) score += 80;
        if (lower.Contains("asset")) score += 20;
        if (lower.Contains("current")) score += 10;
        if (lower.Contains("runtime")) score += 5;
        if (lower.Contains("prefab")) score -= 40;
        if (lower.Contains("icon")) score -= 80;
        return score;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => obj != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj) : 0;
    }

    private static SkeletonDataAsset ResolveSkeletonDataAsset(SkeletonAnimation animation, SkeletonRenderer renderer = null)
    {
        if (animation != null)
        {
            object value = GetMemberValue(animation, "skeletonDataAsset");
            if (value is SkeletonDataAsset direct)
                return direct;

            value = GetMemberValue(animation, "SkeletonDataAsset");
            if (value is SkeletonDataAsset directProperty)
                return directProperty;
        }

        if (renderer == null && animation != null)
            renderer = animation.GetComponent<SkeletonRenderer>();

        if (renderer != null)
        {
            object value = GetMemberValue(renderer, "skeletonDataAsset");
            if (value is SkeletonDataAsset rendererAsset)
                return rendererAsset;

            value = GetMemberValue(renderer, "SkeletonDataAsset");
            if (value is SkeletonDataAsset rendererAssetProperty)
                return rendererAssetProperty;
        }

        return null;
    }

    private static Material[] ResolveSourceMaterials(GameObject sourceObject, SkeletonDataAsset sourceDataAsset)
    {
        // V24: UnitDefinition 的 Spine .asset 是第一来源。
        // 旧版先读当前 MeshRenderer.sharedMaterials，导致切换 SkeletonDataAsset 后仍沿用旧模型材质，表现为“单位定义改了但视觉没有同步”。
        if (sourceDataAsset != null)
        {
            Material[] fromAsset = TryResolveMaterialsFromSkeletonDataAsset(sourceDataAsset);
            if (fromAsset != null && fromAsset.Length > 0)
                return fromAsset;
        }

        MeshRenderer meshRenderer = sourceObject != null ? sourceObject.GetComponent<MeshRenderer>() : null;
        if (meshRenderer != null && meshRenderer.sharedMaterials != null && meshRenderer.sharedMaterials.Length > 0 && meshRenderer.sharedMaterials[0] != null)
            return meshRenderer.sharedMaterials;

        return null;
    }

    private static Material[] TryResolveMaterialsFromSkeletonDataAsset(SkeletonDataAsset asset)
    {
        if (asset == null)
            return null;

        object atlasAssetsObject = GetMemberValue(asset, "atlasAssets");
        if (atlasAssetsObject is AtlasAssetBase[] atlasAssets)
        {
            for (int i = 0; i < atlasAssets.Length; i++)
            {
                Material[] materials = ResolveMaterialsFromAtlasAsset(atlasAssets[i]);
                if (materials != null && materials.Length > 0)
                    return materials;
            }
        }

        atlasAssetsObject = GetMemberValue(asset, "AtlasAssets");
        if (atlasAssetsObject is AtlasAssetBase[] atlasAssetsProperty)
        {
            for (int i = 0; i < atlasAssetsProperty.Length; i++)
            {
                Material[] materials = ResolveMaterialsFromAtlasAsset(atlasAssetsProperty[i]);
                if (materials != null && materials.Length > 0)
                    return materials;
            }
        }

        return null;
    }

    private static Material[] ResolveMaterialsFromAtlasAsset(UnityEngine.Object atlasAsset)
    {
        if (atlasAsset == null)
            return null;

        object materialsObject = GetMemberValue(atlasAsset, "materials");
        if (materialsObject is Material[] materials && materials.Length > 0)
            return materials;

        materialsObject = GetMemberValue(atlasAsset, "Materials");
        if (materialsObject is Material[] materialsProperty && materialsProperty.Length > 0)
            return materialsProperty;

        return null;
    }

    private static void SetSkeletonDataAsset(Component component, SkeletonDataAsset asset)
    {
        if (component == null || asset == null)
            return;

        if (SetMemberValue(component, "skeletonDataAsset", asset))
            return;

        SetMemberValue(component, "SkeletonDataAsset", asset);
    }

    private static void TryInvokeInitialize(Component component)
    {
        if (component == null)
            return;

        System.Type type = component.GetType();
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != "Initialize")
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
            {
                method.Invoke(component, new object[] { true });
                return;
            }

            if (parameters.Length == 0)
            {
                method.Invoke(component, null);
                return;
            }
        }
    }

    private static object GetMemberValue(object target, string memberName)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
            return null;

        System.Type type = target.GetType();

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            return field.GetValue(target);

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead)
            return property.GetValue(target, null);

        return null;
    }

    private static bool SetMemberValue(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
            return false;

        System.Type type = target.GetType();

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
        {
            field.SetValue(target, value);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && (value == null || property.PropertyType.IsInstanceOfType(value)))
        {
            property.SetValue(target, value, null);
            return true;
        }

        return false;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
            field.SetValue(target, value);
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform match = FindDeepChild(child, childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static void MarkDirty(params UnityEngine.Object[] objects)
    {
#if UNITY_EDITOR
        if (Application.isPlaying || objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                EditorUtility.SetDirty(objects[i]);
        }
#endif
    }

    private void RefreshSceneMarkerCache()
    {
        SkyPrisonSceneUnitMarker marker = GetComponent<SkyPrisonSceneUnitMarker>();
        if (marker != null)
        {
            marker.RefreshBindingCache();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(marker);
#endif
        }
    }
}
