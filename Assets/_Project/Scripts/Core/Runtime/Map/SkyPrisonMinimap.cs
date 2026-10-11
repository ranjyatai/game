using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 战术地图的运行时数据(单例，跨场景常驻，每次切场景重置)：
///   · 地图范围：取场景里的 SkyPrisonMapBounds；
///   · 探索记录：把地图切成格子，玩家视野半径内的格子记为「走过」，存成一张 R8 贴图给着色器当遮罩；
///   · 地形：第一次要看的时候用一台临时正交相机从正上方抓一张(只拍 World3D 层：地面/墙/建筑，不含角色和战争迷雾)；
///   · 4 个玩家标记：只在本局有效；窗口没开时在屏幕上显示头顶小标 + 距离(在屏幕外就贴边)。
/// 窗口见 SkyPrisonMinimapWindow。
/// </summary>
public sealed class SkyPrisonMinimap : MonoBehaviour
{
    public const int MarkerSlots = 4;
    // 标记默认是白色(靠编号区分)；玩家可以在几种候选色里给每个槽换色(克制：5 种，不做色盘)
    public static readonly Color[] Palette =
    {
        Color.white,                          // 默认
        new Color(1f, 0.82f, 0.30f, 1f),      // 琥珀
        new Color(1f, 0.38f, 0.34f, 1f),      // 红
        new Color(0.40f, 0.90f, 1f, 1f),      // 青
        new Color(0.50f, 1f, 0.60f, 1f),      // 绿
    };
    const string ColorPrefsKey = "minimap_marker_colors";

    /// <summary>每个槽当前选的候选色下标(颜色属于槽位：换位置 / 撤销再放，颜色保留；存本地，下次进游戏还在)。</summary>
    public readonly int[] MarkerColorIndex = new int[MarkerSlots];
    public Color MarkerColor(int slot) => Palette[Mathf.Abs(MarkerColorIndex[Mathf.Clamp(slot, 0, MarkerSlots - 1)]) % Palette.Length];

    public void CycleMarkerColor(int slot)
    {
        if (slot < 0 || slot >= MarkerSlots) return;
        MarkerColorIndex[slot] = (MarkerColorIndex[slot] + 1) % Palette.Length;
        ApplyWorldBeamColor(slot);
        PlayerPrefs.SetString(ColorPrefsKey, string.Join(",", MarkerColorIndex)); PlayerPrefs.Save();
    }

    void LoadColors()
    {
        try
        {
            var parts = PlayerPrefs.GetString(ColorPrefsKey, "").Split(',');
            for (int i = 0; i < MarkerSlots && i < parts.Length; i++) if (int.TryParse(parts[i], out int v)) MarkerColorIndex[i] = Mathf.Abs(v) % Palette.Length;
        }
        catch { }
    }

    public static SkyPrisonMinimap Instance { get; private set; }

    public bool HasMap { get; private set; }
    public Bounds MapBounds { get; private set; }
    public Texture2D Explored { get; private set; }
    public RenderTexture Terrain { get; private set; }
    public readonly bool[] MarkerSet = new bool[MarkerSlots];
    public readonly Vector3[] MarkerPos = new Vector3[MarkerSlots];

    const float BaseVisionRadius = 11f;       // 白天视野半径(夜间乘 SkyPrisonVisionManager.NightPlayerVisionMultiplier，即 11→7)
    const int MaxCells = 384;                 // 探索格子最长一边的格数

    byte[] _exp; int _cx, _cz; float _cell;
    float _nextTrack, _nextFind;
    Vector3 _lastTracked = new Vector3(1e9f, 0f, 0f);
    bool _dirty;

