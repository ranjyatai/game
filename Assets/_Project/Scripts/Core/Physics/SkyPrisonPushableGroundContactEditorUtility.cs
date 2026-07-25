#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 可推动物体 80% 自动接地标准化工具。
///
/// 目标：
/// - 不再依赖每个模型手动调接地点。
/// - 默认以实体 Collider 的最低点作为接地标准。
/// - 将可推动物体内容整体校正到 Root localY = 0。
/// - 同步运行时参数，让 Ground Magnet 优先按 Collider Bounds 贴地，而不是按 Pivot Anchor 误锁。
///
/// 注意：
/// - 这是资产校正工具，只处理选中的 prefab / scene instance。
/// - 不改地图 GroundBlock，不改图层，不改 Key。
/// </summary>
public static class SkyPrisonPushableGroundContactEditorUtility
{
    private const string MenuRoot = "Tools/Sky Prison/Physics/";
    private const string MenuCalibrateSelected = MenuRoot + "矫正选中可推动物体接地（80%自动）";

    private static readonly string[] PreferredColliderRoots =
    {
        "PushableColliderRoot",
        "PhysicsRoot",
        "ColliderRoot",
        "CollisionRoot"
    };

    private static readonly string[] ExcludedColliderNameKeywords =
    {
        "Trigger",
        "DecorationTrigger",
        "BackTrigger",
        "FrontTrigger",
        "Occlusion",
        "Stencil",
        "Shadow",
        "Fog",
        "Gizmo",
        "Editor"
    };

    [MenuItem(MenuCalibrateSelected, false, 1800)]
    private static void CalibrateSelected()
    {
        Object[] selected = Selection.objects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[SkyPrisonPushableGroundContactEditorUtility] 没有选中任何物体。请选中 prefab 资产或场景中的可推动物体根节点。");
            return;
        }

        int success = 0;
        int failed = 0;

