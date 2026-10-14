using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 创建「空气墙」这一套资产：材质 + 预制体 + 地形装饰物定义。
///
/// 空气墙是纯工具物件，不是美术资产：
/// - Scene 里画成半透明洋红立方体，用来把挡人的体积可视化
/// - VisualRoot 留在 Default(0) 层。本项目四台相机的 cullingMask 都不含第 0 位
///   （Main=World3D+Character2D，GamePlay=UI+FogOfWar，OcclusionMask=OcclusionMask，
///   OverheadUI=OverheadUI），所以游戏里一帧都不渲染；Scene 视图有自己的层显示开关，照常可见
/// - 碰撞盒由 Builder 自动贴合视觉包围盒——视觉存在的意义就是把体积画出来，
///   两者要是能不一致，摆放时看到的范围就是假的
/// - sortPriority 设成负数，在放置列表里置顶
///
/// 幂等：资产已存在就只补字段，不重建，避免覆盖美术后来的调整。
/// </summary>
public static class SkyPrisonAirWallAssetCreator
{
    private const string LogPrefix = "[SkyPrison AirWall]";

    private const string MaterialPath = "Assets/_Project/Art/Materials/System/M_AirWall_EditorOnly.mat";
    private const string PrefabFolder = "Assets/_Project/Data/Definitions/Core/AirWall";
    private const string PrefabPath = PrefabFolder + "/PF_AirWall.prefab";
    private const string DefinitionPath = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations/TD_AirWall.asset";

    private static readonly Color AirWallColor = new Color(1f, 0f, 1f, 0.35f);

    [MenuItem("天空囚笼/地图/创建或修复「空气墙」资产", false, 111)]
    public static void CreateOrRepair()
    {
        Material material = EnsureMaterial();
        GameObject prefab = EnsurePrefab(material);
        TerrainDecorationDefinition definition = EnsureDefinition(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = definition;
        Debug.Log($"{LogPrefix} 完成。定义：{DefinitionPath}", definition);
    }

    private static Material EnsureMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null)
            return existing;

        // Unlit 而不是 Lit：这东西是标记，不该被隧道里的灯影响明暗，
        // 否则在暗处会看不见、亮处又过曝，失去"一眼看到体积"的作用。
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader);
        material.name = Path.GetFileNameWithoutExtension(MaterialPath);
        ApplyTransparentSettings(material);

        EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
        AssetDatabase.CreateAsset(material, MaterialPath);
        Debug.Log($"{LogPrefix} 新建材质 {MaterialPath}。");
        return material;
    }

    private static void ApplyTransparentSettings(Material material)
    {
        // URP 的透明不是设个 alpha 就行，要同时改 Surface Type、混合因子、ZWrite
        // 和渲染队列，还要打开对应 keyword，少一样都会渲染成不透明。
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f); // Transparent
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);   // Alpha
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", AirWallColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", AirWallColor);
    }

    private static GameObject EnsurePrefab(Material material)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
            return existing;

        EnsureFolder(PrefabFolder);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "PF_AirWall";
        cube.layer = 0; // Default——游戏里不渲染的关键

        // 碰撞归 Builder 的 CollisionRoot 管，视觉子树一律不参与碰撞
        // （同 DisableCollidersUnder 的约定）。这里直接删掉 primitive 自带的。
        Collider primitiveCollider = cube.GetComponent<Collider>();
        if (primitiveCollider != null)
            Object.DestroyImmediate(primitiveCollider);

        MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            // 不投影、不接受影子、不进光照探针——它在游戏里根本不存在，
            // 留着这些只会让烘焙和实时阴影多算一份。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(cube, PrefabPath);
        Object.DestroyImmediate(cube);

        Debug.Log($"{LogPrefix} 新建预制体 {PrefabPath}。");
        return prefab;
    }

    private static TerrainDecorationDefinition EnsureDefinition(GameObject prefab)
    {
        TerrainDecorationDefinition definition =
            AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(DefinitionPath);

        bool created = false;
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<TerrainDecorationDefinition>();
            EnsureFolder(Path.GetDirectoryName(DefinitionPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            created = true;
        }

        definition.decorationId = "air_wall";
        definition.displayName = "空气墙";
        definition.category = TerrainDecorationCategory.Custom;
        definition.subCategory = "系统工具";

        definition.sortPriority = -1000;   // 置顶
        definition.editorOnlyVisual = true;

        // 预制体只挂在变体上——TerrainDecorationDefinition 本身没有 prefab 字段。
        if (definition.variants == null)
            definition.variants = new List<TerrainDecorationVariant>();
        if (definition.variants.Count == 0)
            definition.variants.Add(new TerrainDecorationVariant());
        definition.variants[0].variantId = "default";
        definition.variants[0].displayName = "默认版本";
        definition.variants[0].prefab = prefab;

        // 碰撞：Box 模式，尺寸由 Builder 按视觉包围盒自动写入（editorOnlyVisual 分支）。
        definition.collisionMode = TerrainDecorationCollisionMode.Box;
        definition.blockPlayer = true;
        definition.blockEnemy = true;
        definition.blockProjectile = true;   // 空气墙挡子弹，否则玩家能隔墙打到不该打的地方
        definition.blockVision = false;      // 但不挡视线——它在虚构层面不存在
        definition.walkableSurface = null;   // 不是能踩的表面，不产生脚步声

        EditorUtility.SetDirty(definition);
        Debug.Log($"{LogPrefix} {(created ? "新建" : "更新")}定义 {DefinitionPath}。", definition);
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