    // 窗口外的标记提示
    RectTransform _hudRoot;
    readonly Image[] _hudIcon = new Image[MarkerSlots];
    readonly TMP_Text[] _hudDist = new TMP_Text[MarkerSlots];
    readonly TMP_Text[] _hudNum = new TMP_Text[MarkerSlots];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[Minimap]");
        DontDestroyOnLoad(go);
        go.AddComponent<SkyPrisonMinimap>();
    }

    void Awake()
    {
        Instance = this;
        LoadColors();
        SceneManager.sceneLoaded += OnSceneLoaded;
        BuildHud();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
        ReleaseMapData();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        ReleaseMapData();
        _raidStart = -1f;
        for (int i = 0; i < MarkerSlots; i++) ClearMarker(i);
    }

    void ReleaseMapData()
    {
        HasMap = false; _exp = null; _lastTracked = new Vector3(1e9f, 0f, 0f);
        Heights = null;
        if (Explored != null) { Destroy(Explored); Explored = null; }
        if (Terrain != null) { Terrain.Release(); Destroy(Terrain); Terrain = null; }
    }

    // ── 查询 / 坐标换算 ───────────────────────────────────────
    /// <summary>世界坐标 → 地图 uv(0~1，x = 世界 X，y = 世界 Z)。</summary>
    public Vector2 WorldToUv(Vector3 p)
    {
        var b = MapBounds;
        return new Vector2((p.x - b.min.x) / Mathf.Max(0.01f, b.size.x), (p.z - b.min.z) / Mathf.Max(0.01f, b.size.z));
    }

    public Vector3 UvToWorld(Vector2 uv, float y)
    {
        var b = MapBounds;
        return new Vector3(b.min.x + uv.x * b.size.x, y, b.min.z + uv.y * b.size.z);
    }

    public bool IsExplored(Vector3 p)
    {
        if (!HasMap || _exp == null) return false;
        int x = Mathf.FloorToInt((p.x - MapBounds.min.x) / _cell), z = Mathf.FloorToInt((p.z - MapBounds.min.z) / _cell);
        return x >= 0 && z >= 0 && x < _cx && z < _cz && _exp[z * _cx + x] > 128;
    }

    // ── 标记 ─────────────────────────────────────────────────
    readonly GameObject[] _worldBeam = new GameObject[MarkerSlots];

    /// <summary>
    /// 标记该落在的地面高度——用游戏里统一的地面查询(GroundQueryService：模型地面标记 + BaseGroundBlock)，角色 / 掉落物判定地面都靠它，
    /// 所以光柱的根一定落在角色能站的地面上，不会落在墙顶(高度图取到的是结构最高点)或悬在空中。
    /// refY：参考高度(玩家当前高度)，查询是从它上方 4 米往下打射线；查不到(洞 / 虚空 / 没有这个服务)时退回 refY。
    /// </summary>
    public float GroundY(float x, float z, float refY)
    {
        var gs = GroundQueryService.Active;
        if (gs != null && gs.TryQueryGround(new Vector3(x, refY, z), out GroundQueryResult r) && r.hasGround) return r.groundY;
        return refY;
    }

    /// <summary>放 / 移动标记：同时在游戏世界里该处的地面立一条光柱 + 地面光环(World3D 层，主相机能看到)。</summary>
    public void PlaceMarker(int slot, Vector3 world)
    {
        if (slot < 0 || slot >= MarkerSlots) return;
        MarkerSet[slot] = true; MarkerPos[slot] = world;
        if (_worldBeam[slot] == null)
        {
            int layer = LayerMask.NameToLayer("World3D");
            // 每个槽自己一份材质(光柱 / 光环 / 立体数字)，这样各自能有各自的颜色
            _worldMats[slot] = new[] { new Material(SkyPrisonMinimapBeam.WorldBeamMaterial), new Material(SkyPrisonMinimapBeam.WorldRingMaterial), new Material(SkyPrisonMinimapBeam.WorldDigitMaterial) };
            _worldBeam[slot] = SkyPrisonMinimapBeam.Create("MapMarkerBeam" + (slot + 1), layer < 0 ? 0 : layer,
                _worldMats[slot][0], _worldMats[slot][1], _worldMats[slot][2], slot + 1);
            ApplyWorldBeamColor(slot);
        }
        _worldBeam[slot].transform.position = world;
        SkyPrisonMinimapBeam.SetShape(_worldBeam[slot], 1.6f, 80f);
    }

    public void ClearMarker(int slot)
    {
        if (slot < 0 || slot >= MarkerSlots) return;
        MarkerSet[slot] = false;
        if (_worldBeam[slot] != null) { Destroy(_worldBeam[slot]); _worldBeam[slot] = null; }
        if (_worldMats[slot] != null) { foreach (var m in _worldMats[slot]) if (m != null) Destroy(m); _worldMats[slot] = null; }
    }

    readonly Material[][] _worldMats = new Material[MarkerSlots][];

    void ApplyWorldBeamColor(int slot)
    {
        if (_worldMats[slot] == null) return;
        Color c = MarkerColor(slot);
        foreach (var m in _worldMats[slot]) if (m != null) m.SetColor("_Color", c);
    }

    // ── 初始化 / 探索 ─────────────────────────────────────────
    /// <summary>
    /// 现在是不是在 RAID 里：小地图只在 RAID 有用(主城 / 基地不记录探索、不能打开、屏幕上也不显示标记提示)。
    /// 用项目现有的数据判断：AFP 的「局」会话开着(进 RAID 时建立)，或者出发时记下的目标场景就是当前场景；F7 调试放开时一律算在 RAID 里，方便在主城测试。
    /// </summary>
    /// <summary>true = 小地图只在 RAID 里能用；false = 主城 / 基地也能用(方便测试时放开)。下面的判定逻辑不用动。</summary>
    public const bool RaidOnly = true;

    public bool InRaid
    {
        get
        {
            if (!RaidOnly) return true;
            if (SkyPrisonAfpAbsorbShield.DebugForceUnlock) return true;
            var afp = AFPRuntime.Instance;
            if (afp != null && afp.IsInSession) return true;
            string s = SkyPrisonRaidRunContext.Scene;
            return !string.IsNullOrEmpty(s) && s == SceneManager.GetActiveScene().name;
        }
    }

    float _raidStart = -1f;
    /// <summary>进入这个关卡多久了(秒)：从第一次判定「在 RAID 里」起算，用游戏时间(暂停时不走)。不在 RAID 里是 0。</summary>
    public float RaidElapsed => _raidStart < 0f ? 0f : Mathf.Max(0f, Time.time - _raidStart);

    void Update()
    {
        bool inRaid = InRaid;
        if (!inRaid) _raidStart = -1f;
        else if (_raidStart < 0f) _raidStart = Time.time;

        if (!inRaid) { }                                      // 不在 RAID：什么都不记
        else if (!HasMap)
        {
            if (Time.unscaledTime >= _nextFind) { _nextFind = Time.unscaledTime + 0.5f; TryInit(); }
        }
        else Track();
        for (int i = 0; i < MarkerSlots; i++) SkyPrisonMinimapBeam.Spin(_worldBeam[i], Time.time, i);      // 游戏世界里的立体数字绕竖直轴旋转
        UpdateHud();
    }

    void TryInit()
    {
        var mb = FindFirstObjectByType<SkyPrisonMapBounds>();
        if (mb == null) return;
        var b = mb.ResolvedBounds;
        if (b.size.x < 10f || b.size.z < 10f) { mb.RefreshBounds(); b = mb.ResolvedBounds; }
        if (b.size.x < 10f || b.size.z < 10f) return;

        MapBounds = b;
        _cell = Mathf.Max(1f, Mathf.Max(b.size.x, b.size.z) / MaxCells);
        _cx = Mathf.Clamp(Mathf.CeilToInt(b.size.x / _cell), 8, 1024);
        _cz = Mathf.Clamp(Mathf.CeilToInt(b.size.z / _cell), 8, 1024);
        _exp = new byte[_cx * _cz];
        Explored = new Texture2D(_cx, _cz, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "MinimapExplored" };
        Explored.SetPixelData(_exp, 0); Explored.Apply(false);
        HasMap = true;
    }

    void Track()
    {
        if (Time.unscaledTime < _nextTrack) return;
        _nextTrack = Time.unscaledTime + 0.12f;
        var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (unit == null) return;
        Vector3 p = unit.transform.position;
        if ((p - _lastTracked).sqrMagnitude > _cell * _cell * 0.25f)
        {
            _lastTracked = p;
            float r = BaseVisionRadius * (SkyPrisonRaidRunContext.Night ? SkyPrisonVisionManager.NightPlayerVisionMultiplier : 1f);
            MarkVisible(p, r);
        }
        if (_dirty) { Explored.SetPixelData(_exp, 0); Explored.Apply(false); _dirty = false; }
        if (Time.unscaledTime >= _nextFraction)       // 探索比例：每秒数一次(几万字节，很便宜)
        {
            _nextFraction = Time.unscaledTime + 1f;
            int n = 0; for (int i = 0; i < _exp.Length; i++) if (_exp[i] > 128) n++;
            ExploredFraction = n / (float)Mathf.Max(1, _exp.Length);
        }
    }

    /// <summary>已探索的格子占整张图的比例(0~1)，战术地图的「探索度」用。</summary>
    public float ExploredFraction { get; private set; }
    float _nextFraction;

    /// <summary>
    /// 视线扇形：从玩家向四周发一圈射线，每条射线只标到「战争迷雾用的挡视线物」(带 blockVision 的墙)那里为止(连墙本身那一格一起标，墙看得见)。
    /// 所以房间外面不会把墙里面的房间标成走过。没有视野管理器时退回正圆。
    /// </summary>
    void MarkVisible(Vector3 p, float radius)
    {
        MarkCircle(p, Mathf.Min(radius, _cell * 1.6f));          // 脚下一小圈永远标上，免得贴墙站着时什么都没有
        var vm = SkyPrisonVisionManager.Instance;
        if (vm == null) { MarkCircle(p, radius); return; }

        Vector3 o = p + Vector3.up * 1.0f;
        int rays = Mathf.Clamp(Mathf.CeilToInt(radius * 2f * Mathf.PI / (_cell * 0.8f)), 48, 240);
        float stepLen = _cell * 0.6f;
        for (int k = 0; k < rays; k++)
        {
            float a = k * (Mathf.PI * 2f / rays);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float len = radius;
            if (vm.TryGetVisionBlockDistance(o, dir, radius, out float bd)) len = Mathf.Min(radius, bd + _cell * 0.5f);
            for (float s = 0f; s <= len; s += stepLen) MarkCell(p.x + dir.x * s, p.z + dir.z * s);
            MarkCell(p.x + dir.x * len, p.z + dir.z * len);
        }
    }

    void MarkCell(float wx, float wz)
    {
        int x = Mathf.FloorToInt((wx - MapBounds.min.x) / _cell), z = Mathf.FloorToInt((wz - MapBounds.min.z) / _cell);
        if (x < 0 || z < 0 || x >= _cx || z >= _cz) return;
        int i = z * _cx + x;
        if (_exp[i] != 255) { _exp[i] = 255; _dirty = true; }
    }

    void MarkCircle(Vector3 p, float radius)
    {
        int cx0 = Mathf.FloorToInt((p.x - radius - MapBounds.min.x) / _cell), cx1 = Mathf.FloorToInt((p.x + radius - MapBounds.min.x) / _cell);
        int cz0 = Mathf.FloorToInt((p.z - radius - MapBounds.min.z) / _cell), cz1 = Mathf.FloorToInt((p.z + radius - MapBounds.min.z) / _cell);
        float r2 = radius * radius;
        for (int z = Mathf.Max(0, cz0); z <= Mathf.Min(_cz - 1, cz1); z++)
            for (int x = Mathf.Max(0, cx0); x <= Mathf.Min(_cx - 1, cx1); x++)
            {
                float wx = MapBounds.min.x + (x + 0.5f) * _cell - p.x, wz = MapBounds.min.z + (z + 0.5f) * _cell - p.z;
                if (wx * wx + wz * wz > r2) continue;
                int i = z * _cx + x;
                if (_exp[i] != 255) { _exp[i] = 255; _dirty = true; }
            }
    }

    // ── 地形抓图 ─────────────────────────────────────────────
    /// <summary>第一次要看地图时抓一张俯视图(场景里的地形装饰是运行时才生成的，所以不在进图时抓)。</summary>
    public bool EnsureTerrain()
    {
        if (Terrain != null) return true;
        if (!HasMap) return false;
        var b = MapBounds;
        float aspect = b.size.x / b.size.z;
        int w = aspect >= 1f ? 2048 : Mathf.Max(256, Mathf.RoundToInt(2048f * aspect));
        int h = aspect >= 1f ? Mathf.Max(256, Mathf.RoundToInt(2048f / aspect)) : 2048;
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "MinimapTerrain", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false };
        // Unity 6 的 RenderGraph 看的是 depthStencilFormat 而不是旧的 depth 位深参数，不显式设会被判成非法输出(见 ScreenSpaceOutlineRTManager 里的注释)
        rt.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt;
        rt.Create();

        int world3D = LayerMask.NameToLayer("World3D");
        var go = new GameObject("[MinimapCapture]");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = b.size.z * 0.5f;
        cam.transform.position = new Vector3(b.center.x, b.max.y + 300f, b.center.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);       // 朝下，屏幕右 = 世界 +X，屏幕上 = 世界 +Z
        cam.nearClipPlane = 0.1f; cam.farClipPlane = b.size.y + 700f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.cullingMask = world3D >= 0 ? 1 << world3D : ~0;
        cam.allowHDR = false; cam.allowMSAA = false;
        cam.targetTexture = rt;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        cam.Render();
        cam.targetTexture = null;
        Destroy(go);
        Terrain = rt;
        return true;
    }

    // ── 高度图 + 3D 全息网格 ──────────────────────────────────────
    public float[] Heights { get; private set; }
    public bool HasHeightField => Heights != null;
    public int HfX { get; private set; }
    public int HfZ { get; private set; }
    public float HfMin { get; private set; }
    public float HfMax { get; private set; }
    /// <summary>3D 全息里的垂直夸张：几百米宽的地图上墙只有十来米，不夸张就是一张平板。只影响显示，HeightAt 仍是真实高度。</summary>
    public const float VisualHeightScale = 2.5f;

    /// <summary>第一次要看 3D 时生成：从地图上方对每个采样点打一条向下的射线，量出地面 / 墙 / 建筑碰撞体的高度，
    /// 再拱成一张网格(uv 和俯视抓图、探索遮罩是同一套映射，着色器可以直接采样)。只用碰撞体，所以屋顶下的室内、桥下这类悬空结构表现不出来。</summary>
    public bool EnsureHeightField()
    {
        if (Heights != null) return true;
        if (!HasMap) return false;
        var b = MapBounds;
        float step = Mathf.Max(1.2f, Mathf.Max(b.size.x, b.size.z) / 192f);
        HfX = Mathf.Clamp(Mathf.CeilToInt(b.size.x / step) + 1, 8, 256);
        HfZ = Mathf.Clamp(Mathf.CeilToInt(b.size.z / step) + 1, 8, 256);

        // 取高度的来源：把「构成地形结构的碰撞体」直接铺到格子上(射线会漏掉比格子还薄的墙，所以不用射线)。
        // 这个项目里墙 / 建筑的几何在 OccluderProbe(遮挡探针盒，墙高 12 米)和 World3D 层上，GroundPhysics / StaticObstacle 几乎是空的；
        // 地图边界墙(MapBoundary_*)是 404 米长的大盒子，会在边缘围一圈高墙，要排除。
        int mask = LayerMask.GetMask("OccluderProbe", "World3D", "WalkableProbe", "StaticObstacle", "GroundPhysics", "Obstacle_UnitOnly");
        var h = new float[HfX * HfZ];
        for (int i = 0; i < h.Length; i++) h[i] = float.NaN;
        float cellX = b.size.x / (HfX - 1f), cellZ = b.size.z / (HfZ - 1f), tol = Mathf.Max(cellX, cellZ) * 0.5f;      // 轮廓外扩半格：薄墙也至少占一格
        float floorY = float.MaxValue, hi = float.MinValue;
        foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c == null || c.isTrigger || !c.enabled || ((1 << c.gameObject.layer) & mask) == 0) continue;
            if (c.name.StartsWith("MapBoundary")) continue;
            Bounds cb = c.bounds;
            if (cb.size.x > b.size.x * 0.8f && cb.size.z > b.size.z * 0.8f) continue;       // 铺满整张图的大盒子(外壳 / 空气墙)
            bool analytic = c is BoxCollider || c is SphereCollider || c is CapsuleCollider;      // 这三种能用 ClosestPoint 精确判断覆盖
            float topY = cb.max.y;
            floorY = Mathf.Min(floorY, cb.min.y);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((cb.min.x - tol - b.min.x) / cellX)), x1 = Mathf.Min(HfX - 1, Mathf.CeilToInt((cb.max.x + tol - b.min.x) / cellX));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((cb.min.z - tol - b.min.z) / cellZ)), z1 = Mathf.Min(HfZ - 1, Mathf.CeilToInt((cb.max.z + tol - b.min.z) / cellZ));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float wx = b.min.x + x * cellX, wz = b.min.z + z * cellZ;
                    bool covered;
                    float cellTop = topY;
                    if (analytic)
                    {
                        Vector3 cp = c.ClosestPoint(new Vector3(wx, cb.center.y, wz));
                        covered = (new Vector2(cp.x - wx, cp.z - wz)).sqrMagnitude <= tol * tol;
                    }
                    else      // 网格碰撞体：就在格子中心从上往下对这一个碰撞体打一条射线
                    {
                        covered = c.Raycast(new Ray(new Vector3(wx, cb.max.y + 1f, wz), Vector3.down), out RaycastHit mh, cb.size.y + 2f);
                        if (covered) cellTop = mh.point.y;
                    }
                    if (!covered) continue;
                    int idx = z * HfX + x;
                    if (float.IsNaN(h[idx]) || cellTop > h[idx]) h[idx] = cellTop;
                    hi = Mathf.Max(hi, cellTop);
                }
        }
        float lo = floorY == float.MaxValue ? b.min.y : floorY;        // 地面高度：所有结构的最低底面
        if (hi < lo) hi = lo + 1f;                                      // 一个碰撞体都没有：压成平板
        for (int i = 0; i < h.Length; i++) if (float.IsNaN(h[i])) h[i] = lo;
        Heights = h; HfMin = lo; HfMax = Mathf.Max(hi, lo + 1f);

        return true;       // 高度图只用来点击拾取 / 标记接地；3D 全息画的是场景里真实的模型，不再用它生成网格
    }

    /// <summary>世界 (x,z) 处的地面高度——取所在格子的高度(和 3D 方块柱一致，不插值)；没有高度图或在范围外时返回最低值。</summary>
    public float HeightAt(float x, float z)
    {
        if (Heights == null) return MapBounds.min.y;
        int ix = Mathf.RoundToInt((x - MapBounds.min.x) / Mathf.Max(0.01f, MapBounds.size.x) * (HfX - 1));
        int iz = Mathf.RoundToInt((z - MapBounds.min.z) / Mathf.Max(0.01f, MapBounds.size.z) * (HfZ - 1));
        if (ix < 0 || iz < 0 || ix >= HfX || iz >= HfZ) return HfMin;
        return Heights[iz * HfX + ix];
    }

    // ── 窗口外的标记提示 ──────────────────────────────────────
    void BuildHud()
    {
        var cgo = new GameObject("MarkerHud");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 600;
        _hudRoot = (RectTransform)cgo.transform;
        for (int i = 0; i < MarkerSlots; i++)
        {
            var go = new GameObject("M" + i, typeof(RectTransform));
            go.transform.SetParent(_hudRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0f);      // 图钉的尖角是锚点
            var img = go.AddComponent<Image>(); img.raycastTarget = false;
            SkyPrisonMinimapIcons.StyleMarker(img, i);
            _hudNum[i] = SkyPrisonMinimapIcons.AddMarkerNumber(rt, i + 1, 22);          // 屏幕边缘的标记提示：同一种图钉，里面写编号
            _hudIcon[i] = img;

            var tgo = new GameObject("Dist", typeof(RectTransform));
            tgo.transform.SetParent(rt, false);
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f); tr.pivot = new Vector2(0.5f, 1f); tr.anchoredPosition = new Vector2(0f, -2f);
            var t = tgo.AddComponent<TextMeshProUGUI>();                    // 游戏字体(不是系统字体)
            if (SkyPrisonMinimapIcons.GameFont != null) t.font = SkyPrisonMinimapIcons.GameFont;
            t.alignment = TextAlignmentOptions.Top; t.color = Color.white; t.raycastTarget = false;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
            tr.sizeDelta = new Vector2(240f, 40f);
            _hudDist[i] = t;
            go.SetActive(false);
        }
    }

    void UpdateHud()
    {
        var cam = Camera.main;
        var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        bool windowOpen = SkyPrisonMinimapWindow.IsOpen, inRaidNow = InRaid;
        float px = Screen.height / 1080f, margin = 60f * px;
        for (int i = 0; i < MarkerSlots; i++)
        {
            bool show = MarkerSet[i] && !windowOpen && cam != null && inRaidNow;
            if (_hudIcon[i].gameObject.activeSelf != show) _hudIcon[i].gameObject.SetActive(show);
            if (!show) continue;

            Vector3 sp = cam.WorldToScreenPoint(MarkerPos[i] + Vector3.up * 1.5f);
            if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }        // 在相机背后：镜像过来，让它贴到对应那一侧的边上
            sp.x = Mathf.Clamp(sp.x, margin, Screen.width - margin);
            sp.y = Mathf.Clamp(sp.y, margin, Screen.height - margin);
            var rt = (RectTransform)_hudIcon[i].transform;
            rt.anchoredPosition = new Vector2(sp.x, sp.y);
            rt.sizeDelta = new Vector2(30f * px, 30f * px * SkyPrisonMinimapIcons.PinAspect);
            _hudDist[i].fontSize = Mathf.RoundToInt(22f * px);
            Color mc = MarkerColor(i);                                   // 玩家选的颜色：图钉描边 / 编号 / 距离一起变
            _hudIcon[i].color = mc; _hudDist[i].color = mc; if (_hudNum[i] != null) _hudNum[i].color = mc;
            if (unit != null)
            {
                Vector3 d = MarkerPos[i] - unit.transform.position; d.y = 0f;
                _hudDist[i].text = d.magnitude.ToString("0.0") + "m";      // 距离精确到小数点后 1 位
            }
        }
    }
}

