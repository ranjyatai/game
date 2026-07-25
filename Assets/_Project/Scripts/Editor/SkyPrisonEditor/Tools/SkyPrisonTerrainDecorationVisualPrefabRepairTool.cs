#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Repairs terrain-decoration VISUAL prefab assets.
///
/// Important project rule:
/// - Visual PF assets must stay visual-only.
/// - Runtime roots own TerrainDecorationRuntimeBinder / RuntimeApplier / RuleRoot / triggers / occlusion / physics runtime.
/// - Visual PF assets must not contain duplicated RuleRoot / BackTrigger / FrontTrigger / Rigidbody / MeshCollider / PushableRuntime.
///
/// Use this when old visual prefabs were polluted by previous physics/runtime tests.
/// </summary>
public static class SkyPrisonTerrainDecorationVisualPrefabRepairTool
{
    private const string DefaultVisualPrefabFolder = "Assets/_Project/Art/Prefabs/TerrainDecoration";
    private const string WorldLayerName = "World3D";

    private static readonly HashSet<string> RuntimeNodeNames = new HashSet<string>
    {
        "VisualRoot",
        "RuleRoot",
        "CollisionRoot",
        "VisionBlockerRoot",
        "SortAnchor",
        "PlaneOrigin",
        "PlaneForwardReference",
        "BackTrigger",
        "FrontTrigger",
        "FrontOccluderRoot",
        "FrontOccluderProxy_Box",
        "OutlineMaskProxyRoot",
        "OutlineMaskProxy_T_Box",
        "SubOutlineMaskProxy_01",
        "ShadowCasterRoot",
        "StencilWriterRoot",
        "MossRoot",
        "FXRoot",
        "EditorGizmoRoot",
        "PushableColliderRoot",
        "PhysicsProxyRoot",
        "PushableBody",
        "PhysicsBody",
        "SingleBodyPhysicsRoot",
        "SortableRoot",
        "StructureRoot",
        "TerrainPropRoot",
        "PatternDecorationRoot",
        "VFXRoot"
    };

    private static readonly HashSet<string> RuntimeComponentTypeNames = new HashSet<string>
    {
        "TerrainDecorationRuntimeBinder",
        "TerrainDecorationRuntimeApplier",
        "SkyPrisonPushablePropRuntime",
        "SkyPrisonTerrainDecorationPushablePhysicsTest"
    };

