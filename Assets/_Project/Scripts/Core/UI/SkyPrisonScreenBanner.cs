using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕正中的大字横带(可复用：战术研修解放、以后的 MISSION COMPLETE / 撤离成功之类)：
///   白底黑字的实心横带，竖向展开 → 停顿 → 黑白反色闪 2 次(黑带白字) → 收起。不阻塞玩家操作。
///   背后的动态细节：全屏暗层、上下两段会漂移呼吸的点阵、划开的细线+刻度、反向滚动的英文小字带。
/// 字用系统字体(Windows 微软雅黑)，不走游戏字体，所以任何字都能显示。
/// </summary>
public sealed class SkyPrisonScreenBanner : MonoBehaviour
{
    const float BarHeightK = 0.25f;        // 横带高 = 屏幕高的 25%
    const float BarWidthK  = 0.58f;        // 横带宽 = 屏幕宽的 58%
    const float FontK      = 0.155f;       // 字号 = 屏幕高的 15.5%
    const float TextLiftK  = 0.026f;       // 字整体上移 = 屏幕高的 2.6%：系统字体的行框留白上小下大，不补偿字形会偏下，视觉上不在横带正中
    const float DimAlpha   = 0.45f;        // 全屏暗色半透明层的最大不透明度
    const float OpenTime = 0.16f, HoldBefore = 0.45f, FlashStep = 0.11f, HoldAfter = 0.40f, CloseTime = 0.16f;

    const int   DotRows = 6;               // 点阵的行数(从贴着横带的一行往外逐行变淡)
    const float DotDrift = 0.12f;          // 点阵横向漂移速度(每秒漂几个点距)
    const float StripSpeedK = 0.06f;       // 英文小字带滚动速度 = 屏幕宽的这个倍数 / 秒
    // 装饰小字用「东亚重工」风格字体(和物品详情面板的 DATA 装饰字同一份)：写的是英文，字体会把字母渲染成乱码字形
    const string StripUnit = "FIELD TRAINING  //  TACTICAL STUDY UNLOCKED  //  ";
    const string DecoFontGuid = "ea35a3e4c89493f44bb2e5cbe046505c";
    const string DecoFontResourcePath = "UI/HUD/ToaHI SDF";      // 打包后走 Resources(同 SkyPrisonAFPHUD 的做法)
    const int SortingOrder = 32200;        // 要压在所有游戏窗口之上(战术研修窗口是 31900)，但低于鼠标光标(32767)和报错层(32700)

    static SkyPrisonScreenBanner _inst;

    GameObject _canvasGo; RectTransform _stage, _barRt;
    Image _dim, _bar; Text _text; CanvasGroup _details;
    RectTransform _dotsTop, _dotsBottom, _lineTop, _lineBottom;
    RawImage _dotsTopImg, _dotsBottomImg;
    TMP_Text _stripTopText, _stripBottomText; RectTransform _stripTopRt, _stripBottomRt;
    TMP_FontAsset _decoFont;
    Font _font;
    float _barW, _barH, _pitch, _stripPeriod, _dotTiles, _textLift;

    // 闪烁时的色差：红 / 绿 / 蓝(或青 / 品红 / 黄)三份字错开叠在一起，重叠处还原成原来的字色，边缘露出彩边
    readonly Text[] _chroma = new Text[3];
    Material _matAdd, _matMul;
    bool _flashing, _flashInverted;
    CanvasGroup[] _flickGroups;            // 细线 ×2、小字带 ×2：各自随机闪烁

    // 屏幕右下角的「坐标发信」：玩家当前坐标 + 向外扩散的同心圆 + 向几个方向射出的细线(线上有信号点向外跑)
    const int BeamCount = 8;
    static readonly float[] BeamAngle = { 0f, 30f, 65f, 90f, 125f, 160f, 180f, 200f };     // 朝下的只留一条浅角度的，免得线直接穿过下面的坐标文字
    const int PacketsPerBeam = 2;
    const float BeamOverscan = 0.12f;      // 线一直延伸到屏幕边缘之外多出这么多(屏幕高的倍数)，被屏幕边缘截断
    const float PacketSpeedK = 0.5f;       // 信号点跑的速度 = 屏幕高的这个倍数 / 秒
    CanvasGroup _corner; RectTransform _cornerRt, _markerRt;
    readonly RingGraphic[] _rings = new RingGraphic[3];
    float _ringReach;                      // 信号波一直扩散到的半径：覆盖到屏幕最远的角再多一点

