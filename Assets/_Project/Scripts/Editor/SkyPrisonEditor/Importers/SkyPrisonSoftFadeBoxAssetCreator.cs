using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 创建「柔边雾箱」这一套资产：黑/白两份材质 + 共用预制体 + 两份地形装饰物定义。
///
/// 用途：给地图边界做"这里进不去"的视觉提示，跟空气墙分工不同——空气墙管碰撞、
/// 游戏里不渲染；这个箱子纯视觉，六个面各向同性柔和淡出到透明，中心不透明纯色。
/// 两者摆在同一位置配合使用，也可以单独用。
///
/// 纯视觉，不参与碰撞（collisionMode = None）——挡人的活留给空气墙，这里只负责
/// "让人看懂为什么挡"。默认在游戏里可见（不是 editorOnlyVisual），这是它存在的意义。
///
/// 幂等：资产已存在就只补字段，不重建，避免覆盖美术后来的调整。
/// </summary>
public static class SkyPrisonSoftFadeBoxAssetCreator
{
    private const string LogPrefix = "[SkyPrison SoftFadeBox]";

    private const string ShaderPath = "Assets/_Project/Art/Shaders/Custom/Map/SkyPrisonSoftFadeBox.shader";
    private const string BlackMaterialPath = "Assets/_Project/Art/Materials/System/M_SoftFadeBox_Black.mat";
    private const string WhiteMaterialPath = "Assets/_Project/Art/Materials/System/M_SoftFadeBox_White.mat";
    private const string PrefabFolder = "Assets/_Project/Data/Definitions/Core/SoftFadeBox";
    private const string BlackPrefabPath = PrefabFolder + "/PF_SoftFadeBox_Black.prefab";
    private const string WhitePrefabPath = PrefabFolder + "/PF_SoftFadeBox_White.prefab";
    private const string BlackDefinitionPath = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations/TD_SoftFadeBox_Black.asset";
    private const string WhiteDefinitionPath = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations/TD_SoftFadeBox_White.asset";

    // 基础尺寸——摆到地图上时的默认大小，美术可以在Inspector里直接改Transform.scale
    // 调宽高深（着色器按物体自己的scale归一化，不需要额外传参数）。
    private static readonly Vector3 DefaultScale = new Vector3(6f, 4f, 2f);

