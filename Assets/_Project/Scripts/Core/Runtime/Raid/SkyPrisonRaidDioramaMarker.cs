using TMPro;
using UnityEngine;

/// <summary>
/// 沙盘里的 RAID 标记：真正放在 3D 空间里（据点模型正上方），跟着镜头一起透视缩放。
/// 六边形线框 + 战术坐标文字始终朝向镜头（LateUpdate 里转，镜头在 Update 里已经移完，不会慢一帧），
/// 一根竖线连到地面。悬停 / 锁定时文字变绿。点击检测靠 Face 上的碰撞体（由 SkyPrisonRaidMapScreen 用射线查）。
/// </summary>
public sealed class SkyPrisonRaidDioramaMarker : MonoBehaviour
{
    public const int MarkerLayer = 30;
    public string raidId;
    public Transform face;            // 六边形 + 文字，整体朝向镜头
    public SpriteRenderer hex;
    public SpriteRenderer leader;     // 斜 45° 的引线（从六边形引出）
    public SpriteRenderer underline;  // 引线末端的水平线，坐标文字坐在它上面
    public TextMeshPro label;
    public float height = 3f;         // 竖线长度（从标记到据点顶）

    Transform site; Renderer[] siteRenderers; const float Gap = 0f;   // 标记中心到据点顶面的距离

