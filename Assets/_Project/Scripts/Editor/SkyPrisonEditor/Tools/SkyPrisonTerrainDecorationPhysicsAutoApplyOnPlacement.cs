using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Safe restore version for terrain decoration physics auto-apply.
///
/// Important rule after 2026-05-16:
/// - Source PF_TD templates are corrected by SkyPrisonTerrainDecorationDefinitionPage.
/// - This helper must NOT rebuild RuleRoot / CollisionRoot / BackTrigger / FrontOccluderProxy_Box.
/// - This helper must NOT delete RuleRoot/CollisionRoot/Main_Collision_MeshRoot/__PhysicsMeshCollider_*.
/// - When pushable physics is disabled, keep static MeshCollider collision, remove only Rigidbody / Pushable runtime.
///
/// This class intentionally does not subscribe to hierarchyChanged anymore. It only runs from the menu,
/// so it cannot silently overwrite freshly placed objects or corrected source templates.
/// </summary>
public static class SkyPrisonTerrainDecorationPhysicsAutoApplyOnPlacement
{
    private const string SettingsSearchFolder = "Assets/_Project";
    private const string DefaultWorldLayerName = "World3D";
    private const string DefaultPushableLayerName = "PushableProp";
    private const string DecorationTriggerLayerName = "DecorationTrigger";
    private const string CharacterLayerName = "Character2D";
    private const string UnitPhysicsProbeLayerName = "UnitPhysicsProbe";
    private const string UnitBodyLayerName = "UnitBody";
    private const string LegacyPushableColliderRootName = "PushableColliderRoot";

    [MenuItem("Tools/Sky Prison/Terrain Decoration/Physics/Apply Physics Settings To All Scene Runtime Roots")]
    public static void ApplyToAllSceneRuntimeRootsMenu()
    {
        int changed = ApplyToAllSceneRuntimeRoots();
        EditorUtility.DisplayDialog("地形装饰物物理结构", $"已扫描场景运行时根节点。发生修正：{changed} 个。\n\n本恢复版不会自动后台运行，也不会重建遮挡盒。", "知道了");
    }

    [MenuItem("Tools/Sky Prison/Terrain Decoration/Physics/Apply Physics Settings To Selected Runtime Root")]
    public static void ApplyToSelectedRuntimeRootMenu()
    {
        GameObject root = ResolveRuntimeRoot(Selection.activeGameObject);
        if (root == null)
        {
            EditorUtility.DisplayDialog("地形装饰物物理结构", "请在 Hierarchy 中选中地形装饰物运行时根节点，或选中它的任意子节点。", "知道了");
            return;
        }

        TerrainDecorationRuntimeBinder binder = root.GetComponent<TerrainDecorationRuntimeBinder>();
        if (binder == null || binder.definition == null)
        {
            EditorUtility.DisplayDialog("地形装饰物物理结构", "当前对象没有 TerrainDecorationRuntimeBinder，或 Binder 没有绑定 Definition。", "知道了");
            return;
        }

        SkyPrisonTerrainDecorationPhysicsSettings settings = FindPhysicsSettingsForDefinition(binder.definition);
        if (settings == null)
        {
            EditorUtility.DisplayDialog("地形装饰物物理结构", "当前 Definition 没有对应的 SkyPrisonTerrainDecorationPhysicsSettings。", "知道了");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root, "Apply terrain decoration physics settings safely");
        bool changed = ApplySettingsToRuntimeRoot(root, settings);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        EditorUtility.DisplayDialog("地形装饰物物理结构", changed ? "已安全修正当前运行时根节点。" : "当前运行时根节点已经是安全状态。", "知道了");
    }

    private static int ApplyToAllSceneRuntimeRoots()
    {
        TerrainDecorationRuntimeBinder[] binders = UnityEngine.Object.FindObjectsOfType<TerrainDecorationRuntimeBinder>(true);
        HashSet<GameObject> processed = new HashSet<GameObject>();
        int changedCount = 0;

        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null || binder.definition == null)
                continue;

            GameObject root = ResolveRuntimeRoot(binder.gameObject);
            if (root == null || processed.Contains(root) || EditorUtility.IsPersistent(root))
                continue;

