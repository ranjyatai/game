using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 旧工厂白模 v9（屋顶平面图 v8 的落地版）：600×260 米，北 = +Z，左右 = 横版战斗方向。
/// - 空地（街道/院子）= 一整块实心厂区里挖出来的；厂区之外刷「虚空」。
/// - 每栋建筑按自己的轮廓围墙，门洞处断开；围栏沿地面边界自动生成，用「白模·围栏」定义（挡单位，不挡视线和子弹）。
/// - 只摆体量：购买点 / 撤离点 B C / 新手出生点是占位物；南门 RAID 落点 + 撤离点 A 可用；内厂区 4 道钥匙门 = 2 扇升降门挂 old_factory_gate_key。
/// 菜单「天空囚笼/地图/生成旧工厂白模」会单独打开旧工厂场景重新生成（先删掉上一次生成的白模组）。
/// 当前场景有未保存改动时直接中止，不擅自保存或丢弃。
/// </summary>
public static class SkyPrisonOldFactoryWhiteboxBuilder
{
    private const string MapPath = "Assets/_Project/Maps/Raid/OldFactory/Old Factory/MD_Old Factory.asset";
    private const string ScenePath = "Assets/_Project/Maps/Raid/OldFactory/Old Factory/Old Factory.unity";
    private const string RaidPath = "Assets/_Project/Data/Raid/RAID_01_OldFactory.asset";
    private const string PlayerUnitPath = "Assets/_Project/Data/Definitions/Custom/Units/NPC/UD_Player_Axia_01.asset";
    private const string TdFolder = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations/";
    private const string LayerFolder = "Assets/_Project/Data/TerrainLayers/GroundSurface/";
    private const string SurfaceFolder = "Assets/_Project/Data/Definitions/Custom/GroundSurfaceMaterials/";
    private const string DaySkyboxPath = "Assets/_Project/Art/Environment/Skybox/Skybox_Normal_Day.mat";
    private const string GroupName = "旧工厂白模_v9";
    private const string GateKeyFlag = "old_factory_gate_key";
    private const string Log = "[旧工厂白模]";

    // 栅格：1 米一格，覆盖地图边界 600×420（z -284..136，南边是公路和高速）
    private const int GX0 = -300, GZ0 = -284, GW = 600, GH = 420;
    private static readonly Vector3 ArrivalPos = new Vector3(0f, 0f, -100f);

    // ── 数据 ────────────────────────────────────────────────────────────
    // 地面（可走）：x0, z0, x1, z1
    private static readonly float[][] GroundRects =
    {
        new float[] { -285, -4, -45, 16 }, new float[] { -52, 6, 52, 26 }, new float[] { 45, -4, 285, 16 },   // 主街（中段北移成中央广场）
        new float[] { -285, -62, -215, 125 }, new float[] { 230, -62, 285, 125 },                              // 左 / 右翼院
        new float[] { -215, 62, -50, 84 }, new float[] { 50, 62, 230, 84 },                                    // 后排巷
        new float[] { -45, -56, 45, 6 }, new float[] { -14, -115, 14, -56 }, new float[] { -80, -98, -14, -88 }, // 起点院 / 南门通道 / 前排小路
        new float[] { -110, -70, -98, -4 }, new float[] { 45, -46, 150, -4 },                                  // 塌陷仓库小巷 / 东院
        new float[] { -140, 16, -128, 22 }, new float[] { -120, 16, -108, 22 }, new float[] { -100, 16, -88, 22 }, new float[] { 172, 16, 202, 24 }, // 装卸凹口
    };
    // 围栏外的不可进空地（只刷地面，不可走）
    private static readonly float[][] FencedRects =
    {
        new float[] { -52, 26, -40, 62 }, new float[] { 40, 26, 52, 62 },
        new float[] { -215, 84, -50, 125 }, new float[] { 50, 84, 230, 125 },
    };

