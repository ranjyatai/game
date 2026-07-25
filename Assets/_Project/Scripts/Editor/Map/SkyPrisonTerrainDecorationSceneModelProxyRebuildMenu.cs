using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene 实例用：重建 FrontOccluderProxy_Model，使它成为 VisualRoot 的同姿态模型代理。
///
/// V1 - 2026-05-22 - rebuild model proxy from VisualRoot
///
/// 用途：
/// - 解决 FrontOccluderProxy_Model 与 VisualRoot 世界 Bounds 不贴合的问题。
/// - 不处理定义资源，不处理 Prefab Asset。
/// - 不改 BackTrigger / FrontTrigger / FrontOccluderProxy_Box。
/// - Model 代理只负责轮廓遮挡，不吃 Box 投影扩宽规则。
/// </summary>
public static class SkyPrisonTerrainDecorationSceneModelProxyRebuildMenu
{
    private const string MenuRoot = "Tools/Sky Prison/Map/遮挡矫正/";
    private const string OcclusionMaskLayerName = "OcclusionMask";
    private const string ProxyModelName = "FrontOccluderProxy_Model";

    [MenuItem(MenuRoot + "重建选中物体的模型遮挡代理", priority = 2110)]
    public static void RebuildSelectedModelProxy()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("重建模型遮挡代理", "请先选中场景中的地图物体实例，或它的 VisualRoot / RuleRoot / FrontOccluderRoot 子节点。", "知道了");
            return;
        }

        List<GameObject> roots = CollectUniqueTerrainDecorationRoots(selectedObjects);
        if (roots.Count == 0)
        {
            EditorUtility.DisplayDialog("重建模型遮挡代理", "当前选择中没有找到可处理的地图物体实例。", "知道了");
            return;
        }

        int changed = 0;
        int failed = 0;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("重建模型遮挡代理");

        try
        {
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                EditorUtility.DisplayProgressBar(
                    "重建模型遮挡代理",
                    root.name,
                    roots.Count <= 1 ? 1f : (float)i / (roots.Count - 1));

                try
                {
                    Undo.RegisterFullObjectHierarchyUndo(root, "重建模型遮挡代理");
                    bool ok = RebuildModelProxyFromVisualRoot(root, true, true);
                    if (ok)
                    {
                        changed++;
                        EditorUtility.SetDirty(root);
                        MarkOwningSceneDirty(root);
                    }
                }
                catch (System.Exception ex)
                {
                    failed++;
                    UnityEngine.Debug.LogError($"[TD_MODEL_PROXY_REBUILD] 失败: {GetPath(root != null ? root.transform : null)}\n{ex}", root);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Physics.SyncTransforms();
            SceneView.RepaintAll();
            Undo.CollapseUndoOperations(undoGroup);
        }

        UnityEngine.Debug.Log($"[TD_MODEL_PROXY_REBUILD] 完成 roots={roots.Count}, rebuilt={changed}, failed={failed}");
    }

    [MenuItem(MenuRoot + "重建选中物体的模型遮挡代理", true)]
    public static bool ValidateRebuildSelectedModelProxy()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    private static bool RebuildModelProxyFromVisualRoot(GameObject root, bool useUndo, bool logResult)
    {
        if (root == null)
            return false;

        Transform visualRoot = root.transform.Find("VisualRoot");
        Transform ruleRoot = root.transform.Find("RuleRoot");
        if (visualRoot == null || ruleRoot == null)
        {
            UnityEngine.Debug.LogWarning($"[TD_MODEL_PROXY_REBUILD] 缺少 VisualRoot 或 RuleRoot: {GetPath(root.transform)}", root);
            return false;
        }

        Transform frontOccluderRoot = ruleRoot.Find("FrontOccluderRoot");
        if (frontOccluderRoot == null)
        {
            GameObject frontRootGo = new GameObject("FrontOccluderRoot");
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(frontRootGo, "创建 FrontOccluderRoot");
            frontOccluderRoot = frontRootGo.transform;
            frontOccluderRoot.SetParent(ruleRoot, false);
            frontOccluderRoot.localPosition = Vector3.zero;
            frontOccluderRoot.localRotation = Quaternion.identity;
            frontOccluderRoot.localScale = Vector3.one;
        }

        Renderer[] visualRenderers = CollectValidRenderers(visualRoot);
        if (visualRenderers.Length == 0 || !TryCalculateWorldBounds(visualRenderers, out Bounds visualBefore))
        {
            UnityEngine.Debug.LogWarning($"[TD_MODEL_PROXY_REBUILD] VisualRoot 没有可用 Renderer: {GetPath(visualRoot)}", root);
            return false;
        }

        Transform oldProxy = frontOccluderRoot.Find(ProxyModelName);
        Material maskMaterial = FindReusableMaskMaterial(oldProxy);

        if (oldProxy != null)
        {
            if (useUndo)
                Undo.DestroyObjectImmediate(oldProxy.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(oldProxy.gameObject);
        }

        GameObject clone = UnityEngine.Object.Instantiate(visualRoot.gameObject);
        clone.name = ProxyModelName;
        if (useUndo)
            Undo.RegisterCreatedObjectUndo(clone, "重建 FrontOccluderProxy_Model");

        // 关键：先在世界空间对齐 VisualRoot，再挂到 FrontOccluderRoot 下并保持世界姿态。
        clone.transform.position = visualRoot.position;
        clone.transform.rotation = visualRoot.rotation;
        clone.transform.localScale = visualRoot.lossyScale;
        clone.transform.SetParent(frontOccluderRoot, true);

        StripToRenderOnlyProxy(clone, maskMaterial);
        SetLayerRecursivelyIfExists(clone.transform, OcclusionMaskLayerName);
        clone.SetActive(true);

        Renderer[] proxyRenderers = CollectValidRenderers(clone.transform);
        if (!TryCalculateWorldBounds(proxyRenderers, out Bounds proxyAfter))
        {
            UnityEngine.Debug.LogWarning($"[TD_MODEL_PROXY_REBUILD] 重建后代理体没有可用 Renderer: {GetPath(clone.transform)}", clone);
            return true;
        }

        Vector3 centerDelta = visualBefore.center - proxyAfter.center;
        if (centerDelta.sqrMagnitude > 0.000001f)
        {
            if (useUndo)
                Undo.RecordObject(clone.transform, "对齐模型遮挡代理中心");
            clone.transform.position += centerDelta;
            proxyRenderers = CollectValidRenderers(clone.transform);
            TryCalculateWorldBounds(proxyRenderers, out proxyAfter);
        }

        EditorUtility.SetDirty(clone);
        EditorUtility.SetDirty(frontOccluderRoot.gameObject);

        if (logResult)
        {
            Vector3 sizeDelta = proxyAfter.size - visualBefore.size;
            Vector3 finalCenterDelta = proxyAfter.center - visualBefore.center;
            UnityEngine.Debug.Log(
                $"[TD_MODEL_PROXY_REBUILD] {root.name}\n" +
                $"  Visual center={visualBefore.center:F4} size={visualBefore.size:F4}\n" +
                $"  Proxy  center={proxyAfter.center:F4} size={proxyAfter.size:F4}\n" +
                $"  Delta center={finalCenterDelta:F4} dist={finalCenterDelta.magnitude:F4} sizeDelta={sizeDelta:F4}",
                root);
        }

        return true;
    }

    private static void StripToRenderOnlyProxy(GameObject root, Material maskMaterial)
    {
        if (root == null)
            return;

        // 先移除碰撞、刚体、脚本、动画等运行时组件，只保留 Transform + Mesh/Renderer。
        Component[] components = root.GetComponentsInChildren<Component>(true);
        for (int i = components.Length - 1; i >= 0; i--)
        {
            Component c = components[i];
            if (c == null)
                continue;

            if (c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)
                continue;

            if (c is Renderer)
                continue;

            UnityEngine.Object.DestroyImmediate(c);
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            if (maskMaterial != null)
            {
                Material[] materials = r.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    materials = new Material[1];
                for (int m = 0; m < materials.Length; m++)
                    materials[m] = maskMaterial;
                r.sharedMaterials = materials;
            }
        }
    }

    private static Material FindReusableMaskMaterial(Transform oldProxy)
    {
        if (oldProxy != null)
        {
            Renderer[] oldRenderers = oldProxy.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < oldRenderers.Length; i++)
            {
                Renderer r = oldRenderers[i];
                if (r == null)
                    continue;
                Material[] mats = r.sharedMaterials;
                for (int m = 0; mats != null && m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat != null)
                        return mat;
                }
            }
        }

        string[] guids = AssetDatabase.FindAssets("M_OcclusionMask_ForegroundOccluder t:Material");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
        }

        Shader shader = Shader.Find("Custom/FrontOccluder/OcclusionMask/UnlitWhite");
        if (shader != null)
        {
            Material mat = new Material(shader) { name = "M_OcclusionMask_ForegroundOccluder_RuntimeGenerated" };
            return mat;
        }

        return null;
    }

    private static Renderer[] CollectValidRenderers(Transform root)
    {
        if (root == null)
            return new Renderer[0];

        Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
        List<Renderer> result = new List<Renderer>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null)
                continue;
            if (r is ParticleSystemRenderer)
                continue;
            result.Add(r);
        }
        return result.ToArray();
    }

    private static bool TryCalculateWorldBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool has = false;
        if (renderers == null)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            Bounds b = r.bounds;
            if (!has)
            {
                bounds = b;
                has = true;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }
        return has;
    }

    private static void SetLayerRecursivelyIfExists(Transform root, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (root == null || layer < 0)
            return;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layer;
    }

    private static List<GameObject> CollectUniqueTerrainDecorationRoots(GameObject[] selectedObjects)
    {
        List<GameObject> roots = new List<GameObject>();
        HashSet<int> seen = new HashSet<int>();
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject root = FindTerrainDecorationRootFromSelection(selectedObjects[i]);
            if (root == null)
                continue;
            int id = root.GetInstanceID();
            if (seen.Add(id))
                roots.Add(root);
        }
        return roots;
    }

    private static GameObject FindTerrainDecorationRootFromSelection(GameObject selected)
    {
        if (selected == null || EditorUtility.IsPersistent(selected))
            return null;

        Transform current = selected.transform;
        while (current != null)
        {
            if (LooksLikeTerrainDecorationRoot(current))
                return current.gameObject;
            current = current.parent;
        }
        return null;
    }

    private static bool LooksLikeTerrainDecorationRoot(Transform t)
    {
        if (t == null)
            return false;
        if (t.GetComponent<TerrainDecorationRuntimeBinder>() != null)
            return true;
        if (t.GetComponent<TerrainDecorationRuntimeApplier>() != null)
            return true;

        bool hasVisualRoot = t.Find("VisualRoot") != null;
        bool hasRuleRoot = t.Find("RuleRoot") != null;
        bool hasFrontProxy = t.Find("RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Model") != null ||
                             t.Find("RuleRoot/FrontOccluderRoot/FrontOccluderProxy_Box") != null;
        return hasVisualRoot && (hasRuleRoot || hasFrontProxy);
    }

    private static void MarkOwningSceneDirty(GameObject go)
    {
        if (go == null)
            return;
        Scene scene = go.scene;
        if (scene.IsValid() && scene.isLoaded)
            EditorSceneManager.MarkSceneDirty(scene);
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "<null>";
        string path = t.name;
        Transform current = t.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }
}