    [MenuItem("Tools/Sky Prison/Terrain Decoration/Prefab/Repair Selected Visual Prefab Asset")]
    public static void RepairSelectedVisualPrefabAssetMenu()
    {
        List<string> paths = ResolveSelectedPrefabAssetPaths();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "视觉 PF 清理",
                "请选择 Project 里的视觉 Prefab，或在 Hierarchy 里选中地形装饰物实例 / VisualRoot 子节点。",
                "知道了");
            return;
        }

        int changed = RepairPrefabAssets(paths);
        EditorUtility.DisplayDialog(
            "视觉 PF 清理",
            $"已检查 Prefab：{paths.Count} 个\n发生清理：{changed} 个",
            "知道了");
    }

    [MenuItem("Tools/Sky Prison/Terrain Decoration/Prefab/Repair All TerrainDecoration Visual Prefabs")]
    public static void RepairAllTerrainDecorationVisualPrefabsMenu()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultVisualPrefabFolder });
        List<string> paths = new List<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        int changed = RepairPrefabAssets(paths);
        EditorUtility.DisplayDialog(
            "视觉 PF 清理",
            $"已检查 TerrainDecoration 视觉 Prefab：{paths.Count} 个\n发生清理：{changed} 个",
            "知道了");
    }

    public static int RepairPrefabAssets(IEnumerable<string> prefabPaths)
    {
        int changedCount = 0;
        HashSet<string> unique = new HashSet<string>();

        foreach (string rawPath in prefabPaths)
        {
            string path = rawPath;
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".prefab"))
                continue;
            if (!unique.Add(path))
                continue;

            if (RepairSinglePrefabAsset(path))
                changedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return changedCount;
    }

    public static bool RepairSinglePrefabAsset(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return false;

        bool changed = false;
        try
        {
            changed |= RemoveRuntimeComponents(root.transform);
            changed |= RemoveRuntimeStructureNodes(root.transform);
            changed |= ForceLayerRecursive(root.transform, WorldLayerName);

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (changed)
            Debug.Log($"[SkyPrison] Repaired terrain-decoration visual prefab: {prefabPath}");

        return changed;
    }

    private static List<string> ResolveSelectedPrefabAssetPaths()
    {
        List<string> paths = new List<string>();

        foreach (Object obj in Selection.objects)
        {
            if (obj == null)
                continue;

            GameObject selectedGo = obj as GameObject;
            string path = AssetDatabase.GetAssetPath(obj);

            if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".prefab"))
            {
                paths.Add(path);
                continue;
            }

            if (selectedGo == null)
                continue;

            // Scene object: collect prefab sources below VisualRoot first.
            Transform selectedTransform = selectedGo.transform;
            Transform runtimeRoot = FindRuntimeRoot(selectedTransform);
            if (runtimeRoot != null)
            {
                Transform visualRoot = runtimeRoot.Find("VisualRoot");
                if (visualRoot != null)
                    CollectPrefabSourcesUnder(visualRoot, paths);
                else
                    CollectPrefabSourcesUnder(runtimeRoot, paths);
            }
            else
            {
                CollectPrefabSourcesUnder(selectedTransform, paths);
            }
        }

        // Deduplicate while preserving order.
        HashSet<string> seen = new HashSet<string>();
        List<string> result = new List<string>();
        foreach (string p in paths)
        {
            if (!string.IsNullOrWhiteSpace(p) && p.EndsWith(".prefab") && seen.Add(p))
                result.Add(p);
        }
        return result;
    }

    private static void CollectPrefabSourcesUnder(Transform root, List<string> paths)
    {
        if (root == null)
            return;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t == null)
                continue;

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (source == null)
                continue;

            string path = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".prefab"))
                paths.Add(path);
        }
    }

    private static Transform FindRuntimeRoot(Transform start)
    {
        Transform t = start;
        while (t != null)
        {
            if (HasComponentNamed(t.gameObject, "TerrainDecorationRuntimeBinder") ||
                HasComponentNamed(t.gameObject, "TerrainDecorationRuntimeApplier"))
            {
                return t;
            }
            t = t.parent;
        }
        return null;
    }

    private static bool RemoveRuntimeComponents(Transform root)
    {
        bool changed = false;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            if (t == null)
                continue;

            Component[] components = t.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component c = components[i];
                if (c == null)
                    continue;

                string typeName = c.GetType().Name;
                bool remove = RuntimeComponentTypeNames.Contains(typeName) ||
                              c is Rigidbody ||
                              c is Collider;

                if (!remove)
                    continue;

                Object.DestroyImmediate(c, true);
                changed = true;
            }
        }
        return changed;
    }

    private static bool RemoveRuntimeStructureNodes(Transform root)
    {
        bool changed = false;
        List<GameObject> delete = new List<GameObject>();

        // Do not delete the prefab root itself even if it has a bad name.
        for (int i = 0; i < root.childCount; i++)
            CollectRuntimeNodes(root.GetChild(i), delete);

        foreach (GameObject go in delete)
        {
            if (go == null)
                continue;
            Object.DestroyImmediate(go, true);
            changed = true;
        }

        return changed;
    }

    private static void CollectRuntimeNodes(Transform t, List<GameObject> delete)
    {
        if (t == null)
            return;

        string n = t.name;
        if (RuntimeNodeNames.Contains(n) || n.StartsWith("__Auto") || n.StartsWith("__PhysicsProxy") || n.StartsWith("__PushablePhysics"))
        {
            delete.Add(t.gameObject);
            return;
        }

        for (int i = 0; i < t.childCount; i++)
            CollectRuntimeNodes(t.GetChild(i), delete);
    }

    private static bool ForceLayerRecursive(Transform root, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
            return false;

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

    private static bool HasComponentNamed(GameObject go, string typeName)
    {
        if (go == null)
            return false;

        Component[] components = go.GetComponents<Component>();
        foreach (Component c in components)
        {
            if (c == null)
                continue;
            if (c.GetType().Name == typeName)
                return true;
        }
        return false;
    }
}
#endif
