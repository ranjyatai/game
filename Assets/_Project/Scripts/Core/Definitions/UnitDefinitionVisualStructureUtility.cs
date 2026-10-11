#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Spine.Unity;

public static class UnitDefinitionVisualStructureUtility
{
    private const string VisualRootName = "VisualRoot";
    private const string SpineRootName = "SpineRoot";
    private const string Model3DRootName = "Model3DRoot";
    private const string SpineSourceObjectName = "Spine GameObject";

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
        EnsureDefaultOverheadStyle(ud);

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

    private const string GenericShellPrefabPath = "Assets/_Project/Prefabs/Units/PF_UnitRuntimeShell_Generic.prefab";

    /// <summary>没给 UnitDefinition.prefab 配值时的兜底——不是每个NPC都要单独做一份
    /// 专属预制体，大部分只是换Spine外观，可以共用同一个通用壳。找不到就现场生成
    /// 一份放在固定路径，之后所有没配prefab的UnitDefinition都共用这一份。</summary>
    public static GameObject GetOrCreateGenericShellPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GenericShellPrefabPath);
        if (existing != null)
            return existing;

        string folder = System.IO.Path.GetDirectoryName(GenericShellPrefabPath).Replace("\\", "/");
        EnsureFolderRecursive(folder);

        GameObject temp = new GameObject("PF_UnitRuntimeShell_Generic");
        try
        {
            RepairPrefabContents(temp, null);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, GenericShellPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UnitDefinitionVisualStructureUtility] 生成了通用单位运行时壳预制体：{GenericShellPrefabPath}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    /// <summary>UnitDefinition.prefab 没配时兜底填成通用壳，让放置工具/预览之类的地方
    /// 不用因为一个字段没填就整个卡死。写回资产，下次就不用再兜底了。</summary>
    public static GameObject ResolveOrAssignFallbackPrefab(UnitDefinition ud)
    {
        if (ud == null)
            return null;

        if (ud.prefab != null)
            return ud.prefab;

        GameObject shell = GetOrCreateGenericShellPrefab();
        if (shell == null)
            return null;

        Undo.RecordObject(ud, "Assign Fallback Unit Prefab");
        ud.prefab = shell;
        EditorUtility.SetDirty(ud);
        AssetDatabase.SaveAssets();

        Debug.Log($"[UnitDefinitionVisualStructureUtility] {ud.name} 没有配置运行时预制体，已自动套用通用壳：{GenericShellPrefabPath}", ud);
        return shell;
    }

    /// <summary>通用壳预制体已经存在于磁盘上时，GetOrCreateGenericShellPrefab 只会
    /// 直接返回现成的，不会重新跑一遍 RepairPrefabContents——加了阴影贴花之类的
    /// 结构性修复之后，已经生成过的通用壳不会自动补上，需要这个入口手动/批处理
    /// 重新修一遍。</summary>
    [MenuItem("天空囚笼/内容数据/修复通用单位运行时壳预制体", false, 182)]
    public static void RepairGenericShellPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GenericShellPrefabPath);
        try
        {
            RepairPrefabContents(root, null);
            PrefabUtility.SaveAsPrefabAsset(root, GenericShellPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UnitDefinitionVisualStructureUtility] 已修复通用单位运行时壳预制体：{GenericShellPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private const string DefaultOverheadStyleAssetPath = "Assets/_Project/UIUX/HUD/OverheadBarStyles/OBS_NewStyle.asset";

    /// <summary>UnitOverheadUIView.ApplyBarStyle()/ApplyBarVisualStyleOnly() 在
    /// overheadHpBarStyle 为空时会显式把血条画成纯白色填充(没贴图、颜色留白)——
    /// 不是残留的调试占位，是代码写死的"没样式就画白条"。新建的UnitDefinition
    /// 这个字段默认是空的，不兜底的话每个新NPC头顶都会先顶一条白条。</summary>
    public static void EnsureDefaultOverheadStyle(UnitDefinition ud)
    {
        if (ud == null || ud.overheadHpBarStyle != null)
            return;

        OverheadBarStyleAsset defaultStyle = AssetDatabase.LoadAssetAtPath<OverheadBarStyleAsset>(DefaultOverheadStyleAssetPath);
        if (defaultStyle == null)
            return;

        Undo.RecordObject(ud, "Assign Default Overhead Bar Style");
        ud.overheadHpBarStyle = defaultStyle;
        EditorUtility.SetDirty(ud);
        AssetDatabase.SaveAssets();

        Debug.Log($"[UnitDefinitionVisualStructureUtility] {ud.name} 没有配置头顶血条样式，已自动套用默认样式：{DefaultOverheadStyleAssetPath}", ud);
    }

    private static void EnsureFolderRecursive(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
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

    private const string ShadowSpritePath = "Assets/_Project/Art/Textures/Shadows/T_Shadow_Ellipse_01.png";
    private const string ShadowMaterialPath = "Assets/_Project/Art/Materials/FrontOccluder/Shadows/M_ShadowProjector_StencilReader.mat";

    /// <summary>之前这里只建了个空的 Shadow_Projector 占位 Transform，没有实际的
    /// SpriteRenderer/贴图/材质——套通用壳的NPC脚下等于压根没有渲染器，不是被
    /// 挡住或透明。这里补上跟玩家专属预制体同一份阴影贴图+材质，给个通用椭圆
    /// 默认位置/缩放，UnitDefinition.shadowLocalPosition/Scale 之后还能再调；
    /// 只在这个 SpriteRenderer 是刚新增出来时才写默认值，不动手搓预制体上已经
    /// 摆好的那份（比如玩家的 Shadow_Projector）。</summary>
    private static void EnsureShadowProjectorVisual(Transform shadowProjector)
    {
        SpriteRenderer sr = shadowProjector.GetComponent<SpriteRenderer>();
        bool isNew = sr == null;
        if (isNew)
            sr = shadowProjector.gameObject.AddComponent<SpriteRenderer>();

        // 玩家手搓预制体里的 Shadow_Projector 就是放在 Character2D 层——不跟着放会落到
        // Default，摄像机不渲染 Default 层，等于加了个不可见的贴图。放在 isNew 判断之外，
        // 每次修复都强制核对一遍，不然已经跑过一次的通用壳（SpriteRenderer 已存在）
        // 再修一遍图层也不会生效。
        ApplyCharacter2DLayer(shadowProjector.gameObject);

        if (!isNew)
            return;

        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath);
        sr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
        sr.color = new Color(0.0104f, 0.0104f, 0.1069f, 1f);

        if (sr.sprite == null)
            Debug.LogWarning($"[UnitDefinitionVisualStructureUtility] 找不到阴影贴图（{ShadowSpritePath}），Shadow_Projector 会没有可见内容。");
        if (sr.sharedMaterial == null)
            Debug.LogWarning($"[UnitDefinitionVisualStructureUtility] 找不到阴影材质（{ShadowMaterialPath}），Shadow_Projector 会用默认材质渲染。");

        shadowProjector.localPosition = new Vector3(0f, -0.02f, 0f);
        shadowProjector.localScale = new Vector3(0.867286f, 0.433643f, 0.7227384f);
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

        // 血量/头顶名字+血条这一套，UnitDefinitionRuntimeApplier/UnitOverheadHealthBridge
        // 都只会"找现成的"(GetComponent)，不会自动补——手搓的专属预制体一直是靠人工加好
        // 这几个组件的，通用壳如果漏了，摆出来的单位就会头顶完全空白、也没有血量概念，
        // 不是可见性开关的问题，是组件本身就不存在。
        EnsureComponent<UnitHealthController>(root);
        EnsureComponent<UnitDeathController>(root);

        // preserveAuthoredLayout 默认true——意思是"有人已经手工把Canvas摆好比例了，
        // 运行时代码别去动它"。手搓的专属预制体确实是这样，但通用壳这个组件是刚
        // AddComponent出来的，压根没人摆过，默认true会导致EnsureStructure()跳过
        // 缩放/定位(ApplyUnitDefinitionTransformOverrides里的0.02缩放)，头顶UI就会
        // 用Unity默认1:1的Canvas缩放整个爆炸式放大，正是"名字铺满地面"这个现象。
        // 只在这个组件是刚新增出来的时候关掉，不动手搓预制体上原本就有的这份配置。
        bool overheadViewIsNew = root.GetComponent<UnitOverheadUIView>() == null;
        UnitOverheadUIView overheadView = EnsureComponent<UnitOverheadUIView>(root);
        if (overheadViewIsNew)
            overheadView.preserveAuthoredLayout = false;

        EnsureComponent<UnitOverheadHealthBridge>(root);

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
        Transform shadowProjector = EnsureChild(shadowRoot, "Shadow_Projector");
        EnsureShadowProjectorVisual(shadowProjector);

        Transform visualRoot = EnsureChild(root.transform, VisualRootName);
        Transform spineRoot = EnsureChild(visualRoot, SpineRootName);
        EnsureChild(visualRoot, Model3DRootName);

        // 双通道结构到这里为止：VisualRoot / SpineRoot / Model3DRoot。
        // 不再创建 SelfMade2DRoot / 自研包节点。

        // 旧 prefab 经常把 Spine 物体直接放在 UnitRoot 下。
        // 修复结构时把它们收编到 SpineRoot，这样运行时 Spine / 3D 双通道才能真正关闭其它视觉。
        MigrateLegacySpineVisuals(root.transform, spineRoot);

        // 如果 SpineRoot 下面一个 SkeletonAnimation 都没有，UnitDefinitionRuntimeBinder.
        // FindMainSpineAnimation() 找不到任何可绑定对象，会直接跳过绑定——UnitDefinition.
        // spinePrefab 换了骨架数据也不会有任何视觉变化(静默无效)。这里补一个空壳占位对象
        // 承接骨架数据，让"共用同一份运行时预制体、只换Spine数据"真正跑通，不用每个NPC
        // 都单独准备一份已经内置好Spine模型的专属预制体。
        EnsureSpineSourcePlaceholder(spineRoot);

        // UnitOverheadUIView.EnsureStructure() 自己也会找/建这个节点(EnsureTransformChild)，
        // 但只要节点已经存在就直接沿用现成的位置，不会帮你摆高——所以这里预先创建的话
        // 必须顺手给个像样的高度，不然就是这次遇到的"名字/血条整个贴在地上跟角色脚重叠"。
        // 1.6 是给通用壳用的经验默认值，不同角色实际身高不同，摆完之后可以用
        // UnitDefinition.overheadUiOffsetY 再微调，不用去改这个节点本身。
        Transform overheadAnchor = EnsureChild(root.transform, "OverheadAnchor");
        if (overheadAnchor.localPosition == Vector3.zero)
            overheadAnchor.localPosition = new Vector3(0f, 1.6f, 0f);

        EnsureChild(root.transform, "OutlineProxy_Player");
        EnsureChild(root.transform, "OutlineProxy_Enemy");
        EnsureChild(root.transform, "OutlineProxy_Item");
        EnsureChild(root.transform, "OutlineProxy_Ally");
    }


    /// <summary>确保 SpineRoot 下至少有一个承接骨架数据的 SkeletonAnimation 对象——
    /// 不初始化 skeletonDataAsset(留空)，运行时由 UnitDefinitionRuntimeBinder 按
    /// UnitDefinition.spinePrefab 换成具体骨架，这里只负责"占位承接"。</summary>
    private static void EnsureSpineSourcePlaceholder(Transform spineRoot)
    {
        if (spineRoot == null)
            return;

        SkeletonAnimation[] existing = spineRoot.GetComponentsInChildren<SkeletonAnimation>(true);
        if (existing.Length > 0)
        {
            // 已经有Spine视觉对象了(可能是之前跑这个修复工具、还没补层修复的旧壳)——
            // 不重复创建，但补一下层，不然摆出来的单位仍然"不存在于"遮挡/描边系统里。
            foreach (var anim in existing)
                ApplyCharacter2DLayer(anim.gameObject);
            return;
        }

        Transform placeholder = EnsureChild(spineRoot, SpineSourceObjectName);
        GameObject go = placeholder.gameObject;
        ApplyCharacter2DLayer(go);

        if (go.GetComponent<MeshFilter>() == null)
            go.AddComponent<MeshFilter>();
        if (go.GetComponent<MeshRenderer>() == null)
            go.AddComponent<MeshRenderer>();
        if (go.GetComponent<SkeletonRenderer>() == null)
            go.AddComponent<SkeletonRenderer>();
        if (go.GetComponent<SkeletonAnimation>() == null)
            go.AddComponent<SkeletonAnimation>();
    }

    /// <summary>真实角色预制体都是手动把Spine视觉对象放在 Character2D 层(layer 8)——
    /// 遮挡/角色描边这些渲染Feature(CharacterPresenceFeature等)专门按这个层抓取，
    /// 不在这个层的话摆出来的单位视觉上就"不存在于"遮挡/描边系统里。</summary>
    private static void ApplyCharacter2DLayer(GameObject go)
    {
        if (go == null)
            return;

        int character2DLayer = LayerMask.NameToLayer("Character2D");
        if (character2DLayer >= 0)
            go.layer = character2DLayer;
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