/// <summary>
/// 标记的光柱：一束竖直的加算光柱(两片交叉的竖面) + 地面光环。游戏世界里的光柱和战术地图 3D 舞台里的光柱共用同一套网格和着色器，只是亮度不同。
/// </summary>
public static class SkyPrisonMinimapBeam
{
    static Mesh _beamMesh, _ringMesh;
    static Material _worldBeam, _worldRing, _stageBeam, _stageRing;

    static Material _worldDigit, _stageDigit;
    public static Material WorldBeamMaterial => _worldBeam != null ? _worldBeam : (_worldBeam = MakeMaterial(1.4f, 0f));
    public static Material WorldRingMaterial => _worldRing != null ? _worldRing : (_worldRing = MakeMaterial(1.1f, 1f));
    public static Material WorldDigitMaterial => _worldDigit != null ? _worldDigit : (_worldDigit = MakeMaterial(1.1f, 2f));
    public static Material StageBeamMaterial => _stageBeam != null ? _stageBeam : (_stageBeam = MakeMaterial(1.5f, 0f));
    public static Material StageRingMaterial => _stageRing != null ? _stageRing : (_stageRing = MakeMaterial(1.2f, 1f));
    public static Material StageDigitMaterial => _stageDigit != null ? _stageDigit : (_stageDigit = MakeMaterial(1.3f, 2f));