    private class Bld { public string name; public float h; public Vector2[] poly; }
    private static Vector2[] V(params float[] c) { var a = new Vector2[c.Length / 2]; for (int i = 0; i < a.Length; i++) a[i] = new Vector2(c[2 * i], c[2 * i + 1]); return a; }
    private static Vector2[] Rect(float x0, float z0, float x1, float z1) => V(x0, z0, x1, z0, x1, z1, x0, z1);
    private static readonly Bld[] Buildings =
    {
        new Bld { name = "变电站", h = 10, poly = V(-215,16, -152,16, -152,46, -168,46, -168,62, -198,62, -198,52, -215,52) },
        new Bld { name = "仓储区", h = 14, poly = V(-150,16, -140,16, -140,22, -128,22, -128,16, -120,16, -120,22, -108,22, -108,16, -100,16, -100,22, -88,22, -88,16, -52,16, -52,62, -84,62, -84,70, -110,70, -110,62, -150,62) },
        new Bld { name = "封存车间", h = 14, poly = V(-40,26, 40,26, 40,44, 30,44, 30,54, -22,54, -22,48, -40,48) },
        new Bld { name = "管道厂房", h = 12, poly = V(52,16, 122,16, 134,11, 150,11, 150,62, 122,62, 122,54, 84,54, 84,62, 52,62) },
        new Bld { name = "重型机械区", h = 18, poly = V(150,16, 172,16, 172,24, 202,24, 202,16, 230,16, 230,54, 222,62, 206,62, 206,70, 172,70, 172,62, 150,62) },
        new Bld { name = "核心反应炉", h = 28, poly = V(-50,70, -38,62, 38,62, 50,70, 50,114, 38,125, -38,125, -50,114) },
        new Bld { name = "锅炉房", h = 16, poly = V(-215,-4, -150,-4, -150,-18, -172,-55, -215,-55) },
        new Bld { name = "维修库·西", h = 8, poly = V(-150,-4, -110,-4, -110,-50, -134,-50, -134,-58, -150,-58) },
        new Bld { name = "维修库·东", h = 8, poly = V(-98,-4, -45,-4, -45,-14, -33,-28, -45,-42, -58,-42, -58,-62, -98,-62) },
        new Bld { name = "冷凝车间", h = 10, poly = V(150,-4, 230,-4, 230,-46, 214,-62, 136,-62) },
        new Bld { name = "塌陷仓库", h = 12, poly = V(-130,-108, -80,-108, -80,-92, -96,-92, -96,-70, -140,-70) },
        new Bld { name = "门卫", h = 6, poly = V(14,-100, 36,-100, 36,-84, 24,-84, 14,-90) },
        new Bld { name = "值班室", h = 6, poly = Rect(-190,84, -170,102) },
        new Bld { name = "工具间", h = 6, poly = Rect(-122,84, -100,104) },
        new Bld { name = "配电柜间", h = 6, poly = Rect(90,84, 110,102) },
        new Bld { name = "储物间", h = 6, poly = Rect(170,84, 190,104) },
    };
    // 门洞（墙在此处断开）：x0, z0, x1, z1
    private static readonly float[][] Doors =
    {
        new float[] { -98,22, -90,22 }, new float[] { -195,16, -183,16 }, new float[] { 178,24, 190,24 }, new float[] { 180,70, 188,70 },
        new float[] { 26,26, 34,26 }, new float[] { -50,70, -50,82 }, new float[] { 50,70, 50,82 }, new float[] { -110,-70, -98,-70 }, new float[] { -80,-98, -80,-92 },
        new float[] { -183,84, -177,84 }, new float[] { -114,84, -108,84 }, new float[] { 97,84, 103,84 }, new float[] { 177,84, 183,84 },
        // 沿街的实心厂房也各开一扇门（v8 里画成实心，进游戏像一堵墙）
        new float[] { 96,16, 104,16 }, new float[] { -190,-4, -182,-4 }, new float[] { -134,-4, -126,-4 }, new float[] { -76,-4, -68,-4 }, new float[] { 186,-4, 194,-4 }, new float[] { 14,-96, 14,-90 },
        // 相邻厂房之间的连通门（共用墙两侧各一道，门洞要在两栋里都开）
        new float[] { 150,34, 150,42 },   // 管道厂房 ↔ 重型机械区
    };

    // ── 南边：从南门出去的蜿蜒公路，接东西向高速（主线在这里和队友会合上高速）──
    private static readonly Vector2[] RoadCtrl =
    {
        new Vector2(0,-115), new Vector2(0,-140), new Vector2(-50,-165), new Vector2(-120,-172), new Vector2(-185,-198), new Vector2(-170,-228),
        new Vector2(-100,-238), new Vector2(-30,-230), new Vector2(40,-232), new Vector2(95,-236), new Vector2(112,-244), new Vector2(112,-262),
    };
    private const float HighwayZ0 = -275f, HighwayZ1 = -249f, HighwayX = 295f;
    private static List<Vector2> roadPts; private static List<float> roadHW;

