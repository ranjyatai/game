#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 用可视模型的 Mesh 重建可推动物体的真实物理碰撞。
///
/// 目标：
/// - 不再通过 Y 偏移 / 接地点猜测来模拟模型形状。
/// - 让 PushableColliderRoot 下的 MeshCollider 直接贴合 Visual 模型。
/// - 运行时把控制权交给 Unity Rigidbody + MeshCollider + GroundPhysics。
///
/// 使用：
/// 选中 prefab 资产或场景实例后执行：
/// Tools / Sky Prison / Physics / 用模型Mesh重建选中可推动物体Collider
/// </summary>
public static class SkyPrisonPushableMeshColliderFitEditorUtility
{
    private const string MenuRoot = "Tools/Sky Prison/Physics/";
    private const string MenuRebuildSelected = MenuRoot + "用模型Mesh重建选中可推动物体Collider";

    private const string ColliderRootName = "PushableColliderRoot";
    private const string PushableLayerName = "PushableProp";

    private static readonly string[] PreferredVisualRoots =
    {
        "VisualRoot",
        "Visual",
        "ModelRoot",
        "MeshRoot"
    };

    private static readonly string[] ExcludedNameKeywords =
    {
        "Collider",
        "Collision",
        "Trigger",
        "DecorationTrigger",
        "BackTrigger",
        "FrontTrigger",
        "Shadow",
        "Occlusion",
        "Stencil",
        "Fog",
        "Gizmo",
        "Editor",
        "GroundContact"
    };

    [MenuItem(MenuRebuildSelected, false, 1802)]
    private static void RebuildSelected()
    {
        Object[] selected = Selection.objects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[SkyPrisonPushableMeshColliderFitEditorUtility] 没有选中任何物体。请选中 prefab 资产或场景实例。");
            return;
        }

        int success = 0;
        int failed = 0;

        foreach (Object obj in selected)
        {
            if (!(obj is GameObject go))
            {
                failed++;
                continue;
            }

            string path = AssetDatabase.GetAssetPath(go);
            bool isPrefabAsset = !string.IsNullOrEmpty(path) && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab;

            if (isPrefabAsset)
            {
                if (RebuildPrefabAsset(path)) success++; else failed++;
            }
            else
            {
                if (RebuildSceneObject(go)) success++; else failed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SkyPrisonPushableMeshColliderFitEditorUtility] MeshCollider 重建完成：成功 {success}，失败 {failed}。");
    }

    [MenuItem(MenuRebuildSelected, true)]
    private static bool ValidateRebuildSelected()
    {
        return Selection.objects != null && Selection.objects.Length > 0;
    }