    [MenuItem("天空囚笼/地图/创建或修复「柔边雾箱」资产", false, 112)]
    public static void CreateOrRepair()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} 找不到着色器 {ShaderPath}，先确认它已经编译成功。");
            return;
        }

        // 清掉旧版共用预制体——黑白定义曾经共用它，材质写死成黑色，导致"白箱"
        // 摆出来也是黑的。分成两个独立预制体后这个文件就没用了，留着容易被误用。
        const string LegacySharedPrefabPath = PrefabFolder + "/PF_SoftFadeBox.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LegacySharedPrefabPath) != null)
        {
            AssetDatabase.DeleteAsset(LegacySharedPrefabPath);
            Debug.Log($"{LogPrefix} 已删除废弃的共用预制体 {LegacySharedPrefabPath}。");
        }

        Material blackMat = EnsureMaterial(BlackMaterialPath, shader, Color.black);
        Material whiteMat = EnsureMaterial(WhiteMaterialPath, shader, Color.white);

        // 黑白各一个独立预制体——之前共用一个预制体、材质写死在它的MeshRenderer上，
        // 两份定义都指向同一个预制体，导致摆"白箱"时实际生成的还是那份带黑色材质
        // 的预制体，定义里挑的颜色根本没被用上。分开之后材质各自绑死在自己的预制体，
        // 不会再互相覆盖。
        GameObject blackPrefab = EnsurePrefab(BlackPrefabPath, "PF_SoftFadeBox_Black", blackMat);
        GameObject whitePrefab = EnsurePrefab(WhitePrefabPath, "PF_SoftFadeBox_White", whiteMat);

        TerrainDecorationDefinition blackDef = EnsureDefinition(
            BlackDefinitionPath, blackPrefab, blackMat, "soft_fade_box_black", "柔边雾箱（黑）");
        TerrainDecorationDefinition whiteDef = EnsureDefinition(
            WhiteDefinitionPath, whitePrefab, whiteMat, "soft_fade_box_white", "柔边雾箱（白）");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.objects = new Object[] { blackDef, whiteDef };
        Debug.Log($"{LogPrefix} 完成。黑：{BlackDefinitionPath}，白：{WhiteDefinitionPath}");
    }

    private static Material EnsureMaterial(string path, Shader shader, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        Material material = new Material(shader);
        material.name = Path.GetFileNameWithoutExtension(path);
        material.SetColor("_Color", color);

        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateAsset(material, path);
        Debug.Log($"{LogPrefix} 新建材质 {path}。");
        return material;
    }

    private static GameObject EnsurePrefab(string prefabPath, string objectName, Material material)
    {
        // World3D——照着空气墙创建脚本抄结构时漏改的一处：空气墙故意留在 Default(0)
        // 层，靠"四台相机的 cullingMask 都不含第0位"让它游戏里不渲染；这个箱子
        // 是要让人看见的，必须用真正会被渲染的层，否则跟着色器/尺寸完全无关，
        // 表现就是"怎么调都是透明的"——实测就是这么栽的。
        int visibleLayer = LayerMask.NameToLayer("World3D");
        if (visibleLayer < 0)
        {
            Debug.LogWarning($"{LogPrefix} 找不到 World3D 层，退回 Default(0)——" +
                              "这个箱子会跟空气墙一样在游戏里不可见。");
            visibleLayer = 0;
        }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            // 图层/材质是基础设施，不是美术会去手调的内容，已存在的资产也要强制
            // 修正——跟"幂等=不覆盖美术调整"的原则不冲突，这类字段本来就不该留给
            // 幂等检查放过。
            var existingRenderer = existing.GetComponent<MeshRenderer>();
            bool layerWrong = existing.layer != visibleLayer;
            bool materialWrong = existingRenderer != null && existingRenderer.sharedMaterial != material;

            if (layerWrong || materialWrong)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                root.layer = visibleLayer;
                var renderer = root.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log($"{LogPrefix} 已修正 {prefabPath}" +
                          (layerWrong ? "（图层 → World3D）" : "") +
                          (materialWrong ? "（材质 → " + material.name + "）" : "") + "。");
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            return existing;
        }

        EnsureFolder(PrefabFolder);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.layer = visibleLayer;

        // 纯视觉，不参与碰撞——跟空气墙分工不同，这里删掉primitive自带的Collider。
        Collider primitiveCollider = cube.GetComponent<Collider>();
        if (primitiveCollider != null)
            Object.DestroyImmediate(primitiveCollider);

        MeshRenderer meshRenderer = cube.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        cube.transform.localScale = DefaultScale;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(cube, prefabPath);
        Object.DestroyImmediate(cube);

        Debug.Log($"{LogPrefix} 新建预制体 {prefabPath}（默认尺寸 {DefaultScale}，图层 World3D，材质 {material.name}）。");
        return prefab;
    }

    private static TerrainDecorationDefinition EnsureDefinition(
        string definitionPath, GameObject prefab, Material material, string decorationId, string displayName)
    {
        TerrainDecorationDefinition definition =
            AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(definitionPath);

        bool created = false;
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<TerrainDecorationDefinition>();
            EnsureFolder(Path.GetDirectoryName(definitionPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(definition, definitionPath);
            created = true;
        }

        definition.decorationId = decorationId;
        definition.displayName = displayName;
        definition.category = TerrainDecorationCategory.Custom;
        definition.subCategory = "系统工具";

        definition.sortPriority = -999; // 空气墙(-1000)之后紧接着，放置列表里挨在一起
        definition.editorOnlyVisual = false; // 关键区别：游戏里要看得见，这是它存在的意义

        if (definition.variants == null)
            definition.variants = new List<TerrainDecorationVariant>();
        if (definition.variants.Count == 0)
            definition.variants.Add(new TerrainDecorationVariant());
        definition.variants[0].variantId = "default";
        definition.variants[0].displayName = "默认版本";
        definition.variants[0].prefab = prefab;

        // 2026-08-14：改成自带碰撞，照空气墙的设置抄——用户明确要求这个箱子自己
        // 就要挡人，不是必须跟空气墙叠放才行。
        //
        // 这里跟下面的 allowCollisionOverlap 是两件完全独立的事：
        //   collisionMode/blockPlayer 等——生成出来的实例在游戏里"实际挡不挡"，
        //   allowCollisionOverlap    ——编辑器摆放这一刻"准不准许放在跟别的东西
        //                               重叠的位置"（摆放校验，不影响运行时）。
        // 两者互不冲突：摆放时依然可以自由穿墙/穿地形/穿其它装饰物找位置，
        // 但一旦摆下去，生成的碰撞体积跟视觉包围盒一样大，真实挡人。
        definition.collisionMode = TerrainDecorationCollisionMode.Box;
        definition.blockPlayer = true;
        definition.blockEnemy = true;
        definition.blockProjectile = true;
        definition.blockVision = false; // 不挡视线——它在虚构层面不是真实存在的墙
        definition.walkableSurface = null;

        // 摆放时允许穿插——这是给"贴着墙/穿墙覆盖边界"用的视觉标记，本来就该能
        // 自由穿过地形、建筑、其它装饰物摆放，不该被"是否与已有碰撞重叠"这条
        // 摆放校验拦住。
        definition.allowCollisionOverlap = true;

        // 参与前景遮挡——玩家走到箱子后面时身体要被正确遮住/露出。
        //
        // 之前只开了 generateOccluderProbeCollider 一个开关，实测角色依然没被遮挡。
        // 真正决定"参与不参与遮挡"的是 occlusionMode（默认 None，之前完全没设过），
        // generateOccluderProbeCollider 只是配合它生成探测碰撞体，两个都要开——
        // 单独开后者没有意义，SkyPrisonFrontOccluderAudit 里那个"给需要的定义打开
        // 遮挡探测碰撞体"菜单专门就是修「occlusionMode≠None 但 collisionMode=None
        // 时没有靶子」这类漏配的，今天项目里已经有三个定义踩过同一个坑。
        //
        // FrontBack：角色走到后面身体正确挡住/露出（今天全天调的叉车用的就是这个）。
        // FadeWhenBlockingPlayer 是反过来的效果（遮挡物挡镜头时自己变淡），不是要的。
        definition.occlusionMode = TerrainDecorationOcclusionMode.FrontBack;
        definition.generateOccluderProbeCollider = true;

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