    static Material MakeMaterial(float intensity, float mode)      // mode：0 光柱 / 1 地面光环 / 2 立体数字
    {
        var sh = Resources.Load<Shader>("UI/SkyPrisonMinimapBeam");
        if (sh == null) sh = Shader.Find("SkyPrison/MinimapBeam");
        var m = new Material(sh);
        m.SetFloat("_Intensity", intensity); m.SetFloat("_Mode", mode); m.SetColor("_Color", Color.white);
        if (mode > 1.5f)
        {
            // 立体数字：模板位 64 当「这个像素已经画过」的标记(NotEqual = 6, Replace = 2)，段与段重叠 / 双面叠加的地方只画一次，亮度才一致
            m.SetFloat("_StencilRef", 64f); m.SetFloat("_StencilMask", 64f);
            m.SetFloat("_StencilComp", 6f); m.SetFloat("_StencilPass", 2f);
        }
        return m;
    }

    // ── 立体数字(1–4)：用方块拼的七段数码管，真正有厚度的 3D 几何；原点在数字中心，高 1、宽 0.6 ──
    static readonly Mesh[] _digitMesh = new Mesh[5];
    // 段：0=上横 1=右上竖 2=右下竖 3=下横 4=左下竖 5=左上竖 6=中横
    static readonly int[][] DigitSegments = { null, new[] { 1, 2 }, new[] { 0, 1, 6, 4, 3 }, new[] { 0, 1, 6, 2, 3 }, new[] { 5, 6, 1, 2 } };