    /// <summary>程序画的圆环：线宽固定(不像贴图放大后线会变粗)，半径随意变。</summary>
    sealed class RingGraphic : MaskableGraphic
    {
        const int Seg = 120;
        float _radius, _width = 2f;
        public void Set(float radius, float width)
        {
            if (Mathf.Approximately(radius, _radius) && Mathf.Approximately(width, _width)) return;
            _radius = radius; _width = width; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_radius <= 0.5f) return;
            float ri = Mathf.Max(0f, _radius - _width * 0.5f), ro = _radius + _width * 0.5f;
            for (int i = 0; i <= Seg; i++)
            {
                float a = Mathf.PI * 2f * i / Seg; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(d * ri, color, Vector2.zero); vh.AddVert(d * ro, color, Vector2.zero);
            }
            for (int i = 0; i < Seg; i++) { int k = i * 2; vh.AddTriangle(k, k + 1, k + 3); vh.AddTriangle(k, k + 3, k + 2); }
        }
    }
    Image _markerDot;
    readonly RectTransform[] _beams = new RectTransform[BeamCount];
    readonly Image[] _beamImg = new Image[BeamCount];
    readonly Image[] _packets = new Image[BeamCount * PacketsPerBeam];
    readonly RectTransform[] _packetRt = new RectTransform[BeamCount * PacketsPerBeam];
    readonly System.Collections.Generic.List<RectTransform>[] _tickRts = new System.Collections.Generic.List<RectTransform>[BeamCount];
    readonly System.Collections.Generic.List<Image>[] _tickImgs = new System.Collections.Generic.List<Image>[BeamCount];
    readonly float[] _beamLens = new float[BeamCount];
    float _ringMax, _coordNext, _tickStep;
    Text _coordMapText, _coordNumText; TMP_Text _coordDecoText; Font _monoFont;
    static Sprite _diskSpr;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        _inst = null;
        TechTreeReveal.OnRevealChanged -= OnTechRevealed;
        TechTreeReveal.OnRevealChanged += OnTechRevealed;
    }

    static void OnTechRevealed() => Show("战术研修解放");

    public static void Show(string text)
    {
        if (_inst == null)
        {
            var go = new GameObject("[ScreenBanner]");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<SkyPrisonScreenBanner>();
            _inst.Build();
        }
        _inst.StopAllCoroutines();
        _inst.StartCoroutine(_inst.Play(text));
    }

    // ── 搭建(只搭一次；尺寸每次播放时按当前屏幕重新排) ──────────
    void Build()
    {
        _canvasGo = new GameObject("Canvas");
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var dimRt = Stretch(NewRect("Dim", _canvasGo.transform));
        _dim = dimRt.gameObject.AddComponent<Image>();
        _dim.color = new Color(0f, 0f, 0f, 0f); _dim.raycastTarget = false;

        _stage = NewRect("Stage", _canvasGo.transform);
        _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(0.5f, 0.5f);
        _stage.anchoredPosition = Vector2.zero;

        _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 100);

        // 背后的细节(先建，被横带盖在下面)
        var detRt = Stretch(NewRect("Details", _stage));
        _details = detRt.gameObject.AddComponent<CanvasGroup>(); _details.alpha = 0f; _details.blocksRaycasts = false;
        _dotsTop    = MakeDots(detRt, true,  out _dotsTopImg);
        _dotsBottom = MakeDots(detRt, false, out _dotsBottomImg);
        _lineTop    = MakeLine(detRt, true);
        _lineBottom = MakeLine(detRt, false);
        _stripTopRt    = MakeStrip(detRt, out _stripTopText);
        _stripBottomRt = MakeStrip(detRt, out _stripBottomText);

        // 横带本体 + 字
        _barRt = NewRect("Bar", _stage);
        _barRt.anchorMin = _barRt.anchorMax = _barRt.pivot = new Vector2(0.5f, 0.5f);
        _bar = _barRt.gameObject.AddComponent<Image>(); _bar.raycastTarget = false;
        var textRt = Stretch(NewRect("Text", _barRt));
        _text = textRt.gameObject.AddComponent<Text>();
        _text.font = _font; _text.alignment = TextAnchor.MiddleCenter; _text.raycastTarget = false;
        _text.horizontalOverflow = HorizontalWrapMode.Overflow; _text.verticalOverflow = VerticalWrapMode.Overflow;

        // 色差用的三份字(平时不显示，只在反色闪烁时出现)
        var shader = Resources.Load<Shader>("UI/SkyPrisonBlendText");
        if (shader == null) shader = Shader.Find("SkyPrison/UI/BlendText");
        _matAdd = MakeBlendMat(shader, 5, 1, 0f);     // SrcAlpha / One：加算
        _matMul = MakeBlendMat(shader, 2, 0, 1f);     // DstColor / Zero：相乘
        for (int i = 0; i < 3; i++)
        {
            var cr = Stretch(NewRect("Chroma" + i, _barRt));
            var ct = cr.gameObject.AddComponent<Text>();
            ct.font = _font; ct.alignment = TextAnchor.MiddleCenter; ct.raycastTarget = false;
            ct.horizontalOverflow = HorizontalWrapMode.Overflow; ct.verticalOverflow = VerticalWrapMode.Overflow;
            ct.enabled = false;
            _chroma[i] = ct;
        }

        _flickGroups = new[]
        {
            _lineTop.gameObject.AddComponent<CanvasGroup>(), _lineBottom.gameObject.AddComponent<CanvasGroup>(),
            _stripTopRt.gameObject.AddComponent<CanvasGroup>(), _stripBottomRt.gameObject.AddComponent<CanvasGroup>()
        };

        BuildCorner();
        _canvasGo.SetActive(false);
    }

    void BuildCorner()
    {
        _cornerRt = NewRect("Corner", _canvasGo.transform);
        _cornerRt.anchorMin = _cornerRt.anchorMax = _cornerRt.pivot = new Vector2(1f, 0f);
        _cornerRt.anchoredPosition = Vector2.zero; _cornerRt.sizeDelta = Vector2.zero;
        _corner = _cornerRt.gameObject.AddComponent<CanvasGroup>(); _corner.alpha = 0f; _corner.blocksRaycasts = false;

        _monoFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Microsoft YaHei UI" }, 100);

        // 信号波：三圈同心圆错开相位向外扩散
        _markerRt = NewRect("Marker", _cornerRt);
        _markerRt.anchorMin = _markerRt.anchorMax = _markerRt.pivot = new Vector2(1f, 0f);
        for (int i = 0; i < 3; i++)
        {
            var r = NewRect("Ring" + i, _markerRt);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            var ring = r.gameObject.AddComponent<RingGraphic>(); ring.raycastTarget = false; ring.color = new Color(1f, 1f, 1f, 0f);
            _rings[i] = ring;
        }
        // 向各个方向射出的细线：一直延伸到屏幕外，越远越淡；沿线有测距刻度；线上有信号点向外跑
        for (int i = 0; i < BeamCount; i++)
        {
            var b = NewRect("Beam" + i, _markerRt);
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f); b.pivot = new Vector2(0f, 0.5f);
            b.localRotation = Quaternion.Euler(0f, 0f, BeamAngle[i]);
            _beams[i] = b; _beamImg[i] = b.gameObject.AddComponent<Image>();
            _beamImg[i].sprite = FadeLineSprite(); _beamImg[i].type = Image.Type.Simple; _beamImg[i].raycastTarget = false;
            _tickRts[i] = new System.Collections.Generic.List<RectTransform>(); _tickImgs[i] = new System.Collections.Generic.List<Image>();
            for (int k = 0; k < PacketsPerBeam; k++)
            {
                var pk = NewRect("Packet" + k, b); pk.anchorMin = pk.anchorMax = new Vector2(0f, 0.5f); pk.pivot = new Vector2(0.5f, 0.5f);
                int idx = i * PacketsPerBeam + k;
                pk.localRotation = Quaternion.Euler(0f, 0f, -BeamAngle[i]);     // 抵消线的倾斜，方块始终水平竖直摆正
                _packetRt[idx] = pk; _packets[idx] = pk.gameObject.AddComponent<Image>(); _packets[idx].raycastTarget = false;      // 不设 sprite = 实心正方形(随线的角度转，边与线平行)
            }
        }
        var dot = NewRect("Dot", _markerRt);
        dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0.5f, 0.5f);
        _markerDot = dot.gameObject.AddComponent<Image>(); _markerDot.sprite = DiskSprite(); _markerDot.raycastTarget = false;

        // 坐标文字：装饰字(乱码字体) + 地图名 + 坐标数字(等宽字体，看得清)
        if (_decoFont == null) _decoFont = LoadDecoFont();
        var dr = NewRect("CoordDeco", _cornerRt);
        dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(1f, 0f);
        var deco = dr.gameObject.AddComponent<TextMeshProUGUI>();
        if (_decoFont != null) deco.font = _decoFont;
        deco.alignment = TextAlignmentOptions.BottomRight; deco.color = new Color(1f, 1f, 1f, 0.55f); deco.raycastTarget = false;
        deco.enableWordWrapping = false; deco.overflowMode = TextOverflowModes.Overflow; deco.text = "TRANSMITTING SIGNAL";
        _coordDecoText = deco;
        _coordMapText = MakeCoordText("CoordMap", new Color(1f, 1f, 1f, 0.7f));
        _coordNumText = MakeCoordText("CoordNum", new Color(1f, 1f, 1f, 0.95f));
    }

    Text MakeCoordText(string name, Color col)
    {
        var r = NewRect(name, _cornerRt);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 0f);
        var t = r.gameObject.AddComponent<Text>();
        t.font = _monoFont; t.alignment = TextAnchor.LowerRight; t.color = col; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    // 按当前屏幕大小排版右下角
    void LayoutCorner()
    {
        float H = Screen.height, m = H * 0.04f, px = H / 1080f;
        float line = H * 0.032f;
        SetCornerText(_coordNumText.rectTransform, new Vector2(-m, m), line); _coordNumText.fontSize = Mathf.RoundToInt(H * 0.024f);
        SetCornerText(_coordMapText.rectTransform, new Vector2(-m, m + line), line); _coordMapText.fontSize = Mathf.RoundToInt(H * 0.02f);
        SetCornerText(_coordDecoText.rectTransform, new Vector2(-m, m + line * 2f), line); _coordDecoText.fontSize = H * 0.017f;

        _ringMax = H * 0.125f;
        _markerRt.anchoredPosition = new Vector2(-(m + _ringMax + 6f * px), m + line * 3f + _ringMax + 4f * px);
        _markerRt.sizeDelta = Vector2.zero;
        _markerDot.rectTransform.sizeDelta = new Vector2(14f * px, 14f * px);

        // 线的长度：从发信点沿各自方向一直到屏幕边缘，再多出一截(被屏幕截断，看起来是无限延伸出去)
        float W = Screen.width, mx = W + _markerRt.anchoredPosition.x, my = _markerRt.anchoredPosition.y;
        _tickStep = H * 0.09f;
        _ringReach = Mathf.Max(Mathf.Max(Vector2.Distance(new Vector2(mx, my), Vector2.zero), Vector2.Distance(new Vector2(mx, my), new Vector2(0f, H))),
                               Mathf.Max(Vector2.Distance(new Vector2(mx, my), new Vector2(W, 0f)), Vector2.Distance(new Vector2(mx, my), new Vector2(W, H)))) + H * 0.1f;
        for (int i = 0; i < BeamCount; i++)
        {
            float rad = BeamAngle[i] * Mathf.Deg2Rad, dx = Mathf.Cos(rad), dy = Mathf.Sin(rad);
            float tx = dx > 1e-4f ? (W - mx) / dx : dx < -1e-4f ? -mx / dx : float.MaxValue;
            float ty = dy > 1e-4f ? (H - my) / dy : dy < -1e-4f ? -my / dy : float.MaxValue;
            float len = Mathf.Min(tx, ty) + H * BeamOverscan;
            _beamLens[i] = len;
            _beams[i].sizeDelta = new Vector2(len, Mathf.Max(2f, 2f * px));
            for (int k = 0; k < PacketsPerBeam; k++) _packetRt[i * PacketsPerBeam + k].sizeDelta = new Vector2(9f * px, 9f * px);

            // 沿线每隔一段一根测距刻度(每 4 根有一根长的)；够用就行，多余的关掉
            int n = Mathf.FloorToInt(len / _tickStep);
            var rts = _tickRts[i]; var imgs = _tickImgs[i];
            while (rts.Count < n)
            {
                var tk = NewRect("Tick", _beams[i]); tk.anchorMin = tk.anchorMax = new Vector2(0f, 0.5f); tk.pivot = new Vector2(0.5f, 0.5f);
                var ti = tk.gameObject.AddComponent<Image>(); ti.raycastTarget = false;
                rts.Add(tk); imgs.Add(ti);
            }
            for (int k = 0; k < rts.Count; k++)
            {
                bool on = k < n; rts[k].gameObject.SetActive(on);
                if (!on) continue;
                bool longTick = (k + 1) % 4 == 0;
                rts[k].anchoredPosition = new Vector2(_tickStep * (k + 1), 0f);
                rts[k].sizeDelta = new Vector2(Mathf.Max(2f, 2f * px), (longTick ? 20f : 10f) * px);
            }
        }
    }

    static void SetCornerText(RectTransform rt, Vector2 pos, float h) { rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(Screen.width * 0.3f, h); }

    void RefreshCoords()
    {
        var p = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        _coordMapText.text = "LOC  " + map.ToUpperInvariant();
        _coordNumText.text = p != null
            ? "(" + p.transform.position.x.ToString("0.0") + ", " + p.transform.position.z.ToString("0.0") + ")"       // 二元坐标 (x, z)
            : "(---, ---)";
    }

    void UpdateCorner(float t)
    {
        if (t >= _coordNext) { _coordNext = t + 0.2f; RefreshCoords(); }
        float tint = 0.95f;
        _markerDot.color = new Color(1f, 1f, 1f, tint * (0.7f + 0.3f * Mathf.Sin(t * 7f)));
        float ringW = Mathf.Max(1.5f, 1.5f * Screen.height / 1080f);      // 细白线，线宽固定
        for (int i = 0; i < 3; i++)
        {
            float k = Mathf.Repeat(t * 0.28f + i / 3f, 1f);              // 每圈约 3.6 秒扩散到屏幕外，三圈错开
            _rings[i].Set(k * _ringReach, ringW);
            _rings[i].color = new Color(1f, 1f, 1f, Mathf.Min(1f, k * 8f) * Mathf.Pow(1f - k, 1.3f) * 0.85f);     // 刚出现时淡入，越远越淡
        }
        float speed = Screen.height * PacketSpeedK;
        for (int i = 0; i < BeamCount; i++)
        {
            float fl = Flick(t, 11.3f + i * 3.1f, 14f, 0.14f, 0.7f);
            float len = Mathf.Max(1f, _beamLens[i]);
            _beamImg[i].color = new Color(1f, 1f, 1f, 0.6f * fl);       // 线本身的渐隐在贴图里(越远越淡)
            var imgs = _tickImgs[i];
            for (int k = 0; k < imgs.Count; k++)
            {
                if (!imgs[k].gameObject.activeSelf) continue;
                float along = _tickStep * (k + 1) / len;
                imgs[k].color = new Color(1f, 1f, 1f, (1f - 0.85f * along) * 0.7f * fl);
            }
            for (int k = 0; k < PacketsPerBeam; k++)       // 信号点匀速向屏幕外跑，越远越淡，跑出去就从头再来
            {
                float p = Mathf.Repeat(t * speed / len * (0.85f + 0.06f * i) + i * 0.29f + k / (float)PacketsPerBeam, 1f);
                int idx = i * PacketsPerBeam + k;
                _packetRt[idx].anchoredPosition = new Vector2(p * len, 0f);
                _packets[idx].color = new Color(1f, 1f, 1f, Mathf.Min(1f, p * 12f) * (1f - 0.7f * p) * fl);
            }
        }
    }

    static Sprite _fadeLineSpr;
    /// <summary>延伸的细线贴图：近端不透明，越往远端越淡(横向渐隐)。</summary>
    static Sprite FadeLineSprite()
    {
        if (_fadeLineSpr != null) return _fadeLineSpr;
        const int N = 256;
        var tex = new Texture2D(N, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < N; x++)
        {
            float u = x / (float)(N - 1);
            float a = Mathf.Lerp(1f, 0.12f, Mathf.Pow(u, 0.7f));
            for (int y = 0; y < 2; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        _fadeLineSpr = Sprite.Create(tex, new Rect(0, 0, N, 2), new Vector2(0.5f, 0.5f), 100f);
        return _fadeLineSpr;
    }

    static Sprite DiskSprite()
    {
        if (_diskSpr != null) return _diskSpr;
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) / 0.25f)));
            }
        tex.Apply();
        _diskSpr = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _diskSpr;
    }

    static Material MakeBlendMat(Shader s, int src, int dst, float mul)
    {
        var m = new Material(s);
        m.SetFloat("_SrcBlend", src); m.SetFloat("_DstBlend", dst); m.SetFloat("_Mul", mul);
        return m;
    }

    RectTransform MakeDots(RectTransform parent, bool top, out RawImage img)
    {
        var rt = NewRect(top ? "DotsTop" : "DotsBottom", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, top ? 0f : 1f);          // 贴着横带的那条边为轴，向外长出
        img = rt.gameObject.AddComponent<RawImage>();
        img.texture = DotTexture(); img.raycastTarget = false;
        return rt;
    }

    RectTransform MakeLine(RectTransform parent, bool top)
    {
        var rt = NewRect(top ? "LineTop" : "LineBottom", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.75f); img.raycastTarget = false;
        // 刻度：每 1/16 一根短刻度，两端一根长的(都朝外)
        for (int i = 0; i <= 16; i++)
        {
            bool end = i == 0 || i == 16;
            var t = NewRect("Tick", rt);
            float u = i / 16f;
            t.anchorMin = t.anchorMax = new Vector2(u, 0.5f);
            t.pivot = new Vector2(0.5f, top ? 0f : 1f);
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = new Vector2(2f, end ? 2f : 1f);       // 高度排版时按屏幕重算(存个相对值：2=长 1=短)
            var ti = t.gameObject.AddComponent<Image>(); ti.color = new Color(1f, 1f, 1f, end ? 0.75f : 0.5f); ti.raycastTarget = false;
        }
        return rt;
    }

    RectTransform MakeStrip(RectTransform parent, out TMP_Text text)
    {
        var rt = NewRect("Strip", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.gameObject.AddComponent<RectMask2D>();
        var tr = NewRect("Text", rt);
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 0.5f); tr.pivot = new Vector2(0f, 0.5f);
        var tmp = tr.gameObject.AddComponent<TextMeshProUGUI>();
        if (_decoFont == null) _decoFont = LoadDecoFont();
        if (_decoFont != null) tmp.font = _decoFont;
        tmp.alignment = TextAlignmentOptions.MidlineLeft; tmp.color = new Color(1f, 1f, 1f, 0.55f); tmp.raycastTarget = false;
        tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Overflow;
        text = tmp;
        return rt;
    }

    static TMP_FontAsset LoadDecoFont()
    {
#if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(DecoFontGuid);
        if (!string.IsNullOrEmpty(path))
        {
            var f = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f != null) return f;
        }
