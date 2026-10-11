using Spine.Unity;
using UnityEngine;

/// <summary>
/// 光学迷彩(怪人 Mirage)：不是真的隐身——本体用 Spine 顶点色压到几乎透明，另在子物体上共用同一份网格，
/// 用 Hidden/SP/OpticalCamo 按轮廓折射背后的画面，读起来像空气里一块会动的透明玻璃。
/// 不碰 Spine 自己的材质(遮挡系统明确要求不要动 Spine 材质 / CustomMaterialOverride)。
///   · 夜视仪开着：直接现出真身(迷彩层关掉，本体不透明)；
///   · Reveal(秒)：出手前摇 / 被打中时现形一段时间(逐渐淡回迷彩)；
///   · 移动越快扭曲越明显(静止时只有轻微水波)。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)]      // 在灯光接收器等系统之后跑：迷彩中要把脚下影子关掉，不能被它们再打开
public class SkyPrisonOpticalCamo : MonoBehaviour
{
    [Tooltip("迷彩中本体残留的不透明度(0 = 只剩折射，完全看不到本体颜色)。")]
    [Range(0.01f, 0.3f)] public float camoBodyAlpha = 0.04f;
    [Tooltip("现形结束后淡回迷彩用多久(秒)。")]
    public float fadeBackSeconds = 0.6f;
    [Tooltip("现形时分界线从头顶扫到脚底用多久(秒)。")]
    public float revealWipeSeconds = 0.35f;
    [Range(0f, 1f)] public float iridescence = 0.22f;
    [Tooltip("歪曲层的推挤强度(液化感)。")]
    [Range(0f, 0.2f)] public float warpStrength = 0.06f;
    [Tooltip("这个移速(米/秒)算'全速移动'，扭曲最明显。")]
    public float fullMotionSpeed = 4f;
    public float distortionStrength = 0.012f;
    [Range(0f, 1f)] public float edgeGlint = 0.12f;
    [Tooltip("被打中时现形多久(秒)。")]
    public float revealOnHitSeconds = 1.2f;

    private SkeletonAnimation _skeleton;
    private MeshFilter _srcFilter;
    private MeshRenderer _srcRenderer;
    private MeshFilter _camoFilter;
    private MeshRenderer _camoRenderer;
    private Material _camoMat;
    private float _revealUntil = -999f;
    private float _reveal;           // 0 = 迷彩，1 = 现形
    private Vector3 _lastPos;
    private float _motion;
    private readonly System.Collections.Generic.List<Renderer> _shadows = new System.Collections.Generic.List<Renderer>();
    private float _nextShadowScan;
    private bool _shadowsRestored;
    private UnitActionController _action;
    private UnitHealthController _health;

    public bool Camouflaged => _reveal < 0.5f;

    /// <summary>现形 seconds 秒(被打中)。</summary>
    public void Reveal(float seconds) { _revealUntil = Mathf.Max(_revealUntil, Time.time + seconds); }

    /// <summary>不现形，只让空气扭曲在 seconds 秒内保持在 strength(0~1)以上——出招前摇的提示(站着不动也看得到一阵扭曲)。</summary>
    public void Ripple(float seconds, float strength = 0.7f) { _rippleUntil = Mathf.Max(_rippleUntil, Time.time + seconds); _rippleStrength = Mathf.Clamp01(strength); }
    private float _rippleUntil = -999f, _rippleStrength;

