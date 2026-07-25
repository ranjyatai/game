#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class UnitDefinitionVisualStructureUtility
{
    private const string VisualRootName = "VisualRoot";
    private const string SpineRootName = "SpineRoot";
    private const string Model3DRootName = "Model3DRoot";

    public static void RepairUnitDefinition(UnitDefinition ud)
    {
        if (ud == null)
            return;

        Undo.RecordObject(ud, "Repair Unit Definition Visual Structure");

        // Spine 通道现在只保存 Assets/_Project/Art/Spine 下的 ScriptableObject .asset。
        // prefab 只作为单位运行时壳保留，不再迁移到 Spine 通道，避免 Unity 序列化把 Prefab / .asset 混成同一字段。
        if (ud.spinePrefab == null && ud.model3DPrefab == null && ud.prefab != null)
            ud.visualChannel = UnitVisualChannel.Spine;

        if (ud.animationKeys == null)
            ud.animationKeys = new UnitAnimationKeySet();

        EnsureDefaultAnimationKeys(ud.animationKeys);

        if (ud.defineType == UnitDefineType.Character)
        {
            ud.useOcclusionOutline = true;
            ud.useSharedOutlineGroup = true;
        }

        EditorUtility.SetDirty(ud);
        AssetDatabase.SaveAssets();
    }

    public static void RepairUnitPrefabStructure(UnitDefinition ud)
    {
        if (ud == null || ud.prefab == null)
        {
            Debug.LogWarning("[UnitDefinitionVisualStructureUtility] 没有可修复的单位运行时 prefab。请先绑定 UnitDefinition.prefab。");
            return;
        }

        string path = AssetDatabase.GetAssetPath(ud.prefab);
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogWarning($"[UnitDefinitionVisualStructureUtility] {ud.name}: prefab 不是有效的资产路径。", ud);
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            RepairPrefabContents(root, ud);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UnitDefinitionVisualStructureUtility] 已修复单位 prefab 结构：{path}", ud);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static void RepairAll(UnitDefinition ud)
    {
        RepairUnitDefinition(ud);
        RepairUnitPrefabStructure(ud);
    }

    public static GameObject GetVisualPrefabForCurrentChannel(UnitDefinition ud)
    {
        if (ud == null)
            return null;

        if (ud.visualChannel == UnitVisualChannel.Model3D)
            return ud.model3DPrefab;

        // Spine 通道现在保存的是 SkeletonDataAsset / ScriptableObject .asset，不再作为 GameObject Prefab 返回。
        return null;
    }

    private static void RepairPrefabContents(GameObject root, UnitDefinition ud)
    {
        if (root == null)
            return;

        EnsureComponent<UnitDefinitionRuntimeBinder>(root);
        EnsureComponent<UnitDefinitionRuntimeApplier>(root);
        EnsureComponent<UnitOutlineState>(root);
        EnsureComponent<UnitOccludedOutlineProxyGate>(root);
        EnsureComponent<UnitOcclusionMaterialReceiver>(root);
        EnsureComponent<Rigidbody>(root);

        UnitDefinitionRuntimeBinder binder = root.GetComponent<UnitDefinitionRuntimeBinder>();
        if (binder != null)
            binder.SetUnitDefinitionAsset(ud, applyNow: false);

        Transform collisionRoot = EnsureChild(root.transform, "CollisionRoot");
        CapsuleCollider capsule = collisionRoot.GetComponent<CapsuleCollider>();
        if (capsule == null)
            capsule = collisionRoot.gameObject.AddComponent<CapsuleCollider>();
        capsule.enabled = true;
        capsule.isTrigger = false;

        Transform shadowRoot = EnsureChild(root.transform, "ShadowRoot");
        EnsureChild(shadowRoot, "Shadow_Projector");

        Transform visualRoot = EnsureChild(root.transform, VisualRootName);
        Transform spineRoot = EnsureChild(visualRoot, SpineRootName);
        EnsureChild(visualRoot, Model3DRootName);

        // 双通道结构到这里为止：VisualRoot / SpineRoot / Model3DRoot。
        // 不再创建 SelfMade2DRoot / 自研包节点。

        // 旧 prefab 经常把 Spine 物体直接放在 UnitRoot 下。
        // 修复结构时把它们收编到 SpineRoot，这样运行时 Spine / 3D 双通道才能真正关闭其它视觉。
        MigrateLegacySpineVisuals(root.transform, spineRoot);

        EnsureChild(root.transform, "OverheadAnchor");
        EnsureChild(root.transform, "OutlineProxy_Player");
        EnsureChild(root.transform, "OutlineProxy_Enemy");
        EnsureChild(root.transform, "OutlineProxy_Item");
        EnsureChild(root.transform, "OutlineProxy_Ally");
    }


    private static void MigrateLegacySpineVisuals(Transform unitRoot, Transform spineRoot)
    {
        if (unitRoot == null || spineRoot == null)
            return;

        Component[] components = unitRoot.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (!IsSpineRuntimeComponent(component))
                continue;

            Transform visual = component.transform;
            if (visual == unitRoot)
                continue;

            if (visual.IsChildOf(spineRoot))
                continue;

            if (IsProtectedRuntimeTransform(visual, unitRoot))
                continue;

            // 如果 Spine 组件在某个子层级上，通常该节点就是视觉根。
            // 保持世界变换迁移，避免旧 prefab 的位置/缩放被破坏。
            visual.SetParent(spineRoot, true);
        }
    }

    private static bool IsSpineRuntimeComponent(Component component)
    {
        if (component == null)
            return false;

        System.Type type = component.GetType();
        string typeName = type.Name;
        string fullName = type.FullName ?? string.Empty;

        return typeName == "SkeletonAnimation" ||
               typeName == "SkeletonRenderer" ||
               typeName == "SkeletonMecanim" ||
               fullName == "Spine.Unity.SkeletonAnimation" ||
               fullName == "Spine.Unity.SkeletonRenderer" ||
               fullName == "Spine.Unity.SkeletonMecanim";
    }

    private static bool IsProtectedRuntimeTransform(Transform t, Transform unitRoot)
    {
        if (t == null)
            return true;

        string n = t.name;
        if (n.StartsWith("OutlineProxy_")) return true;
        if (n == "CollisionRoot") return true;
        if (n == "ShadowRoot" || n == "Shadow_Projector") return true;
        if (n == "OverheadAnchor") return true;
        if (n.Contains("Overhead")) return true;
        if (n.Contains("UI")) return true;
        if (n.Contains("Fog")) return true;
        if (n.Contains("Vision")) return true;
        if (n.Contains("Trigger")) return true;

        Transform p = t.parent;
        while (p != null && p != unitRoot)
        {
            string pn = p.name;
            if (pn.StartsWith("OutlineProxy_")) return true;
            if (pn == "CollisionRoot") return true;
            if (pn == "ShadowRoot") return true;
            if (pn == "OverheadAnchor") return true;
            if (pn.Contains("Overhead")) return true;
            if (pn.Contains("UI")) return true;
            if (pn.Contains("Fog")) return true;
            if (pn.Contains("Vision")) return true;
            if (pn.Contains("Trigger")) return true;
            p = p.parent;
        }

        return false;
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }

    private static Transform EnsureChild(Transform parent, string childName)
    {
        Transform child = FindDirectChild(parent, childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        child = go.transform;
        child.SetParent(parent, false);
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return child;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;
        }

        return null;
    }

    private static void EnsureDefaultAnimationKeys(UnitAnimationKeySet keys)
    {
        if (keys == null)
            return;

        if (string.IsNullOrWhiteSpace(keys.idleKey)) keys.idleKey = "idle";
        if (string.IsNullOrWhiteSpace(keys.walkKey)) keys.walkKey = "walk";
        if (string.IsNullOrWhiteSpace(keys.runKey)) keys.runKey = "run";
        if (string.IsNullOrWhiteSpace(keys.sneakKey)) keys.sneakKey = "sneak";
        if (string.IsNullOrWhiteSpace(keys.jumpKey)) keys.jumpKey = "jump";
        if (string.IsNullOrWhiteSpace(keys.attackKey)) keys.attackKey = "attack";
        if (string.IsNullOrWhiteSpace(keys.hitKey)) keys.hitKey = "hit";
        if (string.IsNullOrWhiteSpace(keys.dodgeKey)) keys.dodgeKey = "dodge";
        if (string.IsNullOrWhiteSpace(keys.deathKey)) keys.deathKey = "die";
    }
}
#endif