        for (int i = 0; i < selected.Length; i++)
        {
            Object obj = selected[i];
            if (obj == null)
                continue;

            if (obj is GameObject go)
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab)
                {
                    if (CalibratePrefabAsset(path))
                        success++;
                    else
                        failed++;
                }
                else
                {
                    if (CalibrateSceneObject(go))
                        success++;
                    else
                        failed++;
                }
            }
            else
            {
                failed++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SkyPrisonPushableGroundContactEditorUtility] 接地矫正完成：成功 {success}，失败 {failed}。");
    }

    [MenuItem(MenuCalibrateSelected, true)]
    private static bool ValidateCalibrateSelected()
    {
        return Selection.objects != null && Selection.objects.Length > 0;
    }

    private static bool CalibratePrefabAsset(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
            return false;

        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
                return false;

            bool ok = CalibrateRoot(prefabRoot, true);
            if (ok)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Debug.Log($"[SkyPrisonPushableGroundContactEditorUtility] 已矫正 prefab：{prefabPath}");
            }
            return ok;
        }
        finally
        {
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool CalibrateSceneObject(GameObject selected)
    {
        if (selected == null)
            return false;

        GameObject root = FindPushableRoot(selected.transform);
        if (root == null)
            root = selected;

        bool ok = CalibrateRoot(root, false);
        if (ok)
        {
            EditorUtility.SetDirty(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            Debug.Log($"[SkyPrisonPushableGroundContactEditorUtility] 已矫正场景物体：{root.name}", root);
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

    private static bool CalibrateRoot(GameObject root, bool isPrefabAsset)
    {
        if (root == null)
            return false;

        SkyPrisonPushablePropRuntime runtime = root.GetComponent<SkyPrisonPushablePropRuntime>();
        if (runtime != null)
        {
            // 这里只做资产接地点校正，不再写入运行时物理策略字段。
            // 真实物理放权版已经移除了旧的 GroundMagnet / RootLock 字段，
            // 如果这个工具继续直接引用旧字段，会导致 CS1061 编译错误。
            // 物理策略请在 SkyPrisonPushablePropRuntime / Settings 里单独调，
            // 本工具只负责把选中 prefab 的实体 Collider 最低点校正到 Root localY = 0。
            Undo.RecordObject(runtime, "Mark Pushable Runtime Dirty");
            EditorUtility.SetDirty(runtime);
        }

        List<Collider> contactColliders = CollectContactColliders(root.transform);
        if (contactColliders.Count == 0)
        {
            Debug.LogWarning($"[SkyPrisonPushableGroundContactEditorUtility] {root.name} 没有找到可用于接地矫正的实体 Collider。需要至少一个 enabled 且非 Trigger 的 Collider。", root);
            return false;
        }

        Bounds worldBounds = contactColliders[0].bounds;
        for (int i = 1; i < contactColliders.Count; i++)
            worldBounds.Encapsulate(contactColliders[i].bounds);

        // 在根节点本地空间里求接地最低点。只用 Y，不改 X/Z，避免破坏摆放关系。
        float lowestLocalY = float.PositiveInfinity;
        for (int i = 0; i < contactColliders.Count; i++)
        {
            Bounds b = contactColliders[i].bounds;
            Vector3[] corners = GetBoundsCorners(b);
            for (int c = 0; c < corners.Length; c++)
            {
                float localY = root.transform.InverseTransformPoint(corners[c]).y;
                if (localY < lowestLocalY)
                    lowestLocalY = localY;
            }
        }

        if (!float.IsFinite(lowestLocalY))
            return false;

        float deltaY = -lowestLocalY;
        if (Mathf.Abs(deltaY) > 0.0001f)
            ShiftDirectChildren(root.transform, deltaY);

        CreateOrUpdateContactPreview(root.transform, worldBounds);

        EditorUtility.SetDirty(root);
        if (!isPrefabAsset)
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);

        return true;
    }

    private static List<Collider> CollectContactColliders(Transform root)
    {
        List<Collider> result = new List<Collider>();
        if (root == null)
            return result;

        Transform preferredRoot = FindFirstExistingChild(root, PreferredColliderRoots);
        Transform scanRoot = preferredRoot != null ? preferredRoot : root;

        Collider[] colliders = scanRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null || !c.enabled || c.isTrigger)
                continue;
            if (ShouldExcludeCollider(c.transform, preferredRoot != null))
                continue;

            result.Add(c);
        }

        return result;
    }

    private static bool ShouldExcludeCollider(Transform t, bool alreadyInsidePreferredColliderRoot)
    {
        if (t == null)
            return true;
        if (alreadyInsidePreferredColliderRoot)
            return false;

        Transform cur = t;
        while (cur != null)
        {
            string n = cur.name;
            for (int i = 0; i < ExcludedColliderNameKeywords.Length; i++)
            {
                if (n.IndexOf(ExcludedColliderNameKeywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            cur = cur.parent;
        }
        return false;
    }

    private static Transform FindFirstExistingChild(Transform root, string[] names)
    {
        if (root == null || names == null)
            return null;

        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindChildRecursive(root, names[i]);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (t != null && t != root && t.name == name)
                return t;
        }
        return null;
    }

    private static void ShiftDirectChildren(Transform root, float deltaY)
    {
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            Undo.RecordObject(child, "Calibrate Pushable Ground Contact");
            Vector3 p = child.localPosition;
            p.y += deltaY;
            child.localPosition = p;
            EditorUtility.SetDirty(child);
        }
    }

    private static void CreateOrUpdateContactPreview(Transform root, Bounds worldBounds)
    {
        if (root == null)
            return;

        Transform contactRoot = root.Find("GroundContactRoot");
        if (contactRoot == null)
        {
            GameObject go = new GameObject("GroundContactRoot");
            Undo.RegisterCreatedObjectUndo(go, "Create GroundContactRoot");
            contactRoot = go.transform;
            contactRoot.SetParent(root, false);
        }

        Undo.RecordObject(contactRoot, "Update GroundContactRoot");
        contactRoot.localPosition = Vector3.zero;
        contactRoot.localRotation = Quaternion.identity;
        contactRoot.localScale = Vector3.one;

        Transform contact = contactRoot.Find("Contact_BoundsBottomCenter");
        if (contact == null)
        {
            GameObject go = new GameObject("Contact_BoundsBottomCenter");
            Undo.RegisterCreatedObjectUndo(go, "Create Contact_BoundsBottomCenter");
            contact = go.transform;
            contact.SetParent(contactRoot, false);
        }

        Vector3 bottomCenterWorld = new Vector3(worldBounds.center.x, worldBounds.min.y, worldBounds.center.z);
        Vector3 bottomCenterLocal = root.InverseTransformPoint(bottomCenterWorld);
        bottomCenterLocal.y = 0f;

        Undo.RecordObject(contact, "Update Contact_BoundsBottomCenter");
        contact.localPosition = bottomCenterLocal;
        contact.localRotation = Quaternion.identity;
        contact.localScale = Vector3.one;

        EditorUtility.SetDirty(contactRoot);
        EditorUtility.SetDirty(contact);
    }

    private static Vector3[] GetBoundsCorners(Bounds b)
    {
        Vector3 min = b.min;
        Vector3 max = b.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z),
        };
    }
}
#endif