            SkyPrisonTerrainDecorationPhysicsSettings settings = FindPhysicsSettingsForDefinition(binder.definition);
            if (settings == null)
                continue;

            processed.Add(root);
            if (ApplySettingsToRuntimeRoot(root, settings))
                changedCount++;
        }

        EnsureHybridPhysicsLayerMatrix();
        return changedCount;
    }

    private static bool ApplySettingsToRuntimeRoot(GameObject root, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        if (root == null || settings == null)
            return false;

        bool changed = false;
        int worldLayer = LayerMask.NameToLayer(DefaultWorldLayerName);
        int pushableLayer = LayerMask.NameToLayer(string.IsNullOrWhiteSpace(settings.pushableLayerName) ? DefaultPushableLayerName : settings.pushableLayerName);

        if (worldLayer >= 0 && root.layer != worldLayer)
        {
            root.layer = worldLayer;
            changed = true;
        }

        // Never rebuild generated occlusion/collision structure here.
        // Only remove old pollution that is outside the new legal RuleRoot/CollisionRoot mesh-proxy area.
        changed |= RemoveLegacyPushableColliderRoot(root);
        changed |= CleanVisualSubtreeRuntimePollution(root);
        changed |= RemoveNamedPhysicsPollutionNodes(root.transform);
        changed |= RestoreTriggerAndOcclusionLayerState(root);
        changed |= NormalizeRuleMeshColliderState(root, settings, settings.enablePhysicsStructure, worldLayer, pushableLayer);

        if (settings.enablePhysicsStructure)
        {
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = root.AddComponent<Rigidbody>();
                changed = true;
            }
            changed |= ApplyRigidbodyDefaults(rb, settings);

            SkyPrisonPushablePropRuntime runtime = root.GetComponent<SkyPrisonPushablePropRuntime>();
            if (runtime == null)
            {
                runtime = root.AddComponent<SkyPrisonPushablePropRuntime>();
                changed = true;
            }
            changed |= CopySettingsToRuntime(runtime, settings);
        }
        else
        {
            changed |= RemoveComponentIfExists<SkyPrisonPushablePropRuntime>(root);
            changed |= RemoveComponentIfExists<Rigidbody>(root);
            changed |= RemoveRootColliders(root);
        }

        if (changed)
        {
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        }
        return changed;
    }

    private static bool NormalizeRuleMeshColliderState(GameObject root, SkyPrisonTerrainDecorationPhysicsSettings settings, bool pushableEnabled, int worldLayer, int pushableLayer)
    {
        bool changed = false;
        Transform meshRoot = root.transform.Find("RuleRoot/CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            meshRoot = root.transform.Find("CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            return false;

        int targetLayer = pushableEnabled && pushableLayer >= 0 ? pushableLayer : worldLayer;
        if (targetLayer >= 0)
            changed |= SetLayerRecursive(meshRoot, targetLayer);

        MeshCollider[] colliders = meshRoot.GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider mc in colliders)
        {
            if (mc == null)
                continue;
            if (mc.isTrigger)
            {
                mc.isTrigger = false;
                changed = true;
            }
            bool wantedConvex = pushableEnabled && settings.forceConvexMeshCollider;
            if (mc.convex != wantedConvex)
            {
                mc.convex = wantedConvex;
                changed = true;
            }
        }
        return changed;
    }

    private static bool SetLayerRecursive(Transform root, int layer)
    {
        bool changed = false;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t != null && t.gameObject.layer != layer)
            {
                t.gameObject.layer = layer;
                changed = true;
            }
        }
        return changed;
    }

    private static bool RemoveLegacyPushableColliderRoot(GameObject root)
    {
        Transform old = root != null ? root.transform.Find(LegacyPushableColliderRootName) : null;
        if (old == null)
            return false;
        UnityEngine.Object.DestroyImmediate(old.gameObject);
        return true;
    }

    private static bool CleanVisualSubtreeRuntimePollution(GameObject root)
    {
        Transform visualRoot = root != null ? root.transform.Find("VisualRoot") : null;
        if (visualRoot == null)
            return false;

        bool changed = false;
        Transform[] all = visualRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t == null)
                continue;
            changed |= RemoveComponentIfExists<SkyPrisonPushablePropRuntime>(t.gameObject);
            changed |= RemoveComponentIfExists<Rigidbody>(t.gameObject);
            changed |= RemoveComponentIfExists<MeshCollider>(t.gameObject);
        }
        return changed;
    }

    private static bool RestoreTriggerAndOcclusionLayerState(GameObject root)
    {
        if (root == null)
            return false;

        bool changed = false;
        int triggerLayer = LayerMask.NameToLayer(DecorationTriggerLayerName);
        int worldLayer = LayerMask.NameToLayer(DefaultWorldLayerName);

        changed |= SetTriggerBox(root.transform.Find("RuleRoot/BackTrigger"), triggerLayer, true);
        changed |= SetTriggerBox(root.transform.Find("RuleRoot/FrontTrigger"), triggerLayer, true);
        changed |= SetTriggerBox(root.transform.Find("BackTrigger"), triggerLayer, true);
        changed |= SetTriggerBox(root.transform.Find("FrontTrigger"), triggerLayer, true);

        if (worldLayer >= 0)
        {
            changed |= SetLayerIfExists(root.transform.Find("RuleRoot/FrontOccluderRoot"), worldLayer);
            changed |= SetLayerIfExists(root.transform.Find("RuleRoot/OutlineMaskProxyRoot"), worldLayer);
            changed |= SetLayerIfExists(root.transform.Find("FrontOccluderRoot"), worldLayer);
            changed |= SetLayerIfExists(root.transform.Find("OutlineMaskProxyRoot"), worldLayer);
        }
        return changed;
    }

    private static bool SetTriggerBox(Transform tr, int layer, bool isTrigger)
    {
        if (tr == null)
            return false;
        bool changed = false;
        if (layer >= 0 && tr.gameObject.layer != layer)
        {
            tr.gameObject.layer = layer;
            changed = true;
        }
        BoxCollider box = tr.GetComponent<BoxCollider>();
        if (box != null && box.isTrigger != isTrigger)
        {
            box.isTrigger = isTrigger;
            changed = true;
        }
        return changed;
    }

    private static bool SetLayerIfExists(Transform tr, int layer)
    {
        if (tr == null || layer < 0)
            return false;
        bool changed = false;
        Transform[] all = tr.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t != null && t.gameObject.layer != layer)
            {
                t.gameObject.layer = layer;
                changed = true;
            }
        }
        return changed;
    }

    private static bool RemoveComponentIfExists<T>(GameObject go) where T : Component
    {
        T component = go != null ? go.GetComponent<T>() : null;
        if (component == null)
            return false;
        UnityEngine.Object.DestroyImmediate(component);
        return true;
    }

    private static bool RemoveRootColliders(GameObject root)
    {
        if (root == null)
            return false;
        bool changed = false;
        Collider[] colliders = root.GetComponents<Collider>();
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            if (colliders[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(colliders[i]);
                changed = true;
            }
        }
        return changed;
    }

    private static bool RemoveNamedPhysicsPollutionNodes(Transform root)
    {
        if (root == null)
            return false;
        List<GameObject> delete = new List<GameObject>();
        CollectNamedPhysicsPollutionNodes(root, delete);
        bool changed = false;
        foreach (GameObject go in delete)
        {
            if (go != null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                changed = true;
            }
        }
        return changed;
    }

    private static void CollectNamedPhysicsPollutionNodes(Transform t, List<GameObject> delete)
    {
        if (t == null)
            return;
        string n = t.name;
        if (IsProtectedGeneratedMeshPhysicsName(n))
            return;
        if (n == "PhysicsProxyRoot" || n == "PushableBody" || n == "PhysicsBody" || n == "SingleBodyPhysicsRoot" ||
            n.StartsWith("__PhysicsProxy", System.StringComparison.Ordinal) ||
            n.StartsWith("__PushablePhysics", System.StringComparison.Ordinal))
        {
            delete.Add(t.gameObject);
            return;
        }
        for (int i = 0; i < t.childCount; i++)
            CollectNamedPhysicsPollutionNodes(t.GetChild(i), delete);
    }

    private static bool IsProtectedGeneratedMeshPhysicsName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name == "Main_Collision_MeshRoot" ||
               name.StartsWith("__PhysicsMeshCollider_", System.StringComparison.Ordinal) ||
               name == "ManualProxies";
    }

    private static bool ApplyRigidbodyDefaults(Rigidbody rb, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        if (rb == null || settings == null)
            return false;
        bool changed = false;
        if (!Mathf.Approximately(rb.mass, settings.mass)) { rb.mass = settings.mass; changed = true; }
        if (!Mathf.Approximately(rb.linearDamping, settings.linearDamping)) { rb.linearDamping = settings.linearDamping; changed = true; }
        if (!Mathf.Approximately(rb.angularDamping, settings.angularDamping)) { rb.angularDamping = settings.angularDamping; changed = true; }
        if (rb.useGravity) { rb.useGravity = false; changed = true; }
        if (!rb.isKinematic) { rb.isKinematic = true; changed = true; }
        RigidbodyConstraints wanted = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        if (rb.constraints != wanted) { rb.constraints = wanted; changed = true; }
        return changed;
    }

    private static bool CopySettingsToRuntime(SkyPrisonPushablePropRuntime runtime, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        // 恢复安全版：不再复制 PushableRuntime 的字段。
        // 当前项目的 SkyPrisonPushablePropRuntime / PhysicsSettings 字段版本多次变化，
        // 为了先恢复编译和窗口可打开，这里只保留方法入口，不引用任何可能不存在的字段。
        return false;
    }

    private static void SetBool(ref bool field, bool value, ref bool changed)
    {
        if (field != value) { field = value; changed = true; }
    }

    private static void SetFloat(ref float field, float value, ref bool changed)
    {
        if (!Mathf.Approximately(field, value)) { field = value; changed = true; }
    }

    private static GameObject ResolveRuntimeRoot(GameObject any)
    {
        if (any == null)
            return null;
        TerrainDecorationRuntimeBinder binder = any.GetComponentInParent<TerrainDecorationRuntimeBinder>();
        return binder != null ? binder.gameObject : any;
    }

    private static SkyPrisonTerrainDecorationPhysicsSettings FindPhysicsSettingsForDefinition(TerrainDecorationDefinition def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.decorationId))
            return null;
        string[] guids = AssetDatabase.FindAssets($"t:SkyPrisonTerrainDecorationPhysicsSettings {def.decorationId}", new[] { SettingsSearchFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            SkyPrisonTerrainDecorationPhysicsSettings settings = AssetDatabase.LoadAssetAtPath<SkyPrisonTerrainDecorationPhysicsSettings>(path);
            if (settings != null && settings.decorationId == def.decorationId)
                return settings;
        }
        return null;
    }

    private static void EnsureHybridPhysicsLayerMatrix()
    {
        int character = LayerMask.NameToLayer(CharacterLayerName);
        int unitProbe = LayerMask.NameToLayer(UnitPhysicsProbeLayerName);
        int unitBody = LayerMask.NameToLayer(UnitBodyLayerName);
        int trigger = LayerMask.NameToLayer(DecorationTriggerLayerName);
        int pushable = LayerMask.NameToLayer(DefaultPushableLayerName);

        SetIgnoreLayerCollisionSafe(character, pushable, true);
        SetIgnoreLayerCollisionSafe(unitProbe, pushable, true);
        SetIgnoreLayerCollisionSafe(unitBody, pushable, false);
        SetIgnoreLayerCollisionSafe(unitBody, trigger, false);
        SetIgnoreLayerCollisionSafe(unitProbe, trigger, false);
    }

    private static void SetIgnoreLayerCollisionSafe(int a, int b, bool ignore)
    {
        if (a < 0 || b < 0 || a == b)
            return;
        if (Physics.GetIgnoreLayerCollision(a, b) != ignore)
            Physics.IgnoreLayerCollision(a, b, ignore);
    }
}
