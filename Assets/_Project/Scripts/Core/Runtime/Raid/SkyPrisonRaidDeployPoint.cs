using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RAID 出发点：地面特效（不是 NPC）。玩家靠近时光环变亮、出交互提示，按 E 打开全屏选本地图。
/// 地图读 raids 列表，未解锁的本不显示。
/// </summary>
public sealed class SkyPrisonRaidDeployPoint : MonoBehaviour, IInteractable
{
    [Tooltip("可选的 RAID 本，按 order 排。")]
    public List<RaidDefinition> raids = new List<RaidDefinition>();
    public DoorLocalizedLabel promptLabel = new DoorLocalizedLabel();
    [Min(0.5f)] public float radius = 2.5f;
    public Color ringColor = new Color(0.25f, 0.85f, 1f, 1f);

    [Header("远处也看得见：光柱 + 悬浮标记")]
    // 刻意不序列化：场景里存过的旧值会盖掉这里改的默认值（和选关标记的衬底参数是同一个坑）
    [System.NonSerialized] public float beamHeight = 2.4f;     // 从地面圈升到全息牌底部的投射光锥
    [System.NonSerialized] public float beamWidth = 2.6f;
    [System.NonSerialized] public float markerHeight = 3.0f;   // 全息牌中心离地高度

    LineRenderer ring, ringInner;
    Transform player;
    float glow;
    Material beamMat; MeshRenderer beamRenderer;
    Transform markerRoot; SpriteRenderer markerHex, markerBack, markerSweep; TMPro.TextMeshPro markerText, markerSub;

    public Vector3 InteractPosition => transform.position;
    public string InteractLabel => promptLabel.Resolve("raid_deploy_prompt", "出发 · 选择 RAID");
    public bool CanInteract => raids != null && raids.Count > 0 && !SkyPrisonRaidMapScreen.IsOpen;

    public void Interact() => SkyPrisonRaidMapScreen.Open(this);

    void OnEnable() => InteractableRegistry.Register(this);
    void OnDisable() => InteractableRegistry.Unregister(this);

    void Awake()
    {
        ring = MakeRing("Ring", radius, 0.12f);
        ringInner = MakeRing("RingInner", radius * 0.62f, 0.06f);
        BuildBeam();
        BuildUpdraft();
        BuildMarker();

        // 主相机只渲染 Debris / World3D / Character2D 三层，新建物体默认在 Default 层，整套光环 / 光柱 / 气流 / 标记都不会被画出来。
        // 全部挪到 World3D（和场景里别的地面几何体一样）。
        int world3D = LayerMask.NameToLayer("World3D");
        if (world3D >= 0)
            foreach (Transform ch in GetComponentsInChildren<Transform>(true))
                if (ch != transform) ch.gameObject.layer = world3D;
    }