    private static void BuildRoadSamples()
    {
        roadPts = new List<Vector2>(); roadHW = new List<float>();
        int n = RoadCtrl.Length; float s = 0f; Vector2 prev = RoadCtrl[0];
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 p0 = RoadCtrl[Mathf.Max(i - 1, 0)], p1 = RoadCtrl[i], p2 = RoadCtrl[i + 1], p3 = RoadCtrl[Mathf.Min(i + 2, n - 1)];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 6f));
            for (int k = 0; k < steps; k++)
            {
                float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                Vector2 pt = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                s += Vector2.Distance(prev, pt); prev = pt;
                roadPts.Add(pt); roadHW.Add(0.5f * Mathf.Lerp(28f, 14f, Mathf.Clamp01(s / 30f)));
                if (pt.y <= HighwayZ1) return;
            }
        }
    }
    private static float SegDist(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 ab = b - a; float l2 = ab.sqrMagnitude; t = l2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
        return Vector2.Distance(p, a + ab * t);
    }

    private static TerrainDecorationDefinition wall, box, cylinder, platform, fence, liftGate;
    private static Transform groupRoot, gBuild, gFence, gProp, gMark;
    private static int placed;

    [MenuItem("天空囚笼/地图/生成旧工厂白模", false, 120)]
    public static void Run()
    {
        var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapPath);
        if (map == null) { Debug.LogError($"{Log} 找不到地图定义 {MapPath}。"); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning($"{Log} 运行中，没有执行。"); return; }
        var active = SceneManager.GetActiveScene();
        if (active.path != ScenePath && active.isDirty) { Debug.LogError($"{Log} 当前场景「{active.name}」有未保存改动，先保存再运行（没有改动任何东西）。"); return; }

        Scene scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        try
        {
            map.mapBoundsCenter = new Vector3(0f, 0f, -74f);
            map.mapBoundsSize = new Vector3(600f, 8f, 420f);
            EditorUtility.SetDirty(map);
            SkyPrisonMapEditorUtility.SyncMapBoundsToScene(map);
            map.environmentPreset = MapEnvironmentPreset.Day;
            map.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(DaySkyboxPath);
            SkyPrisonMapEnvironmentEditorUtility.ApplyEnvironmentToScene(map, scene, false);

            BuildGrids();
            PaintTerrain();
            int count = BuildObjects(scene);
            EnsureBuildSettings();
            SetRaidArrival();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Log} 完成：{count} 个白模物体，场景已保存，已加入 Build Settings。");
        }
        catch (System.Exception e) { Debug.LogError($"{Log} 失败，场景没有保存：{e}"); }
    }

    // ── 栅格 ────────────────────────────────────────────────────────────
    private static bool[,] gMask, bMask, bDil, fMask, rMask;
    private static bool In(int x, int z) => x >= 0 && z >= 0 && x < GW && z < GH;

    private static void BuildGrids()
    {
        gMask = new bool[GW, GH]; bMask = new bool[GW, GH]; bDil = new bool[GW, GH]; fMask = new bool[GW, GH]; rMask = new bool[GW, GH];
        BuildRoadSamples();
        foreach (var r in GroundRects) FillRect(gMask, r);
        foreach (var r in FencedRects) FillRect(fMask, r);
        foreach (var b in Buildings)
        {
            float minX = b.poly.Min(p => p.x), maxX = b.poly.Max(p => p.x), minZ = b.poly.Min(p => p.y), maxZ = b.poly.Max(p => p.y);
            for (int x = Mathf.FloorToInt(minX) - GX0; x <= Mathf.CeilToInt(maxX) - GX0; x++)
                for (int z = Mathf.FloorToInt(minZ) - GZ0; z <= Mathf.CeilToInt(maxZ) - GZ0; z++)
                    if (In(x, z) && PointIn(b.poly, x + GX0 + 0.5f, z + GZ0 + 0.5f)) bMask[x, z] = true;
        }
        for (int x = 0; x < GW; x++)
            for (int z = 0; z < GH; z++)
                if (bMask[x, z])
                    for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (In(x + dx, z + dz)) bDil[x + dx, z + dz] = true;
        for (int x = 0; x < GW; x++) for (int z = 0; z < GH; z++) if (bMask[x, z]) gMask[x, z] = false;   // 地面 = 街道 − 建筑
        // 公路 + 高速（只用来告诉围栏逻辑「这里不是不可走」；护栏另外沿路沿放）
        for (int i = 0; i < roadPts.Count - 1; i++)
        {
            Vector2 a = roadPts[i], b = roadPts[i + 1]; float hw = Mathf.Max(roadHW[i], roadHW[i + 1]) + 1f;
            for (int x = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - hw) - GX0; x <= Mathf.CeilToInt(Mathf.Max(a.x, b.x) + hw) - GX0; x++)
                for (int z = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - hw) - GZ0; z <= Mathf.CeilToInt(Mathf.Max(a.y, b.y) + hw) - GZ0; z++)
                {
                    if (!In(x, z)) continue; float t; float d = SegDist(new Vector2(x + GX0 + 0.5f, z + GZ0 + 0.5f), a, b, out t);
                    if (d <= Mathf.Lerp(roadHW[i], roadHW[i + 1], t)) rMask[x, z] = true;
                }
        }
        for (int x = (int)-HighwayX - GX0; x < (int)HighwayX - GX0; x++) for (int z = (int)HighwayZ0 - GZ0; z < (int)HighwayZ1 - GZ0; z++) if (In(x, z)) rMask[x, z] = true;
    }
    private static void FillRect(bool[,] m, float[] r)
    {
        for (int x = (int)r[0] - GX0; x < (int)r[2] - GX0; x++) for (int z = (int)r[1] - GZ0; z < (int)r[3] - GZ0; z++) if (In(x, z)) m[x, z] = true;
    }
    private static bool PointIn(Vector2[] poly, float px, float pz)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > pz) != (poly[j].y > pz) && px < (poly[j].x - poly[i].x) * (pz - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        return inside;
    }

    // ── 地形：整张图都是白模地面（墙外不涂虚空），公路和高速刷沥青 ──
    private static void PaintTerrain()
    {
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) throw new System.Exception("场景里没有 Terrain。");
        var td = terrain.terrainData;
        var gLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerFolder + "TL_graybox_ground.terrainlayer");
        var vLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerFolder + "TL_void.terrainlayer");
        var aLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerFolder + "TL_new_ground_surface_1_copy_copy.terrainlayer");
        var gSurf = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(SurfaceFolder + "GSM_白模地面.asset");
        var vSurf = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(SurfaceFolder + "GSM_虚空.asset");
        var aSurf = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(SurfaceFolder + "GSM_马路-柏油.asset");
        if (gLayer == null || vLayer == null || aLayer == null || gSurf == null || vSurf == null || aSurf == null) throw new System.Exception("找不到白模地面 / 虚空 / 柏油的地形层或地表材质。");

        td.terrainLayers = new[] { gLayer, vLayer, aLayer };     // 虚空层保留（权重全 0），免得解除绑定
        td.alphamapResolution = 2048;
        int w = td.alphamapWidth, h = td.alphamapHeight;
        Vector3 o = terrain.transform.position, size = td.size;
        var maps = new float[h, w, 3];
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) maps[j, i, 0] = 1f;
        float fx = (w - 1f) / size.x, fz = (h - 1f) / size.z;
        for (int k = 0; k < roadPts.Count - 1; k++)
        {
            Vector2 a = roadPts[k], b = roadPts[k + 1]; float hw = Mathf.Max(roadHW[k], roadHW[k + 1]) + 1f;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - hw - o.x) * fx)), i1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + hw - o.x) * fx));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - hw - o.z) * fz)), j1 = Mathf.Min(h - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + hw - o.z) * fz));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float t; float d = SegDist(new Vector2(o.x + i / fx, o.z + j / fz), a, b, out t);
                    if (d <= Mathf.Lerp(roadHW[k], roadHW[k + 1], t)) { maps[j, i, 0] = 0f; maps[j, i, 2] = 1f; }
                }
        }
        int hj0 = Mathf.Max(0, Mathf.CeilToInt((HighwayZ0 - o.z) * fz)), hj1 = Mathf.Min(h - 1, Mathf.FloorToInt((HighwayZ1 - o.z) * fz));
        int hi0 = Mathf.Max(0, Mathf.CeilToInt((-HighwayX - o.x) * fx)), hi1 = Mathf.Min(w - 1, Mathf.FloorToInt((HighwayX - o.x) * fx));
        for (int j = hj0; j <= hj1; j++) for (int i = hi0; i <= hi1; i++) { maps[j, i, 0] = 0f; maps[j, i, 2] = 1f; }
        td.SetAlphamaps(0, 0, maps);
        SkyPrisonTerrainGrateBinder.EnsureOn(terrain, vSurf, vLayer);
        SkyPrisonGroundAudioBindingUtility.EnsureBinding(terrain, gLayer, gSurf);
        SkyPrisonGroundAudioBindingUtility.EnsureBinding(terrain, vLayer, vSurf);
        SkyPrisonGroundAudioBindingUtility.EnsureBinding(terrain, aLayer, aSurf);
        SkyPrisonTerrainHoleQuery.InvalidateVoid(td);
        EditorUtility.SetDirty(td);
    }

    // ── 物体 ────────────────────────────────────────────────────────────
    private static int BuildObjects(Scene scene)
    {
        wall = LoadTd("TD_Graybox_Wall"); box = LoadTd("TD_Graybox_Box"); cylinder = LoadTd("TD_Graybox_Cylinder");
        platform = LoadTd("TD_Graybox_Platform"); liftGate = LoadTd("TD_Graybox_LiftGate"); fence = EnsureFenceDef();

        Transform graybox = EnsurePath(scene, "WorldRoot", "BackgroundRoot", "GrayboxRoot");
        foreach (var n in new[] { GroupName, "旧工厂白模_v1" }) { var old = graybox.Find(n); if (old != null) Object.DestroyImmediate(old.gameObject); }
        groupRoot = new GameObject(GroupName).transform; groupRoot.SetParent(graybox, false);
        gBuild = Sub("建筑"); gFence = Sub("围栏"); gProp = Sub("道具"); gMark = Sub("点位与门");
        placed = 0;

        foreach (var b in Buildings) BuildingWalls(b);
        BuildFences();
        BuildRoadRails();
        BuildProps();
        BuildGatesAndMarkers();
        return placed;
    }
    /// <summary>只重建指定建筑的墙（改了门洞表之后用），不动围栏、道具和其他建筑。要求当前打开的就是旧工厂场景。</summary>
    public static void RebuildBuildings(params string[] names)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new System.Exception("当前不是旧工厂场景。");
        var grp = GameObject.Find(GroupName);
        if (grp == null || grp.transform.Find("建筑") == null) throw new System.Exception("找不到生成的白模组，先整体生成一次。");
        wall = LoadTd("TD_Graybox_Wall");
        gBuild = grp.transform.Find("建筑");
        foreach (var n in names)
        {
            var b = Buildings.FirstOrDefault(x => x.name == n);
            if (b == null) { Debug.LogWarning($"{Log} 没有叫「{n}」的建筑。"); continue; }
            var old = gBuild.Find(n); if (old != null) Object.DestroyImmediate(old.gameObject);
            BuildingWalls(b);
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }
    private static Transform Sub(string n) { var t = new GameObject(n).transform; t.SetParent(groupRoot, false); return t; }
    private static TerrainDecorationDefinition LoadTd(string n)
    {
        var d = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(TdFolder + n + ".asset");
        if (d == null) throw new System.Exception($"找不到白模定义 {n}。");
        return d;
    }
    /// <summary>围栏 = 白模墙的副本，关掉挡视线 / 挡子弹 / 前后遮挡：挡人，但看得见、打得穿。</summary>
    private static TerrainDecorationDefinition EnsureFenceDef()
    {
        string path = TdFolder + "TD_Graybox_Fence.asset";
        if (AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(path) == null)
            AssetDatabase.CopyAsset(TdFolder + "TD_Graybox_Wall.asset", path);
        var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(path);
        var so = new SerializedObject(def);
        so.FindProperty("decorationId").stringValue = "graybox_fence";
        so.FindProperty("displayName").stringValue = "白模·围栏";
        so.FindProperty("blockVision").boolValue = false;
        so.FindProperty("blockProjectile").boolValue = false;
        so.FindProperty("occlusionMode").enumValueIndex = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssets();
        return def;
    }

    private static void BuildingWalls(Bld b)
    {
        var parent = new GameObject(b.name).transform; parent.SetParent(gBuild, false);
        int n = b.poly.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 A = b.poly[i], B = b.poly[(i + 1) % n];
            float L = Vector2.Distance(A, B); Vector2 d = (B - A) / L;
            var gaps = new List<Vector2>();
            foreach (var dr in Doors)
            {
                Vector2 p0 = new Vector2(dr[0], dr[1]), p1 = new Vector2(dr[2], dr[3]);
                if (DistToLine(A, d, p0) > 0.05f || DistToLine(A, d, p1) > 0.05f) continue;
                float t0 = Vector2.Dot(p0 - A, d), t1 = Vector2.Dot(p1 - A, d);
                if (t0 > t1) { var t = t0; t0 = t1; t1 = t; }
                if (t1 < -0.05f || t0 > L + 0.05f) continue;
                gaps.Add(new Vector2(Mathf.Max(0f, t0), Mathf.Min(L, t1)));
            }
            gaps.Sort((p, q) => p.x.CompareTo(q.x));
            float cursor = 0f;
            foreach (var g in gaps.Append(new Vector2(L, L)))
            {
                if (g.x - cursor > 0.2f) WallSeg(parent, b.name, A, d, cursor, g.x, L, b.h, 0.8f, wall, i);
                cursor = Mathf.Max(cursor, g.y);
            }
        }
    }
    private static float DistToLine(Vector2 a, Vector2 d, Vector2 p) { Vector2 v = p - a; return Mathf.Abs(v.x * d.y - v.y * d.x); }

    /// <summary>沿边 A→(d) 从 s 到 e 放一段墙；两端落在多边形顶点上时各外延半个墙厚，转角处不留缺口。</summary>
    private static void WallSeg(Transform parent, string name, Vector2 A, Vector2 d, float s, float e, float L, float h, float thick, TerrainDecorationDefinition def, int idx)
    {
        float ext = thick / 2f;
        float s2 = s - (s <= 0.001f ? ext : 0f), e2 = e + (e >= L - 0.001f ? ext : 0f);
        Vector2 c = A + d * ((s2 + e2) / 2f);
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg - 90f;
        Place(def, new Vector3(c.x, 0f, c.y), yaw, new Vector3((e2 - s2) / 4f, h / 4f, thick / 0.5f), parent, $"{name}·墙{idx}");
    }

    private static void BuildFences()
    {
        bool[,] nonwalk = new bool[GW, GH], gc = new bool[GW, GH];
        for (int x = 0; x < GW; x++) for (int z = 0; z < GH; z++) { gc[x, z] = gMask[x, z]; nonwalk[x, z] = !gMask[x, z] && !bDil[x, z] && !rMask[x, z]; }
        var edgeH = new bool[GW, GH + 1]; var edgeV = new bool[GW + 1, GH];
        for (int x = 0; x < GW; x++)
            for (int z = 0; z < GH; z++)
            {
                if (!gc[x, z]) continue;
                if (!In(x, z + 1) || nonwalk[x, z + 1]) edgeH[x, z + 1] = true;
                if (!In(x, z - 1) || nonwalk[x, z - 1]) edgeH[x, z] = true;
                if (!In(x + 1, z) || nonwalk[x + 1, z]) edgeV[x + 1, z] = true;
                if (!In(x - 1, z) || nonwalk[x - 1, z]) edgeV[x, z] = true;
            }
        const float FenceH = 4f, FenceT = 0.4f;
        int runs = 0;
        for (int zl = 0; zl <= GH; zl++)
            for (int x = 0; x < GW; x++)
            {
                if (!edgeH[x, zl]) continue;
                int x1 = x; while (x1 + 1 < GW && edgeH[x1 + 1, zl]) x1++;
                float len = x1 - x + 1;
                Place(fence, new Vector3(GX0 + x + len / 2f, 0f, GZ0 + zl), 0f, new Vector3(len / 4f, FenceH / 4f, FenceT / 0.5f), gFence, "围栏");
                x = x1; runs++;
            }
        for (int xl = 0; xl <= GW; xl++)
            for (int z = 0; z < GH; z++)
            {
                if (!edgeV[xl, z]) continue;
                int z1 = z; while (z1 + 1 < GH && edgeV[xl, z1 + 1]) z1++;
                float len = z1 - z + 1;
                Place(fence, new Vector3(GX0 + xl, 0f, GZ0 + z + len / 2f), 90f, new Vector3(len / 4f, FenceH / 4f, FenceT / 0.5f), gFence, "围栏");
                z = z1; runs++;
            }
        Debug.Log($"{Log} 围栏 {runs} 段");
    }

    /// <summary>公路两侧和高速两侧的护栏（低矮，挡人不挡视线 / 子弹）；高速北侧在公路接入处留口。</summary>
    private static void BuildRoadRails()
    {
        int n = roadPts.Count; var L = new Vector2[n]; var R = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 t = (roadPts[Mathf.Min(i + 1, n - 1)] - roadPts[Mathf.Max(i - 1, 0)]).normalized; Vector2 nr = new Vector2(-t.y, t.x);
            L[i] = roadPts[i] + nr * roadHW[i]; R[i] = roadPts[i] - nr * roadHW[i];
        }
        for (int i = 0; i < n - 1; i++) { Rail(L[i], L[i + 1]); Rail(R[i], R[i + 1]); }
        float gx = roadPts[n - 1].x;
        Rail(new Vector2(-HighwayX, HighwayZ1), new Vector2(gx - 8f, HighwayZ1)); Rail(new Vector2(gx + 8f, HighwayZ1), new Vector2(HighwayX, HighwayZ1));
        Rail(new Vector2(-HighwayX, HighwayZ0), new Vector2(HighwayX, HighwayZ0));
    }
    private static void Rail(Vector2 a, Vector2 b)
    {
        Vector2 d = b - a; float len = d.magnitude; if (len < 0.1f) return;
        Vector2 c = (a + b) / 2f; float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg - 90f;
        Place(fence, new Vector3(c.x, 0f, c.y), yaw, new Vector3((len + 0.3f) / 4f, 1.6f / 4f, 0.4f / 0.5f), gFence, "公路护栏");
    }

    private static void BuildProps()
    {
        void Box(float x, float z, float sx, float sy, float sz, string n, float yaw = 0f) => Place(box, new Vector3(x, 0f, z), yaw, new Vector3(sx, sy, sz), gProp, n);
        void Cyl(float x, float z, float dia, float hgt, string n) => Place(cylinder, new Vector3(x, 0f, z), 0f, new Vector3(dia, hgt, dia), gProp, n);
        foreach (var c in new[] { (-25f, -42f, 20f), (25f, -28f, 20f), (75f, -40f, 40f), (122f, -40f, 35f), (80f, -24f, 40f), (127f, -24f, 35f) }) Box(c.Item1, c.Item2, c.Item3, 2.6f, 4f, "集装箱排");
        Box(-100, 28, 80, 3, 4, "货架"); Box(-100, 52, 80, 3, 4, "货架");
        Box(-205, 34, 6, 4, 10, "变压器"); Box(-188, 34, 6, 4, 10, "变压器");
        for (int i = 0; i < 3; i++) { Box(166 + i * 22, 32, 8, 5, 8, "机床"); Box(166 + i * 22, 48, 8, 5, 8, "机床"); }
        Box(-116, -93, 25, 6, 14, "塌方");
        Cyl(-255, -15, 20, 30, "冷却塔"); Cyl(-240, 35, 20, 30, "冷却塔"); Cyl(-262, 85, 16, 26, "冷却塔");
        foreach (var p in new[] { (250f, -30f, 14f), (270f, 5f, 14f), (248f, 40f, 18f), (268f, 80f, 14f) }) Cyl(p.Item1, p.Item2, 14, p.Item3, "储罐");
        Box(251, 62, 22, 0.3f, 15, "泄漏池");
        Cyl(0, 93, 28, 22, "反应炉"); Cyl(30, 110, 8, 42, "烟囱"); Cyl(-185, -30, 8, 38, "锅炉房烟囱");
    }

    private static void BuildGatesAndMarkers()
    {
        // 钥匙门：主街西段 x=-158、东段 x=227，喉口宽 10 米（z 1..11），两侧各一个门柱
        foreach (float gx in new[] { -158f, 227f })
        {
            float w = gx < 0 ? 8f : 6f;
            Place(box, new Vector3(gx, 0f, -1.5f), 0f, new Vector3(w, 12f, 5f), gMark, "门柱");
            Place(box, new Vector3(gx, 0f, 13.5f), 0f, new Vector3(w, 12f, 5f), gMark, "门柱");
            LockWithKey(Place(liftGate, new Vector3(gx, 0f, 6f), 90f, new Vector3(10f / 4f, 12f / 4f, 1f), gMark, gx < 0 ? "钥匙门·西" : "钥匙门·东"));
        }
        // 机械区北门（通后排巷）：钥匙前这里不能直通后排，同样挂钥匙锁
        LockWithKey(Place(liftGate, new Vector3(184f, 0f, 70f), 0f, new Vector3(8f / 4f, 12f / 4f, 1f), gMark, "钥匙门·机械区北"));
        // 购买点（占位，全部通用）
        int k = 1;
        foreach (var p in new[] { (16f, -48f), (-90f, 6f), (110f, 6f), (-75f, 76f), (75f, 76f), (-250f, 100f), (258f, 100f) })
            Place(cylinder, new Vector3(p.Item1, 0f, p.Item2), 0f, new Vector3(2f, 1.5f, 2f), gMark, $"购买点{k++}");
        // 撤离点：A 在南门，B 西、C 东
        Place(cylinder, new Vector3(-9f, 0f, -104f), 0f, new Vector3(2.5f, 1.5f, 2.5f), gMark, "撤离点A_南门").gameObject.AddComponent<ExtractionTerminal>();
        Place(cylinder, new Vector3(-270f, 0f, 118f), 0f, new Vector3(2.5f, 1.5f, 2.5f), gMark, "撤离点B_西").gameObject.AddComponent<ExtractionTerminal>();
        Place(cylinder, new Vector3(270f, 0f, 118f), 0f, new Vector3(2.5f, 1.5f, 2.5f), gMark, "撤离点C_东").gameObject.AddComponent<ExtractionTerminal>();
        // 主线会合点（公路接高速处）+ 队友车队 + 高速入口（去下一区域），都是占位
        Place(cylinder, new Vector3(112f, 0f, -262f), 0f, new Vector3(3f, 1.5f, 3f), gMark, "主线会合点(占位)");
        foreach (var v in new[] { (88f, -268f), (132f, -268f), (150f, -256f) }) Place(box, new Vector3(v.Item1, 0f, v.Item2), 0f, new Vector3(7f, 2.6f, 3f), gMark, "队友车辆(占位)");
        Place(cylinder, new Vector3(-280f, 0f, -262f), 0f, new Vector3(4f, 2f, 4f), gMark, "高速入口_去下一区域(占位)");
        // RAID 落点（南门）+ 新手出生点（封存车间内，仅占位）
        var arrival = new GameObject("RAID落点_南门"); arrival.transform.SetParent(gMark, false); arrival.transform.position = ArrivalPos;
        arrival.AddComponent<SkyPrisonRaidArrivalPoint>().playerUnit = AssetDatabase.LoadAssetAtPath<UnitDefinition>(PlayerUnitPath);
        var tut = new GameObject("新手出生点_封存车间内(占位)"); tut.transform.SetParent(gMark, false); tut.transform.position = new Vector3(-34f, 0f, 38f);
    }

    private static void LockWithKey(GameObject gate)
    {
        var comp = gate.GetComponentInChildren<SkyPrisonSlidingDoor>(true);
        if (comp == null) { Debug.LogWarning($"{Log} {gate.name} 上没有 SkyPrisonSlidingDoor，没加锁。", gate); return; }
        var so = new SerializedObject(comp); var list = so.FindProperty("unlockConditions");
        list.ClearArray(); list.InsertArrayElementAtIndex(0);
        var c = list.GetArrayElementAtIndex(0);
        c.FindPropertyRelative("kind").enumValueIndex = (int)DoorConditionKind.QuestFlag;
        c.FindPropertyRelative("variableName").stringValue = GateKeyFlag;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── 结构（和放置工具 / 批量导入同一套）──
    public static GameObject Place(TerrainDecorationDefinition def, Vector3 pos, float yaw, Vector3 scale, Transform parent, string name)
    {
        int world = LayerMask.NameToLayer("World3D");
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, yaw, 0);

        var variant = def.variants[0];
        var binder = root.AddComponent<TerrainDecorationRuntimeBinder>();
        binder.definition = def;
        binder.instanceId = $"{def.decorationId}_{System.DateTime.Now:yyyyMMddHHmmssfff}_{placed}";
        binder.selectedVariantId = variant.variantId;
        binder.placementEuler = new Vector3(0, yaw, 0);
        binder.finalScale = scale;

        var visualRoot = new GameObject("VisualRoot").transform;
        visualRoot.SetParent(root.transform, false);
        visualRoot.localScale = scale;
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(variant.prefab, visualRoot);
        visual.name = variant.prefab.name;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        foreach (var c in visual.GetComponentsInChildren<TerrainDecorationRuntimeApplier>(true)) Object.DestroyImmediate(c);
        foreach (var c in visual.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(true)) Object.DestroyImmediate(c);
        if (world >= 0)
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = world;

        var applier = root.AddComponent<TerrainDecorationRuntimeApplier>();
        DisableAutoApply(applier);
        applier.ApplyDefinition();
        SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(root, def, false);
        DisableAutoApply(applier);
        if (def.blockVision) AddVisionBlocker(root);
        placed++;
        return root;
    }

    /// <summary>
    /// 战争迷雾只认 RuleRoot/VisionBlockerRoot/Vision_Blocker_Box 这套节点（放置工具摆完东西后会补这一步，批量生成要自己补）。
    /// 触发器盒子在 Default 层，按可视网格在根节点本地空间里的包围盒贴合——斜墙也是贴合的 OBB，不是世界轴对齐盒。
    /// </summary>
    private static void AddVisionBlocker(GameObject root)
    {
        Transform rule = root.transform.Find("RuleRoot");
        if (rule == null) { rule = new GameObject("RuleRoot").transform; rule.SetParent(root.transform, false); }
        Transform vbr = rule.Find("VisionBlockerRoot");
        if (vbr == null) { vbr = new GameObject("VisionBlockerRoot").transform; vbr.SetParent(rule, false); }
        Transform blk = vbr.Find("Vision_Blocker_Box");
        if (blk == null) { blk = new GameObject("Vision_Blocker_Box").transform; blk.SetParent(vbr, false); }
        vbr.gameObject.layer = 0; blk.gameObject.layer = 0;
        vbr.localPosition = Vector3.zero; vbr.localRotation = Quaternion.identity; vbr.localScale = Vector3.one;
        blk.localPosition = Vector3.zero; blk.localRotation = Quaternion.identity; blk.localScale = Vector3.one;

        var col = blk.GetComponent<BoxCollider>();
        if (col == null) col = blk.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true; col.enabled = true;

        Bounds lb = new Bounds(Vector3.zero, Vector3.one * 0.25f); bool init = false;
        Transform visual = root.transform.Find("VisualRoot");
        foreach (var mf in (visual != null ? visual : root.transform).GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = blk.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!init) { lb = new Bounds(local, Vector3.zero); init = true; } else lb.Encapsulate(local);
            }
        }
        col.center = lb.center;
        col.size = new Vector3(Mathf.Max(lb.size.x, 0.05f), Mathf.Max(lb.size.y, 0.05f), Mathf.Max(lb.size.z, 0.05f));
    }
    private static void DisableAutoApply(TerrainDecorationRuntimeApplier applier)
    {
        var so = new SerializedObject(applier);
        var a = so.FindProperty("applyOnEnable"); var b = so.FindProperty("applyInEditMode");
        if (a != null) a.boolValue = false;
        if (b != null) b.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static Transform EnsurePath(Scene scene, params string[] names)
    {
        Transform cur = scene.GetRootGameObjects().FirstOrDefault(r => r.name == names[0])?.transform;
        if (cur == null) { cur = new GameObject(names[0]).transform; SceneManager.MoveGameObjectToScene(cur.gameObject, scene); }
        for (int i = 1; i < names.Length; i++)
        {
            Transform next = cur.Find(names[i]);
            if (next == null) { next = new GameObject(names[i]).transform; next.SetParent(cur, false); }
            cur = next;
        }
        return cur;
    }

    // ── 工程配置 ──
    private static void EnsureBuildSettings()
    {
        var list = EditorBuildSettings.scenes.Where(s => !string.IsNullOrEmpty(s.path) && System.IO.File.Exists(s.path)).ToList();   // 顺手清掉已不存在的（UnknownWorkshop）
        if (!list.Any(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
    private static void SetRaidArrival()
    {
        var raid = AssetDatabase.LoadAssetAtPath<RaidDefinition>(RaidPath);
        if (raid == null) return;
        raid.targetScene = "Old Factory";
        raid.arrivalPosition = ArrivalPos;
        EditorUtility.SetDirty(raid);
    }
}
