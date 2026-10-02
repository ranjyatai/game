using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 创建「白模」这一套资产：网格材质 + 长方体/圆柱体/墙体/远景体块预制体 + 对应的地形装饰物定义。
///
/// 用途：关卡白模阶段先用纯几何体把体量、高差、遮挡关系摆出来，再换美术资产。
///
/// - 视觉在 World3D 层，游戏里可见（跟空气墙相反）。
/// - 材质是 SkyPrison/Graybox：世界空间网格，一格固定 1 米、每 5 格一条粗线，
///   任意缩放下都能直接数尺寸。
/// - 碰撞用 Mesh 模式，由 Builder 按视觉网格生成——视觉和碰撞是同一份几何，
///   不存在"看到的和挡人的不一样"。圆柱用 Box 碰撞会在四角多出一块，所以统一走 Mesh。
/// - 预制体轴心在底面中心，摆下去贴地，Y 缩放就是高度（默认 1×1×1 米）。
/// - 遮挡/视线/子弹设置照搬"废路电车"这类真实建筑：挡视线、挡子弹、参与前后遮挡
///   并生成遮挡探测碰撞体——白模的意义就是提前看到真实建筑会造成的遮挡。
/// - allowCollisionOverlap：白模要能互相叠放、穿插拼出台地和复合体块。
///
/// 幂等：资产已存在就只补定义字段，不重建预制体和材质，避免覆盖后来的调整。
/// </summary>
public static class SkyPrisonGrayboxAssetCreator
{
    private const string LogPrefix = "[SkyPrison Graybox]";

    private const string ShaderPath = "Assets/_Project/Art/Shaders/Custom/Graybox/SkyPrisonGraybox.shader";
    private const string MaterialPath = "Assets/_Project/Art/Materials/System/M_Graybox_Grid.mat";
    private const string PrefabFolder = "Assets/_Project/Data/Definitions/Core/Graybox";
    private const string BoxPrefabPath = PrefabFolder + "/PF_Graybox_Box.prefab";
    private const string CylinderPrefabPath = PrefabFolder + "/PF_Graybox_Cylinder.prefab";
    private const string DefinitionFolder = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations";
    private const string BoxDefinitionPath = DefinitionFolder + "/TD_Graybox_Box.asset";
    private const string CylinderDefinitionPath = DefinitionFolder + "/TD_Graybox_Cylinder.asset";

    // 通用墙体：长方体压扁拉高。默认 4 米宽 × 4 米高 × 0.5 米厚，宽和厚都落在整米/半米上，
    // 网格吸附摆放时墙和墙能首尾对齐；要别的尺寸直接缩放，世界空间网格不会被拉伸。
    private const string WallPrefabPath = PrefabFolder + "/PF_Graybox_Wall.prefab";
    private const string WallDefinitionPath = DefinitionFolder + "/TD_Graybox_Wall.asset";
    private static readonly Vector3 WallSize = new Vector3(4f, 4f, 0.5f);

    // 远景体块：格栅地面下方的建筑群占位。纯视觉——没有碰撞、不参与遮挡、不挡视线，
    // 不然会被地面检测当成脚下的地、被遮挡系统误判成挡住角色、挡住视野射线。
    // 颜色压暗，一眼和可玩层的白模区分开，也提前验证"下层要比上层暗"的明暗关系。
    private const string BackdropMaterialPath = "Assets/_Project/Art/Materials/System/M_Graybox_Grid_Backdrop.mat";
    private const string BackdropBoxPrefabPath = PrefabFolder + "/PF_Graybox_BackdropBox.prefab";
    private const string BackdropBoxDefinitionPath = DefinitionFolder + "/TD_Graybox_BackdropBox.asset";