#endif
        return Resources.Load<TMP_FontAsset>(DecoFontResourcePath);
    }

    // 按当前屏幕大小排版所有细节
    void Layout()
    {
        float H = Screen.height, W = Screen.width, px = H / 1080f;
        _barW = W * BarWidthK; _barH = H * BarHeightK;
        _stage.sizeDelta = new Vector2(_barW, _barH);
        _barRt.sizeDelta = new Vector2(_barW, _barH);
        _text.fontSize = Mathf.RoundToInt(H * FontK);
        foreach (var c in _chroma) c.fontSize = _text.fontSize;
        _textLift = H * TextLiftK;
        _text.rectTransform.anchoredPosition = new Vector2(0f, _textLift);

        _pitch = Mathf.Max(5f, 8f * px);
        float dotsH = DotRows * _pitch;
        _dotTiles = _barW / _pitch;
        float half = _barH * 0.5f;
        float gap = 6f * px, lineH = Mathf.Max(2f, 2f * px), stripH = 30f * px;

        SetBox(_dotsTop,    new Vector2(0f, half), new Vector2(_barW, dotsH));
        SetBox(_dotsBottom, new Vector2(0f, -half), new Vector2(_barW, dotsH));
        float lineY = half + dotsH + gap;
        SetBox(_lineTop,    new Vector2(0f, lineY), new Vector2(_barW, lineH));
        SetBox(_lineBottom, new Vector2(0f, -lineY), new Vector2(_barW, lineH));
        foreach (var rt in new[] { _lineTop, _lineBottom })
            foreach (RectTransform t in rt)
            {
                bool longTick = t.sizeDelta.y >= 2f - 0.01f && t.sizeDelta.y <= 2f + 0.01f;
                t.sizeDelta = new Vector2(Mathf.Max(2f, 2f * px), (longTick ? 22f : 10f) * px);
            }
        SetBox(_stripTopRt,    new Vector2(0f, lineY + lineH + 6f * px + stripH * 0.5f), new Vector2(_barW, stripH));
        SetBox(_stripBottomRt, new Vector2(0f, -(lineY + lineH + 6f * px + stripH * 0.5f)), new Vector2(_barW, stripH));

        int small = Mathf.RoundToInt(H * 0.024f);
        foreach (var st in new[] { _stripTopText, _stripBottomText }) { st.fontSize = small; st.text = StripUnit; }
        float one = _stripTopText.preferredWidth;
        _stripTopText.text = StripUnit + StripUnit;
        _stripPeriod = Mathf.Max(10f, _stripTopText.preferredWidth - one);       // 两份减一份 = 一个周期(含结尾空格，不受首尾裁剪影响)
        int copies = Mathf.CeilToInt(_barW / _stripPeriod) + 3;
        var sb = new System.Text.StringBuilder(); for (int i = 0; i < copies; i++) sb.Append(StripUnit);
        _stripTopText.text = _stripBottomText.text = sb.ToString();
        foreach (var st in new[] { _stripTopText, _stripBottomText })
            st.rectTransform.sizeDelta = new Vector2(_stripPeriod * copies, stripH);

        LayoutCorner();
    }

    static void SetBox(RectTransform rt, Vector2 pos, Vector2 size) { rt.anchoredPosition = pos; rt.sizeDelta = size; }

    // ── 播放 ─────────────────────────────────────────────────
    IEnumerator Play(string text)
    {
        Layout();
        _text.text = text; _text.color = Color.black; _bar.color = Color.white;
        foreach (var c in _chroma) c.text = text;
        EndFlash();
        _stage.localScale = Vector3.one;
        _barRt.localScale = new Vector3(1f, 0f, 1f);
        _dim.color = new Color(0f, 0f, 0f, 0f);
        _details.alpha = 0f; _corner.alpha = 0f; _coordNext = 0f;
        foreach (var rt in new[] { _dotsTop, _dotsBottom }) rt.localScale = new Vector3(1f, 0f, 1f);
        foreach (var rt in new[] { _lineTop, _lineBottom }) rt.localScale = new Vector3(0f, 1f, 1f);
        _canvasGo.SetActive(true);

        // 横带展开；细节稍后跟着长出来
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.BannerOpen);
        StartCoroutine(Details(true));
        yield return Anim(OpenTime, k => { _barRt.localScale = new Vector3(1f, k, 1f); _dim.color = new Color(0f, 0f, 0f, k * DimAlpha); });
        yield return new WaitForSecondsRealtime(HoldBefore);
        for (int i = 0; i < 2; i++)
        {
            SetFlash(true);  yield return new WaitForSecondsRealtime(FlashStep);     // 黑带白字 + 红绿蓝色差
            SetFlash(false); yield return new WaitForSecondsRealtime(FlashStep);     // 白带黑字 + 青品红黄色差
        }
        EndFlash();
        yield return new WaitForSecondsRealtime(HoldAfter);
        StartCoroutine(Details(false));
        yield return Anim(CloseTime, k => { _barRt.localScale = new Vector3(1f, 1f - k, 1f); _dim.color = new Color(0f, 0f, 0f, (1f - k) * DimAlpha); });
        yield return new WaitForSecondsRealtime(0.2f);       // 细节淡出收尾
        _canvasGo.SetActive(false);
    }

    // 闪烁的一拍：横带黑 / 白，字换成三份错开的色差字
    void SetFlash(bool inverted)
    {
        _flashing = true; _flashInverted = inverted;        _bar.color = inverted ? Color.black : Color.white;
        _text.enabled = false;
        // 黑带上叠红绿蓝(加算)，白带上叠青品红黄(相乘)：三份重叠处正好还原成白字 / 黑字
        Color[] cols = inverted
            ? new[] { new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 1f) }
            : new[] { new Color(0f, 1f, 1f, 1f), new Color(1f, 0f, 1f, 1f), new Color(1f, 1f, 0f, 1f) };
        for (int i = 0; i < 3; i++) { _chroma[i].material = inverted ? _matAdd : _matMul; _chroma[i].color = cols[i]; _chroma[i].enabled = true; }
    }

    void EndFlash()
    {
        _flashing = false;
        _bar.color = Color.white; _text.color = Color.black; _text.enabled = true;
        foreach (var c in _chroma) { c.enabled = false; c.rectTransform.anchoredPosition = new Vector2(0f, _textLift); }
        if (_flickGroups != null) foreach (var g in _flickGroups) g.alpha = 1f;
    }

    static float Hash(float x) { float s = Mathf.Sin(x * 12.9898f) * 43758.5453f; return s - Mathf.Floor(s); }

    // 随机闪烁：大多数时候是 1，偶尔掉到较暗(像不稳定的投影)；闪烁期间掉得更勤、更深
    static float Flick(float t, float seed, float rate, float chance, float depth)
    {
        float step = Mathf.Floor(t * rate + seed * 17f);
        return Hash(step + seed) > 1f - chance ? 1f - depth * (0.4f + 0.6f * Hash(step * 1.7f + seed)) : 1f;
    }

    // 细节长出 / 收回：点阵向外长、细线从中间向两端划开、整体淡入淡出
    IEnumerator Details(bool show)
    {
        if (show) yield return new WaitForSecondsRealtime(0.06f);
        yield return Anim(show ? 0.34f : 0.2f, k =>
        {
            float g = show ? k : 1f - k;
            _details.alpha = g; _corner.alpha = g;
            var dots = new Vector3(1f, g, 1f); _dotsTop.localScale = dots; _dotsBottom.localScale = dots;
            var line = new Vector3(g, 1f, 1f); _lineTop.localScale = line; _lineBottom.localScale = line;
        });
    }

    IEnumerator Anim(float dur, System.Action<float> step)
    {
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime) { step(Mathf.SmoothStep(0f, 1f, t / dur)); yield return null; }
        step(1f);
    }

    // 点阵漂移 + 呼吸；小字带反向滚动
    void Update()
    {
        if (_canvasGo == null || !_canvasGo.activeSelf) return;
        float t = Time.unscaledTime;
        UpdateCorner(t);
        float off = t * DotDrift;
        _dotsTopImg.uvRect    = new Rect(off, 0f, _dotTiles, 1f);
        _dotsBottomImg.uvRect = new Rect(-off, 1f, _dotTiles, -1f);
        float rate = _flashing ? 30f : 12f, chance = _flashing ? 0.55f : 0.16f, depth = _flashing ? 0.9f : 0.65f;
        float breathe = 0.78f + 0.22f * Mathf.Sin(t * 3.2f);
        _dotsTopImg.color    = new Color(1f, 1f, 1f, breathe * Flick(t, 1.3f, rate, chance, depth));
        _dotsBottomImg.color = new Color(1f, 1f, 1f, breathe * Flick(t, 4.1f, rate, chance, depth));
        for (int i = 0; i < _flickGroups.Length; i++) _flickGroups[i].alpha = Flick(t, 7.7f + i * 2.9f, rate, chance, depth);

        if (_flashing)       // 色差抖动：红 / 蓝两份往两边错开，每帧幅度和上下都有随机
        {
            float amp = Screen.height * 0.009f * (0.55f + 0.45f * Hash(t * 61f));
            float vy = (Hash(t * 47f) - 0.5f) * amp * 0.35f;
            _chroma[0].rectTransform.anchoredPosition = new Vector2(-amp, _textLift + vy);
            _chroma[1].rectTransform.anchoredPosition = new Vector2(0f, _textLift);
            _chroma[2].rectTransform.anchoredPosition = new Vector2(amp, _textLift - vy);
        }

        float run = Mathf.Repeat(t * Screen.width * StripSpeedK, _stripPeriod);
        _stripTopText.rectTransform.anchoredPosition    = new Vector2(-run, 0f);
        _stripBottomText.rectTransform.anchoredPosition = new Vector2(-_stripPeriod + run, 0f);
    }

    // ── 小工具 ────────────────────────────────────────────────
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Texture2D _dotTex;
    /// <summary>点阵贴图：8 像素宽(横向平铺)，高 DotRows 行(竖向不平铺)；第 0 行最亮，往外逐行变淡。</summary>
    static Texture2D DotTexture()
    {
        if (_dotTex != null) return _dotTex;
        const int N = 8;
        _dotTex = new Texture2D(N, N * DotRows, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        for (int row = 0; row < DotRows; row++)
        {
            float a = Mathf.Lerp(0.85f, 0.1f, row / (float)(DotRows - 1));
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool dot = x >= 2 && x <= 5 && y >= 2 && y <= 5 && !((x == 2 || x == 5) && (y == 2 || y == 5));
                    _dotTex.SetPixel(x, row * N + y, new Color(1f, 1f, 1f, dot ? a : 0f));
                }
        }
        _dotTex.Apply();
        return _dotTex;
    }
}
