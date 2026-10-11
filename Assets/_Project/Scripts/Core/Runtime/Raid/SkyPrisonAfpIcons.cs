using UnityEngine;

/// <summary>
/// AFP 能力图标：运行时程序绘制的白色线框(2x2 超采样抗锯齿)，用 Image.color 染色。
/// 0 = 盾(抵消盾)  1 = 圆环+十字(生命保险)。AFP 购买窗口和玩家身上的状态图标共用同一份贴图。
/// 之后有正式美术图时，替换 GetSprite 的返回即可。
/// </summary>
public static class SkyPrisonAfpIcons
{
    public const int Shield = 0, Insurance = 1, Bolt = 2, Bullet = 3, Cycle = 4, Drop = 5;
    /// <summary>能力满档(最高档)的强调色：淡紫。购买窗口和角色面板共用。</summary>
    public static readonly Color MaxTierColor = new Color(0.79f, 0.66f, 1f, 1f);
    static readonly Sprite[] cache = new Sprite[6];
    static readonly Sprite[] cacheFramed = new Sprite[6];

    /// <summary>不带框的图案(AFP 窗口、角色面板用)。</summary>
    public static Sprite GetSprite(int kind) => Build(kind, false);

    /// <summary>导出用：不带框的图案按指定尺寸渲染成 PNG 字节(2x2 超采样)。状态图标的六边形外框由美术资产提供，这里只画中间图案。</summary>
    public static byte[] RenderPng(int kind, int size)
    {
        kind = Mathf.Clamp(kind, 0, 5);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = ((x + (sx + 0.5f) * 0.5f) / size) * 2f - 1f, v = ((y + (sy + 0.5f) * 0.5f) / size) * 2f - 1f;
                        if (InGlyph(kind, u, v)) hit++;
                    }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 4));
            }
        tex.SetPixels32(px); tex.Apply();
        var bytes = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return bytes;
    }

    /// <summary>带断续六边形外框的版本(状态图标用，导出成 PNG 资产)。</summary>
    public static Sprite GetFramedSprite(int kind) => Build(kind, true);

    static Sprite Build(int kind, bool framed)
    {
        kind = Mathf.Clamp(kind, 0, 5);
        var store = framed ? cacheFramed : cache;
        if (store[kind] != null) return store[kind];
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = ((x + (sx + 0.5f) * 0.5f) / S) * 2f - 1f, v = ((y + (sy + 0.5f) * 0.5f) / S) * 2f - 1f;
                        if (framed ? (InHexFrame(u, v) || InGlyph(kind, u / GlyphScale, v / GlyphScale)) : InGlyph(kind, u, v)) hit++;
                    }
                px[y * S + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 4));
            }
        tex.SetPixels32(px); tex.Apply(false, false);     // 保持可读：编辑器里要把它导出成 PNG 资产
        store[kind] = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        store[kind].hideFlags = HideFlags.HideAndDontSave;
        return store[kind];
    }

    // 闪电：一个 6 个顶点的折线多边形，射线法判断点在不在里面
    static readonly Vector2[] BoltPoly =
    {
        new Vector2(0.20f, 0.92f), new Vector2(-0.42f, -0.02f), new Vector2(-0.04f, -0.02f),
        new Vector2(-0.22f, -0.92f), new Vector2(0.44f, 0.10f), new Vector2(0.06f, 0.10f),
    };
    static bool InBolt(float u, float v)
    {
        bool inside = false;
        for (int i = 0, j = BoltPoly.Length - 1; i < BoltPoly.Length; j = i++)
        {
            var a = BoltPoly[i]; var b = BoltPoly[j];
            if ((a.y > v) != (b.y > v) && u < (b.x - a.x) * (v - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    // 所有能力图标统一带一圈「断续六边形」外框(左上、右下各一个缺口)，中间的图案缩到 GlyphScale 大小
    const float GlyphScale = 0.72f;

    static bool InGlyph(int kind, float u, float v)
    {
        return kind == Shield ? InShieldOutline(u, v) : kind == Insurance ? InInsuranceGlyph(u, v) : kind == Bullet ? InBullet(u, v) : kind == Cycle ? InCycle(u, v) : kind == Drop ? InDrop(u, v) : InBolt(u, v);
    }

    static bool InHexFrame(float u, float v)
    {
        float au = Mathf.Abs(u), av = Mathf.Abs(v);
        float h = Mathf.Max(av, 0.8660254f * au + 0.5f * av);       // 尖角朝左右的正六边形的「距离」，边界在 R·√3/2
        const float outer = 0.80f, inner = 0.725f;
        if (h > outer || h < inner) return false;
        float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;
        if (ang > 98f && ang < 158f) return false;                 // 左上缺口
        if (ang > 278f && ang < 338f) return false;                // 右下缺口
        return true;
    }

    // 篡夺者：一滴血，中间一个向上的箭头(攻击把红色部分夺回来)
    static bool InDrop(float u, float v)
    {
        float cy = -0.18f;
        float d = Mathf.Sqrt(u * u + (v - cy) * (v - cy));
        bool circle = d <= 0.62f;
        // 水滴上半部：从圆顶切线收成一个尖
        bool tip = v > cy && v <= 0.92f && Mathf.Abs(u) <= 0.62f * Mathf.Pow(Mathf.Clamp01((0.92f - v) / (0.92f - cy)), 1.15f);
        if (!(circle || tip)) return false;
        // 里面挖出一个向上的箭头(镂空)
        bool shaft = Mathf.Abs(u) <= 0.09f && v >= -0.42f && v <= 0.06f;
        bool head = v > 0.06f && v <= 0.36f && Mathf.Abs(u) <= (0.36f - v) * 0.8f + 0.0f;
        return !(shaft || head);
    }

    // 致命子弹：朝右飞的子弹(弹壳 + 弹头，弹壳上一道缝) + 前方的爆炸星(8 道尖角)
    static bool InBullet(float u, float v)
    {
        bool body = u >= -0.92f && u <= -0.10f && Mathf.Abs(v) <= 0.20f && !(u >= -0.62f && u <= -0.55f);
        bool tip = u > -0.10f && u <= 0.34f && Mathf.Abs(v) <= 0.20f * Mathf.Sqrt(Mathf.Max(0f, 1f - (u + 0.10f) / 0.44f));
        if (body || tip) return true;
        float du = u - 0.64f;
        float rr = Mathf.Sqrt(du * du + v * v);
        float ang = Mathf.Atan2(v, du);
        float R = 0.14f + 0.22f * Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * ang)), 3f);
        return rr <= R && rr <= 0.36f;
    }

    // 回转：一圈缺口的环 + 缺口处的箭头(顺时针转回来)
    static Vector2 Pol(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
    static bool InCycle(float u, float v)
    {
        float d = Mathf.Sqrt(u * u + v * v);
        float ang = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
        if (d >= 0.58f && d <= 0.80f && !(ang > 38f && ang < 80f)) return true;
        return InTri(new Vector2(u, v), Pol(80f, 0.97f), Pol(80f, 0.41f), Pol(40f, 0.69f));
    }

    static bool InShield(float u, float v)       // 盾形：上宽下尖
    {
        if (v < -0.9f || v > 0.85f) return false;
        float hw = v > 0f ? 0.72f : 0.72f * Mathf.Sqrt(Mathf.Clamp01((v + 0.9f) / 0.9f));
        return Mathf.Abs(u) <= hw;
    }

    static bool InShieldOutline(float u, float v)
    {
        if (!InShield(u, v)) return false;
        if (!InShield(u * 1.2f, (v - 0.04f) * 1.2f)) return true;                  // 外框
        return Mathf.Abs(u) < 0.07f && v > -0.35f && v < 0.45f;                    // 中间一竖
    }

    // 生命保险：公文包轮廓(带提手)，里面一个十字
    static bool InInsuranceGlyph(float u, float v)
    {
        const float t = 0.11f;
        bool body = u >= -0.78f && u <= 0.78f && v >= -0.62f && v <= 0.40f;
        bool bodyInner = u >= -0.78f + t && u <= 0.78f - t && v >= -0.62f + t && v <= 0.40f - t;
        if (body && !bodyInner) return true;
        bool handle = Mathf.Abs(u) <= 0.30f && v > 0.40f && v <= 0.64f;
        bool handleInner = Mathf.Abs(u) < 0.30f - t && v < 0.64f - t;
        if (handle && !handleInner) return true;
        float cv = v + 0.11f;
        if (Mathf.Abs(u) < 0.08f && Mathf.Abs(cv) < 0.28f) return true;             // 十字
        return Mathf.Abs(cv) < 0.08f && Mathf.Abs(u) < 0.28f;
    }
}