    private void EnsureRefs()
    {
        if (_action == null) _action = GetComponentInChildren<UnitActionController>(true);
        if (_health == null) { _health = GetComponentInChildren<UnitHealthController>(true); if (_health != null) _health.OnDamaged += OnDamaged; }
        if (_skeleton == null)
        {
            // 单位身上除了真正显示的本体，还有好几个描边 / 高亮代理(也是 SkeletonAnimation，挂着 SpineOutlineFollower，
            // 在主相机不渲染的描边图层上)。第一个找到的往往是代理——必须挑没有 SpineOutlineFollower 的那个本体。
            foreach (var sa in GetComponentsInChildren<SkeletonAnimation>(true))
                if (sa.GetComponent<SpineOutlineFollower>() == null) { _skeleton = sa; break; }
        }
        if (_skeleton == null) return;
        if (_srcFilter == null) _srcFilter = _skeleton.GetComponent<MeshFilter>();
        if (_srcRenderer == null) _srcRenderer = _skeleton.GetComponent<MeshRenderer>();
        if (_camoRenderer != null || _srcRenderer == null) return;

        var go = new GameObject("OpticalCamoLayer");
        go.transform.SetParent(_skeleton.transform, false);
        go.layer = _skeleton.gameObject.layer;
        _camoFilter = go.AddComponent<MeshFilter>();
        _camoRenderer = go.AddComponent<MeshRenderer>();
        _camoRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _camoRenderer.receiveShadows = false;
        var shader = Shader.Find("Hidden/SP/OpticalCamo");
        _camoMat = new Material(shader);
        var srcMat = _srcRenderer.sharedMaterial;
        if (srcMat != null && srcMat.mainTexture != null) _camoMat.SetTexture("_MainTex", srcMat.mainTexture);
        _camoRenderer.sharedMaterial = _camoMat;
        _camoRenderer.sortingLayerID = _srcRenderer.sortingLayerID;
        // 网格层现在只画现形扫描(真身 + 扫描带)，跟本体一起排序
        _camoRenderer.sortingOrder = _srcRenderer.sortingOrder + 1;
        _lastPos = transform.position;

    }

    private void LateUpdate()
    {
        EnsureRefs();
        if (_skeleton == null || _camoRenderer == null) return;

        // 共用 Spine 当前帧的网格(Spine 双缓冲，每帧换一个 Mesh 对象，所以每帧重新取)
        if (_srcFilter != null) _camoFilter.sharedMesh = _srcFilter.sharedMesh;
        var srcMat = _srcRenderer.sharedMaterial;
        if (srcMat != null && srcMat.mainTexture != null && _camoMat.GetTexture("_MainTex") != srcMat.mainTexture) _camoMat.SetTexture("_MainTex", srcMat.mainTexture);

        // 移动程度
        Vector3 p = transform.position;
        float speed = Time.deltaTime > 0f ? new Vector2(p.x - _lastPos.x, p.z - _lastPos.z).magnitude / Time.deltaTime : 0f;
        _lastPos = p;
        _motion = Mathf.MoveTowards(_motion, Mathf.Clamp01(speed / Mathf.Max(0.1f, fullMotionSpeed)), Time.deltaTime * 4f);

        // 出招不现形(用户 10-12：攻击不会现形，只有受到伤害才现形)；出招前摇期间由 AI 调 Ripple() 让空气扭曲加强，作为唯一的提示

        // 现形：夜视仪开着 / 现形计时中
        // 灼烧 / 冻结中藏不住(用户 10-12：弱灼热，被点燃时强制现形——灼热武器是克它的手段之一；被冻住也不能隐身)
        if (_status == null) _status = GetComponentInChildren<UnitStatusController>(true);
        bool statusLocked = _status != null && (_status.HasStatus("status_burn") || _status.HasStatus("status_iced"));
        bool reveal = SkyPrisonNightVisionMode.IsActive || statusLocked || Time.time < _revealUntil;
        float target = reveal ? 1f : 0f;
        float rate = reveal ? 1f / Mathf.Max(0.05f, revealWipeSeconds) : 1f / Mathf.Max(0.05f, fadeBackSeconds);
        _reveal = Mathf.MoveTowards(_reveal, target, Time.deltaTime * rate);

        // 现形 = 分界线从头顶扫到脚底：扫描过程中本体仍压到几乎透明，由迷彩层在分界线上方画真身；
        // 扫完(=1)才切回 Spine 本体、关掉迷彩层。回到迷彩时分界线从脚底往上退。
        bool fully = _reveal >= 0.999f;
        float bodyA = fully ? 1f : camoBodyAlpha;
        SetBodyAlpha(bodyA);
        _camoMat.SetFloat("_VertexAlphaScale", 1f / Mathf.Max(0.01f, bodyA));      // 骨架 alpha 乘进了共用网格的顶点 alpha，迷彩层要除回去
        _camoRenderer.enabled = !fully && _reveal > 0.001f;       // 网格层只负责现形扫描(分界线上方的真身 + 扫描带)
        // 迷彩中脚下的影子(脚底投影 Shadow_Projector + 灯光轮廓影子 LampShadow)也看不见，完全现形才显示
        if (Time.time >= _nextShadowScan)
        {
            _nextShadowScan = Time.time + 0.5f;        // 灯光影子是运行时按需生成的，定期重新收集
            _shadows.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.name == "Shadow_Projector" || r.name.StartsWith("LampShadow")) _shadows.Add(r);
        }
        for (int i = 0; i < _shadows.Count; i++) if (_shadows[i] != null && !fully) _shadows[i].enabled = false;
        if (fully && !_shadowsRestored) { for (int i = 0; i < _shadows.Count; i++) if (_shadows[i] != null) _shadows[i].enabled = true; }
        _shadowsRestored = fully;