    static Texture2D softDot;
    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int N = 32; softDot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
            float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
            softDot.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        softDot.Apply(); return softDot;
    }

    /// <summary>上升的气流：一圈细长的光丝从地面圈里升起，边升边绕着中心缓慢旋转、渐隐——一眼能看出"这里有东西，往上走"。</summary>
    void BuildUpdraft()
    {
        var go = new GameObject("Updraft"); go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 0.05f, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = ringColor;
        main.maxParticles = 160;

        var em = ps.emission; em.rateOverTime = 34f;

        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = radius * 0.85f; sh.radiusThickness = 0.35f;     // 偏向圈的边缘出生，中间稀一点
        sh.rotation = new Vector3(-90f, 0f, 0f);                      // 圆盘躺在地面上

        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);   // 绕竖轴慢慢旋转
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch; psr.lengthScale = 5f; psr.velocityScale = 0.12f;   // 沿运动方向拉成细长光丝
        var shd = Shader.Find("Sprites/Default");
        if (shd != null) { var m = new Material(shd) { mainTexture = SoftDot() }; psr.sharedMaterial = m; }
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; psr.receiveShadows = false; psr.sortingOrder = 45;
        ps.Play();
    }

    /// <summary>竖直光柱：两片交叉的竖直面片，下亮上淡，从任何角度都看得到；玩家靠近时更亮。</summary>
    void BuildBeam()
    {
        var go = new GameObject("Beam"); go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>(); beamRenderer = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name = "RaidBeam" };
        float w = beamWidth * 0.5f, h = beamHeight;
        var v = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>();
        for (int k = 0; k < 2; k++)
        {
            Vector3 axis = k == 0 ? Vector3.right : Vector3.forward;
            int b = v.Count;
            v.Add(-axis * w); v.Add(axis * w); v.Add(-axis * w * 0.55f + Vector3.up * h); v.Add(axis * w * 0.55f + Vector3.up * h);
            c.Add(new Color(1, 1, 1, 0.85f)); c.Add(new Color(1, 1, 1, 0.85f)); c.Add(new Color(1, 1, 1, 0f)); c.Add(new Color(1, 1, 1, 0f));
            tri.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3, b, b + 1, b + 2, b + 1, b + 3, b + 2 });   // 双面
        }
        mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds(); mf.sharedMesh = mesh;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) { beamMat = new Material(sh); beamRenderer.sharedMaterial = beamMat; }
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; beamRenderer.receiveShadows = false;
    }

    static Sprite holoPanel, holoLine;
    /// <summary>全息牌底图：淡淡的填充 + 横向扫描线 + 四角的括号，整体是白色（运行时按 ringColor 染色）。</summary>
    static Sprite HoloPanelSprite()
    {
        if (holoPanel != null) return holoPanel;
        const int W = 192, H = 72;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            float a = 0.08f;                                   // 淡填充
            if (y % 4 == 0) a += 0.10f;                         // 横向扫描线
            float edge = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
            bool nearCornerX = x < 22 || x > W - 23, nearCornerY = y < 14 || y > H - 15;
            if (edge < 2 && (nearCornerX || nearCornerY)) a = 1f;            // 四角括号
            else if (edge < 1) a = Mathf.Max(a, 0.28f);                       // 细边框
            tex.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        tex.Apply();
        holoPanel = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 48f);   // 4 × 1.5 个世界单位
        return holoPanel;
    }

    static Sprite HoloLineSprite()
    {
        if (holoLine != null) return holoLine;
        const int W = 64;
        var tex = new Texture2D(W, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 4; y++) for (int x = 0; x < W; x++)
        {
            float fx = 1f - Mathf.Abs(x / (W - 1f) * 2f - 1f); float fy = 1f - Mathf.Abs(y / 3f * 2f - 1f);
            tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(fx * 2f) * fy));
        }
        tex.Apply();
        holoLine = Sprite.Create(tex, new Rect(0, 0, W, 4), new Vector2(0.5f, 0.5f), 16f);   // 4 × 0.25
        return holoLine;
    }

    /// <summary>
    /// 全息投影牌：竖直立在地面圈上方的一块「START」牌（只绕竖轴转向镜头，不会随镜头俯仰倒下），
    /// 带底图 / 扫描线 / 角括号，一层淡淡的后影做出厚度，一条亮线从上到下反复扫过，偶尔闪一下；
    /// 地面圈往上投出一束光锥托住它。
    /// </summary>
    void BuildMarker()
    {
        markerRoot = new GameObject("Marker").transform; markerRoot.SetParent(transform, false);
        markerRoot.localPosition = new Vector3(0, markerHeight, 0);

        var backGo = new GameObject("PanelBack"); backGo.transform.SetParent(markerRoot, false);
        backGo.transform.localPosition = new Vector3(0.1f, -0.06f, 0.14f); backGo.transform.localScale = new Vector3(1.06f, 1.06f, 1f);
        markerBack = backGo.AddComponent<SpriteRenderer>(); markerBack.sprite = HoloPanelSprite(); markerBack.sortingOrder = 48;

        var panelGo = new GameObject("Panel"); panelGo.transform.SetParent(markerRoot, false);
        markerHex = panelGo.AddComponent<SpriteRenderer>(); markerHex.sprite = HoloPanelSprite(); markerHex.sortingOrder = 50;

        var sweepGo = new GameObject("Sweep"); sweepGo.transform.SetParent(markerRoot, false);
        sweepGo.transform.localScale = new Vector3(0.95f, 1f, 1f);
        markerSweep = sweepGo.AddComponent<SpriteRenderer>(); markerSweep.sprite = HoloLineSprite(); markerSweep.sortingOrder = 52;

        var f = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentTMPFont() : null;
        var tgo = new GameObject("Text"); tgo.transform.SetParent(markerRoot, false);
        tgo.transform.localPosition = new Vector3(0, 0.12f, -0.02f);
        markerText = tgo.AddComponent<TMPro.TextMeshPro>();
        if (f != null) markerText.font = f;
        markerText.text = DoorText.Get("raid_deploy_start", "START"); markerText.fontSize = 6f; markerText.fontStyle = TMPro.FontStyles.Bold;
        markerText.alignment = TMPro.TextAlignmentOptions.Center; markerText.enableWordWrapping = false; markerText.sortingOrder = 51;

        var sgo = new GameObject("Sub"); sgo.transform.SetParent(markerRoot, false);
        sgo.transform.localPosition = new Vector3(0, -0.5f, -0.02f);
        markerSub = sgo.AddComponent<TMPro.TextMeshPro>();
        if (f != null) markerSub.font = f;
        markerSub.text = "RAID"; markerSub.fontSize = 2.2f; markerSub.alignment = TMPro.TextAlignmentOptions.Center;
        markerSub.enableWordWrapping = false; markerSub.sortingOrder = 51;
    }

    float glitchUntil;
    void LateUpdate()
    {
        if (markerRoot == null) return;
        var cam = Camera.main; if (cam == null) return;
        // 只绕竖轴转向镜头（立在地上的牌，不跟着镜头俯仰）
        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude > 1e-4f) markerRoot.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        float bob = Mathf.Sin(Time.time * 1.4f) * 0.06f;
        // 偶尔小小的故障：牌左右抖一下、变暗
        if (Time.time > glitchUntil && Random.value < Time.deltaTime * 0.35f) glitchUntil = Time.time + 0.12f;
        float jitter = Time.time < glitchUntil ? (Random.value - 0.5f) * 0.25f : 0f;
        markerRoot.position = transform.position + Vector3.up * (markerHeight + bob) + markerRoot.right * jitter;
        // 扫描亮线从牌底扫到牌顶，循环
        if (markerSweep != null)
        {
            float k = Mathf.Repeat(Time.time * 0.55f, 1f);
            markerSweep.transform.localPosition = new Vector3(0, Mathf.Lerp(-0.7f, 0.7f, k), -0.03f);
        }
    }

    LineRenderer MakeRing(string n, float r, float w)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 0.06f, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.alignment = LineAlignment.TransformZ;
        lr.widthMultiplier = w;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        const int N = 64; lr.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
        }
        return lr;
    }

    void Update()
    {
        if (player == null) { var p = GameObject.FindGameObjectWithTag("Player"); if (p) player = p.transform; }
        float target = 0f;
        if (player != null)
        {
            var d = player.position - transform.position; d.y = 0;
            target = Mathf.Clamp01(1f - d.magnitude / (radius * 2.2f));
        }
        glow = Mathf.MoveTowards(glow, target, Time.deltaTime * 3f);
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 3f);
        var c = ringColor; c.a = Mathf.Lerp(0.5f, 1f, glow) * pulse;
        ring.startColor = ring.endColor = c;
        var c2 = ringColor; c2.a = Mathf.Lerp(0.1f, 0.7f, glow);
        ringInner.startColor = ringInner.endColor = c2;
        ringInner.transform.Rotate(0, 0, 25f * Time.deltaTime, Space.Self);
        ring.widthMultiplier = Mathf.Lerp(0.16f, 0.26f, glow);
        // 光柱和悬浮标记：远处就能看到（底亮度比光环高），靠近时更亮
        float vis = Mathf.Lerp(0.5f, 1f, glow) * (0.8f + 0.2f * pulse);
        if (beamMat != null) { var bc = ringColor; bc.a = 0.26f * vis;   // 光柱只做淡淡的背景，主角是上升的气流
             beamMat.color = bc; }
        float flick = 0.86f + 0.14f * Mathf.PerlinNoise(Time.time * 17f, 3.7f);      // 全息的轻微闪烁
        if (Time.time < glitchUntil) flick *= 0.4f;
        float hv = vis * flick;
        if (markerHex != null) { var hc = ringColor; hc.a = hv; markerHex.color = hc; }
        if (markerBack != null) { var bc2 = ringColor; bc2.a = hv * 0.35f; markerBack.color = bc2; }
        if (markerSweep != null) { var sc = Color.white; sc.a = hv * 0.9f; markerSweep.color = sc; }
        if (markerText != null) { var tc = Color.Lerp(ringColor, Color.white, 0.55f); tc.a = hv; markerText.color = tc; }
        if (markerSub != null) { var uc = ringColor; uc.a = hv * 0.85f; markerSub.color = uc; }
    }

    // Scene 视图里的彩色标记由编辑器脚本 SkyPrisonRaidDeployPointSceneMarker 画（不受 Scene 视图 Gizmos 开关影响）。
}