    [MenuItem("天空囚笼/地图/创建或修复「白模」资产", false, 110)]
    public static void CreateOrRepair()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} 找不到着色器 {ShaderPath}，先确认它已经编译成功。");
            return;
        }

        Material material = EnsureMaterial(shader);

        GameObject boxPrefab = EnsurePrefab(BoxPrefabPath, "PF_Graybox_Box", PrimitiveType.Cube, material);
        GameObject cylinderPrefab = EnsurePrefab(CylinderPrefabPath, "PF_Graybox_Cylinder", PrimitiveType.Cylinder, material);

        TerrainDecorationDefinition boxDef = EnsureDefinition(
            BoxDefinitionPath, boxPrefab, "graybox_box", "白模·长方体", -998);
        TerrainDecorationDefinition cylinderDef = EnsureDefinition(
            CylinderDefinitionPath, cylinderPrefab, "graybox_cylinder", "白模·圆柱体", -997);

        GameObject wallPrefab = EnsurePrefab(WallPrefabPath, "PF_Graybox_Wall", PrimitiveType.Cube, material, WallSize);
        TerrainDecorationDefinition wallDef = EnsureDefinition(
            WallDefinitionPath, wallPrefab, "graybox_wall", "白模·墙体", -995);

        Material backdropMaterial = EnsureMaterial(shader, BackdropMaterialPath, backdrop: true);
        GameObject backdropPrefab = EnsurePrefab(BackdropBoxPrefabPath, "PF_Graybox_BackdropBox", PrimitiveType.Cube, backdropMaterial);
        TerrainDecorationDefinition backdropDef = EnsureDefinition(
            BackdropBoxDefinitionPath, backdropPrefab, "graybox_backdrop_box", "白模·远景体块", -996);
        ApplyBackdropRules(backdropDef);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.objects = new Object[] { boxDef, cylinderDef, wallDef, backdropDef };
        Debug.Log($"{LogPrefix} 完成。长方体：{BoxDefinitionPath}，圆柱体：{CylinderDefinitionPath}，墙体：{WallDefinitionPath}，远景体块：{BackdropBoxDefinitionPath}");
    }

    /// <summary>远景体块在通用白模规则之上关掉一切交互：碰撞、遮挡、视线、子弹。</summary>
    private static void ApplyBackdropRules(TerrainDecorationDefinition definition)
    {
        definition.collisionMode = TerrainDecorationCollisionMode.None;
        definition.blockPlayer = false;
        definition.blockEnemy = false;
        definition.blockProjectile = false;
        definition.blockVision = false;
        definition.occlusionMode = TerrainDecorationOcclusionMode.None;
        definition.generateOccluderProbeCollider = false;
        definition.frontOccluderProxyMode = TerrainDecorationFrontOccluderProxyMode.None;
        definition.placementCollisionMode = TerrainDecorationPlacementCollisionMode.None;
        EditorUtility.SetDirty(definition);
    }

    private static Material EnsureMaterial(Shader shader) => EnsureMaterial(shader, MaterialPath, backdrop: false);

    private static Material EnsureMaterial(Shader shader, string materialPath, bool backdrop)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (existing != null)
        {
            if (existing.shader != shader)
                existing.shader = shader;
            return existing;
        }

        Material material = new Material(shader) { name = Path.GetFileNameWithoutExtension(materialPath) };
        // 开 GPU Instancing：白模会大量重复摆放，同材质同网格可以合成一次绘制。
        material.enableInstancing = true;

        if (backdrop)
        {
            // 远景：整体压暗、偏冷，网格线也收淡——它只负责体量和明暗，不该抢可玩层的注意力。
            material.SetColor("_BaseColor", new Color(0.30f, 0.33f, 0.38f, 1f));
            material.SetColor("_MinorLineColor", new Color(0.25f, 0.28f, 0.32f, 1f));
            material.SetColor("_MajorLineColor", new Color(0.19f, 0.21f, 0.25f, 1f));
        }

        EnsureFolder(Path.GetDirectoryName(materialPath).Replace('\\', '/'));
        AssetDatabase.CreateAsset(material, materialPath);
        Debug.Log($"{LogPrefix} 新建材质 {materialPath}。");
        return material;
    }

    private static GameObject EnsurePrefab(string prefabPath, string objectName, PrimitiveType primitive, Material material)
        => EnsurePrefab(prefabPath, objectName, primitive, material, Vector3.one);

    /// <param name="size">体块尺寸（米）。轴心始终在底面中心，网格往上抬半个高度。</param>
    private static GameObject EnsurePrefab(string prefabPath, string objectName, PrimitiveType primitive, Material material, Vector3 size)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
            return existing;

        int visibleLayer = LayerMask.NameToLayer("World3D");
        if (visibleLayer < 0)
        {
            Debug.LogWarning($"{LogPrefix} 找不到 World3D 层，退回 Default(0)——白模会在游戏里不可见。");
            visibleLayer = 0;
        }

        EnsureFolder(PrefabFolder);

        // 根节点在底面中心，网格挂在子节点上往上抬半个高度。
        var root = new GameObject(objectName) { layer = visibleLayer };

        GameObject mesh = GameObject.CreatePrimitive(primitive);
        mesh.name = "Mesh";
        mesh.layer = visibleLayer;
        mesh.transform.SetParent(root.transform, false);
        mesh.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        // Unity 自带圆柱高 2 米，Y 再减半，和长方体同为 size 指定的体块。
        mesh.transform.localScale = primitive == PrimitiveType.Cylinder
            ? new Vector3(size.x, size.y * 0.5f, size.z)
            : size;

        // 碰撞归 Builder 的 CollisionRoot 管（Mesh 模式按这份网格生成），
        // 视觉子树一律不带碰撞，同空气墙/柔边雾箱的约定。
        Collider primitiveCollider = mesh.GetComponent<Collider>();
        if (primitiveCollider != null)
            Object.DestroyImmediate(primitiveCollider);

        MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"{LogPrefix} 新建预制体 {prefabPath}（{size.x}×{size.y}×{size.z} 米，轴心在底面，图层 World3D）。");
        return prefab;
    }

    private static TerrainDecorationDefinition EnsureDefinition(
        string definitionPath, GameObject prefab, string decorationId, string displayName, int sortPriority)
    {
        TerrainDecorationDefinition definition =
            AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(definitionPath);

        bool created = false;
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<TerrainDecorationDefinition>();
            EnsureFolder(DefinitionFolder);
            AssetDatabase.CreateAsset(definition, definitionPath);
            created = true;
        }

        definition.decorationId = decorationId;
        definition.displayName = displayName;
        definition.category = TerrainDecorationCategory.Custom;
        definition.subCategory = "白模";
        definition.isGraybox = true; // 放置工具按这个字段分到「白模」模块、放进 GrayboxRoot
        definition.sortPriority = sortPriority; // 紧跟空气墙(-1000)、柔边雾箱(-999)
        definition.editorOnlyVisual = false;

        if (definition.variants == null)
            definition.variants = new List<TerrainDecorationVariant>();
        if (definition.variants.Count == 0)
            definition.variants.Add(new TerrainDecorationVariant());
        definition.variants[0].variantId = "default";
        definition.variants[0].displayName = "默认版本";
        definition.variants[0].prefab = prefab;

        definition.allowMove = true;
        definition.allowRotate = true;
        definition.allowScale = true;
        definition.snapToGrid = true;
        definition.allowCollisionOverlap = true;

        // 碰撞 = 视觉网格本身。
        definition.collisionMode = TerrainDecorationCollisionMode.Mesh;
        definition.blockPlayer = true;
        definition.blockEnemy = true;
        definition.blockProjectile = true;
        definition.blockVision = true;
        definition.walkableSurface = null;

        // 遮挡：同废路电车（FrontBack + 探测碰撞体）。只开 occlusionMode 不开探测体、
        // 或反过来，都会出现"角色走到后面没被挡"——两个都要开，见柔边雾箱创建器的记录。
        definition.occlusionMode = TerrainDecorationOcclusionMode.FrontBack;
        definition.generateOccluderProbeCollider = true;
        definition.frontBackPlaneMode = TerrainDecorationFrontBackPlaneMode.CollisionBounds;
        definition.frontOccluderProxyMode = TerrainDecorationFrontOccluderProxyMode.ModelProxy;

        definition.shadowMode = TerrainDecorationShadowMode.MeshRenderer;
        definition.castShadow = true;
        definition.receiveShadow = true;
        definition.fogMode = TerrainDecorationFogMode.AlwaysVisible;

        EditorUtility.SetDirty(definition);
        Debug.Log($"{LogPrefix} {(created ? "新建" : "更新")}定义 {definitionPath}。", definition);
        return definition;
    }

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