    static Mesh DigitMesh(int n)
    {
        n = Mathf.Clamp(n, 1, 4);
        if (_digitMesh[n] != null) return _digitMesh[n];
        const float W = 0.6f, H = 1f, T = 0.15f, D = 0.15f;
        var verts = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>(); var uvs = new System.Collections.Generic.List<Vector2>();
        System.Action<Vector3, Vector3> box = (c, s) =>
        {
            int k = verts.Count; Vector3 h = s * 0.5f;
            for (int i = 0; i < 8; i++) { verts.Add(c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z)); uvs.Add(Vector2.zero); }
            int[] f = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            foreach (int t in f) tris.Add(k + t);
        };
        foreach (int s in DigitSegments[n])
        {
            if (n == 1) { box(new Vector3(0f, 0f, 0f), new Vector3(T, H, D)); break; }      // 「1」：居中的一根竖
            switch (s)
            {
                case 0: box(new Vector3(0f, H * 0.5f - T * 0.5f, 0f), new Vector3(W, T, D)); break;
                case 1: box(new Vector3(W * 0.5f - T * 0.5f, H * 0.25f, 0f), new Vector3(T, H * 0.5f, D)); break;
                case 2: box(new Vector3(W * 0.5f - T * 0.5f, -H * 0.25f, 0f), new Vector3(T, H * 0.5f, D)); break;
                case 3: box(new Vector3(0f, -H * 0.5f + T * 0.5f, 0f), new Vector3(W, T, D)); break;
                case 4: box(new Vector3(-W * 0.5f + T * 0.5f, -H * 0.25f, 0f), new Vector3(T, H * 0.5f, D)); break;
                case 5: box(new Vector3(-W * 0.5f + T * 0.5f, H * 0.25f, 0f), new Vector3(T, H * 0.5f, D)); break;
                default: box(new Vector3(0f, 0f, 0f), new Vector3(W, T, D)); break;
            }
        }
        var m = new Mesh { name = "MapDigit" + n };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return _digitMesh[n] = m;
    }

    static Mesh BeamMesh()
    {
        if (_beamMesh != null) return _beamMesh;
        var m = new Mesh { name = "MapBeam" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 1f, 0.5f), new Vector3(0f, 1f, -0.5f),
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
        m.RecalculateBounds();
        return _beamMesh = m;
    }

    static Mesh RingMesh()
    {
        if (_ringMesh != null) return _ringMesh;
        var m = new Mesh { name = "MapRing" };
        m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return _ringMesh = m;
    }

    /// <summary>建一束光柱(根物体在地面位置，子物体 Beam / Ring)。layer：游戏世界里用 World3D，3D 舞台里用舞台层。</summary>
    public static GameObject Create(string name, int layer, Material beamMat, Material ringMat, Material digitMat, int number)
    {
        var root = new GameObject(name) { layer = layer };
        var digit = new GameObject("Digit") { layer = layer }; digit.transform.SetParent(root.transform, false);
        digit.AddComponent<MeshFilter>().sharedMesh = DigitMesh(number);
        var dr = digit.AddComponent<MeshRenderer>(); dr.sharedMaterial = digitMat; dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; dr.receiveShadows = false;
        var beam = new GameObject("Beam") { layer = layer }; beam.transform.SetParent(root.transform, false);
        beam.AddComponent<MeshFilter>().sharedMesh = BeamMesh();
        var br = beam.AddComponent<MeshRenderer>(); br.sharedMaterial = beamMat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.receiveShadows = false;
        var ring = new GameObject("Ring") { layer = layer }; ring.transform.SetParent(root.transform, false);
        ring.AddComponent<MeshFilter>().sharedMesh = RingMesh();
        var rr = ring.AddComponent<MeshRenderer>(); rr.sharedMaterial = ringMat; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
        return root;
    }

    /// <summary>width：柱子宽度；height：柱子高度；地面光环直径 = 宽度的 4 倍。</summary>
    public static void SetShape(GameObject root, float width, float height)
    {
        if (root == null) return;
        var beam = root.transform.Find("Beam"); var ring = root.transform.Find("Ring"); var digit = root.transform.Find("Digit");
        if (beam != null) beam.localScale = new Vector3(width, height, width);
        if (ring != null) { ring.localScale = new Vector3(width * 4f, 1f, width * 4f); ring.localPosition = new Vector3(0f, 0.06f, 0f); }
        // 立体数字悬在光柱上、离地面一段距离的地方，大小随柱子宽度
        if (digit != null) { float dh = width * 2.4f; digit.localScale = new Vector3(dh, dh, dh); digit.localPosition = new Vector3(0f, width * 3.4f, 0f); }
    }

    /// <summary>让立体数字绕竖直轴持续缓慢旋转(全息展示台的感觉)；phase 让四个数字错开相位，不要整齐划一。</summary>
    public static void Spin(GameObject root, float time, float phase)
    {
        if (root == null) return;
        var digit = root.transform.Find("Digit");
        if (digit != null) digit.rotation = Quaternion.Euler(0f, time * 70f + phase * 90f, 0f);
    }
}

