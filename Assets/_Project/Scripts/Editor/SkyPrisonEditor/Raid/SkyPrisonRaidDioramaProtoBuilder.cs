using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 选关沙盘原型（权游片头那种机械微缩模型）：独立场景，不碰现有的选本界面。
/// 数据来自 Tools 脚本从用户手绘城市图提取的 blocks.json（塔位置 + 塔周围街区矩形）。
/// 世界尺寸 160 × 90（x 向东，z 向北），地面贴图的线条发光；街区按离塔的距离分 12 圈，各圈一个网格，控制器按圈依次升起。
/// </summary>
public static class SkyPrisonRaidDioramaProtoBuilder
{
    const string Dir = "Assets/_Project/Proto/RaidDiorama";
    const string ScenePath = Dir + "/RaidMapDiorama_Proto.unity";
    const string Log = "[沙盘原型]";
    const float WW = 160f, WH = 90f, MaxH = 3.6f;
    const int Rings = 16;

    [Serializable] class Data { public float[] tower; public float radius; public List<float[]> rects; }

    [MenuItem("天空囚笼/RAID/生成选关沙盘原型", false, 300)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning($"{Log} 运行中，没有执行。"); return; }
        var cur = EditorSceneManager.GetActiveScene();
        if (cur.isDirty && cur.path != ScenePath) { Debug.LogError($"{Log} 当前场景「{cur.name}」有未保存改动，先保存再运行。"); return; }
        try { BuildInternal(); }
        catch (Exception e) { Debug.LogError($"{Log} 失败：{e}"); }
    }

    const float ChunkXZ = 1.18f, ChunkY = 0.8f;
    /// <summary>让据点更矮、更宽、更厚实：水平放大，纵向在升起动画里按 ChunkY 压扁。</summary>
    static Transform Chunk(Transform tr) { var s = tr.localScale; tr.localScale = new Vector3(s.x * ChunkXZ, s.y, s.z * ChunkXZ); return tr; }

    static Material sPaint, sDark;
    static int meshSerial; static Mesh[] pineCache = new Mesh[3]; static Mesh earCache;

    static void BuildInternal()
    {
        meshSerial = 0; pineCache = new Mesh[3]; earCache = null;
        Directory.CreateDirectory(Dir + "/Meshes"); Directory.CreateDirectory(Dir + "/Materials");
        AssetDatabase.Refresh();
        var raw = File.ReadAllText(Dir + "/blocks.json").Replace(" ", "").Replace("\n", "");
        float tx, tz; var rects = ParseRects(raw, out tx, out tz);

        // ── 贴图 ──
        var ti = (TextureImporter)AssetImporter.GetAtPath(Dir + "/RaidCityMap_World.png");
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 8; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
        ti.SaveAndReimport();
        var mapTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/RaidCityMap_World.png");
        var ramp = MakeRamp();

        // ── 材质 ──
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mGround = Mat("Ground", shader, new Color(0.2f, 0.22f, 0.27f), 0.85f, 0.97f, null, new Color(3.2f, 3.2f, 3.6f), mapTex);   // 深灰大理石：高光滑、带金属感，能映出聚光
        var mBlock = Mat("Block", shader, Color.white, 0.15f, 0.45f, ramp, Color.black, null);
        var mBrass = MatGrainGold(shader);   // 塔 / 机械环 / 木马华盖边的金色：颗粒黄金
        var mSite = Mat("Site", shader, new Color(0.6f, 0.62f, 0.66f), 0.0f, 0.3f, null, Color.black, null);
        var mPink = Mat("HorsePink", shader, new Color(0.82f, 0.68f, 0.72f), 0.0f, 0.45f, null, Color.black, null);
        var mGrass = Mat("Grass", shader, new Color(0.2f, 0.27f, 0.23f), 0.0f, 0.2f, null, Color.black, null);
        var mWood = Mat("OldWood", shader, new Color(0.62f, 0.62f, 0.6f), 0.0f, 0.2f, null, Color.black, null);
        var mRoof = Mat("OldRoof", shader, new Color(0.34f, 0.36f, 0.42f), 0.0f, 0.3f, null, Color.black, null);
        var mRed = Mat("CrossRed", shader, new Color(0.62f, 0.2f, 0.2f), 0.0f, 0.3f, null, new Color(0.3f, 0.04f, 0.04f), null);
        sDark = Mat("SignDark", shader, new Color(0.5f, 0.52f, 0.56f), 0.0f, 0.35f, null, Color.black, null);
        sPaint = Mat("ChimneyPaint", shader, new Color(0.82f, 0.5f, 0.5f), 0.0f, 0.25f, null, Color.black, null);
        var mRail = Mat("Rail", shader, new Color(0.82f, 0.84f, 0.88f), 0.0f, 0.2f, null, Color.black, null);
        var mTram = Mat("TramCar", shader, new Color(0.66f, 0.68f, 0.72f), 0.0f, 0.3f, null, Color.black, null);
        var mCream = Mat("Cream", shader, new Color(0.42f, 0.4f, 0.36f), 0.0f, 0.3f, null, Color.black, null);
        var mWin = Mat("TramWin", shader, new Color(0.15f, 0.2f, 0.3f), 0.1f, 0.8f, null, new Color(0.45f, 0.36f, 0.18f), null);
        var mAmber = Mat("Amber", shader, new Color(1f, 0.6f, 0.2f), 0.2f, 0.4f, null, new Color(1.2f, 0.55f, 0.14f), null);

        // ── 新场景 ──
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Diorama");
        var ctrl = root.AddComponent<SkyPrisonRaidDioramaController>();

        // 地面
        var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer)); ground.transform.SetParent(root.transform, false);
        var gm = new Mesh { name = "Ground" };
        gm.vertices = new[] { new Vector3(-WW / 2, 0, -WH / 2), new Vector3(WW / 2, 0, -WH / 2), new Vector3(WW / 2, 0, WH / 2), new Vector3(-WW / 2, 0, WH / 2) };
        gm.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        gm.triangles = new[] { 0, 2, 1, 0, 3, 2 }; gm.RecalculateNormals(); gm.RecalculateBounds();
        ground.GetComponent<MeshFilter>().sharedMesh = SaveMesh(gm, "Ground"); ground.GetComponent<MeshRenderer>().sharedMaterial = mGround;

        // 地图外的地板：比地图大一圈，免得镜头边缘露出黑底
        var mFloor = Mat("Floor", shader, new Color(0.2f, 0.225f, 0.29f), 0.0f, 0.25f, null, Color.black, null);
        var floorGo = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer)); floorGo.transform.SetParent(root.transform, false); floorGo.transform.localPosition = new Vector3(0, -0.02f, 0);
        var fm = new Mesh { name = "Floor" };
        fm.vertices = new[] { new Vector3(-500, 0, -300), new Vector3(500, 0, -300), new Vector3(500, 0, 300), new Vector3(-500, 0, 300) };
        fm.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }; fm.triangles = new[] { 0, 2, 1, 0, 3, 2 }; fm.RecalculateNormals();
        floorGo.GetComponent<MeshFilter>().sharedMesh = SaveMesh(fm, "Floor"); floorGo.GetComponent<MeshRenderer>().sharedMaterial = mFloor;

        // 街区：六边形棱柱。把矩形栅格化成 0.1 精度的高度图，六边形中心及周围采样，覆盖够多才立起来（直线街不锯齿，路自然成沟）
        const float Cell = 0.1f, HexR = 0.42f, Cover = 0.5f;
        int gw = (int)(WW / Cell), gh = (int)(WH / Cell);
        var hmap = new float[gw * gh];
        foreach (var r in rects)
        {
            int x0 = Mathf.Max(0, (int)((r[0] + WW / 2) / Cell)), x1 = Mathf.Min(gw, (int)Mathf.Ceil((r[2] + WW / 2) / Cell));
            int z0 = Mathf.Max(0, (int)((r[1] + WH / 2) / Cell)), z1 = Mathf.Min(gh, (int)Mathf.Ceil((r[3] + WH / 2) / Cell));
            for (int zi = z0; zi < z1; zi++) for (int xi = x0; xi < x1; xi++) hmap[zi * gw + xi] = r[4];
        }
        float Sample(float x, float z) { int xi = (int)((x + WW / 2) / Cell), zi = (int)((z + WH / 2) / Cell); return xi < 0 || zi < 0 || xi >= gw || zi >= gh ? 0f : hmap[zi * gw + xi]; }
        Vector3 Pt(float u, float v) { return new Vector3(-WW / 2 + u * WW, 0f, WH / 2 - v * WH); }
        var pYuq = Pt(0.15f, 0.86f); var pSchool = Pt(0.27f, 0.75f); var pFactory = Pt(0.06f, 0.70f); var pHosp = Pt(0.69f, 0.34f); var pGold = Pt(0.80f, 0.22f);
        var sitePts = new[] { pYuq, pSchool, pFactory, pHosp, pGold }; var padRs = new[] { 9.6f, 8.0f, 4.8f, 4.8f, 6.4f };
        // 塔周围 26 单位内用小六边形，外面用大六边形（远处不需要那么细，面数也省）
        var hexIn = new List<List<Vector3>>(); var hexOut = new List<List<Vector3>>();
        for (int i = 0; i < Rings; i++) { hexIn.Add(new List<Vector3>()); hexOut.Add(new List<Vector3>()); }
        const float HexR2 = 0.72f, Split = 26f, MaxDist = 105f; int total = 0;
        void HexPass(float R, float dmin, float dmax, List<List<Vector3>> outl)
        {
            float sx = R * 1.7320508f, sz = R * 1.5f;
            for (int row = (int)(-WH / 2 / sz) - 1; row <= (int)(WH / 2 / sz) + 1; row++)
                for (int col = (int)(-WW / 2 / sx) - 1; col <= (int)(WW / 2 / sx) + 1; col++)
                {
                    float cx = col * sx + ((row & 1) != 0 ? sx * 0.5f : 0f), cz = row * sz;
                    if (Mathf.Abs(cx) > WW / 2 || Mathf.Abs(cz) > WH / 2) continue;
                    float d = Mathf.Sqrt((cx - tx) * (cx - tx) + (cz - tz) * (cz - tz)); if (d < dmin || d >= dmax) continue;
                    if (dmin <= 0f)
                    {
                        // 塔旁边的一个凹陷大坑（AXIA 当年和 Memento 之王交战的地方）——不在塔下，而在塔东北侧一点：
                        // 坑心 2.4 以内是空地（只剩地面），往外两级台阶抬高，再外面一圈参差破碎的坑沿，坑沿外才是城市
                        const float FloorR = 2.4f, Step1 = 4.4f, PitR = 6.8f, RimW = 2.2f;
                        float dp = Mathf.Sqrt((cx - (tx + 12f)) * (cx - (tx + 12f)) + (cz - (tz + 9f)) * (cz - (tz + 9f)));
                        int ring0 = Mathf.Min(Rings - 1, (int)(d / MaxDist * Rings));
                        float noise = Mathf.Abs(Mathf.Sin(cx * 12.9898f + cz * 78.233f) * 43758.5453f) % 1f;
                        if (dp < FloorR) continue;
                        if (dp < PitR) { outl[ring0].Add(new Vector3(cx, dp < Step1 ? 0.16f : 0.42f, cz)); total++; continue; }
                        if (dp < PitR + RimW)
                        {
                            if (noise < 0.2f) continue;                                    // 坑沿被打出缺口
                            float k = 1f - (dp - PitR) / RimW;                              // 越靠坑内越高
                            outl[ring0].Add(new Vector3(cx, 0.9f + noise * 1.5f * (0.4f + 0.6f * k), cz)); total++; continue;
                        }
                    }
                    if (dmin > 0f)
                    {
                        float sd = 1e9f; for (int si = 0; si < sitePts.Length; si++) { var sp = sitePts[si]; sd = Mathf.Min(sd, Mathf.Sqrt((cx - sp.x) * (cx - sp.x) + (cz - sp.z) * (cz - sp.z)) - padRs[si]); }
                        const float RimW = 1.4f, RimW2 = 2.8f;
                        if (sd < 0f) { outl[Mathf.Min(Rings - 1, (int)(d / MaxDist * Rings))].Add(new Vector3(cx, 0.12f, cz)); total++; continue; }
                        if (sd < RimW) { outl[Mathf.Min(Rings - 1, (int)(d / MaxDist * Rings))].Add(new Vector3(cx, 0.5f, cz)); total++; continue; }
                        if (sd < RimW2) { outl[Mathf.Min(Rings - 1, (int)(d / MaxDist * Rings))].Add(new Vector3(cx, 0.9f, cz)); total++; continue; }
                    }
                    int hit = 0, n = 7; float hsum = 0f;
                    for (int k = -1; k < 6; k++)
                    {
                        float an = k * Mathf.PI / 3f + Mathf.PI / 6f; float ox = k < 0 ? 0 : Mathf.Cos(an) * R * 0.7f, oz = k < 0 ? 0 : Mathf.Sin(an) * R * 0.7f;
                        float h = Sample(cx + ox, cz + oz); if (h > 0f) { hit++; hsum += h; }
                    }
                    if (hit < n * (dmin > 0f ? 0.5f : Cover)) continue;
                    outl[Mathf.Min(Rings - 1, (int)(d / MaxDist * Rings))].Add(new Vector3(cx, hsum / hit, cz)); total++;
                }
        }
        HexPass(HexR, 0f, Split, hexIn); HexPass(HexR2, Split, 9999f, hexOut);
        // 地图外：把地图沿边界镜像出去——城市接着是城市（越往外越矮，渐隐），海洋接着是海洋（镜像处没有街区就不放）
        {
            float Mir(float v, float half) { float p = half * 4f; float u = ((v + half) % p + p) % p; if (u > half * 2f) u = p - u; return u - half; }
            float sx = HexR2 * 1.7320508f, sz = HexR2 * 1.5f; const float Ext = 48f;
            for (int row = (int)(-(WH / 2 + Ext) / sz) - 1; row <= (int)((WH / 2 + Ext) / sz) + 1; row++)
                for (int col = (int)(-(WW / 2 + Ext) / sx) - 1; col <= (int)((WW / 2 + Ext) / sx) + 1; col++)
                {
                    float cx = col * sx + ((row & 1) != 0 ? sx * 0.5f : 0f), cz = row * sz;
                    float ex = Mathf.Max(0f, Mathf.Abs(cx) - WW / 2), ez = Mathf.Max(0f, Mathf.Abs(cz) - WH / 2);
                    float e = Mathf.Sqrt(ex * ex + ez * ez); if (e <= 0.01f || e > Ext) continue;
                    int hit = 0; float hsum = 0f;
                    for (int k2 = -1; k2 < 6; k2++)
                    {
                        float an = k2 * Mathf.PI / 3f + Mathf.PI / 6f; float ox = k2 < 0 ? 0 : Mathf.Cos(an) * HexR2 * 0.7f, oz = k2 < 0 ? 0 : Mathf.Sin(an) * HexR2 * 0.7f;
                        float h0 = Sample(Mir(cx + ox, WW / 2), Mir(cz + oz, WH / 2)); if (h0 > 0f) { hit++; hsum += h0; }
                    }
                    if (hit < 3.5f) continue;
                    float f = 1f - e / Ext; float h = hsum / hit * f * f; if (h < 0.1f) continue;
                    hexOut[Rings - 1].Add(new Vector3(cx, h, cz));
                }
        }
        var rises = new List<SkyPrisonRaidDioramaController.RiseGroup>();
        for (int i = 0; i < Rings; i++)
        {
            var go = new GameObject("BlockRing_" + i.ToString("00"), typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root.transform, false);
            var mesh = HexMesh(hexIn[i], HexR * 1.01f); var mo = HexMesh(hexOut[i], HexR2 * 1.01f);
            go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(Combine(mesh, mo), "BlockRing_" + i.ToString("00")); go.GetComponent<MeshRenderer>().sharedMaterial = mBlock;
            rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = go.transform, delay = 1.0f + i * 0.11f, duration = 0.85f });
        }
        Debug.Log($"{Log} 六边形 {total} 个");

        // 塔：四段收分的黄铜塔身 + 尖顶 + 顶端发光球；比最高的街区（3.6）高近三倍
        var tower = new GameObject("Tower"); tower.transform.SetParent(root.transform, false); tower.transform.position = new Vector3(tx, 0, tz);
        var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer)); body.transform.SetParent(tower.transform, false);
        body.GetComponent<MeshFilter>().sharedMesh = SaveMesh(SlabTowerMesh(14, 1.1f, 0.5f, 0.26f, 24f), "TowerBody"); body.GetComponent<MeshRenderer>().sharedMaterial = mBrass;
        var core = new GameObject("Core", typeof(MeshFilter), typeof(MeshRenderer)); core.transform.SetParent(tower.transform, false);
        core.GetComponent<MeshFilter>().sharedMesh = SaveMesh(FrustumMesh(0.3f, 0.62f, 11.2f, 40), "TowerCore"); core.GetComponent<MeshRenderer>().sharedMaterial = mAmber;
        var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(plinth.GetComponent<Collider>()); plinth.name = "Plinth"; plinth.transform.SetParent(tower.transform, false);
        plinth.transform.localPosition = new Vector3(0, 0.2f, 0); plinth.transform.localScale = new Vector3(3.6f, 0.4f, 3.6f); plinth.GetComponent<MeshRenderer>().sharedMaterial = mBrass;
        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere); UnityEngine.Object.DestroyImmediate(cap.GetComponent<Collider>()); cap.name = "Beacon";
        cap.transform.SetParent(tower.transform, false); cap.transform.localPosition = new Vector3(0, 11.5f, 0); cap.transform.localScale = Vector3.one * 0.7f; cap.GetComponent<MeshRenderer>().sharedMaterial = mAmber;
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = tower.transform, delay = 0f, duration = 1.7f, overshoot = 1.2f });

        // 机械环：三圈不同速度
        var ringsRoot = new GameObject("TowerRings"); ringsRoot.transform.SetParent(root.transform, false); ringsRoot.transform.position = new Vector3(tx, 3.7f, tz);
        var spinners = new List<SkyPrisonRaidDioramaController.Spinner>();
        float[][] rr = { new[] { 2.2f, 2.45f, 12f }, new[] { 3.4f, 3.62f, -8f }, new[] { 4.55f, 4.7f, 5f } };
        for (int i = 0; i < rr.Length; i++)
        {
            var ring = new GameObject("Ring_" + i, typeof(MeshFilter), typeof(MeshRenderer)); ring.transform.SetParent(ringsRoot.transform, false);
            ring.GetComponent<MeshFilter>().sharedMesh = SaveMesh(RingMesh(rr[i][0], rr[i][1], 0.3f + i * 0.08f, 96, i == 0 ? 0 : 6 + i * 2), "Ring_" + i);
            ring.GetComponent<MeshRenderer>().sharedMaterial = mBrass; spinners.Add(new SkyPrisonRaidDioramaController.Spinner { t = ring.transform, degPerSec = rr[i][2] });
        }
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = ringsRoot.transform, delay = 0.6f, duration = 1.0f, overshoot = 1f });

        // 据点（归一化图坐标 → 世界）：余穹在左下角远处，学校和旧工厂在它附近
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = Chunk(BuildYuqiong(root.transform, pYuq, mSite, mTram, mAmber, mRail)), baseY = ChunkY, delay = 3.4f, duration = 1.3f, overshoot = 1.6f });
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = BuildSchool(root.transform, pSchool, mSite, mWood, mRoof, mAmber, mWin, mGrass), delay = 3.7f, duration = 1.3f, overshoot = 1.6f });
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = Chunk(BuildHospital(root.transform, pHosp, mSite, mTram, mAmber, mWin, mRed)), baseY = ChunkY, delay = 3.9f, duration = 1.3f, overshoot = 1.6f });
        Transform carouselSpin; var gold = BuildCarousel(root.transform, pGold, mTram, mPink, mAmber, mCream, out carouselSpin);
        spinners.Add(new SkyPrisonRaidDioramaController.Spinner { t = carouselSpin, degPerSec = -16f });
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = Chunk(gold), baseY = ChunkY, delay = 4.2f, duration = 1.3f, overshoot = 1.6f });
        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = Chunk(BuildSite(root.transform, pFactory, 12f, mSite, mAmber)), baseY = ChunkY, delay = 4.0f, duration = 1.3f, overshoot = 1.6f });

        rises.Add(new SkyPrisonRaidDioramaController.RiseGroup { root = BuildTram(root.transform, mRail, mTram, mWin), delay = 4.4f, duration = 1.0f, overshoot = 1.2f });

        // RAID 标记：真 3D 放在据点正上方（目前只有旧工厂有关卡）
        BuildMarker(root.transform, "old_factory", "Site_OldFactory");

        // 反射环境：深色大理石 / 漆面需要"可反射的东西"，沙盘里没有天空盒，所以做一个自带柔光箱的小立方体贴图，挂在一个自定义反射探针上
        {
            var probeGo = new GameObject("ReflectionProbe"); probeGo.transform.SetParent(root.transform, false); probeGo.transform.localPosition = new Vector3(0f, 20f, 0f);
            var rp = probeGo.AddComponent<ReflectionProbe>(); rp.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom; rp.size = new Vector3(1400f, 260f, 900f);
            rp.boxProjection = false; rp.importance = 10; rp.intensity = 0.35f; rp.customBakedTexture = MakeStageCubemap();
        }

        // 灯光
        var sun = new GameObject("Sun"); sun.transform.SetParent(root.transform, false); sun.transform.rotation = Quaternion.Euler(40f, -38f, 0f);
        var l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.color = new Color(0.8f, 0.85f, 1f); l.intensity = 0.2f; l.shadows = LightShadows.Soft; l.shadowStrength = 0.95f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.03f, 0.045f, 0.09f); RenderSettings.skybox = null; RenderSettings.fog = false;
        var glow = new GameObject("TowerGlow"); glow.transform.SetParent(root.transform, false); glow.transform.position = new Vector3(tx, 7.5f, tz);
        // 舞台聚光：一盏，跟着镜头看的位置走（控制器每帧更新位置和亮度）
        var followGo = new GameObject("FollowSpot"); followGo.transform.SetParent(root.transform, false);
        var fl = followGo.AddComponent<Light>(); fl.type = LightType.Spot; fl.color = new Color(0.84f, 0.92f, 1f); fl.spotAngle = 50f; fl.innerSpotAngle = 26f;
        fl.range = 60f; fl.intensity = 200f; fl.shadows = LightShadows.Soft; fl.shadowStrength = 0.9f;
        ctrl.followLight = fl; ctrl.followBase = 600f;
        var pl = glow.AddComponent<Light>(); pl.type = LightType.Point; pl.color = new Color(1f, 0.65f, 0.3f); pl.intensity = 16f; pl.range = 20f;

        // 相机 + 后处理
        var camGo = new GameObject("DioramaCamera", typeof(Camera)); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.GetComponent<Camera>(); cam.fieldOfView = 26f; cam.nearClipPlane = 1f; cam.farClipPlane = 800f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.012f, 0.013f, 0.018f);
        var ucd = camGo.AddComponent<UniversalAdditionalCameraData>(); ucd.renderPostProcessing = true; ucd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root.transform, false); vol.isGlobal = true; vol.profile = MakeProfile();

        // 控制器
        foreach (var rg in rises) if (rg.root != null && rg.root.name.StartsWith("Site_")) { rg.onView = true; rg.duration = 2.6f; rg.overshoot = 1.1f; }   // 据点：进入画面才慢慢升起
        ctrl.MinDist = 36f; ctrl.MaxDist = 100f; ctrl.applyAmbient = true; ctrl.ambient = new Color(0.03f, 0.045f, 0.09f); ctrl.flySmooth = 0.14f; ctrl.cam = cam; ctrl.volume = vol; ctrl.rises = rises; ctrl.spinners = spinners; ctrl.autoPlay = true;
        ctrl.shots = new List<SkyPrisonRaidDioramaController.Shot>
        {
            new SkyPrisonRaidDioramaController.Shot { name = "总览", target = new Vector3(0, 0, 0), distance = 100f, elevation = 52f, azimuth = 0f },
            new SkyPrisonRaidDioramaController.Shot { name = "塔", target = new Vector3(tx, 3.2f, tz), distance = 44f, elevation = 28f, azimuth = 24f },
            new SkyPrisonRaidDioramaController.Shot { name = "余穹", target = pYuq + Vector3.up * 0.8f, distance = 40f, elevation = 34f, azimuth = 20f },
            new SkyPrisonRaidDioramaController.Shot { name = "旧工厂", target = pFactory + Vector3.up * 0.6f, distance = 34f, elevation = 36f, azimuth = -32f },
            new SkyPrisonRaidDioramaController.Shot { name = "塔顶近景", target = new Vector3(tx, 7.5f, tz), distance = 18f, elevation = 14f, azimuth = 40f },
        };
        EditorSceneManager.SaveScene(scene, ScenePath);
        Directory.CreateDirectory("Assets/_Project/Resources/UI/Raid");
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/_Project/Resources/UI/Raid/RaidDiorama.prefab");
        Debug.Log($"{Log} 完成：{rects.Count} 个街区矩形（{Rings} 圈），塔 ({tx:F2}, {tz:F2})，场景 {ScenePath}");
    }

    // ── 数据解析（blocks.json：{"tower":[x,z],"radius":R,"rects":[[x0,z0,x1,z1,h,d],...]}）──
    static List<float[]> ParseRects(string raw, out float tx, out float tz)
    {
        var tw = raw.IndexOf("\"tower\":[", StringComparison.Ordinal); var te = raw.IndexOf(']', tw);
        var tp = raw.Substring(tw + 9, te - tw - 9).Split(','); tx = float.Parse(tp[0], System.Globalization.CultureInfo.InvariantCulture); tz = float.Parse(tp[1], System.Globalization.CultureInfo.InvariantCulture);
        var rs = raw.IndexOf("\"rects\":[", StringComparison.Ordinal) + 9; var body = raw.Substring(rs).TrimEnd('}', ']', ' ', '\n');
        var list = new List<float[]>();
        foreach (var part in body.Split(new[] { "],[" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var nums = part.Trim('[', ']', ' ').Split(',');
            if (nums.Length != 6) continue;
            list.Add(nums.Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }
        return list;
    }

    // ── 几何 ──
    /// <summary>病院：方方正正的积木感。中间一栋更高的方块楼加一层退台，左右各一个很短的侧楼，楼上一个红十字，一个小雨棚，左楼顶停机坪。</summary>
    static Transform BuildHospital(Transform parent, Vector3 pos, Material steel, Material white, Material amber, Material win, Material red)
    {
        var g = new GameObject("Site_Hospital"); g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0, 10f, 0);
        Prim(g.transform, PrimitiveType.Cube, "Main", new Vector3(0, 1.9f, 0), new Vector3(3.4f, 3.8f, 3.2f), white);
        Prim(g.transform, PrimitiveType.Cube, "Top", new Vector3(0, 4.25f, 0), new Vector3(2.4f, 1.1f, 2.2f), white);
        Prim(g.transform, PrimitiveType.Cube, "WingL", new Vector3(-2.3f, 0.85f, 0.1f), new Vector3(1.2f, 1.7f, 2.8f), white);
        Prim(g.transform, PrimitiveType.Cube, "WingR", new Vector3(2.3f, 0.85f, 0.1f), new Vector3(1.2f, 1.7f, 2.8f), white);
        Prim(g.transform, PrimitiveType.Cube, "CrossH", new Vector3(0, 2.4f, -1.62f), new Vector3(1.3f, 0.42f, 0.1f), red);
        Prim(g.transform, PrimitiveType.Cube, "CrossV", new Vector3(0, 2.4f, -1.62f), new Vector3(0.42f, 1.3f, 0.1f), red);
        Prim(g.transform, PrimitiveType.Cube, "WinL", new Vector3(-2.3f, 0.95f, -1.34f), new Vector3(0.8f, 0.4f, 0.08f), win);
        Prim(g.transform, PrimitiveType.Cube, "WinR", new Vector3(2.3f, 0.95f, -1.34f), new Vector3(0.8f, 0.4f, 0.08f), win);
        Prim(g.transform, PrimitiveType.Cube, "WinM", new Vector3(0, 3.5f, -1.62f), new Vector3(2.2f, 0.4f, 0.08f), win);
        Prim(g.transform, PrimitiveType.Cube, "Canopy", new Vector3(0, 0.95f, -2.2f), new Vector3(2.2f, 0.16f, 1.2f), white);
        Prim(g.transform, PrimitiveType.Cube, "PillarL", new Vector3(-0.85f, 0.45f, -2.6f), new Vector3(0.3f, 0.9f, 0.3f), white);
        Prim(g.transform, PrimitiveType.Cube, "PillarR", new Vector3(0.85f, 0.45f, -2.6f), new Vector3(0.3f, 0.9f, 0.3f), white);
        Prim(g.transform, PrimitiveType.Cylinder, "Helipad", new Vector3(-2.3f, 1.74f, 0.1f), new Vector3(1.05f, 0.04f, 1.05f), win);
        Prim(g.transform, PrimitiveType.Cube, "H", new Vector3(-2.3f, 1.8f, 0.1f), new Vector3(0.5f, 0.04f, 0.1f), amber);
        Prim(g.transform, PrimitiveType.Cube, "H1", new Vector3(-2.5f, 1.8f, 0.1f), new Vector3(0.1f, 0.04f, 0.5f), amber);
        Prim(g.transform, PrimitiveType.Cube, "H2", new Vector3(-2.1f, 1.8f, 0.1f), new Vector3(0.1f, 0.04f, 0.5f), amber);
        Prim(g.transform, PrimitiveType.Cube, "Beacon", new Vector3(0, 5.0f, 0), new Vector3(0.22f, 0.4f, 0.22f), red);
        AmberRing(g.transform, 5.0f, amber); return g.transform;
    }

    /// <summary>一匹能认出来的小马：横躺的身体、斜起的脖子、向前的头 + 耳朵、四条腿、尾巴、一条鬃毛，穿一根金柱。朝 +z。</summary>
    static void AddHorse(Transform h, Material coat, Material pole, Material mane)
    {
        Prim(h, PrimitiveType.Cylinder, "Pole", new Vector3(0, 0.1f, 0), new Vector3(0.07f, 1.25f, 0.07f), pole);
        Prim(h, PrimitiveType.Capsule, "Body", new Vector3(0, 0.3f, 0), new Vector3(0.42f, 0.46f, 0.42f), coat).transform.localRotation = Quaternion.Euler(90f, 0, 0);
        Prim(h, PrimitiveType.Capsule, "Neck", new Vector3(0, 0.62f, 0.4f), new Vector3(0.22f, 0.3f, 0.22f), coat).transform.localRotation = Quaternion.Euler(35f, 0, 0);
        Prim(h, PrimitiveType.Capsule, "Head", new Vector3(0, 0.74f, 0.74f), new Vector3(0.28f, 0.32f, 0.28f), coat).transform.localRotation = Quaternion.Euler(140f, 0, 0);
        // 耳朵：削尖的小锥，向后倾靠在头上
        if (earCache == null) earCache = SaveMesh(LatheMesh(new[] { 0.055f, 0.0f }, new[] { 0f, 0.19f }, 8), "HorseEar");
        foreach (var sx in new[] { -1f, 1f })
        {
            var ear = new GameObject("Ear", typeof(MeshFilter), typeof(MeshRenderer)); ear.transform.SetParent(h, false);
            ear.transform.localPosition = new Vector3(sx * 0.075f, 0.99f, 0.56f); ear.transform.localRotation = Quaternion.Euler(-28f, 0, sx * -6f);
            ear.GetComponent<MeshFilter>().sharedMesh = earCache; ear.GetComponent<MeshRenderer>().sharedMaterial = coat;
        }
        Prim(h, PrimitiveType.Cube, "Mane", new Vector3(0, 0.74f, 0.3f), new Vector3(0.05f, 0.5f, 0.1f), mane).transform.localRotation = Quaternion.Euler(35f, 0, 0);
        foreach (var sx in new[] { -1f, 1f })
        {
            Prim(h, PrimitiveType.Cylinder, "LegF", new Vector3(sx * 0.1f, 0.04f, 0.26f), new Vector3(0.07f, 0.2f, 0.07f), coat).transform.localRotation = Quaternion.Euler(-22f, 0, 0);
            Prim(h, PrimitiveType.Cylinder, "LegB", new Vector3(sx * 0.1f, 0.04f, -0.26f), new Vector3(0.07f, 0.2f, 0.07f), coat).transform.localRotation = Quaternion.Euler(22f, 0, 0);
        }
        Prim(h, PrimitiveType.Capsule, "Tail", new Vector3(0, 0.22f, -0.56f), new Vector3(0.08f, 0.22f, 0.08f), mane).transform.localRotation = Quaternion.Euler(-140f, 0, 0);
    }

    /// <summary>黄金乡：旋转木马。金色圆台、中央立柱、伞形华盖、一圈会起伏的马；整体由 spin 转动。</summary>
    static Transform BuildCarousel(Transform parent, Vector3 pos, Material gold, Material white, Material amber, Material stripe, out Transform spin)
    {
        var g = new GameObject("Site_GoldenKingdom"); g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = new Vector3(1.25f, 1f, 1.25f);
        var s = new GameObject("Spin"); s.transform.SetParent(g.transform, false); spin = s.transform;
        Prim(s.transform, PrimitiveType.Cylinder, "Deck", new Vector3(0, 0.3f, 0), new Vector3(6.2f, 0.1f, 6.2f), gold);
        Prim(s.transform, PrimitiveType.Cylinder, "Pole", new Vector3(0, 2.0f, 0), new Vector3(0.5f, 1.7f, 0.5f), gold);
        var canopy = new GameObject("Canopy", typeof(MeshFilter), typeof(MeshRenderer)); canopy.transform.SetParent(s.transform, false); canopy.transform.localPosition = new Vector3(0, 3.8f, 0);
        canopy.GetComponent<MeshFilter>().sharedMesh = SaveMesh(CanopyMesh(3.3f, 1.2f, 24), "CarouselCanopy"); canopy.GetComponent<MeshRenderer>().sharedMaterials = new[] { gold, stripe };
        Prim(s.transform, PrimitiveType.Sphere, "Finial", new Vector3(0, 5.2f, 0), new Vector3(0.45f, 0.45f, 0.45f), amber);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f; float r = 2.45f; var pp = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            var holder = new GameObject("Horse" + i); holder.transform.SetParent(s.transform, false); holder.transform.localPosition = pp + Vector3.up * 1.15f;
            holder.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0); holder.transform.localScale = Vector3.one * 1.3f;
            var bob = holder.AddComponent<SkyPrisonRaidDioramaBob>(); bob.phase = i * 0.8f * (i % 2 == 0 ? 1f : -1f); bob.amplitude = 0.2f;
            AddHorse(holder.transform, white, gold, amber);
        }
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2f; Prim(s.transform, PrimitiveType.Sphere, "Bulb" + i, new Vector3(Mathf.Cos(a) * 3.3f, 3.8f, Mathf.Sin(a) * 3.3f), new Vector3(0.14f, 0.14f, 0.14f), amber);
        }
        AmberRing(g.transform, 5.0f, amber); return g.transform;
    }

    /// <summary>伞形华盖：底宽顶窄的锥面，底边略微波浪。</summary>
    static Mesh CanopyMesh(float radius, float height, int seg)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var triA = new List<int>(); var triB = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); float wob = 0.12f * Mathf.Sin(a * 12f);
            v.Add(d * radius + Vector3.up * wob); v.Add(d * 0.25f + Vector3.up * height); uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
        }
        for (int i = 0; i < seg; i++) { int k = i * 2; var tl = (i % 2 == 0) ? triA : triB; tl.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, subMeshCount = 2 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(triA, 0); m.SetTriangles(triB, 1); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    /// <summary>余穹：中枢 / 地铁站。一块高大的六边形竖牌（正面是余穹 Logo），脚下是下行的地铁站入口，两条白轨从入口两侧伸出。</summary>
    static Transform BuildYuqiong(Transform parent, Vector3 pos, Material steel, Material brass, Material amber, Material rail)
    {
        var g = new GameObject("Site_Yuqiong"); g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.localScale = new Vector3(1.35f, 1f, 1.35f);
        // 六边形牌子：六边形的"面"朝南（-z）竖起来，像一块立着的牌子；脚下一个矮的六边形底座
        const float R = 2.8f, Thick = 0.5f;
        var plate = new GameObject("HexPlate", typeof(MeshFilter), typeof(MeshRenderer)); plate.transform.SetParent(g.transform, false);
        plate.transform.localRotation = Quaternion.Euler(-90f, 0, 0); plate.transform.localPosition = new Vector3(0, 3.5f, Thick * 0.5f);
        plate.GetComponent<MeshFilter>().sharedMesh = SaveMesh(HexMesh(new List<Vector3> { new Vector3(0, Thick, 0) }, R), "YuqiongHexPlate"); plate.GetComponent<MeshRenderer>().sharedMaterial = sDark;
        var plinth = new GameObject("HexPlinth", typeof(MeshFilter), typeof(MeshRenderer)); plinth.transform.SetParent(g.transform, false);
        plinth.GetComponent<MeshFilter>().sharedMesh = SaveMesh(HexMesh(new List<Vector3> { new Vector3(0, 0.7f, 0) }, 2.0f), "YuqiongPlinth"); plinth.GetComponent<MeshRenderer>().sharedMaterial = brass;
        // Logo 画在六边形的前面（alpha 裁剪的 Unlit）
        var logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/UI/Raid/StillVaultLogo.png");
        if (logoTex != null)
        {
            var li = (TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Resources/UI/Raid/StillVaultLogo.png");
            if (li != null && (!li.alphaIsTransparency || li.mipmapEnabled)) { li.alphaIsTransparency = true; li.mipmapEnabled = false; li.wrapMode = TextureWrapMode.Clamp; li.SaveAndReimport(); }
            var lm = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_YuqiongLogo" };
            lm.SetTexture("_BaseMap", logoTex); lm.SetColor("_BaseColor", new Color(2.4f, 2.8f, 3.4f));   // >1：自发光，配合泛光就是一块亮着的 Logo 灯牌
            lm.SetFloat("_AlphaClip", 1f); lm.SetFloat("_Cutoff", 0.5f); lm.EnableKeyword("_ALPHATEST_ON"); lm.renderQueue = 2450;
            string lp = Dir + "/Materials/M_YuqiongLogo.mat"; AssetDatabase.DeleteAsset(lp); AssetDatabase.CreateAsset(lm, lp); lm = AssetDatabase.LoadAssetAtPath<Material>(lp);
            var q2 = GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.DestroyImmediate(q2.GetComponent<Collider>()); q2.name = "Logo"; q2.transform.SetParent(g.transform, false);
            q2.transform.localPosition = new Vector3(0, 3.5f, -Thick * 0.5f - 0.02f); q2.transform.localScale = new Vector3(3.6f, 3.9f, 1f); q2.GetComponent<MeshRenderer>().sharedMaterial = lm;
        }
        var glowGo = new GameObject("LogoGlow"); glowGo.transform.SetParent(g.transform, false); glowGo.transform.localPosition = new Vector3(0, 3.6f, -2.4f);
        var gl = glowGo.AddComponent<Light>(); gl.type = LightType.Point; gl.color = new Color(0.65f, 0.82f, 1f); gl.intensity = 5f; gl.range = 9f; gl.shadows = LightShadows.None;
        // 地铁站入口（参考图）：更高的石座 + 台阶口 + 两侧护墙；顶棚只盖后半截、抬得更高，前半截敞开，台阶和护墙看得清。整体转 45° 斜放在牌子前面
        var en = new GameObject("StationEntrance"); en.transform.SetParent(g.transform, false); en.transform.localPosition = new Vector3(0, 0, -3.9f); en.transform.localRotation = Quaternion.Euler(0, 45f, 0); en.transform.localScale = Vector3.one * 0.78f;   // 缩小一点，避免压到牌子底座和地台围边
        Prim(en.transform, PrimitiveType.Cube, "Base", new Vector3(0, 0.45f, 0), new Vector3(3.8f, 0.9f, 3.0f), steel);
        Prim(en.transform, PrimitiveType.Cube, "StairWell", new Vector3(0, 0.93f, 0.1f), new Vector3(2.2f, 0.06f, 2.4f), amber);
        Prim(en.transform, PrimitiveType.Cube, "Stairs", new Vector3(0, 1.0f, -0.9f), new Vector3(1.7f, 0.14f, 1.1f), brass).transform.localRotation = Quaternion.Euler(-20f, 0, 0);
        foreach (var sx in new[] { -1f, 1f })
        {
            Prim(en.transform, PrimitiveType.Cube, "SideWall", new Vector3(sx * 1.25f, 1.3f, 0.1f), new Vector3(0.3f, 0.8f, 2.6f), steel);
            Prim(en.transform, PrimitiveType.Cube, "SideCap", new Vector3(sx * 1.25f, 1.74f, 0.1f), new Vector3(0.38f, 0.1f, 2.7f), brass);
            Prim(en.transform, PrimitiveType.Cylinder, "Pillar", new Vector3(sx * 1.55f, 2.2f, 1.2f), new Vector3(0.18f, 1.0f, 0.18f), brass);   // 只在后面立柱
        }
        Prim(en.transform, PrimitiveType.Cube, "Canopy", new Vector3(0, 3.3f, 0.65f), new Vector3(3.9f, 0.14f, 1.8f), steel);
        for (int i = 0; i < 4; i++) Prim(en.transform, PrimitiveType.Cube, "Rib", new Vector3(0, 3.4f, -0.1f + i * 0.5f), new Vector3(3.9f, 0.05f, 0.1f), brass);
        Prim(en.transform, PrimitiveType.Cube, "Sign", new Vector3(0, 3.62f, -0.2f), new Vector3(1.6f, 0.34f, 0.08f), amber);
        AmberRing(g.transform, 4.65f, amber); return g.transform;
    }

    /// <summary>高架电车：路径沿城市主干道，高架在街区上方（柱子每隔几个单位一根），三节车厢来回跑。</summary>
    static Transform BuildTram(Transform parent, Material rail, Material car, Material window)
    {
        var g = new GameObject("Tram"); g.transform.SetParent(parent, false);
        const float H = 4.6f;
        var pts = new List<Vector3>
        {
            new Vector3(-60f, H, -32.4f), new Vector3(-38f, H, -17f), new Vector3(-16f, H, -9f), new Vector3(2.8f, H, -6.5f),
            new Vector3(20f, H, -1f), new Vector3(40f, H, 10f), new Vector3(62f, H, 22f)
        };
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1]; var b = pts[i]; var mid = (a + b) * 0.5f; var dirv = b - a; float len = dirv.magnitude;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(seg.GetComponent<Collider>()); seg.name = "Rail" + i; seg.transform.SetParent(g.transform, false);
            seg.transform.localPosition = mid + Vector3.down * 0.12f; seg.transform.localRotation = Quaternion.LookRotation(dirv, Vector3.up); seg.transform.localScale = new Vector3(0.4f, 0.1f, len + 0.15f);
            seg.GetComponent<MeshRenderer>().sharedMaterial = rail;
            int pillars = Mathf.Max(1, (int)(len / 5f));
            for (int k = 0; k <= pillars; k++)
            {
                var pp = Vector3.Lerp(a, b, k / (float)pillars);
                var pil = GameObject.CreatePrimitive(PrimitiveType.Cylinder); UnityEngine.Object.DestroyImmediate(pil.GetComponent<Collider>()); pil.name = "Pillar"; pil.transform.SetParent(g.transform, false);
                pil.transform.localPosition = new Vector3(pp.x, (pp.y - 0.2f) / 2f, pp.z); pil.transform.localScale = new Vector3(0.12f, (pp.y - 0.2f) / 2f, 0.12f); pil.GetComponent<MeshRenderer>().sharedMaterial = rail;
            }
        }
        // 电车车厢先不放了（做得太简陋）：只保留高架轨道。需要时再恢复车厢与 SkyPrisonRaidDioramaTram。
        return g.transform;
    }

    static Mesh Combine(Mesh a, Mesh b)
    {
        var ci = new CombineInstance[] { new CombineInstance { mesh = a, transform = Matrix4x4.identity }, new CombineInstance { mesh = b, transform = Matrix4x4.identity } };
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.CombineMeshes(ci, true, false); m.RecalculateBounds(); return m;
    }

    static GameObject Prim(Transform parent, PrimitiveType t, string n, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t); UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>()); g.name = n; g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale; g.GetComponent<MeshRenderer>().sharedMaterial = m; return g;
    }

    static Transform AmberRing(Transform g, float r, Material amber)
    {
        var ring = new GameObject("NodeRing", typeof(MeshFilter), typeof(MeshRenderer)); ring.transform.SetParent(g, false); ring.transform.localPosition = new Vector3(0, 0.02f, 0);
        ring.GetComponent<MeshFilter>().sharedMesh = SaveMesh(RingMesh(r, r + 0.3f, 0.05f, 72, 0), "SiteRing_" + g.name); ring.GetComponent<MeshRenderer>().sharedMaterial = amber; return ring.transform;
    }

    /// <summary>余穹：围墙环抱的堡垒，中间一座高主楼，四角塔楼。</summary>
    static Transform BuildFortress(Transform parent, Vector3 pos, Material steel, Material amber)
    {
        var g = new GameObject("Site_Yuqiong"); g.transform.SetParent(parent, false); g.transform.position = pos;
        Prim(g.transform, PrimitiveType.Cube, "WallN", new Vector3(0, 0.3f, 2.4f), new Vector3(5.6f, 0.6f, 0.3f), steel); Prim(g.transform, PrimitiveType.Cube, "WallS", new Vector3(0, 0.3f, -2.4f), new Vector3(5.6f, 0.6f, 0.3f), steel);
        Prim(g.transform, PrimitiveType.Cube, "WallE", new Vector3(2.7f, 0.3f, 0), new Vector3(0.3f, 0.6f, 4.8f), steel); Prim(g.transform, PrimitiveType.Cube, "WallW", new Vector3(-2.7f, 0.3f, 0), new Vector3(0.3f, 0.6f, 4.8f), steel);
        foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) Prim(g.transform, PrimitiveType.Cylinder, "Turret", new Vector3(sx * 2.7f, 0.7f, sz * 2.4f), new Vector3(0.7f, 0.7f, 0.7f), steel);
        Prim(g.transform, PrimitiveType.Cube, "Keep", new Vector3(0, 0.9f, 0), new Vector3(2.2f, 1.8f, 1.6f), steel); Prim(g.transform, PrimitiveType.Cube, "Tower", new Vector3(-0.5f, 2.0f, 0), new Vector3(0.9f, 4.0f, 0.9f), steel);
        Prim(g.transform, PrimitiveType.Cube, "WingE", new Vector3(1.7f, 0.5f, -0.9f), new Vector3(1.0f, 1.0f, 1.2f), steel); Prim(g.transform, PrimitiveType.Cube, "WingW", new Vector3(-1.8f, 0.4f, 0.8f), new Vector3(1.0f, 0.8f, 1.4f), steel);
        AmberRing(g.transform, 4.5f, amber); return g.transform;
    }

    /// <summary>松树：细树干 + 三层向上收窄的锥形树冠。</summary>
    static void Pine(Transform parent, Vector3 pos, float scale, Material trunk, Material leaf)
    {
        var g = new GameObject("Pine"); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = Vector3.one * scale;
        Prim(g.transform, PrimitiveType.Cylinder, "Trunk", new Vector3(0, 0.25f, 0), new Vector3(0.14f, 0.25f, 0.14f), trunk);
        float[] r = { 0.62f, 0.5f, 0.36f }; float[] y = { 0.4f, 0.95f, 1.45f }; float[] h = { 0.8f, 0.7f, 0.65f };
        for (int i = 0; i < 3; i++)
        {
            var c = new GameObject("Tier" + i, typeof(MeshFilter), typeof(MeshRenderer)); c.transform.SetParent(g.transform, false); c.transform.localPosition = new Vector3(0, y[i], 0);
            if (pineCache[i] == null) pineCache[i] = SaveMesh(LatheMesh(new[] { r[i], 0.02f }, new[] { 0f, h[i] }, 12), "PineTier" + i);
            c.GetComponent<MeshFilter>().sharedMesh = pineCache[i]; c.GetComponent<MeshRenderer>().sharedMaterial = leaf;
        }
    }

    /// <summary>双坡屋顶棱柱：屋脊沿 X，底面在 y=0，两个山墙三角面。</summary>
    static Mesh RoofMesh(float length, float width, float height)
    {
        float L = length / 2f, W = width / 2f; var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int n = v.Count; v.Add(a); v.Add(b); v.Add(c); for (int i = 0; i < 3; i++) uv.Add(new Vector2(0.5f, 0.5f)); tri.AddRange(new[] { n, n + 1, n + 2 }); }
        var a0 = new Vector3(-L, 0, -W); var a1 = new Vector3(L, 0, -W); var b0 = new Vector3(-L, 0, W); var b1 = new Vector3(L, 0, W); var r0 = new Vector3(-L, height, 0); var r1 = new Vector3(L, height, 0);
        Tri(a0, r0, r1); Tri(a0, r1, a1);          // 南坡
        Tri(b1, r1, r0); Tri(b1, r0, b0);          // 北坡
        Tri(a0, b0, r0); Tri(a1, r1, b1);          // 山墙
        Tri(a0, a1, b1); Tri(a0, b1, b0);          // 底
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    static GameObject Roof(Transform parent, string n, Vector3 pos, float yaw, float length, float width, float height, Material m)
    {
        var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(RoofMesh(length, width, height), "Roof_" + n + "_" + parent.name + "_" + (meshSerial++)); go.GetComponent<MeshRenderer>().sharedMaterial = m; return go;
    }

    /// <summary>学校：混凝土 L 形教学楼。比之前更高更厚（占地不变）；围墙、门柱、绿化带都加粗加高，更敦实。</summary>
    static Transform BuildSchool(Transform parent, Vector3 pos, Material steel, Material conc, Material roof, Material amber, Material win, Material grass)
    {
        var g = new GameObject("Site_School"); g.transform.SetParent(parent, false); g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0, -8f, 0);
        // 主楼（三层，更高）+ 侧楼（两层，更厚）
        Prim(g.transform, PrimitiveType.Cube, "Main", new Vector3(0.4f, 1.4f, 2.2f), new Vector3(8.4f, 2.8f, 2.4f), conc);
        Prim(g.transform, PrimitiveType.Cube, "Wing", new Vector3(-3.2f, 1.0f, -0.7f), new Vector3(2.6f, 2.0f, 3.6f), conc);
        Prim(g.transform, PrimitiveType.Cube, "RoofMain", new Vector3(0.4f, 2.9f, 2.2f), new Vector3(8.7f, 0.22f, 2.7f), roof);
        Prim(g.transform, PrimitiveType.Cube, "RoofWing", new Vector3(-3.2f, 2.1f, -0.7f), new Vector3(2.9f, 0.22f, 3.9f), roof);
        Prim(g.transform, PrimitiveType.Cube, "Stair", new Vector3(-1.2f, 3.35f, 2.6f), new Vector3(1.4f, 0.9f, 1.2f), conc);
        foreach (var wy in new[] { 0.7f, 1.9f }) Prim(g.transform, PrimitiveType.Cube, "Win", new Vector3(0.4f, wy, 1.03f), new Vector3(7.4f, 0.55f, 0.08f), win);
        foreach (var wy in new[] { 0.6f, 1.5f }) Prim(g.transform, PrimitiveType.Cube, "WinW", new Vector3(-4.52f, wy, -0.7f), new Vector3(0.08f, 0.45f, 3.0f), win);
        Prim(g.transform, PrimitiveType.Cube, "Canopy", new Vector3(1.0f, 1.15f, 0.7f), new Vector3(2.0f, 0.16f, 1.1f), conc);
        Prim(g.transform, PrimitiveType.Cube, "Door", new Vector3(1.0f, 0.5f, 1.0f), new Vector3(1.2f, 1.0f, 0.08f), amber);
        // 矮墙：更厚更高
        Prim(g.transform, PrimitiveType.Cube, "WallL", new Vector3(-4.6f, 0.4f, -1.25f), new Vector3(0.36f, 0.8f, 9.86f), conc);
        Prim(g.transform, PrimitiveType.Cube, "WallR", new Vector3(5.0f, 0.4f, -1.25f), new Vector3(0.36f, 0.8f, 9.86f), conc);
        Prim(g.transform, PrimitiveType.Cube, "WallBack", new Vector3(0.2f, 0.4f, 3.5f), new Vector3(9.28f, 0.8f, 0.36f), conc);
        Prim(g.transform, PrimitiveType.Cube, "WallFL", new Vector3(-2.81f, 0.4f, -6.0f), new Vector3(3.22f, 0.8f, 0.36f), conc);
        Prim(g.transform, PrimitiveType.Cube, "WallFR", new Vector3(2.61f, 0.4f, -6.0f), new Vector3(4.42f, 0.8f, 0.36f), conc);
        Prim(g.transform, PrimitiveType.Cube, "PillarL", new Vector3(-0.9f, 0.65f, -6.0f), new Vector3(0.6f, 1.3f, 0.6f), conc);
        Prim(g.transform, PrimitiveType.Cube, "PillarR", new Vector3(0.7f, 0.65f, -6.0f), new Vector3(0.6f, 1.3f, 0.6f), conc);
        // 绿化带：更宽更高，贴着墙内侧
        Prim(g.transform, PrimitiveType.Cube, "GreenL", new Vector3(-3.95f, 0.22f, -4.2f), new Vector3(0.85f, 0.44f, 3.2f), grass);
        Prim(g.transform, PrimitiveType.Cube, "GreenFL", new Vector3(-2.8f, 0.22f, -5.4f), new Vector3(3.4f, 0.44f, 0.85f), grass);
        Prim(g.transform, PrimitiveType.Cube, "GreenFR", new Vector3(3.0f, 0.22f, -5.4f), new Vector3(3.4f, 0.44f, 0.85f), grass);
        Prim(g.transform, PrimitiveType.Cube, "GreenR", new Vector3(4.35f, 0.22f, -2.2f), new Vector3(0.85f, 0.44f, 6.0f), grass);
        AmberRing(g.transform, 8.8f, amber); return g.transform;
    }

    /// <summary>圆台（下细上粗），底在 y=0。</summary>
    static Mesh FrustumMesh(float r0, float r1, float h, int seg)
    {
        var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v.Add(d * r0); v.Add(d * r1 + Vector3.up * h); uv.Add(new Vector2(0, 0.5f)); uv.Add(new Vector2(1, 0.5f));
        }
        for (int i = 0; i < seg; i++) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
        int c = v.Count; v.Add(Vector3.up * h); uv.Add(new Vector2(1, 0.5f));
        for (int i = 0; i < seg; i++) { tri.AddRange(new[] { c, i * 2 + 3, i * 2 + 1 }); }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    /// <summary>等宽的方形片层层叠起，层间留缝，每层比下一层多转 stepDeg，形成螺旋上升。</summary>
    /// <summary>按顶点位置做的伪三维投影 UV：任何朝向的面上贴图都不会退化成一条线（颗粒贴图用）。</summary>
    static List<Vector2> ProjUV(List<Vector3> v)
    {
        var o = new List<Vector2>(v.Count);
        foreach (var p in v) o.Add(new Vector2(p.x * 0.9f + p.y * 0.37f, p.z * 0.9f + p.y * 0.61f));
        return o;
    }

    /// <summary>颗粒黄金：程序生成的细噪点反照率 + 法线颗粒 + 高金属度，带一点暖色自发光，让聚光下像撒了金砂。</summary>
    /// <summary>程序生成的"展台"环境立方体贴图：暗蓝渐变 + 顶上一块大柔光箱 + 一条冷白灯带 + 一小块暖色补光，供漆面 / 大理石 / 黄金反射。</summary>
    static Cubemap MakeStageCubemap()
    {
        const int N = 128; var cube = new Cubemap(N, TextureFormat.RGBAHalf, false);
        System.Func<Vector3, Color> shade = d =>
        {
            d.Normalize(); float up = d.y;
            Color c = Color.Lerp(new Color(0.07f, 0.10f, 0.16f), new Color(0.015f, 0.022f, 0.05f), Mathf.Clamp01(up * 1.2f));
            if (up < 0f) c = Color.Lerp(c, new Color(0.01f, 0.012f, 0.02f), Mathf.Clamp01(-up * 3f));
            // 顶上的大柔光箱
            Vector3 sb = new Vector3(0.28f, 0.78f, 0.55f).normalized; float a = Mathf.Clamp01((Vector3.Dot(d, sb) - 0.80f) / 0.16f); c += new Color(1.5f, 1.65f, 1.9f) * a * a;
            // 一条横向冷白灯带（水平线稍上，约 130° 范围）
            float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; float band = Mathf.Clamp01(1f - Mathf.Abs(up - 0.22f) / 0.05f) * Mathf.Clamp01(1f - Mathf.Abs(Mathf.DeltaAngle(az, -35f)) / 65f);
            c += new Color(0.8f, 0.95f, 1.15f) * band;
            // 左后方一小块暖色补光
            Vector3 wm = new Vector3(-0.75f, 0.45f, -0.45f).normalized; float w = Mathf.Clamp01((Vector3.Dot(d, wm) - 0.93f) / 0.06f); c += new Color(2.4f, 1.4f, 0.55f) * w;
            c.a = 1f; return c;
        };
        for (int f = 0; f < 6; f++)
        {
            var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f; Vector3 d;
                switch ((CubemapFace)f)
                {
                    case CubemapFace.PositiveX: d = new Vector3(1f, -v, -u); break;
                    case CubemapFace.NegativeX: d = new Vector3(-1f, -v, u); break;
                    case CubemapFace.PositiveY: d = new Vector3(u, 1f, v); break;
                    case CubemapFace.NegativeY: d = new Vector3(u, -1f, -v); break;
                    case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1f); break;
                    default: d = new Vector3(-u, -v, -1f); break;
                }
                px[y * N + x] = shade(d);
            }
            cube.SetPixels(px, (CubemapFace)f);
        }
        cube.Apply(false);
        string path = Dir + "/StageEnv.cubemap"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(cube, path);
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    static Material MatGrainGold(Shader sh)
    {
        const int N = 256; var h = new float[N, N]; var rng = new System.Random(21);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) h[x, y] = (float)rng.NextDouble();
        // 一点点低频起伏 + 高频颗粒
        var low = new float[N, N]; var r2 = new System.Random(5); var coarse = new float[17, 17];
        for (int i = 0; i < 17; i++) for (int j = 0; j < 17; j++) coarse[i, j] = (float)r2.NextDouble();
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float fx = x / 16f, fy = y / 16f; int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
            float a = Mathf.Lerp(coarse[ix, iy], coarse[ix + 1 > 16 ? 0 : ix + 1, iy], tx), b2 = Mathf.Lerp(coarse[ix, iy + 1 > 16 ? 0 : iy + 1], coarse[ix + 1 > 16 ? 0 : ix + 1, iy + 1 > 16 ? 0 : iy + 1], tx);
            low[x, y] = Mathf.Lerp(a, b2, ty);
        }
        var albedo = new Texture2D(N, N, TextureFormat.RGBA32, true); var nrm = new Texture2D(N, N, TextureFormat.RGBA32, true);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float g = h[x, y] * 0.75f + low[x, y] * 0.25f; float v = Mathf.Lerp(0.62f, 1.0f, g);
            albedo.SetPixel(x, y, new Color(v, v, v, 1f));
            float dx = (h[(x + 1) % N, y] - h[(x + N - 1) % N, y]) * 1.6f, dy = (h[x, (y + 1) % N] - h[x, (y + N - 1) % N]) * 1.6f;
            var n3 = new Vector3(-dx, -dy, 1f).normalized; nrm.SetPixel(x, y, new Color(n3.x * 0.5f + 0.5f, n3.y * 0.5f + 0.5f, n3.z * 0.5f + 0.5f, 1f));
        }
        albedo.Apply(); nrm.Apply();
        string pa = Dir + "/Textures/GoldGrain_A.png", pn = Dir + "/Textures/GoldGrain_N.png";
        Directory.CreateDirectory(Dir + "/Textures");
        File.WriteAllBytes(pa, albedo.EncodeToPNG()); File.WriteAllBytes(pn, nrm.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(albedo); UnityEngine.Object.DestroyImmediate(nrm);
        AssetDatabase.ImportAsset(pa); AssetDatabase.ImportAsset(pn);
        var ni = (TextureImporter)AssetImporter.GetAtPath(pn); ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport();
        var ai = (TextureImporter)AssetImporter.GetAtPath(pa); ai.mipmapEnabled = true; ai.wrapMode = TextureWrapMode.Repeat; ai.anisoLevel = 4; ai.SaveAndReimport();
        string mp = Dir + "/Materials/M_GoldGrain.mat"; AssetDatabase.DeleteAsset(mp);
        var m = new Material(sh) { name = "M_GoldGrain" };
        m.SetColor("_BaseColor", new Color(1.0f, 0.8f, 0.38f)); m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(pa)); m.SetTextureScale("_BaseMap", new Vector2(1.1f, 1.1f));
        m.SetFloat("_Metallic", 0.88f); m.SetFloat("_Smoothness", 0.5f);
        m.EnableKeyword("_NORMALMAP"); m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(pn)); m.SetTextureScale("_BumpMap", new Vector2(1.1f, 1.1f)); m.SetFloat("_BumpScale", 2.2f);
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.5f, 0.31f, 0.08f)); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        AssetDatabase.CreateAsset(m, mp); return AssetDatabase.LoadAssetAtPath<Material>(mp);
    }

    static Mesh SlabTowerMesh(int count, float half, float thick, float gap, float stepDeg)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int i = 0; i < count; i++)
        {
            float y0 = 0.4f + i * (thick + gap), y1 = y0 + thick; var q = Quaternion.Euler(0, i * stepDeg, 0); float u = i / (float)(count - 1);
            var b = new Vector3[4]; var t = new Vector3[4]; var c = new[] { new Vector3(-half, 0, -half), new Vector3(half, 0, -half), new Vector3(half, 0, half), new Vector3(-half, 0, half) };
            for (int k = 0; k < 4; k++) { b[k] = q * c[k] + Vector3.up * y0; t[k] = q * c[k] + Vector3.up * y1; }
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { int n = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); for (int z = 0; z < 4; z++) uv.Add(new Vector2(u, 0.5f)); tri.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 }); }
            float ih = half * 0.66f; var ci = new[] { new Vector3(-ih, 0, -ih), new Vector3(ih, 0, -ih), new Vector3(ih, 0, ih), new Vector3(-ih, 0, ih) };
            var bi = new Vector3[4]; var ti = new Vector3[4];
            for (int k = 0; k < 4; k++) { bi[k] = q * ci[k] + Vector3.up * y0; ti[k] = q * ci[k] + Vector3.up * y1; }
            for (int k = 0; k < 4; k++)
            {
                int k2 = (k + 1) % 4;
                Quad(t[k2], t[k], ti[k], ti[k2]);              // 顶面（方环）
                Quad(b[k], b[k2], bi[k2], bi[k]);              // 底面
                Quad(b[k], t[k], t[k2], b[k2]);                // 外壁
                Quad(bi[k2], ti[k2], ti[k], bi[k]);            // 内壁（朝向镂空里面）
            }
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, ProjUV(v)); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    /// <summary>四边形截面、逐层旋转并收分的螺旋塔身：底半宽 w0，顶半宽 w1，总扭转 twistDeg。</summary>
    static Mesh TwistTowerMesh(float height, float w0, float w1, float twistDeg, int slices)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        Vector3[] Ring(int i)
        {
            float f = i / (float)slices; float w = Mathf.Lerp(w0, w1, Mathf.Pow(f, 0.85f)); float y = f * height; float rot = twistDeg * f * f * 0.5f + twistDeg * f * 0.5f; var q = Quaternion.Euler(0, rot, 0);
            return new[] { q * new Vector3(-w, 0, -w) + Vector3.up * y, q * new Vector3(w, 0, -w) + Vector3.up * y, q * new Vector3(w, 0, w) + Vector3.up * y, q * new Vector3(-w, 0, w) + Vector3.up * y };
        }
        for (int i = 0; i < slices; i++)
        {
            var a = Ring(i); var b = Ring(i + 1); float u = i / (float)slices;
            for (int k = 0; k < 4; k++)
            {
                int n = v.Count; int k2 = (k + 1) % 4;
                v.Add(a[k]); v.Add(a[k2]); v.Add(b[k2]); v.Add(b[k]);
                for (int q = 0; q < 4; q++) uv.Add(new Vector2(u, 0.5f));
                tri.AddRange(new[] { n, n + 3, n + 2, n, n + 2, n + 1 });
            }
        }
        var top = Ring(slices); int t0 = v.Count; foreach (var p in top) { v.Add(p); uv.Add(new Vector2(1, 0.5f)); }
        tri.AddRange(new[] { t0, t0 + 2, t0 + 1, t0, t0 + 3, t0 + 2 });
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    public static Mesh HexMesh(List<Vector3> cells, float r)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        foreach (var c in cells)
        {
            float h = c.y, u = Mathf.Clamp01(h / MaxH); var corner = new Vector3[6];
            for (int k = 0; k < 6; k++) { float an = k * Mathf.PI / 3f + Mathf.PI / 6f; corner[k] = new Vector3(c.x + Mathf.Cos(an) * r, 0, c.z + Mathf.Sin(an) * r); }
            int t0 = v.Count;
            for (int k = 0; k < 6; k++) { v.Add(corner[k] + Vector3.up * h); uv.Add(new Vector2(u, 0.5f)); }
            for (int k = 1; k < 5; k++) { tri.Add(t0); tri.Add(t0 + k + 1); tri.Add(t0 + k); }   // 顶面（俯视顺时针朝上）
            for (int k = 0; k < 6; k++)
            {
                var a = corner[k]; var b = corner[(k + 1) % 6]; int i = v.Count;
                v.Add(a); v.Add(a + Vector3.up * h); v.Add(b + Vector3.up * h); v.Add(b);
                for (int q = 0; q < 4; q++) uv.Add(new Vector2(u * 0.9f, 0.5f));
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    static Mesh BoxMesh(List<float[]> rects)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) uv.Add(new Vector2(u, 0.5f)); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
        foreach (var r in rects)
        {
            float x0 = r[0], z0 = r[1], x1 = r[2], z1 = r[3], h = r[4], u = Mathf.Clamp01(h / MaxH);
            Quad(new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0), u);     // 顶
            Quad(new Vector3(x0, 0, z0), new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, 0, z0), u);     // 南
            Quad(new Vector3(x1, 0, z1), new Vector3(x1, h, z1), new Vector3(x0, h, z1), new Vector3(x0, 0, z1), u);     // 北
            Quad(new Vector3(x0, 0, z1), new Vector3(x0, h, z1), new Vector3(x0, h, z0), new Vector3(x0, 0, z0), u);     // 西
            Quad(new Vector3(x1, 0, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x1, 0, z1), u);     // 东
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    /// <summary>平放的环（外壁、内壁、顶面）；gaps > 0 时在环上留 gaps 个缺口，看起来像有咬合齿。</summary>
    static Mesh RingMesh(float r0, float r1, float h, int seg, int teeth)
    {
        var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) uv.Add(new Vector2(0.5f, 0.5f)); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
        for (int i = 0; i < seg; i++)
        {
            if (teeth > 0 && (i * teeth / seg) % 2 == 1 && (i * teeth) % seg < seg / teeth * 0 + 0) continue;
            float a0 = i / (float)seg * Mathf.PI * 2, a1 = (i + 1) / (float)seg * Mathf.PI * 2;
            float ho = (teeth > 0 && ((i * teeth * 2) / seg) % 2 == 1) ? h * 1.9f : h;
            Vector3 o0 = new Vector3(Mathf.Cos(a0) * r1, 0, Mathf.Sin(a0) * r1), o1 = new Vector3(Mathf.Cos(a1) * r1, 0, Mathf.Sin(a1) * r1);
            Vector3 i0 = new Vector3(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0), i1 = new Vector3(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
            Vector3 up = Vector3.up * ho;
            Quad(i0 + up, o0 + up, o1 + up, i1 + up);                  // 顶（顺时针俯视：内→外→外→内）
            Quad(o1, o1 + up, o0 + up, o0);                            // 外壁
            Quad(i0, i0 + up, i1 + up, i1);                            // 内壁
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, ProjUV(v)); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    static void Cyl(Transform parent, string name, Material mat, float baseY, float cy, float diameter, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0, baseY == 0 ? cy : cy, 0); go.transform.localScale = new Vector3(diameter, height / 2f, diameter);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>旋转体（按半径-高度轮廓转一圈），底部不封口。</summary>
    static Mesh LatheMesh(float[] radii, float[] ys, int seg)
    {
        var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
        for (int j = 0; j < radii.Length; j++)
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; v.Add(new Vector3(Mathf.Cos(a) * radii[j], ys[j], Mathf.Sin(a) * radii[j])); uv.Add(new Vector2(0.5f, 0.5f));
            }
        int row = seg + 1;
        for (int j = 0; j < radii.Length - 1; j++)
            for (int i = 0; i < seg; i++)
            {
                int k = j * row + i;
                tri.AddRange(new[] { k, k + row, k + 1, k + 1, k + row, k + row + 1 });
            }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    /// <summary>有壁厚的旋转体（冷却塔）：外壁 + 内壁（反向）+ 顶部圆环，从上往下看也是实心的厚壁，不会看穿。</summary>
    static Mesh LatheShellMesh(float[] radii, float[] ys, int seg, float wall)
    {
        var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
        int n = radii.Length, row = seg + 1;
        for (int pass = 0; pass < 2; pass++)
            for (int j = 0; j < n; j++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f; float r = pass == 0 ? radii[j] : Mathf.Max(0.05f, radii[j] - wall);
                    v.Add(new Vector3(Mathf.Cos(a) * r, ys[j], Mathf.Sin(a) * r)); uv.Add(new Vector2(0.5f, 0.5f));
                }
        int inner = n * row;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < seg; i++)
            {
                int k = j * row + i;
                tri.AddRange(new[] { k, k + row, k + 1, k + 1, k + row, k + row + 1 });                                   // 外壁
                int m = inner + k;
                tri.AddRange(new[] { m, m + 1, m + row, m + 1, m + row + 1, m + row });                                   // 内壁（反向）
            }
        int top = (n - 1) * row;
        for (int i = 0; i < seg; i++)
        {
            int o0 = top + i, o1 = top + i + 1, i0 = inner + top + i, i1 = inner + top + i + 1;
            tri.AddRange(new[] { o0, i0, o1, o1, i0, i1 });                                                              // 顶部圆环（朝上）
        }
        var m2 = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m2.SetVertices(v); m2.SetUVs(0, uv); m2.SetTriangles(tri, 0); m2.RecalculateNormals(); m2.RecalculateBounds(); return m2;
    }

    static GameObject LatheShellObj(Transform parent, string n, Vector3 pos, float[] r, float[] y, float wall, Material m)
    {
        var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
        go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(LatheShellMesh(r, y, 32, wall), n + "_" + parent.name); go.GetComponent<MeshRenderer>().sharedMaterial = m; return go;
    }

    static float ProfileR(float[] r, float[] y, float h)
    {
        for (int i = 1; i < y.Length; i++) if (y[i] > y[i - 1] && h <= y[i]) return Mathf.Lerp(r[i - 1], r[i], (h - y[i - 1]) / (y[i] - y[i - 1]));
        return r[r.Length - 1];
    }

    static void PaintBands(Transform parent, string n, Vector3 pos, float[] r, float[] y, float[][] bands, Material mat)
    {
        for (int i = 0; i < bands.Length; i++)
        {
            float y0 = bands[i][0], y1 = bands[i][1];
            var go = new GameObject(n + "Paint" + i, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = pos + Vector3.up * y0;
            go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(LatheMesh(new[] { ProfileR(r, y, y0) + 0.03f, ProfileR(r, y, y1) + 0.03f }, new[] { 0f, y1 - y0 }, 32), n + "Paint" + i + "_" + parent.name);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    static GameObject LatheObj(Transform parent, string n, Vector3 pos, float[] r, float[] y, Material m)
    {
        var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
        go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(LatheMesh(r, y, 32), n + "_" + parent.name); go.GetComponent<MeshRenderer>().sharedMaterial = m; return go;
    }

    /// <summary>旧工厂：两根高烟囱 + 反应堆（圆顶安全壳）+ 双曲线冷却塔，脚下一座低矮厂房。</summary>
    static Transform BuildSite(Transform parent, Vector3 pos, float yaw, Material steel, Material amber)
    {
        var root0 = new GameObject("Site_OldFactory"); root0.transform.SetParent(parent, false); root0.transform.position = pos; root0.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var g = new GameObject("Content"); g.transform.SetParent(root0.transform, false); g.transform.localPosition = new Vector3(-1.3f, 0f, 0f);
        // 烟囱：粗高、微收分，顶端一圈琥珀色警示带
        float[] cr = { 0.8f, 0.68f, 0.55f, 0.48f, 0.55f, 0.48f }; float[] cy = { 0f, 2.4f, 4.8f, 7.0f, 7.0f, 7.5f };
        LatheShellObj(g.transform, "Chimney1", new Vector3(-2.0f, 0f, 0.7f), cr, cy, 0.16f, steel);
        PaintBands(g.transform, "Chimney1", new Vector3(-2.0f, 0f, 0.7f), cr, cy, new[] { new[] { 4.3f, 5.3f }, new[] { 6.0f, 7.0f } }, sPaint);
        LatheShellObj(g.transform, "Chimney2", new Vector3(-1.5f, 0f, -1.4f), new[] { 0.68f, 0.55f, 0.42f, 0.38f }, new[] { 0f, 2.2f, 4.7f, 5.5f }, 0.14f, steel);
        PaintBands(g.transform, "Chimney2", new Vector3(-1.5f, 0f, -1.4f), new[] { 0.68f, 0.55f, 0.42f, 0.38f }, new[] { 0f, 2.2f, 4.7f, 5.5f }, new[] { new[] { 2.9f, 3.7f }, new[] { 4.4f, 5.2f } }, sPaint);
        // 反应堆：厚圆筒 + 半球顶，顶上一点发光
        Prim(g.transform, PrimitiveType.Cylinder, "Reactor", new Vector3(0.9f, 1.5f, 0f), new Vector3(3.4f, 1.5f, 3.4f), steel);
        Prim(g.transform, PrimitiveType.Sphere, "ReactorDome", new Vector3(0.9f, 3.0f, 0f), new Vector3(3.4f, 1.7f, 3.4f), steel);
        // 冷却塔：腰部收窄的双曲线筒
        LatheShellObj(g.transform, "CoolingTower", new Vector3(4.0f, 0f, -0.9f), new[] { 1.5f, 1.2f, 0.95f, 1.0f, 1.2f }, new[] { 0f, 1.1f, 2.2f, 3.3f, 4.2f }, 0.3f, steel);
        // 圆底座 + 一圈圆形围栏（南面留个门口）
        var ringT = AmberRing(g.transform, 4.9f, amber); ringT.localPosition = new Vector3(1.35f, 0.02f, 0f); return root0.transform;
    }

    static TMPro.TMP_FontAsset FindGarbledFont()
    {
        string dir = "Assets/_Project/UIUX/Fonts/TMP/";
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { dir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.Contains("ع")) return AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(p);     // 物品详情 DATA 用的那款（文件名里带乱码字符）
        }
        return null;
    }

    static void BuildMarker(Transform root, string raidId, string siteName)
    {
        var site = root.Find(siteName); if (site == null) return;
        Bounds b = default; bool first = true;
        foreach (var r in site.GetComponentsInChildren<MeshRenderer>()) { if (r.name == "NodeRing") continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        float h = 3f; var go = new GameObject("Marker_" + raidId); go.transform.SetParent(root, false); go.transform.position = new Vector3(b.center.x, b.max.y + h, b.center.z);
        var m = go.AddComponent<SkyPrisonRaidDioramaMarker>(); m.raidId = raidId; m.height = h;
        var face = new GameObject("Face"); face.transform.SetParent(go.transform, false); m.face = face.transform;
        var hexGo = new GameObject("Hex"); hexGo.transform.SetParent(face.transform, false); hexGo.transform.localScale = Vector3.one * 1.25f;
        m.hex = hexGo.AddComponent<SpriteRenderer>(); m.hex.sprite = SkyPrisonRaidDioramaMarker.HexSprite(); m.hex.sortingOrder = 50;
        // 引线：从六边形斜 45° 引出，再接一条水平线；坐标文字坐在水平线上面（左对齐）
        var diag = new GameObject("Leader"); diag.transform.SetParent(face.transform, false); diag.transform.localPosition = new Vector3(0.49f, 0.49f, 0f); diag.transform.localRotation = Quaternion.Euler(0f, 0f, 45f); diag.transform.localScale = new Vector3(1.76f, 0.06f, 1f);
        m.leader = diag.AddComponent<SpriteRenderer>(); m.leader.sprite = SkyPrisonRaidDioramaMarker.LineSpriteH(); m.leader.sortingOrder = 49;
        var ul = new GameObject("Underline"); ul.transform.SetParent(face.transform, false); ul.transform.localPosition = new Vector3(1.72f, 1.72f, 0f); ul.transform.localScale = new Vector3(9.6f, 0.06f, 1f);
        m.underline = ul.AddComponent<SpriteRenderer>(); m.underline.sprite = SkyPrisonRaidDioramaMarker.LineSpriteH(); m.underline.sortingOrder = 49;
        var tg = new GameObject("Label"); tg.transform.SetParent(face.transform, false); tg.transform.localPosition = new Vector3(1.9f, 1.82f, 0f);
        var tmp = tg.AddComponent<TMPro.TextMeshPro>(); tmp.text = "RAID"; tmp.fontSize = 3.3f; tmp.alignment = TMPro.TextAlignmentOptions.BottomLeft; tmp.enableWordWrapping = false;
        tmp.rectTransform.pivot = new Vector2(0f, 0f); tmp.rectTransform.sizeDelta = new Vector2(9.2f, 2.2f); tmp.sortingOrder = 51;
        var font = FindGarbledFont(); if (font != null) tmp.font = font;
        m.label = tmp;
        var col = face.AddComponent<BoxCollider>(); col.size = new Vector3(11.5f, 4.2f, 0.4f); col.center = new Vector3(5.2f, 1.4f, 0f);
        foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = SkyPrisonRaidDioramaMarker.MarkerLayer;   // 单独一层，由专门的相机画到不模糊的 RT 里
    }

    // ── 资源 ──
    static Mesh SaveMesh(Mesh m, string name)
    {
        string p = Dir + "/Meshes/" + name + ".asset"; AssetDatabase.DeleteAsset(p); m.name = name; AssetDatabase.CreateAsset(m, p); return AssetDatabase.LoadAssetAtPath<Mesh>(p);
    }

    static Material Mat(string name, Shader sh, Color baseCol, float metallic, float smooth, Texture baseTex, Color emission, Texture emissionTex)
    {
        string p = Dir + "/Materials/M_" + name + ".mat"; AssetDatabase.DeleteAsset(p);
        var m = new Material(sh) { name = "M_" + name };
        m.SetColor("_BaseColor", baseCol); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
        if (baseTex != null) m.SetTexture("_BaseMap", baseTex);
        if (emission.maxColorComponent > 0.001f)
        {
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (emissionTex != null) m.SetTexture("_EmissionMap", emissionTex);
        }
        AssetDatabase.CreateAsset(m, p); return AssetDatabase.LoadAssetAtPath<Material>(p);
    }

    static Texture2D MakeRamp()
    {
        string p = Dir + "/Ramp.png"; var t = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        Color[] stops = { new Color(0.30f, 0.33f, 0.40f), new Color(0.46f, 0.47f, 0.52f), new Color(0.78f, 0.66f, 0.45f), new Color(0.95f, 0.78f, 0.42f) };
        for (int x = 0; x < 256; x++) { float f = x / 255f * 3f; int i = Mathf.Min(2, (int)f); t.SetPixel(x, 0, Color.Lerp(stops[i], stops[i + 1], f - i)); }
        File.WriteAllBytes(p, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); AssetDatabase.ImportAsset(p);
        var ti = (TextureImporter)AssetImporter.GetAtPath(p); ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static VolumeProfile MakeProfile()
    {
        string p = Dir + "/DioramaPostFX.asset"; AssetDatabase.DeleteAsset(p);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, p);
        T Add<T>() where T : VolumeComponent { var c = prof.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, prof); return c; }
        var bloom = Add<Bloom>(); bloom.threshold.Override(1.1f); bloom.intensity.Override(0.8f); bloom.scatter.Override(0.68f);
        var vig = Add<Vignette>(); vig.intensity.Override(0.08f); vig.smoothness.Override(0.4f);
        var tm = Add<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
        var ca = Add<ColorAdjustments>(); ca.postExposure.Override(-0.5f); ca.contrast.Override(10f); ca.saturation.Override(-4f); ca.colorFilter.Override(new Color(0.68f, 0.85f, 1.15f));
        var wb = Add<WhiteBalance>(); wb.temperature.Override(-48f); wb.tint.Override(-10f);
        var dof = Add<DepthOfField>(); dof.mode.Override(DepthOfFieldMode.Gaussian); dof.focusDistance.Override(190f); dof.focalLength.Override(250f); dof.aperture.Override(1.6f);
        dof.gaussianStart.Override(160f); dof.gaussianEnd.Override(380f); dof.gaussianMaxRadius.Override(1.0f);
        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets(); return prof;
    }
}