        var b = _srcRenderer.bounds;
        const float band = 0.25f;
        _camoMat.SetFloat("_WipeY", Mathf.Lerp(b.max.y + band, b.min.y - band, _reveal));
        _camoMat.SetFloat("_WipeBand", band);
        _camoMat.SetFloat("_Iridescence", iridescence);
        _camoMat.SetFloat("_Camo", 1f);
        _camoMat.SetFloat("_Motion", _motion);
        _camoMat.SetFloat("_DistortionStrength", distortionStrength);
        _camoMat.SetFloat("_EdgeGlint", edgeGlint);

        // 屏幕遮罩权重 = 移动程度 × 未现形比例：静止 / 已现形 → 0 → 歪曲层完全透明
        float drive = Time.time < _rippleUntil ? Mathf.Max(_motion, _rippleStrength) : _motion;
        float weight = drive * (1f - _reveal);
        // 歪曲面片：怪人周围、面向相机、比身体大(模糊会往外扩)。由 SkyPrisonCamoWarpFeature 在透明物体画完之后实时截图、推挤
        var cam = Camera.main;
        float size = Mathf.Max(b.size.x, b.size.y) * 1.8f + 1.5f;
        var rot = cam != null ? cam.transform.rotation : Quaternion.identity;
        SkyPrisonCamoMaskRenderer.Set(GetInstanceID(), new SkyPrisonCamoMaskRenderer.Entry
        {
            renderer = _srcRenderer, weight = weight, vertexAlphaScale = 1f / Mathf.Max(0.01f, bodyA),
            warpMatrix = Matrix4x4.TRS(b.center, rot, new Vector3(size, size, 1f)), warpStrength = warpStrength,
        });
        _camoRenderer.sortingOrder = _srcRenderer.sortingOrder + 1;

        // 迷彩中头顶血条 / 名字也藏起来(用户 10-12：隐身后血条要隐藏)；现形扫描开始就淡入
        if (_overhead == null) _overhead = GetComponentInChildren<UnitOverheadUIView>(true);
        if (_overhead != null) _overhead.externalVisibility = Mathf.Clamp01(_reveal * 2f);
    }

    private UnitOverheadUIView _overhead;
    private UnitStatusController _status;

    private void OnDisable() { SkyPrisonCamoMaskRenderer.Remove(GetInstanceID()); }

    // 只改骨架颜色的 alpha，保留 RGB(别的系统可能在用骨架颜色做色调)
    private void SetBodyAlpha(float a)
    {
        var sk = _skeleton != null ? _skeleton.Skeleton : null;
        if (sk == null) return;
        var c = sk.GetColor();
        sk.SetColor(c.r, c.g, c.b, a);
    }

    private void OnDamaged(UnitHealthController h, float amount) { if (amount > 0f) Reveal(revealOnHitSeconds); }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDamaged -= OnDamaged;
        if (_camoMat != null) Destroy(_camoMat);
        SkyPrisonCamoMaskRenderer.Remove(GetInstanceID());
        if (_skeleton != null) SetBodyAlpha(1f);
        if (_overhead != null) _overhead.externalVisibility = 1f;
    }
}