/// <summary>小地图 / 标记共用的小图标和字体。</summary>
public static class SkyPrisonMinimapIcons
{
    static Font _uiFont;
    public static Font UiFont => _uiFont != null ? _uiFont : (_uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 100));

    static Sprite _disk, _triangle;

    public static Sprite Disk()
    {
        if (_disk != null) return _disk;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) / 0.06f)));
            }
        tex.Apply();
        return _disk = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite _ring;
    /// <summary>细环(吸附高亮用)。</summary>
    public static Sprite Ring()
    {
        if (_ring != null) return _ring;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.06f)));
            }
        tex.Apply();
        return _ring = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    public static Sprite Triangle()
    {
        if (_triangle != null) return _triangle;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 尖朝上：在 y 处的半宽 = (1 - y/N) * N/2
                float half = (1f - y / (float)N) * N * 0.5f;
                float a = Mathf.Clamp01((half - Mathf.Abs(x - N * 0.5f)) / 1.2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return _triangle = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite _diamond;
    public static Sprite Diamond()
    {
        if (_diamond != null) return _diamond;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = (Mathf.Abs(x - N * 0.5f) + Mathf.Abs(y - N * 0.5f)) / (N * 0.5f);      // 0 中心 → 1 边缘(菱形)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) / 0.06f)));
            }
        tex.Apply();
        return _diamond = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>图钉的宽高比(高 / 宽)：六边形框 + 底部一个向下的尖角，贴图 128×160。</summary>
    public const float PinAspect = 1.25f;
    static Sprite _hexPin;

    /// <summary>
    /// 战术图钉：平顶六边形(白色描边 + 深色半透明底)，底部带一个实心的向下尖角——尖角指的是标记的精确位置，所以用它时把图标的 pivot 设成 (0.5, 0)。
    /// 编号写在六边形里(见 AddMarkerNumber)。
    /// </summary>
    public static Sprite HexPin()
    {
        if (_hexPin != null) return _hexPin;
        const int W = 128, H = 160;
        const float cx = 64f, cy = 98f, apothem = 48f, stroke = 4.2f;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float px = Mathf.Abs(x + 0.5f - cx), py = Mathf.Abs(y + 0.5f - cy);
                float sd = Mathf.Max(px * 0.8660254f + py * 0.5f, py) - apothem;                // 平顶正六边形的有向距离(<0 在里面)
                float outline = Mathf.Clamp01(1f - (Mathf.Abs(sd) - stroke * 0.5f));
                float a = outline, v = outline;           // 六边形里面完全透明，只留白色描边(不要黑色填充)
                // 底部尖角：顶边贴着六边形下边缘，尖点在 (64, 5)
                float ty = y + 0.5f; const float topY = cy - apothem + 1f, apexY = 5f, halfBase = 15f;
                if (ty < topY && ty > apexY)
                {
                    float half = halfBase * (ty - apexY) / (topY - apexY);
                    float k = Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - cx) + 0.5f);
                    if (k > 0f) { a = Mathf.Max(a, k); v = Mathf.Max(v, k); }
                }
                tex.SetPixel(x, y, new Color(v, v, v, a));
            }
        tex.Apply();
        return _hexPin = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 100f);
    }

    /// <summary>四个标记统一用同一种战术图钉，靠里面的编号区分；slot 参数保留是为了以后想按槽位换样式。</summary>
    public static void StyleMarker(Image img, int slot)
    {
        img.sprite = HexPin();
        img.color = Color.white;
    }

    // 游戏里统一的 UI 字体(各个悬浮窗用的那个)和「东亚重工」装饰字体(占位字 NO DATA 之类)
    static TMP_FontAsset _gameFont, _decoFont;
    const string DecoFontGuid = "ea35a3e4c89493f44bb2e5cbe046505c";
    const string DecoFontResourcePath = "UI/HUD/ToaHI SDF";      // 打包后走 Resources(同 SkyPrisonAFPHUD 的做法)

    public static TMP_FontAsset GameFont => _gameFont != null ? _gameFont : (_gameFont = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF"));

    public static TMP_FontAsset DecoFont
    {
        get
        {
            if (_decoFont != null) return _decoFont;
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(DecoFontGuid);
            if (!string.IsNullOrEmpty(path)) _decoFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
#endif
            if (_decoFont == null) _decoFont = Resources.Load<TMP_FontAsset>(DecoFontResourcePath);
            return _decoFont;
        }
    }

    /// <summary>
    /// 在图钉的六边形里写编号：游戏字体、白色粗体。fontPx = 字号。
    /// 居中：文字框先对准六边形中心(图钉高度的 61.25%)，再由 SkyPrisonPinNumberFixer 在第一帧排版完成后，
    /// 用 TMP 实际生成的字形包围盒把数字平移到正中——不靠猜字体的行框 / 基线。
    /// </summary>
    public static TMP_Text AddMarkerNumber(RectTransform icon, int number, float fontPx)
    {
        var go = new GameObject("Number", typeof(RectTransform));
        go.transform.SetParent(icon, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.05f, 0.3125f); rt.anchorMax = new Vector2(0.95f, 0.9125f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (GameFont != null) t.font = GameFont;
        t.text = number.ToString(); t.fontSize = fontPx; t.fontStyle = FontStyles.Bold;
        t.color = Color.white; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        go.AddComponent<SkyPrisonPinNumberFixer>();
        return t;
    }
}

/// <summary>把图钉里的数字按 TMP 实际生成的字形包围盒平移到文字框正中(只在第一次排版完成后做一次)。</summary>
public sealed class SkyPrisonPinNumberFixer : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return null; yield return null;           // 等父级(图标)尺寸设好、画布排版完
        var t = GetComponent<TMP_Text>();
        if (t == null) yield break;
        t.ForceMeshUpdate();
        Vector3 c = t.textBounds.center;                // 相对文字框 pivot(居中) 的本地坐标
        var rt = (RectTransform)transform;
        rt.anchoredPosition -= new Vector2(c.x, c.y);
    }
}
