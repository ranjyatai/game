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

    // 1/4 圆弧墙：中线半径 4 米、厚 0.5 米、高 4 米，和通用墙体同厚同高，能和直墙首尾拼接。
    // 轴心在圆心（地面），弧段占 +X → +Z 这一象限：把圆心吸附到网格交点，弧的两端正好落在
    // 圆心正右方 / 正前方 4 米处，转 90° 即可拼出四个象限的整圆。
    // 网格是程序生成的，存成 Mesh 资产；Mesh 碰撞和遮挡探测体都按这份网格走。
    private const string ArcWallMeshPath = PrefabFolder + "/Mesh_Graybox_ArcWall.asset";
    private const string ArcWallPrefabPath = PrefabFolder + "/PF_Graybox_ArcWall.prefab";
    private const string ArcWallDefinitionPath = DefinitionFolder + "/TD_Graybox_ArcWall.asset";
    private const float ArcWallCenterRadius = 4f;
    private const float ArcWallThickness = 0.5f;
    private const float ArcWallHeight = 4f;
    private const int ArcWallSegments = 24;

    // 圆形高台：圆柱体底座 4×2×4（直径 4、高 2 米，轴心在底面中心），和「白模·高台」同一套尺寸约定，缩放即可得到任意直径和高度。
    private const string RoundPlatformPrefabPath = PrefabFolder + "/PF_Graybox_RoundPlatform.prefab";
    private const string RoundPlatformDefinitionPath = DefinitionFolder + "/TD_Graybox_RoundPlatform.asset";
    private static readonly Vector3 RoundPlatformSize = new Vector3(4f, 2f, 4f);

    // 30° 圆弧墙瓣：中线半径 34 米（对准中央大厅 68 米直径的圆形高台）、厚 0.5、高 4，轴心在圆心。
    // 弧段从 +X 转向 +Z 共 30°；12 瓣放在同一点、依次绕 Y 轴转 30°（yaw = -k*30°）就拼成整圈，
    // 删掉哪几瓣哪里就是门。不能直接用 1/4 圆弧墙放大：它的厚度会跟着半径一起放大。
    private const string ArcWall30MeshPath = PrefabFolder + "/Mesh_Graybox_ArcWall30_R34.asset";
    private const string ArcWall30PrefabPath = PrefabFolder + "/PF_Graybox_ArcWall30_R34.prefab";
    private const string ArcWall30DefinitionPath = DefinitionFolder + "/TD_Graybox_ArcWall30_R34.asset";
    private const float ArcWall30CenterRadius = 34f;
    private const float ArcWall30AngleDegrees = 30f;
    private const int ArcWall30Segments = 8;

    // 斜坡 + 高台：配套使用。斜坡宽 4 × 水平长 6 × 高 2 米（坡度约 18°，角色走着舒服，
    // 控制器的物理地面坡度上限是 58°、可走坡度 45°）；高台 4 × 4 米、高 2 米，和斜坡最高处等高，
    // 放在斜坡升高的那一端（+Z）正好接上。轴心都在底面中心；统一缩放时两者仍然等高。
    private const string RampMeshPath = PrefabFolder + "/Mesh_Graybox_Ramp.asset";
    private const string RampPrefabPath = PrefabFolder + "/PF_Graybox_Ramp.prefab";
    private const string RampDefinitionPath = DefinitionFolder + "/TD_Graybox_Ramp.asset";
    private const string PlatformPrefabPath = PrefabFolder + "/PF_Graybox_Platform.prefab";
    private const string PlatformDefinitionPath = DefinitionFolder + "/TD_Graybox_Platform.asset";
    private const float RampWidth = 4f;
    private const float RampLength = 6f;
    private const float RampHeight = 2f;
    private static readonly Vector3 PlatformSize = new Vector3(4f, 2f, 4f);

    // 楼梯：外观是一级级的台阶，碰撞用斜坡楔形——项目的物理地面只认很小的台阶（maxPhysicsGroundStepUpHeight），
    // 真台阶的竖直立面会被当墙绊脚，所以脚踩的必须是斜面。尺寸和斜坡完全一致（4×6×2，沿 +Z 升高），
    // 同样可以用缩放改宽、改长、改高；5 级时每级高 = 总高/5。
    // 预制体里：Mesh_VisualOnly（台阶，只显示，名字以 _VisualOnly 结尾，Builder 不为它生成碰撞）
    //          CollisionProxy（斜坡网格，只有 MeshFilter 没有 Renderer，不显示但生成碰撞）。
    private const string StairsMeshPath = PrefabFolder + "/Mesh_Graybox_Stairs5.asset";
    private const string StairsPrefabPath = PrefabFolder + "/PF_Graybox_Stairs.prefab";
    private const string StairsDefinitionPath = DefinitionFolder + "/TD_Graybox_Stairs.asset";
    private const int StairsStepCount = 5;

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

        GameObject arcWallPrefab = EnsureArcWallPrefab(material);
        TerrainDecorationDefinition arcWallDef = EnsureDefinition(
            ArcWallDefinitionPath, arcWallPrefab, "graybox_arc_wall", "白模·1/4圆弧墙", -994);

        GameObject rampPrefab = EnsureMeshPrefab(
            RampPrefabPath, "PF_Graybox_Ramp", RampMeshPath, () => BuildRampMesh(RampWidth, RampLength, RampHeight), material);
        TerrainDecorationDefinition rampDef = EnsureDefinition(
            RampDefinitionPath, rampPrefab, "graybox_ramp", "白模·斜坡", -993);

        GameObject platformPrefab = EnsurePrefab(PlatformPrefabPath, "PF_Graybox_Platform", PrimitiveType.Cube, material, PlatformSize);
        TerrainDecorationDefinition platformDef = EnsureDefinition(
            PlatformDefinitionPath, platformPrefab, "graybox_platform", "白模·高台", -992);

        // 斜坡和高台是"能踩"的结构，必须填 walkableSurface：Motor 的身体扫掠只对挂了
        // GroundSurfaceMarker 的碰撞体放行可走的斜面/台沿，没有的话走到坡脚、坡顶接高台处
        // 会被当墙挡住，表现为每次上下坡都"绊一下"并掉速。
        ApplyWalkableSurface(rampDef);
        ApplyWalkableSurface(platformDef);
        EnsureStairsAssets(material);
        EnsureHallAssets(material);

        Material backdropMaterial = EnsureMaterial(shader, BackdropMaterialPath, backdrop: true);
        GameObject backdropPrefab = EnsurePrefab(BackdropBoxPrefabPath, "PF_Graybox_BackdropBox", PrimitiveType.Cube, backdropMaterial);
        TerrainDecorationDefinition backdropDef = EnsureDefinition(
            BackdropBoxDefinitionPath, backdropPrefab, "graybox_backdrop_box", "白模·远景体块", -996);
        ApplyBackdropRules(backdropDef);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.objects = new Object[] { boxDef, cylinderDef, wallDef, arcWallDef, rampDef, platformDef, backdropDef };
        Debug.Log($"{LogPrefix} 完成。长方体：{BoxDefinitionPath}，圆柱体：{CylinderDefinitionPath}，墙体：{WallDefinitionPath}，远景体块：{BackdropBoxDefinitionPath}");
    }

    private const string WalkableSurfacePath = "Assets/_Project/Data/Definitions/Custom/GroundSurfaceMaterials/GSM_白模地面.asset";

    private static void ApplyWalkableSurface(TerrainDecorationDefinition definition)
    {
        if (definition == null || definition.walkableSurface != null)
            return; // 已有的不覆盖，地图作者可能换成了别的地表材质。

        var surface = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(WalkableSurfacePath);
        if (surface == null)
        {
            Debug.LogWarning($"{LogPrefix} 找不到 {WalkableSurfacePath}，{definition.name} 没有填 walkableSurface，上下坡可能会绊脚。");
            return;
        }

        definition.walkableSurface = surface;
        EditorUtility.SetDirty(definition);
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

    private static GameObject EnsureArcWallPrefab(Material material)
        => EnsureArcPrefab(material, ArcWallMeshPath, ArcWallPrefabPath, "PF_Graybox_ArcWall",
            ArcWallCenterRadius, 90f, ArcWallSegments, "1/4 圆弧墙");

    /// <summary>楼梯条目。单独暴露出来，方便只补这一个条目而不重跑整套「创建或修复」。</summary>
    public static void EnsureStairsAssets()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} 找不到着色器 {ShaderPath}，先确认它已经编译成功。");
            return;
        }

        EnsureStairsAssets(EnsureMaterial(shader));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureStairsAssets(Material material)
    {
        GameObject stairsPrefab = EnsureStairsPrefab(material);
        TerrainDecorationDefinition stairsDef = EnsureDefinition(
            StairsDefinitionPath, stairsPrefab, "graybox_stairs", "白模·楼梯", -991);
        ApplyWalkableSurface(stairsDef);
    }

    private static GameObject EnsureStairsPrefab(Material material)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StairsPrefabPath);
        if (existing != null)
            return existing;

        int visibleLayer = LayerMask.NameToLayer("World3D");
        if (visibleLayer < 0)
            visibleLayer = 0;

        EnsureFolder(PrefabFolder);

        Mesh stairsMesh = AssetDatabase.LoadAssetAtPath<Mesh>(StairsMeshPath);
        if (stairsMesh == null)
        {
            stairsMesh = BuildStairsMesh(RampWidth, RampLength, RampHeight, StairsStepCount);
            stairsMesh.name = Path.GetFileNameWithoutExtension(StairsMeshPath);
            AssetDatabase.CreateAsset(stairsMesh, StairsMeshPath);
        }

        Mesh rampMesh = AssetDatabase.LoadAssetAtPath<Mesh>(RampMeshPath);
        if (rampMesh == null)
        {
            rampMesh = BuildRampMesh(RampWidth, RampLength, RampHeight);
            rampMesh.name = Path.GetFileNameWithoutExtension(RampMeshPath);
            AssetDatabase.CreateAsset(rampMesh, RampMeshPath);
        }

        var root = new GameObject("PF_Graybox_Stairs") { layer = visibleLayer };

        var visual = new GameObject("Mesh_VisualOnly") { layer = visibleLayer };
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = stairsMesh;
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        var collision = new GameObject("CollisionProxy") { layer = visibleLayer };
        collision.transform.SetParent(root.transform, false);
        collision.AddComponent<MeshFilter>().sharedMesh = rampMesh;   // 没有 Renderer：不显示，但 Builder 会按它生成碰撞

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, StairsPrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"{LogPrefix} 新建预制体 {StairsPrefabPath}（外观台阶 {StairsStepCount} 级，碰撞是斜坡楔形）。");
        return prefab;
    }

    /// <summary>
    /// 台阶网格：底面宽 × 长，沿 +Z 分 steps 级升到 height，和斜坡网格的外包尺寸、轴心完全一致。
    /// 每个面独立顶点、法线平整。
    /// </summary>
    private static Mesh BuildStairsMesh(float width, float length, float height, int steps)
    {
        float hx = width * 0.5f, hz = length * 0.5f;
        float dz = length / steps, dh = height / steps;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // 四边形 (a,b,c,d)，n 是期望法线：绕序反了就翻转。
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f)
            {
                Vector3 t = b; b = d; d = t;
            }

            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(n);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.one); uvs.Add(Vector2.up);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
        }

        for (int i = 0; i < steps; i++)
        {
            float z0 = -hz + i * dz, z1 = z0 + dz, h0 = i * dh, h1 = (i + 1) * dh;
            Quad(new Vector3(-hx, h1, z0), new Vector3(-hx, h1, z1), new Vector3(hx, h1, z1), new Vector3(hx, h1, z0), Vector3.up);        // 踏面
            Quad(new Vector3(-hx, h0, z0), new Vector3(hx, h0, z0), new Vector3(hx, h1, z0), new Vector3(-hx, h1, z0), Vector3.back);      // 立面（朝 -Z，迎着上楼的人）
            Quad(new Vector3(hx, 0f, z0), new Vector3(hx, 0f, z1), new Vector3(hx, h1, z1), new Vector3(hx, h1, z0), Vector3.right);       // +X 侧
            Quad(new Vector3(-hx, 0f, z0), new Vector3(-hx, 0f, z1), new Vector3(-hx, h1, z1), new Vector3(-hx, h1, z0), Vector3.left);    // -X 侧
        }

        Quad(new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), new Vector3(hx, height, hz), new Vector3(-hx, height, hz), Vector3.forward); // 背面（+Z）
        Quad(new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz), Vector3.down);         // 底面

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>圆形高台 + 30° 圆弧墙瓣。单独暴露出来，方便只补这两个条目而不重跑整套「创建或修复」。</summary>
    public static void EnsureHallAssets()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} 找不到着色器 {ShaderPath}，先确认它已经编译成功。");
            return;
        }

        EnsureHallAssets(EnsureMaterial(shader));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureHallAssets(Material material)
    {
        GameObject roundPrefab = EnsurePrefab(
            RoundPlatformPrefabPath, "PF_Graybox_RoundPlatform", PrimitiveType.Cylinder, material, RoundPlatformSize);
        TerrainDecorationDefinition roundDef = EnsureDefinition(
            RoundPlatformDefinitionPath, roundPrefab, "graybox_round_platform", "白模·圆形高台", -991);
        // 和方形高台一样是「能踩」的结构，必须填 walkableSurface（原因见上面方形高台处的说明）。
        ApplyWalkableSurface(roundDef);

        GameObject arcPrefab = EnsureArcPrefab(material, ArcWall30MeshPath, ArcWall30PrefabPath, "PF_Graybox_ArcWall30_R34",
            ArcWall30CenterRadius, ArcWall30AngleDegrees, ArcWall30Segments, "30° 圆弧墙瓣");
        EnsureDefinition(
            ArcWall30DefinitionPath, arcPrefab, "graybox_arc_wall_30_r34", "白模·圆弧墙30°（R34）", -990);
    }

    private static GameObject EnsureArcPrefab(Material material, string meshPath, string prefabPath, string objectName,
        float centerRadius, float angleDegrees, int segments, string label)
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

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = BuildArcWallMesh(
                centerRadius - ArcWallThickness * 0.5f,
                centerRadius + ArcWallThickness * 0.5f,
                ArcWallHeight,
                angleDegrees * Mathf.Deg2Rad,
                segments);
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
        }

        var root = new GameObject(objectName) { layer = visibleLayer };
        var visual = new GameObject("Mesh") { layer = visibleLayer };
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"{LogPrefix} 新建预制体 {prefabPath}（{label}，中线半径 {centerRadius} 米，厚 {ArcWallThickness} 米，高 {ArcWallHeight} 米，轴心在圆心）。");
        return prefab;
    }

    /// <summary>
    /// 圆心在原点、底面在 y=0、弧段从 +X 转向 +Z、张角为 arcRadians 的圆环柱（90° 即 1/4 圆环柱）。
    /// 内外侧面用沿弧平滑的法线，顶/底/两端截面各自独立顶点保持平整。
    /// </summary>
    private static Mesh BuildArcWallMesh(float innerRadius, float outerRadius, float height, float arcRadians, int segments)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        Vector3 Dir(int i)
        {
            float a = (float)i / segments * arcRadians;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            // 顶点顺序 a,b,c,d 为沿四边形一圈；(a,b,c)+(a,c,d) 的法线 = cross(b-a, c-a)，朝向观察者。
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            normals.Add(na); normals.Add(nb); normals.Add(nc); normals.Add(nd);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
        }

        Vector3 up = Vector3.up * height;
        for (int i = 0; i < segments; i++)
        {
            Vector3 d0 = Dir(i), d1 = Dir(i + 1);
            float u0 = (float)i / segments * arcRadians * outerRadius;
            float u1 = (float)(i + 1) / segments * arcRadians * outerRadius;

            // 外侧面（朝外）
            AddQuad(d0 * outerRadius, d0 * outerRadius + up, d1 * outerRadius + up, d1 * outerRadius,
                d0, d0, d1, d1,
                new Vector2(u0, 0f), new Vector2(u0, height), new Vector2(u1, height), new Vector2(u1, 0f));
            // 内侧面（朝圆心）
            AddQuad(d0 * innerRadius, d1 * innerRadius, d1 * innerRadius + up, d0 * innerRadius + up,
                -d0, -d1, -d1, -d0,
                new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, height), new Vector2(u0, height));
            // 顶面
            AddQuad(d0 * innerRadius + up, d1 * innerRadius + up, d1 * outerRadius + up, d0 * outerRadius + up,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
            // 底面
            AddQuad(d0 * innerRadius, d0 * outerRadius, d1 * outerRadius, d1 * innerRadius,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f));
        }

        // 起端截面（角度 0，朝 -Z）
        Vector3 s0 = Dir(0);
        AddQuad(s0 * innerRadius, s0 * innerRadius + up, s0 * outerRadius + up, s0 * outerRadius,
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            new Vector2(0f, 0f), new Vector2(0f, height), new Vector2(1f, height), new Vector2(1f, 0f));
        // 末端截面（角度 = arcRadians，朝弧的切线方向；90° 时就是 -X）
        Vector3 e0 = Dir(segments);
        Vector3 endNormal = new Vector3(-Mathf.Sin(arcRadians), 0f, Mathf.Cos(arcRadians));
        AddQuad(e0 * outerRadius, e0 * outerRadius + up, e0 * innerRadius + up, e0 * innerRadius,
            endNormal, endNormal, endNormal, endNormal,
            new Vector2(0f, 0f), new Vector2(0f, height), new Vector2(1f, height), new Vector2(1f, 0f));

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // 自检：每个三角形的绕序法线必须和顶点法线同向，否则这面在游戏里会被背面剔除掉。
        int wrong = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            Vector3 n = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]);
            Vector3 vn = normals[tris[t]] + normals[tris[t + 1]] + normals[tris[t + 2]];
            if (Vector3.Dot(n, vn) <= 0f)
                wrong++;
        }
        if (wrong > 0)
            Debug.LogError($"{LogPrefix} 圆弧墙网格有 {wrong} 个三角形绕序反了。");
        return mesh;
    }

    /// <summary>
    /// 程序生成网格的白模预制体：网格存成 Mesh 资产，预制体里一个 Mesh 子节点挂 MeshFilter + MeshRenderer。
    /// 已存在就直接复用，不覆盖后来的调整。
    /// </summary>
    private static GameObject EnsureMeshPrefab(string prefabPath, string objectName, string meshPath, System.Func<Mesh> buildMesh, Material material)
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

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = buildMesh();
            mesh.name = Path.GetFileNameWithoutExtension(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
        }

        var root = new GameObject(objectName) { layer = visibleLayer };
        var visual = new GameObject("Mesh") { layer = visibleLayer };
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"{LogPrefix} 新建预制体 {prefabPath}。");
        return prefab;
    }

    /// <summary>
    /// 斜坡楔形体：底面 宽 × 长，沿 +Z 从 0 升到 height。轴心在底面中心。
    /// 顶面、背面（+Z 竖直面）、底面、两侧三角形各自独立顶点，法线平整。
    /// </summary>
    private static Mesh BuildRampMesh(float width, float length, float height)
    {
        float hx = width * 0.5f, hz = length * 0.5f;
        Vector3 a = new Vector3(-hx, 0f, -hz);
        Vector3 b = new Vector3(hx, 0f, -hz);
        Vector3 c = new Vector3(hx, 0f, hz);
        Vector3 d = new Vector3(-hx, 0f, hz);
        Vector3 e = new Vector3(-hx, height, hz);
        Vector3 f = new Vector3(hx, height, hz);

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // 三角形 (p0,p1,p2) 的法线 = cross(p1-p0, p2-p0)，朝向观察者。
        void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            int s = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2);
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.up);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            Tri(p0, p1, p2);
            Tri(p0, p2, p3);
        }

        Quad(a, e, f, b);   // 斜面（朝上、略朝 -Z）
        Quad(d, c, f, e);   // 背面（+Z 竖直面）
        Quad(a, b, c, d);   // 底面
        Tri(a, d, e);       // 左侧（-X）
        Tri(b, f, c);       // 右侧（+X）

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // 自检：每个三角形的绕序法线必须和相邻顶点法线同向（这里每个面独立顶点，法线就是绕序法线本身，
        // 所以改成检查"朝外"——法线点积（三角形中心 - 网格中心）必须为正），否则这面在游戏里会被背面剔除。
        Vector3 center = mesh.bounds.center;
        int wrong = 0;
        for (int t = 0; t < tris.Count; t += 3)
        {
            Vector3 tc = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f;
            if (Vector3.Dot(normals[tris[t]], tc - center) <= 0f)
                wrong++;
        }
        if (wrong > 0)
            Debug.LogError($"{LogPrefix} 斜坡网格有 {wrong} 个三角形朝向反了。");
        return mesh;
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
