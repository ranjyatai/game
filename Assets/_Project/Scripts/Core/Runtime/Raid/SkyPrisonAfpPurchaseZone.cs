using System;
using UnityEngine;

/// <summary>
/// AFP 购买区：地图上一块矩形的地面区域（在地图放置工具里拖出来）。
/// 游戏里地面上画出冷绿色的边框 + 淡淡的底 + 正中的 AXIA 六边形标志，一道扫描线缓缓扫过；
/// 玩家走进区域就出现交互提示 E，按 E 触发 OnOpenRequested（购买界面之后接在这个事件上）。
/// 周围有敌人盯着玩家(战斗状态)时区域变灰、不可交互。
/// 区域是普通场景物体：位置 = 矩形中心，朝向 = 物体的 Y 旋转，大小 = size(x 宽、y 深)。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonAfpPurchaseZone : MonoBehaviour, IInteractable
{
    [Tooltip("矩形大小(米)：x = 沿本地 X 的宽，y = 沿本地 Z 的深。")]
    public Vector2 size = new Vector2(12f, 8f);
    public Color lineColor = new Color(0.42f, 0.92f, 0.68f, 1f);
    [Tooltip("AXIA 标志的宽度(米)，固定大小居中，不随矩形缩放。")]
    public float logoWidth = 3.6f;
    public float frameWidth = 0.2f;

    /// <summary>玩家在区域里按 E：购买界面订阅这个事件。</summary>
    public static event Action<SkyPrisonAfpPurchaseZone> OnOpenRequested;

    const float GroundLift = 0.06f;
    static readonly Color DisabledColor = new Color(0.55f, 0.58f, 0.6f, 1f);

    LineRenderer frame, scan;
    MeshRenderer fillRenderer, logoRenderer;
    Material fillMat, logoMat, frameMat, scanMat;
    Transform player;
    float near, inside01, scanT;

    public Vector3 InteractPosition => (insideNow && player != null) ? player.position : new Vector3(1e6f, 1e6f, 1e6f);
    public string InteractLabel => PlayerCombatStateRuntime.IsInCombat
        ? DoorText.Get("afp_zone_combat", "战斗中无法使用")
        : DoorText.Get("afp_zone_prompt_v2", "进行AFP部署");
    public bool CanInteract => unlocked && insideNow && !PlayerCombatStateRuntime.IsInCombat;

    /// <summary>战术研修里「AFP 系统解放」(seeker_root) 点亮后适应系统才开放：没点亮时购买区整个不显示、不可交互。</summary>
    public const string UnlockNodeId = "seeker_root";
    bool unlocked = true;
    bool insideNow;
    public bool PlayerInside => insideNow;

    public void Interact()
    {
        OnOpenRequested?.Invoke(this);
        SkyPrisonAfpShopWindow.Show(this);
    }

    void OnEnable() => InteractableRegistry.Register(this);
    void OnDisable() => InteractableRegistry.Unregister(this);

    void Awake()
    {
        BuildVisuals();
        int world3D = LayerMask.NameToLayer("World3D");   // 主相机只渲染几个固定层，新建物体默认的 Default 层画不出来
        if (world3D >= 0)
            foreach (Transform ch in GetComponentsInChildren<Transform>(true))
                ch.gameObject.layer = world3D;
    }

    void BuildVisuals()
    {
        var spriteShader = Shader.Find("Sprites/Default");
        Vector2 h = size * 0.5f;

        // 底：整块矩形一层淡淡的色
        var fillGo = new GameObject("Fill"); fillGo.transform.SetParent(transform, false);
        fillGo.transform.localPosition = new Vector3(0f, GroundLift * 0.5f, 0f);
        var fm = new Mesh { name = "AfpFill" };
        fm.vertices = new[] { new Vector3(-h.x, 0, -h.y), new Vector3(h.x, 0, -h.y), new Vector3(h.x, 0, h.y), new Vector3(-h.x, 0, h.y) };
        fm.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };    // 双面
        fm.RecalculateBounds();
        fillGo.AddComponent<MeshFilter>().sharedMesh = fm;
        fillRenderer = fillGo.AddComponent<MeshRenderer>();
        fillMat = new Material(spriteShader);
        fillRenderer.sharedMaterial = fillMat;
        fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fillRenderer.receiveShadows = false;

        // 边框：封闭折线，四个角同样的圆滑接法
        frame = MakeLine("Frame", frameWidth, true, 4);
        frame.SetPosition(0, new Vector3(-h.x, -h.y, 0)); frame.SetPosition(1, new Vector3(h.x, -h.y, 0));
        frame.SetPosition(2, new Vector3(h.x, h.y, 0)); frame.SetPosition(3, new Vector3(-h.x, h.y, 0));
        frame.numCornerVertices = 3;
        frameMat = frame.material;

        // 扫描线：沿深度方向缓缓扫过
        scan = MakeLine("Scan", frameWidth * 0.45f, false, 2);
        scanMat = scan.material;

        // 标志：贴在地面、固定大小、居中；方向始终是「屏幕朝上」(不随区域的旋转转)
        var logoGo = new GameObject("Logo"); logoGo.transform.SetParent(transform, false);
        logoGo.transform.position = transform.position + Vector3.up * GroundLift;
        logoGo.transform.rotation = Quaternion.identity;
        float lw = logoWidth * 0.5f, lh = lw * 469f / 523f;
        var lm = new Mesh { name = "AfpLogo" };
        lm.vertices = new[] { new Vector3(-lw, 0, -lh), new Vector3(lw, 0, -lh), new Vector3(lw, 0, lh), new Vector3(-lw, 0, lh) };
        lm.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        lm.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
        lm.RecalculateBounds();
        logoGo.AddComponent<MeshFilter>().sharedMesh = lm;
        logoRenderer = logoGo.AddComponent<MeshRenderer>();
        logoMat = new Material(spriteShader) { mainTexture = Resources.Load<Texture2D>("UI/AFP/AXIA_Hex") };
        logoRenderer.sharedMaterial = logoMat;
        logoRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; logoRenderer.receiveShadows = false;
    }

    LineRenderer MakeLine(string n, float w, bool loop, int count)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, GroundLift, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);          // 点按 (x, z, 0) 给，躺到地面上
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = loop; lr.alignment = LineAlignment.TransformZ;
        lr.widthMultiplier = w; lr.positionCount = count;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        return lr;
    }

    void Update()
    {
        if (player == null)
        {
            var p = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (p != null) player = p.transform;
        }

        bool nowUnlocked = SkyPrisonAfpAbsorbShield.NodeUnlocked(UnlockNodeId);
        if (nowUnlocked != unlocked || (!nowUnlocked && frame.enabled))
        {
            unlocked = nowUnlocked;
            frame.enabled = scan.enabled = fillRenderer.enabled = logoRenderer.enabled = unlocked;
        }
        if (!unlocked) { insideNow = false; return; }

        float distToRect = 999f;
        insideNow = false;
        if (player != null)
        {
            Vector3 lp = transform.InverseTransformPoint(player.position);
            Vector2 h = size * 0.5f;
            float dx = Mathf.Max(Mathf.Abs(lp.x) - h.x, 0f), dz = Mathf.Max(Mathf.Abs(lp.z) - h.y, 0f);
            distToRect = Mathf.Sqrt(dx * dx + dz * dz);
            insideNow = dx <= 0f && dz <= 0f;
        }
        near = Mathf.MoveTowards(near, Mathf.Clamp01(1f - distToRect / 10f), Time.deltaTime * 2.5f);
        inside01 = Mathf.MoveTowards(inside01, insideNow ? 1f : 0f, Time.deltaTime * 4f);

        bool combat = PlayerCombatStateRuntime.IsInCombat;
        Color c = combat ? DisabledColor : lineColor;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.4f);
        float vis = (Mathf.Lerp(0.35f, 0.8f, near) + 0.2f * inside01) * pulse * (combat ? 0.55f : 1f);

        SetLineColor(frame, frameMat, c, vis);
        fillMat.color = new Color(c.r, c.g, c.b, (0.05f + 0.07f * near + 0.06f * inside01) * (combat ? 0.6f : 1f));
        logoMat.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(vis * 0.95f));

        // 扫描线：从区域一侧匀速扫到另一侧，淡入淡出
        scanT = Mathf.Repeat(scanT + Time.deltaTime * 0.22f, 1f);
        Vector2 hh = size * 0.5f;
        float z = Mathf.Lerp(-hh.y, hh.y, scanT);
        scan.SetPosition(0, new Vector3(-hh.x, z, 0)); scan.SetPosition(1, new Vector3(hh.x, z, 0));
        float edge = Mathf.Sin(scanT * Mathf.PI);
        SetLineColor(scan, scanMat, c, vis * 0.45f * edge);
    }

    static void SetLineColor(LineRenderer lr, Material m, Color c, float alpha)
    {
        var col = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
        lr.startColor = lr.endColor = col;
        if (m != null) m.color = col;
    }

    void OnDestroy()
    {
        if (fillMat != null) Destroy(fillMat);
        if (logoMat != null) Destroy(logoMat);
    }

#if UNITY_EDITOR
    // Scene 视图里画出矩形，方便在地图上看到购买区的范围(游戏运行时的地面特效在 Awake 里才生成)。
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.42f, 0.92f, 0.68f, 1f);
        var m = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * 0.05f, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.02f, size.y));
        Gizmos.matrix = m;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, "AFP购买区  " + size.x.ToString("0.#") + " x " + size.y.ToString("0.#"));
    }
#endif
}