    private static bool RebuildPrefabAsset(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return false;

        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
                return false;

            bool ok = RebuildRoot(prefabRoot, true);
            if (ok)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Debug.Log($"[SkyPrisonPushableMeshColliderFitEditorUtility] 已重建 prefab MeshCollider：{prefabPath}");
            }
            return ok;
        }
        finally
        {
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool RebuildSceneObject(GameObject selected)
    {
        if (selected == null)
            return false;

        GameObject root = FindPushableRoot(selected.transform);
        if (root == null)
            root = selected;

        bool ok = RebuildRoot(root, false);
        if (ok)
        {
            EditorUtility.SetDirty(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            Debug.Log($"[SkyPrisonPushableMeshColliderFitEditorUtility] 已重建场景物体 MeshCollider：{root.name}", root);
        }
        return ok;
    }

    private static GameObject FindPushableRoot(Transform start)
    {
        Transform t = start;
        while (t != null)
        {
            if (t.GetComponent<SkyPrisonPushablePropRuntime>() != null)
                return t.gameObject;
            t = t.parent;
        }
        return null;
    }

    private static bool RebuildRoot(GameObject root, bool isPrefabAsset)
    {
        if (root == null)
            return false;

        Transform visualRoot = FindFirstExistingChild(root.transform, PreferredVisualRoots);
        Transform scanRoot = visualRoot != null ? visualRoot : root.transform;

        List<MeshSource> meshSources = CollectMeshSources(scanRoot, root.transform);
        if (meshSources.Count == 0)
        {
            Debug.LogWarning($"[SkyPrisonPushableMeshColliderFitEditorUtility] {root.name} 没有找到可用于 MeshCollider 的 MeshFilter / SkinnedMeshRenderer。", root);
            return false;
        }

        Transform colliderRoot = EnsureChild(root.transform, ColliderRootName);
        Undo.RegisterFullObjectHierarchyUndo(root, "Rebuild Pushable Mesh Colliders");

        ClearGeneratedColliders(colliderRoot);

        int layer = LayerMask.NameToLayer(PushableLayerName);
        if (layer < 0)
            layer = root.layer;

        int created = 0;
        foreach (MeshSource source in meshSources)
        {
            if (source.mesh == null)
                continue;

            GameObject colliderObject = new GameObject($"MC_{source.source.name}");
            Undo.RegisterCreatedObjectUndo(colliderObject, "Create Pushable MeshCollider");
            colliderObject.layer = layer;
            colliderObject.transform.SetParent(colliderRoot, false);
            CopyWorldTransform(source.source, colliderObject.transform);

            MeshCollider meshCollider = colliderObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = source.mesh;
            meshCollider.convex = true;
            meshCollider.isTrigger = false;

            created++;
        }

        SkyPrisonPushablePropRuntime runtime = root.GetComponent<SkyPrisonPushablePropRuntime>();
        if (runtime != null)
        {
            Undo.RecordObject(runtime, "Configure Pushable Real Physics Runtime");
            runtime.realPhysicsAuthorityAfterKnockdown = true;
            runtime.realPhysicsReleaseAfterPivotTip = true;
            runtime.realPhysicsKeepDynamicWhenStable = true;
            runtime.realPhysicsSkipGroundProtection = true;
            runtime.realPhysicsSkipPlanarSpeedLimit = true;
            runtime.realPhysicsAllowUnitBodyCollision = true;
            runtime.realPhysicsUseGravity = true;
            runtime.realPhysicsUseOffCenterImpulse = true;
            runtime.realPhysicsUseExtraScriptTorque = false;
            runtime.realPhysicsExtraTorqueMultiplier = 0f;
            runtime.realPhysicsEnsureGroundCollision = true;
            runtime.paperDollApplyGroundProtectionAfterPostPush = false;
            runtime.paperDollPostKnockdownUseRealPhysics = true;
            runtime.paperDollPostKnockdownForceAtEdge = true;
            runtime.paperDollUseGravityWhenPushedAfterKnockdown = true;
            runtime.releaseRigidbodyAfterPivotTip = false;
            runtime.useGravityAfterPivotRelease = false;
            EditorUtility.SetDirty(runtime);
        }

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Undo.RecordObject(rb, "Configure Pushable Rigidbody");
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxAngularVelocity = 14f;
            rb.solverIterations = Mathf.Max(rb.solverIterations, 12);
            rb.solverVelocityIterations = Mathf.Max(rb.solverVelocityIterations, 4);
            EditorUtility.SetDirty(rb);
        }

        EditorUtility.SetDirty(root);
        if (!isPrefabAsset)
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);

        Debug.Log($"[SkyPrisonPushableMeshColliderFitEditorUtility] {root.name} 已用可视 Mesh 重建 {created} 个 Convex MeshCollider。", root);
        return created > 0;
    }

    private static List<MeshSource> CollectMeshSources(Transform scanRoot, Transform root)
    {
        List<MeshSource> result = new List<MeshSource>();
        if (scanRoot == null)
            return result;

        MeshFilter[] meshFilters = scanRoot.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null)
                continue;
            if (ShouldExclude(mf.transform, root))
                continue;
            Renderer renderer = mf.GetComponent<Renderer>();
            if (renderer != null && !renderer.enabled)
                continue;
            result.Add(new MeshSource(mf.transform, mf.sharedMesh));
        }

        SkinnedMeshRenderer[] skinnedRenderers = scanRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer smr in skinnedRenderers)
        {
            if (smr == null || smr.sharedMesh == null || !smr.enabled)
                continue;
            if (ShouldExclude(smr.transform, root))
                continue;
            result.Add(new MeshSource(smr.transform, smr.sharedMesh));
        }

        return result;
    }

    private static bool ShouldExclude(Transform t, Transform root)
    {
        if (t == null)
            return true;

        Transform cur = t;
        while (cur != null && cur != root.parent)
        {
            string n = cur.name;
            foreach (string keyword in ExcludedNameKeywords)
            {
                if (n.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (cur == root)
                break;
            cur = cur.parent;
        }
        return false;
    }

    private static void ClearGeneratedColliders(Transform colliderRoot)
    {
        if (colliderRoot == null)
            return;

        List<GameObject> children = new List<GameObject>();
        for (int i = 0; i < colliderRoot.childCount; i++)
            children.Add(colliderRoot.GetChild(i).gameObject);

        foreach (GameObject child in children)
        {
            if (child == null)
                continue;
            Undo.DestroyObjectImmediate(child);
        }
    }

    private static Transform EnsureChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(go, "Create PushableColliderRoot");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private static Transform FindFirstExistingChild(Transform root, string[] names)
    {
        if (root == null || names == null)
            return null;

        foreach (string name in names)
        {
            Transform t = FindDeepChild(root, name);
            if (t != null)
                return t;
        }
        return null;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;

        if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void CopyWorldTransform(Transform source, Transform target)
    {
        if (source == null || target == null)
            return;

        target.position = source.position;
        target.rotation = source.rotation;

        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        Vector3 sourceScale = source.lossyScale;
        target.localScale = new Vector3(
            SafeDivide(sourceScale.x, parentScale.x),
            SafeDivide(sourceScale.y, parentScale.y),
            SafeDivide(sourceScale.z, parentScale.z));
    }

    private static float SafeDivide(float a, float b)
    {
        if (Mathf.Abs(b) < 0.000001f)
            return a;
        return a / b;
    }

    private readonly struct MeshSource
    {
        public readonly Transform source;
        public readonly Mesh mesh;

        public MeshSource(Transform source, Mesh mesh)
        {
            this.source = source;
            this.mesh = mesh;
        }
    }
}
#endif