    /// <summary>绑定到据点：之后标记每帧都贴着据点的真实包围盒顶面（随升起 / 压扁一起动），不再用烘焙时的固定高度。</summary>
    public void AttachTo(Transform siteRoot)
    {
        site = siteRoot;
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in siteRoot.GetComponentsInChildren<Renderer>(true)) if (r.name != "NodeRing") list.Add(r);
        // 锚点：据点里"体量最大"的那一组建筑（占地接近最大者）的最高点——标记贴在它头顶，不再悬在整个据点最高的烟囱之上，
        // 这样镜头转动 / 缩放时和这栋建筑的视差很小，看起来就是钉在模型上
        // 用"相对据点根"的局部尺寸算体量——据点还没升起（根缩放≈0）时 bounds 是 0，不能用 bounds，否则会误判成整个据点最高点
        System.Func<Renderer, Vector3> relSize = r =>
        {
            Vector3 ls = r.localBounds.size, sc = r.transform.lossyScale, root = siteRoot.lossyScale;
            return new Vector3(ls.x * sc.x / Mathf.Max(1e-5f, root.x), ls.y * sc.y / Mathf.Max(1e-5f, root.y), ls.z * sc.z / Mathf.Max(1e-5f, root.z));
        };
        float maxFoot = 0f;
        foreach (var r in list) { var sz = relSize(r); if (sz.y > 0.4f) maxFoot = Mathf.Max(maxFoot, sz.x * sz.z); }
        // 只锚在"占地最大"的那一个网格上（并列取更高的），标记的水平位置就是它的正中心，不再是几栋建筑的平均位置
        Renderer best = null; float bestFoot = -1f, bestTop = -1f;
        foreach (var r in list)
        {
            var sz = relSize(r); if (sz.y <= 0.4f) continue;
            float foot = sz.x * sz.z; float top = r.transform.position.y + sz.y;
            if (foot > bestFoot * 1.05f || (foot > bestFoot * 0.95f && top > bestTop)) { best = r; bestFoot = foot; bestTop = top; }
        }
        siteRenderers = best != null ? new[] { best } : list.ToArray();
        // 记下"锚点建筑顶面中心"在据点根局部坐标里的位置（沿层级链乘局部矩阵，不依赖据点根当前缩放），
        // 之后标记位置 = 据点根.TransformPoint(这个局部点)：相当于挂在据点上，据点不动它就不动，据点升起 / 压扁它就一起动
        if (best != null)
        {
            var chain = Matrix4x4.identity; var tr = best.transform;
            while (tr != null && tr != siteRoot) { chain = Matrix4x4.TRS(tr.localPosition, tr.localRotation, tr.localScale) * chain; tr = tr.parent; }
            var lb = best.localBounds; anchorLocal = chain.MultiplyPoint3x4(lb.center + new Vector3(0f, lb.extents.y, 0f)); hasAnchor = true;
        }
    }

    const float FrontPush = 14f;        // 标记往镜头方向推出的距离（够越过最大的圆顶 / 烟囱）
    public float ReferenceDist = 46f;   // 设计尺寸对应的镜头距离
    Vector3 anchorLocal; bool hasAnchor;
    Camera cam; float alpha = 1f; Color hot = Color.white; bool isHot;
    static Sprite hexSprite, lineSprite, lineSpriteH;

    public static Sprite HexSprite()
    {
        if (hexSprite != null) return hexSprite;
        const int N = 256; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float px = (x + 0.5f) / N * 2f - 1f, py = (y + 0.5f) / N * 2f - 1f;
                float qx = Mathf.Abs(px), qy = Mathf.Abs(py);
                float d = Mathf.Max(qx * 0.8660254f + qy * 0.5f, qy) / 0.92f;
                float ring = 1f - S(0f, 0.06f, Mathf.Abs(d - 0.9f) - 0.04f);
                float core = 1f - S(0f, 0.05f, d - 0.26f);
                float glow = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.16f) * 0.22f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(ring, core) + glow)));
            }
        tex.Apply();
        hexSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 200f);
        return hexSprite;
    }

    public static Sprite LineSprite()
    {
        if (lineSprite != null) return lineSprite;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); tex.Apply();
        lineSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 1f), 2f);      // 1×1 单位，轴心在顶
        return lineSprite;
    }

    /// <summary>1×1 单位的白条，轴心在左边中点（沿 +X 延伸），用来画斜线和水平线。</summary>
    public static Sprite LineSpriteH()
    {
        if (lineSpriteH != null) return lineSpriteH;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); tex.Apply();
        lineSpriteH = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0f, 0.5f), 2f);
        return lineSpriteH;
    }

    static float S(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

    public void SetText(string t) { if (label != null) label.text = t; }
    public void SetAlpha(float a) { alpha = a; ApplyColor(); }
    public void SetHot(bool h) { isHot = h; }
    /// <summary>设为"新 RAID"：显示 NEW；设为 false = 已经看过，淡出后不再出现。</summary>
    public void SetNew(bool isNew) { showNew = isNew; }

    // 标记的线和字不接受鼠标（下面把碰撞体关掉）：只有建筑模型可点
    void Start() { var c = GetComponentInParent<SkyPrisonRaidDioramaController>(); cam = c != null ? c.cam : null; if (hex != null) hex.sprite = HexSprite(); if (leader != null) leader.sprite = LineSpriteH(); if (underline != null) underline.sprite = LineSpriteH(); var bc = face != null ? face.GetComponent<BoxCollider>() : null; if (bc != null) { bc.size = new Vector3(bc.size.x * 1.7f, bc.size.y * 1.7f, bc.size.z); bc.enabled = false; } BuildGhosts(); BuildPlate(); BuildNew(); }

    void ApplyColor()
    {
        var white = new Color(0.9f, 0.97f, 1f); var green = new Color(0.35f, 1f, 0.7f);
        hot = Color.Lerp(hot, isHot ? green : white, 0.35f);
        if (hex != null) hex.color = new Color(hot.r, hot.g, hot.b, alpha);
        if (leader != null) leader.color = new Color(hot.r, hot.g, hot.b, 0.85f * alpha);
        if (underline != null) underline.color = new Color(hot.r, hot.g, hot.b, 0.85f * alpha);
        if (label != null) label.color = new Color(isHot ? green.r : 1f, isHot ? green.g : 1f, isHot ? green.b : 1f, alpha);
    }

    void LateUpdate()
    {
        if (cam == null) { var c = GetComponentInParent<SkyPrisonRaidDioramaController>(); cam = c != null ? c.cam : null; if (cam == null) return; }
        if (hasAnchor && site != null) transform.position = site.TransformPoint(anchorLocal);
        else if (siteRenderers != null && siteRenderers.Length > 0)
        {
            Bounds b = default; bool first = true;
            foreach (var r in siteRenderers) { if (r == null) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            if (!first) transform.position = new Vector3(b.center.x, b.max.y + Gap, b.center.z);
        }
        if (face != null)
        {
            face.rotation = cam.transform.rotation;
            // 沿"镜头→锚点"的视线往镜头方向推出 FrontPush：屏幕投影位置不变（仍钉在模型上），但永远在模型前面，不会被圆顶 / 烟囱遮住
            Vector3 toCam = cam.transform.position - transform.position; float dist0 = toCam.magnitude;
            float push = Mathf.Min(FrontPush, Mathf.Max(0f, dist0 - 6f));
            face.position = dist0 > 1e-4f ? transform.position + toCam / dist0 * push : transform.position;
            // 屏幕上大小恒定：离镜头越远就按距离等比放大，缩放镜头时标记和文字看起来一样大
            float k = Mathf.Clamp((dist0 - push) / ReferenceDist, 0.4f, 4f);
            face.localScale = Vector3.one * k;
        }
        ApplyColor();
        if (face != null) UpdateGhosts(Vector3.Distance(cam.transform.position, face.position));
        UpdatePlate(); UpdateNew();
    }

    // ───────── 选中色差：悬停 / 锁定时，标记和文字出现红、青两路错位的重影；刚选中的一瞬间再抖一下 ─────────
    Transform ghostR, ghostB; SpriteRenderer[] rSr, bSr; TMP_Text[] rTx, bTx;
    float chroma, glitch; bool prevHot;
    static readonly Color GhostRed = new Color(1f, 0.2f, 0.3f), GhostCyan = new Color(0.2f, 0.85f, 1f);

    // ───────── NEW 标：第一次出现的新 RAID，六边形左上角一个淡红色的 NEW，鼠标指上去就淡出 ─────────
    TMP_Text newLabel; bool showNew; float newFade = 1f;
    static readonly Color NewColor = new Color(1f, 0.55f, 0.55f);        // 淡红

    void BuildNew()
    {
        if (label == null || hex == null || newLabel != null) return;
        var go = Instantiate(label.gameObject, face); go.name = "NewTag";
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var ch in go.GetComponentsInChildren<Transform>(true)) if (ch != go.transform && ch.name == "TextPlate") Destroy(ch.gameObject);   // 复制来的衬底不要
        newLabel = go.GetComponent<TMP_Text>();
        newLabel.text = SkyPrisonRaidMapScreen.LocText("raid_new", "NEW"); newLabel.alignment = TextAlignmentOptions.BottomRight; newLabel.fontSize = label.fontSize * 0.8f;
        newLabel.enableWordWrapping = false; newLabel.color = new Color(NewColor.r, NewColor.g, NewColor.b, 0f);
        // 放在六边形的左上角外侧（右下角对齐到六边形的左上角）
        var hp = hex.transform.localPosition; float r = 0.64f * hex.transform.localScale.x;      // 六边形半宽（256px / 200ppu 的一半）
        newLabel.transform.localPosition = new Vector3(hp.x - r * 0.55f, hp.y + r * 0.95f, 0f);
        newLabel.transform.localRotation = Quaternion.identity; newLabel.transform.localScale = Vector3.one;
        go.SetActive(true);
    }

    void UpdateNew()
    {
        if (newLabel == null) return;
        // 选中 / 悬停（isHot）就算看过：淡出。没看过且没被选中才显示
        bool want = showNew && !isHot;
        newFade = Mathf.MoveTowards(newFade, want ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        newLabel.color = new Color(NewColor.r, NewColor.g, NewColor.b, alpha * newFade);
        if (newLabel.gameObject.activeSelf != (newFade > 0.01f)) newLabel.gameObject.SetActive(newFade > 0.01f);
    }

    // ───────── 文字衬底：黑色半透明、四边虚化的底板，垫在文字后面，浅色沙盘上也看得清 ─────────
    SpriteRenderer plate; string plateText; static Sprite plateSprite;
    // 注意：这三项刻意不序列化——之前它们是 public 字段，沙盘预制体在 Unity 里存过一次旧值（0.6 / 2），
    // 之后改代码里的默认值完全不生效（画面一直是旧值）。现在以代码为准。
    [System.NonSerialized] public float plateOpacity = 0.10f;      // 衬底不透明度
    [System.NonSerialized] public float plateExpandX = 15.0f;      // 横向比文字多出的边（单位：字号的 0.1；文字宽约 14 个单位时，两边各多出约 2.5）
    [System.NonSerialized] public float plateExpandY = 1.2f;       // 纵向比文字多出的边

    static Sprite PlateSprite()
    {
        if (plateSprite != null) return plateSprite;
        const int N = 64; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                float ex = Mathf.Min(u, 1f - u) / 0.2f, ey = Mathf.Min(v, 1f - v) / 0.42f;      // 横向 / 纵向各留一段羽化
                float a = S(0f, 1f, Mathf.Clamp01(ex)) * S(0f, 1f, Mathf.Clamp01(ey));
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
        tex.Apply();
        plateSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);   // 1×1 世界单位
        return plateSprite;
    }

    void BuildPlate()
    {
        if (label == null) return;
        var go = new GameObject("TextPlate"); go.layer = label.gameObject.layer;
        go.transform.SetParent(label.transform, false);
        plate = go.AddComponent<SpriteRenderer>(); plate.sprite = PlateSprite(); plate.color = new Color(0f, 0f, 0f, 0f);
        plate.sortingOrder = label.sortingOrder - 2;
    }

    void UpdatePlate()
    {
        if (plate == null || label == null) return;
        if (plateText != label.text) { plateText = label.text; label.ForceMeshUpdate(); }
        var b = label.textBounds;                                              // 每帧按文字当前的实际范围取，不缓存（缓存会量到还没排好的文字）
        if (b.size.x <= 0.0001f) return;
        float u = label.fontSize * 0.1f;                                       // 以字号为单位留边，字大字小都成比例
        plate.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.002f);
        plate.transform.localScale = new Vector3(b.size.x + u * plateExpandX, b.size.y + u * plateExpandY, 1f);
        plate.color = new Color(0f, 0f, 0f, plateOpacity * alpha);
    }

    void BuildGhosts()
    {
        if (face == null || ghostR != null) return;
        ghostR = MakeGhost("GhostR", out rSr, out rTx); ghostB = MakeGhost("GhostB", out bSr, out bTx);
    }

    Transform MakeGhost(string n, out SpriteRenderer[] sr, out TMP_Text[] tx)
    {
        var g = Instantiate(face.gameObject, transform); g.name = n;
        foreach (var col in g.GetComponentsInChildren<Collider>(true)) Destroy(col);          // 只有主体接收点击
        foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.sortingOrder -= 1;     // 重影垫在主体后面
        sr = g.GetComponentsInChildren<SpriteRenderer>(true); tx = g.GetComponentsInChildren<TMP_Text>(true);
        g.SetActive(false);
        return g.transform;
    }

    void UpdateGhosts(float dist)
    {
        if (ghostR == null) return;
        if (isHot && !prevHot) glitch = 1f;
        prevHot = isHot;
        chroma = Mathf.MoveTowards(chroma, isHot ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        glitch = Mathf.MoveTowards(glitch, 0f, Time.unscaledDeltaTime * 4f);
        bool on = chroma > 0.01f;
        if (ghostR.gameObject.activeSelf != on) { ghostR.gameObject.SetActive(on); ghostB.gameObject.SetActive(on); }
        if (!on) return;
        float wpp = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / 1080f;      // 沙盘 RT 高 1080：一个像素对应的世界长度
        float px = chroma * 1.0f + glitch * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 90f)) * 3f;
        Vector3 off = cam.transform.right * (px * wpp);
        Place(ghostR, rSr, rTx, -off, GhostRed); Place(ghostB, bSr, bTx, off, GhostCyan);
    }

    void Place(Transform g, SpriteRenderer[] srs, TMP_Text[] txs, Vector3 off, Color tint)
    {
        g.rotation = face.rotation; g.localScale = face.localScale; g.position = face.position + off;
        var col = new Color(tint.r, tint.g, tint.b, alpha * chroma * 0.45f);
        foreach (var s in srs) s.color = col;
        foreach (var t in txs) { t.color = col; if (label != null && t.text != label.text) t.text = label.text; }
    }
}
