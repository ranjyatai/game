#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonTerrainDecorationSelectedRootPhysicsTool
{
    private const string PushableLayerName = "PushableProp";
    private const string WorldLayerName = "World3D";
    private const string DecorationTriggerLayerName = "DecorationTrigger";
    private const string CharacterLayerName = "Character2D";
    private const string UnitPhysicsProbeLayerName = "UnitPhysicsProbe";
    private const string UnitBodyLayerName = "UnitBody";
    private const string PhysicsColliderRootName = "PushableColliderRoot";

    [MenuItem("Tools/Sky Prison/Terrain Decoration/Physics/Force Install Physics On Selected Runtime Root")]
    public static void ForceInstallPhysicsOnSelectedRuntimeRoot()
    {
        Transform selected = Selection.activeTransform;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("物理结构安装", "请先在 Hierarchy 里选中一个地形装饰物实例或它的子节点。", "OK");
            return;
        }

        Transform root = FindRuntimeRoot(selected);
        if (root == null)
        {
            EditorUtility.DisplayDialog(
                "物理结构安装",
                "没有找到 TerrainDecorationRuntimeBinder / TerrainDecorationRuntimeApplier 所在的运行时根节点。\n\n请确认你选中的是场景 Hierarchy 里的地形装饰物实例，不是 Project 里的视觉 PF。",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Force Install Terrain Decoration Physics On Runtime Root");

        CleanWrongPhysicsOnChildren(root);
        RemoveAllRootColliders(root);
        InstallPhysicsOnRoot(root);
        RestoreLegacyTriggerAndOcclusionState(root);
        EnsureHybridPhysicsLayerMatrix();

        Selection.activeGameObject = root.gameObject;
        EditorUtility.SetDirty(root.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[SkyPrison] Physics installed on runtime root: {GetPath(root)}", root.gameObject);
    }

    [MenuItem("Tools/Sky Prison/Terrain Decoration/Physics/Force Clean Physics From Selected Runtime Root")]
    public static void ForceCleanPhysicsFromSelectedRuntimeRoot()
    {
        Transform selected = Selection.activeTransform;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("物理结构清理", "请先在 Hierarchy 里选中一个地形装饰物实例或它的子节点。", "OK");
            return;
        }

        Transform root = FindRuntimeRoot(selected);
        if (root == null)
        {
            EditorUtility.DisplayDialog(
                "物理结构清理",
                "没有找到 TerrainDecorationRuntimeBinder / TerrainDecorationRuntimeApplier 所在的运行时根节点。",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Force Clean Terrain Decoration Physics From Runtime Root");

        RemoveComponentIfExists<SkyPrisonPushablePropRuntime>(root.gameObject);
        RemoveComponentIfExists<Rigidbody>(root.gameObject);
        RemoveAllRootColliders(root);
        CleanWrongPhysicsOnChildren(root);
        RemovePhysicsColliderRoot(root);

        int worldLayer = LayerMask.NameToLayer(WorldLayerName);
        if (worldLayer >= 0)
            root.gameObject.layer = worldLayer;

        RestoreLegacyTriggerAndOcclusionState(root);
        EnsureHybridPhysicsLayerMatrix();

        Selection.activeGameObject = root.gameObject;
        EditorUtility.SetDirty(root.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[SkyPrison] Physics cleaned from runtime root: {GetPath(root)}", root.gameObject);
    }

    private static Transform FindRuntimeRoot(Transform start)
    {
        Transform current = start;
        while (current != null)
        {
            if (HasComponentNamed(current.gameObject, "TerrainDecorationRuntimeBinder") ||
                HasComponentNamed(current.gameObject, "TerrainDecorationRuntimeApplier"))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private static bool HasComponentNamed(GameObject go, string typeName)
    {
        Component[] components = go.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null)
                continue;

            Type type = component.GetType();
            if (type.Name == typeName)
                return true;
        }

        return false;
    }

    private static void CleanWrongPhysicsOnChildren(Transform root)
    {
        Rigidbody[] childRigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rb in childRigidbodies)
        {
            if (rb == null || rb.transform == root)
                continue;

            Undo.DestroyObjectImmediate(rb);
        }

        MeshCollider[] childMeshColliders = root.GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider mc in childMeshColliders)
        {
            if (mc == null || mc.transform == root || mc.transform.name == PhysicsColliderRootName)
                continue;

            Undo.DestroyObjectImmediate(mc);
        }

        SkyPrisonPushablePropRuntime[] childPushables = root.GetComponentsInChildren<SkyPrisonPushablePropRuntime>(true);
        foreach (SkyPrisonPushablePropRuntime pushable in childPushables)
        {
            if (pushable == null || pushable.transform == root)
                continue;

            Undo.DestroyObjectImmediate(pushable);
        }
    }

    private static void InstallPhysicsOnRoot(Transform root)
    {
        int worldLayer = LayerMask.NameToLayer(WorldLayerName);
        if (worldLayer >= 0)
            root.gameObject.layer = worldLayer;

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = Undo.AddComponent<Rigidbody>(root.gameObject);

        rb.mass = 1f;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 2.5f;
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationY;

        Transform colliderRoot = GetOrCreatePhysicsColliderRoot(root);
        int pushableLayer = LayerMask.NameToLayer(PushableLayerName);
        if (pushableLayer >= 0)
            colliderRoot.gameObject.layer = pushableLayer;

        colliderRoot.localPosition = Vector3.zero;
        colliderRoot.localRotation = Quaternion.identity;
        colliderRoot.localScale = Vector3.one;

        MeshCollider meshCollider = colliderRoot.GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = Undo.AddComponent<MeshCollider>(colliderRoot.gameObject);

        Mesh mainMesh = FindLargestVisibleMesh(root);
        if (mainMesh != null)
            meshCollider.sharedMesh = mainMesh;

        meshCollider.convex = true;
        meshCollider.isTrigger = false;

        SkyPrisonPushablePropRuntime runtime = root.GetComponent<SkyPrisonPushablePropRuntime>();
        if (runtime == null)
            runtime = Undo.AddComponent<SkyPrisonPushablePropRuntime>(root.gameObject);

        ApplyRuntimeDefaults(runtime);
    }

    private static Transform GetOrCreatePhysicsColliderRoot(Transform root)
    {
        Transform colliderRoot = root.Find(PhysicsColliderRootName);
        if (colliderRoot != null)
        {
            colliderRoot.localPosition = Vector3.zero;
            colliderRoot.localRotation = Quaternion.identity;
            colliderRoot.localScale = Vector3.one;
            return colliderRoot;
        }

        GameObject go = new GameObject(PhysicsColliderRootName);
        Undo.RegisterCreatedObjectUndo(go, "Create Pushable Collider Root");
        go.transform.SetParent(root, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private static void RemovePhysicsColliderRoot(Transform root)
    {
        Transform colliderRoot = root != null ? root.Find(PhysicsColliderRootName) : null;
        if (colliderRoot != null)
            Undo.DestroyObjectImmediate(colliderRoot.gameObject);
    }

    private static void RemoveAllRootColliders(Transform root)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponents<Collider>();
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            if (colliders[i] != null)
                Undo.DestroyObjectImmediate(colliders[i]);
        }
    }

    private static Mesh FindLargestVisibleMesh(Transform root)
    {
        Mesh bestMesh = null;
        float bestScore = -1f;

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in meshFilters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            if (filter.transform == root)
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                continue;

            if (IsIgnoredPhysicsSearchPath(filter.transform))
                continue;

            Bounds bounds = renderer.bounds;
            Vector3 size = bounds.size;
            float score = Mathf.Max(0.0001f, size.x) * Mathf.Max(0.0001f, size.y) * Mathf.Max(0.0001f, size.z);

            if (score > bestScore)
            {
                bestScore = score;
                bestMesh = filter.sharedMesh;
            }
        }

        return bestMesh;
    }

    private static bool IsIgnoredPhysicsSearchPath(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string n = current.name;
            if (n.Contains("CollisionRoot") ||
                n.Contains("Main_Collision_Box") ||
                n.Contains("FrontTrigger") ||
                n.Contains("BackTrigger") ||
                n.Contains("VisionBlocker") ||
                n.Contains("FrontOccluder") ||
                n.Contains("OutlineMask") ||
                n.Contains("ShadowCaster") ||
                n.Contains("StencilWriter") ||
                n.Contains("EditorGizmo") ||
                n.Contains(PhysicsColliderRootName))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void ApplyRuntimeDefaults(SkyPrisonPushablePropRuntime runtime)
    {
        SetField(runtime, "receiveVolumeCollision", true);
        SetField(runtime, "receiveAttackImpulse", true);
        SetField(runtime, "receiveExplosionImpulse", true);
        SetField(runtime, "receiveScriptedImpulse", true);

        SetField(runtime, "mass", 1f);
        SetField(runtime, "linearDamping", 1.5f);
        SetField(runtime, "angularDamping", 2.5f);
        SetField(runtime, "maxPlanarSpeed", 6f);

        SetField(runtime, "externalPushMultiplier", 1f);
        SetField(runtime, "applyForceAtTop", true);
        SetField(runtime, "topForceHeight", 0.9f);
        SetField(runtime, "topForceMultiplier", 2.5f);

        SetField(runtime, "enableKnockdown", true);
        SetField(runtime, "protectAfterPivotRelease", true);
        SetField(runtime, "useLastKnownGroundWhenRayMisses", true);
        SetField(runtime, "useFallbackGroundPlaneWhenRayMisses", false);
        SetField(runtime, "fallbackGroundY", 0f);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        if (target == null || string.IsNullOrEmpty(fieldName) || value == null)
            return;

        Type type = target.GetType();
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return;

        try
        {
            if (value is int intValue && field.FieldType == typeof(float))
                field.SetValue(target, (float)intValue);
            else if (value is float floatValue && field.FieldType == typeof(int))
                field.SetValue(target, Mathf.RoundToInt(floatValue));
            else if (field.FieldType.IsAssignableFrom(value.GetType()))
                field.SetValue(target, value);
        }
        catch
        {
            // Keep this editor utility compatible across runtime script iterations.
        }
    }

    private static void RemoveComponentIfExists<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component != null)
            Undo.DestroyObjectImmediate(component);
    }


    private static void RestoreLegacyTriggerAndOcclusionState(Transform root)
    {
        if (root == null)
            return;

        int worldLayer = LayerMask.NameToLayer(WorldLayerName);
        int decorationTriggerLayer = LayerMask.NameToLayer(DecorationTriggerLayerName);
        int pushableLayer = LayerMask.NameToLayer(PushableLayerName);

        if (worldLayer >= 0)
            root.gameObject.layer = worldLayer;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t == null)
                continue;

            string n = t.name;
            bool isFrontBackTrigger = n == "BackTrigger" || n == "FrontTrigger";
            if (isFrontBackTrigger)
            {
                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);

                int wantedLayer = decorationTriggerLayer >= 0 ? decorationTriggerLayer : worldLayer;
                if (wantedLayer >= 0)
                    t.gameObject.layer = wantedLayer;

                Collider[] colliders = t.GetComponentsInChildren<Collider>(true);
                foreach (Collider col in colliders)
                {
                    if (col == null)
                        continue;
                    col.enabled = true;
                    col.isTrigger = true;
                    if (wantedLayer >= 0)
                        col.gameObject.layer = wantedLayer;
                }
            }
            else if (worldLayer >= 0 && pushableLayer >= 0 && t.name != PhysicsColliderRootName)
            {
                bool mustNotBePushable =
                    n == "VisualRoot" || n == "RuleRoot" || n == "CollisionRoot" ||
                    n == "SortAnchor" || n == "PlaneOrigin" || n == "PlaneForwardReference" ||
                    n == "MossRoot" || n == "FXRoot" || n == "EditorGizmoRoot";

                if (mustNotBePushable && t.gameObject.layer == pushableLayer)
                    t.gameObject.layer = worldLayer;
            }
        }
    }

    private static void EnsureHybridPhysicsLayerMatrix()
    {
        int character = LayerMask.NameToLayer(CharacterLayerName);
        int world = LayerMask.NameToLayer(WorldLayerName);
        int decorationTrigger = LayerMask.NameToLayer(DecorationTriggerLayerName);
        int probe = LayerMask.NameToLayer(UnitPhysicsProbeLayerName);
        int unitBody = LayerMask.NameToLayer(UnitBodyLayerName);
        int pushable = LayerMask.NameToLayer(PushableLayerName);

        SetIgnoreLayerCollisionSafe(character, world, false);
        SetIgnoreLayerCollisionSafe(character, decorationTrigger, false);
        SetIgnoreLayerCollisionSafe(probe, pushable, false);
        SetIgnoreLayerCollisionSafe(unitBody, pushable, true);
    }

    private static void SetIgnoreLayerCollisionSafe(int a, int b, bool ignore)
    {
        if (a < 0 || b < 0 || a == b)
            return;

        if (Physics.GetIgnoreLayerCollision(a, b) != ignore)
            Physics.IgnoreLayerCollision(a, b, ignore);
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
#endif
