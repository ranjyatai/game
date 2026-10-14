using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕顶部的 BOSS 血条。
/// 规则：characterIdentity == Boss 的单位不显示头顶单位 HUD(这里每帧强制关掉它的 Canvas)，改由这里在屏幕顶端统一显示。
/// 出现时机：BOSS 看见玩家，或已经受伤；消失：BOSS 死亡(先停留 TARGET NEUTRALIZED)，或回满血且很久没看见玩家(被拴绳拉回/玩家逃走)。
/// 自带独立 Canvas，BeforeSceneLoad 自建(做法同 DialogueSubtitleHUD)。同一时刻只显示一个 BOSS(取最先接战的)。
/// 样式：白色窄条 + 黑色底(带很淡的方格分隔线) + 两端白色小矩形；名字居中淡红；左上 TARGET LOCKED、右上 SYS 血量。
/// 色差：整个模块挂和玩家 HP/LP 同一套 HUD 模块后处理(真·RT 色差)。
/// 登场演出：电路走线场流动 → 方格点亮成血条(名字乱码解码) → 走线淡去、端头弹出。
/// </summary>
public class SkyPrisonBossHealthBar : MonoBehaviour
{
    private const float PollInterval = 0.25f;
    private const float DisengageSeconds = 4f;
    private const float FadeSpeed = 4f;
    private const float ChipDelay = 0.5f;       // 掉血后残影停留多久再追上
    private const float ChipSpeed = 0.6f;       // 残影追赶速度(占总血量的比例/秒)

    private const float IntroSeconds = 1.2f;    // 登场演出总长
    private const float NeutralizedShowSeconds = 2.2f;
    private const float CellPx = 14f;           // 方格单元边长，等于血条高度
    private const string ScrambleChars = "01#%/|<>[]=+*:;";

    private static readonly Color NameColor = new Color(1f, 0.64f, 0.69f, 1f);      // 名字/标签：淡红
    private static readonly Color ChipColor = new Color(0.42f, 0.12f, 0.12f, 1f);   // 一般敌人头顶血条的扣血色(DamageRef)

    // 电路走线场(参考「失落的宇宙」OP)：长短不一的横/竖/斜线持续流过、成束并行、小圆点和半透明色块。每次登场重新随机
    private const int TraceCount = 170, DotCount = 30, BlockCount = 9;
    private const float FieldH = 90f, FieldBottom = -24f;

    private static SkyPrisonBossHealthBar _instance;

    private Canvas _canvas;
    private CanvasGroup _group;        // 挂在 Canvas 上，交给 SceneLoader 揭幕系统管(加载画面期间它会压成 0)，这里绝不能改它的 alpha
    private CanvasGroup _fadeGroup;    // 血条自己的淡入淡出
    private readonly System.Collections.Generic.HashSet<UnitHealthController> _hooked = new System.Collections.Generic.HashSet<UnitHealthController>();
    private readonly System.Collections.Generic.HashSet<UnitHealthController> _hurt = new System.Collections.Generic.HashSet<UnitHealthController>();
    // 所有 Boss 身份单位的头顶 UI：每帧强制关掉它的 Canvas。
    // 不能指望 UnitOverheadHealthBridge 来隐藏——实测 Boss 身上这个组件运行时是被禁用的，里面的逻辑根本不会执行。
    private readonly System.Collections.Generic.List<UnitOverheadUIView> _bossOverheadViews = new System.Collections.Generic.List<UnitOverheadUIView>();

    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _hpText;
    private TextMeshProUGUI _labelText;     // 左上角战术标签：TARGET LOCKED / TARGET NEUTRALIZED
    private RectTransform _moduleRoot;      // 色差模块根(和玩家 HP/LP 同一套真·RT 色差)
    private RectTransform _trackBg;         // 损失血量的深色底(CleanBG_，不进色差 RT)
    private RectTransform _barRt;
    private Image _fill;
    private Image _chip;
    private RawImage _edgeGlow;             // 实血右端的纵向发光(受击时脉冲变亮)，CleanFG_ 前景层，不进色差 RT
    private static Texture2D _glowTex;
    private float _glowPulse;
    private float _barInsetL, _barW;        // 发光体定位用：血条在模块根里的左边距和宽度

    private RectTransform _gridRoot, _cellsRoot, _capL, _capR;
    private CanvasGroup _gridCg, _cellsCg, _trackBgCg;
    private RectTransform[] _tr; private Image[] _trImg;
    private float[] _trDelay, _trLife, _trAlpha, _trVel, _trBaseX, _trBaseY; private Vector2[] _trDir;
    private RectTransform[] _dots; private Image[] _dotImg; private float[] _dotDelay;
    private RectTransform[] _blocks; private Image[] _blockImg; private float[] _blockDelay, _blockAlpha;
    private Image[] _cells; private float[] _cellDelay;
    private int _cellN;
    private float _fieldW;

    private bool _chromaAttached;
    private float _nextAttachTry;
    private float _introStart = -999f;
    private float _deathTime = -1f;

    private UnitHealthController _boss;
    private UnitDeathController _bossDeath;
    private UnitPerceptionRuntime _bossPerception;
    private string _bossName = "";
    private float _nextPoll;
    private float _lastSeenOrHurtTime;
    private float _chipFill = 1f;
    private float _lastHp = -1f;
    private float _chipHoldUntil;
    private bool _fontBound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        var existing = FindObjectsByType<SkyPrisonBossHealthBar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (existing.Length > 0)
        {
            _instance = existing[0];
            for (int i = 1; i < existing.Length; i++)
                if (existing[i] != null) Destroy(existing[i].gameObject);
            return;
        }
        if (_instance != null) return;
        // 不能用 HideAndDontSave：带 DontSave 的物体退出 Play 后不会被销毁，会一直残留在编辑器的 Game 视图里
        var go = new GameObject("[SkyPrisonBossHealthBar]") { hideFlags = HideFlags.HideInHierarchy };
        _instance = go.AddComponent<SkyPrisonBossHealthBar>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        TryBindFont();

        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + PollInterval;
            PollBoss();
        }

        HideBossOverheadUi();

        float targetAlpha = _boss != null ? 1f : 0f;
        _fadeGroup.alpha = Mathf.MoveTowards(_fadeGroup.alpha, targetAlpha, FadeSpeed * Time.unscaledDeltaTime);
        if (_boss == null) return;

        TryAttachChromatic();
        UpdatePresentation();

        float max = Mathf.Max(1f, _boss.MaxHealth);
        float cur = Mathf.Clamp01(_boss.CurrentHealth / max);
        if (_lastHp >= 0f && _boss.CurrentHealth < _lastHp - 0.001f)
        {
            _chipHoldUntil = Time.unscaledTime + ChipDelay;
            _glowPulse = 1f;
            _lastSeenOrHurtTime = Time.unscaledTime;
        }
        _lastHp = _boss.CurrentHealth;

        if (cur >= _chipFill) _chipFill = cur;
        else if (Time.unscaledTime >= _chipHoldUntil)
            _chipFill = Mathf.MoveTowards(_chipFill, cur, ChipSpeed * Time.unscaledDeltaTime);

        SetFill(_fill, cur);
        {
            var crt = (RectTransform)_chip.transform;      // 残影只显示「已掉血但还没追上」的那一段
            crt.anchorMin = new Vector2(Mathf.Clamp01(cur), 0f);
            crt.anchorMax = new Vector2(Mathf.Clamp01(_chipFill), 1f);
        }

        // 实血右端纵向发光：平时微亮，受击瞬间更宽更高更亮
        if (_edgeGlow != null)
        {
            _glowPulse = Mathf.MoveTowards(_glowPulse, 0f, 2.2f * Time.unscaledDeltaTime);
            var grt = _edgeGlow.rectTransform;
            grt.anchoredPosition = new Vector2(_barInsetL + cur * _barW, 7f);
            float fillA = Mathf.Clamp01((Time.unscaledTime - _introStart - 0.9f) / 0.2f);
            float a = (0.55f + 0.45f * _glowPulse) * fillA * (cur > 0.001f ? 1f : 0f);
            _edgeGlow.color = new Color(1f, 1f, 1f, a);
            grt.sizeDelta = new Vector2(30f + 26f * _glowPulse, 66f + 22f * _glowPulse);
        }

        if (_hpText != null && _deathTime < 0f && Time.unscaledTime - _introStart >= IntroSeconds)
            _hpText.text = "SYS " + Mathf.CeilToInt(_boss.CurrentHealth) + " / " + Mathf.CeilToInt(_boss.MaxHealth);
    }

    private static void SetFill(Image img, float v)
    {
        var rt = (RectTransform)img.transform;
        rt.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
    }

    // 登场/击杀演出：走线场流动 → 方格点亮成条(名字乱码解码) → 走线淡去、端头弹出
    private void UpdatePresentation()
    {
        float now = Time.unscaledTime;
        float el = now - _introStart;
        bool intro = el < IntroSeconds;

        _gridRoot.gameObject.SetActive(intro);
        _cellsRoot.gameObject.SetActive(el < IntroSeconds - 0.05f);
        if (intro)
        {
            _gridCg.alpha = 1f - Mathf.Clamp01((el - 0.8f) / 0.35f);

            bool respawn = el < IntroSeconds - 0.4f;     // 收尾阶段不再补新线，让它们自然流完淡出
            for (int k = 0; k < _tr.Length; k++)
            {
                float age = el - _trDelay[k];
                if (age > _trLife[k] && respawn) { RespawnTrace(k, el); age = 0f; }
                bool on = age >= 0f && age <= _trLife[k];
                _trImg[k].enabled = on;
                if (!on) continue;
                float u = age / _trLife[k];
                _tr[k].anchoredPosition = new Vector2(_trBaseX[k], _trBaseY[k]) + _trDir[k] * (_trVel[k] * age);
                float env = Mathf.Sin(u * Mathf.PI);                                   // 淡入淡出包络：线段从无到有再消失
                float fl = 0.8f + 0.2f * Mathf.Sin(now * 45f + k * 3.1f);
                _trImg[k].color = new Color(1f, 1f, 1f, _trAlpha[k] * Mathf.Clamp01(env * 1.6f) * fl);
            }
            for (int d = 0; d < _dots.Length; d++)
                _dotImg[d].enabled = el > _dotDelay[d] && ((int)(now * 18f) + d) % 4 != 0;
            for (int q = 0; q < _blocks.Length; q++)
            {
                _blockImg[q].enabled = el > _blockDelay[q];
                _blockImg[q].color = new Color(1f, 1f, 1f, _blockAlpha[q] * (0.7f + 0.3f * Mathf.Sin(now * 23f + q)));
            }

            // 方格点亮：从左到右一波，带随机闪烁；只亮到当前血量的比例
            float max = _boss != null ? Mathf.Max(1f, _boss.MaxHealth) : 1f;
            float curFrac = _boss != null ? Mathf.Clamp01(_boss.CurrentHealth / max) : 1f;
            _cellsCg.alpha = 1f - Mathf.Clamp01((el - 0.9f) / 0.2f);
            for (int i = 0; i < _cells.Length; i++)
            {
                float ti = _cellDelay[i];
                bool lit = el > ti && (float)i / _cellN < curFrac;
                if (lit && el < ti + 0.2f) lit = ((int)(now * 25f) + i * 7) % 3 != 0;    // 刚点亮时闪烁
                _cells[i].enabled = lit;
            }
        }
        _trackBgCg.alpha = Mathf.Clamp01((el - 0.35f) / 0.5f);

        // 实血：方格交接给实血条(0.9~1.1 交叉淡入)
        float fillAlpha = Mathf.Clamp01((el - 0.9f) / 0.2f);
        _fill.color = new Color(1f, 1f, 1f, fillAlpha);
        _chip.color = ChipColor;

        // 端头最后弹出
        float cap = Mathf.Clamp01((el - 0.88f) / 0.12f);
        _capL.localScale = new Vector3(1f, cap, 1f);
        _capR.localScale = new Vector3(1f, cap, 1f);

        // 名字：先乱码，再从左到右逐字解码定格
        string real = _bossName ?? "";
        float nt = Mathf.Clamp01((el - 0.25f) / 0.6f);
        if (el < 0.2f) _nameText.text = "";
        else if (nt >= 1f) { if (_nameText.text != real) _nameText.text = real; }
        else
        {
            int resolved = Mathf.Min(real.Length, Mathf.FloorToInt(nt * real.Length));
            var sb = new System.Text.StringBuilder(real.Substring(0, resolved));
            for (int k = resolved; k < real.Length; k++)
                sb.Append(ScrambleChars[(int)(Mathf.Abs(Mathf.Sin(now * 31f + k * 12.9898f)) * 1000f) % ScrambleChars.Length]);
            _nameText.text = sb.ToString();
        }
        _nameText.maxVisibleCharacters = 99;

        // 标签：锁定瞬间闪两下，随后稳定；击杀后换成 TARGET NEUTRALIZED
        if (_deathTime >= 0f)
        {
            _labelText.text = "TARGET NEUTRALIZED";
            _hpText.text = "SYS ----";
            _labelText.color = NameColor;
        }
        else
        {
            _labelText.text = "TARGET LOCKED";
            float t = Mathf.Clamp01(el / IntroSeconds);
            bool blinkOff = t < 0.6f && ((int)(t * 10f) % 2 == 1);
            _labelText.color = blinkOff ? new Color(NameColor.r, NameColor.g, NameColor.b, 0.15f) : NameColor;
            if (t < 1f) _hpText.text = "";
        }
    }

    private void HideBossOverheadUi()
    {
        for (int i = _bossOverheadViews.Count - 1; i >= 0; i--)
        {
            var v = _bossOverheadViews[i];
            if (v == null) { _bossOverheadViews.RemoveAt(i); continue; }
            if (v.runtimeUiRoot == null) continue;
            var cv = v.runtimeUiRoot.GetComponent<Canvas>();
            if (cv != null && cv.enabled) cv.enabled = false;
            var cg = v.runtimeUiRoot.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha > 0f) cg.alpha = 0f;
        }
    }

    // ── 选择要显示的 BOSS ───────────────────────────────────────

    private void CollectBossOverheadViews()
    {
        var binders = FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < binders.Length; i++)
        {
            var ud = binders[i] != null ? binders[i].UnitDefinitionAsset : null;
            if (ud == null || ud.characterIdentity != CharacterIdentity.Boss) continue;
            var view = binders[i].GetComponentInChildren<UnitOverheadUIView>(true) ?? binders[i].GetComponentInParent<UnitOverheadUIView>();
            if (view != null && !_bossOverheadViews.Contains(view)) _bossOverheadViews.Add(view);
        }
    }

    private void PollBoss()
    {
        CollectBossOverheadViews();
        if (_boss != null)
        {
            bool dead = _boss.IsDead || (_bossDeath != null && _bossDeath.IsDeadLike);
            bool sees = _bossPerception != null && _bossPerception.CanSeePlayer;
            if (sees) _lastSeenOrHurtTime = Time.unscaledTime;
            bool full = _boss.CurrentHealth >= _boss.MaxHealth - 0.01f;
            bool disengaged = full && Time.unscaledTime - _lastSeenOrHurtTime > DisengageSeconds;
            if (dead)
            {
                // 击杀：先停留显示 TARGET NEUTRALIZED，再淡出
                if (_deathTime < 0f) _deathTime = Time.unscaledTime;
                if (Time.unscaledTime - _deathTime > NeutralizedShowSeconds) { _boss = null; _deathTime = -1f; }
                else return;
            }
            else if (disengaged) _boss = null;
            else return;
        }

        var binders = FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < binders.Length; i++)
        {
            var b = binders[i];
            var ud = b != null ? b.UnitDefinitionAsset : null;
            if (ud == null || ud.characterIdentity != CharacterIdentity.Boss) continue;

            var hp = b.GetComponent<UnitHealthController>() ?? b.GetComponentInParent<UnitHealthController>();
            if (hp == null || hp.IsDead) continue;
            var death = hp.GetComponent<UnitDeathController>() ?? hp.GetComponentInParent<UnitDeathController>();
            if (death != null && death.IsDeadLike) continue;
            var per = hp.GetComponentInChildren<UnitPerceptionRuntime>(true) ?? hp.GetComponentInParent<UnitPerceptionRuntime>();

            // 只认「看见玩家」或「真的挨过打」(OnDamaged)。不能看血量<满血：出生时血量还没按 UD 回满，会被误判成已受伤
            if (_hooked.Add(hp)) { var captured = hp; hp.OnDamaged += (h, amt) => _hurt.Add(captured); }
            bool engaged = (per != null && per.CanSeePlayer) || _hurt.Contains(hp);
            if (!engaged) continue;

            _boss = hp;
            RerollCircuit();
            _introStart = Time.unscaledTime;
            _deathTime = -1f;
            _bossDeath = death;
            _bossPerception = per;
            string n = ud.GetLocalizedDisplayName();
            _bossName = string.IsNullOrWhiteSpace(n) ? ud.name : n;
            _lastSeenOrHurtTime = Time.unscaledTime;
            _chipFill = Mathf.Clamp01(hp.CurrentHealth / Mathf.Max(1f, hp.MaxHealth));
            _lastHp = hp.CurrentHealth;
            return;
        }
    }

    // ── UI(纯代码搭)───────────────────────────────────────────

    private void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 505;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 1f;       // 显隐全交给 _fadeGroup；这个组只给揭幕系统用
        _group.blocksRaycasts = false;
        _group.interactable = false;
        SceneLoader.RegisterGameCanvasForReveal(_group);

        var root = NewRect("BossBarRoot", transform);
        _moduleRoot = root;
        _fadeGroup = root.gameObject.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;
        _fadeGroup.blocksRaycasts = false;
        _fadeGroup.interactable = false;
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -110f);
        root.sizeDelta = new Vector2(1500f, 90f);

        var nameRt = NewRect("Name", root);
        nameRt.anchorMin = new Vector2(0f, 1f);
        nameRt.anchorMax = new Vector2(1f, 1f);
        nameRt.pivot = new Vector2(0f, 1f);
        nameRt.anchoredPosition = Vector2.zero;
        nameRt.sizeDelta = new Vector2(0f, 48f);
        _nameText = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
        _nameText.alignment = TextAlignmentOptions.Bottom;
        _nameText.fontSize = 34f;
        _nameText.color = NameColor;
        _nameText.raycastTarget = false;

        var labelRt = NewRect("Label", root);
        labelRt.anchorMin = new Vector2(0f, 1f);
        labelRt.anchorMax = new Vector2(1f, 1f);
        labelRt.pivot = new Vector2(0f, 1f);
        labelRt.anchoredPosition = Vector2.zero;
        labelRt.sizeDelta = new Vector2(0f, 48f);
        _labelText = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        _labelText.alignment = TextAlignmentOptions.BottomLeft;
        _labelText.fontSize = 24f;
        _labelText.color = NameColor;
        _labelText.raycastTarget = false;

        var hpRt = NewRect("HpText", root);
        hpRt.anchorMin = new Vector2(0f, 1f);
        hpRt.anchorMax = new Vector2(1f, 1f);
        hpRt.pivot = new Vector2(1f, 1f);
        hpRt.anchoredPosition = Vector2.zero;
        hpRt.sizeDelta = new Vector2(0f, 48f);
        _hpText = hpRt.gameObject.AddComponent<TextMeshProUGUI>();
        _hpText.alignment = TextAlignmentOptions.BottomRight;
        _hpText.fontSize = 28f;
        _hpText.color = Color.white;
        _hpText.raycastTarget = false;

        // 血条：无描边。损失血量的深色底是 CleanBG_ 背板(不进色差 RT，纯黑才看得见)，实血白色
        const float BarH = 14f, Border = 0f;
        var bar = NewRect("Bar", root);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = new Vector2(0f, BarH);
        _barRt = bar;

        _trackBg = NewRect("CleanBG_BarTrack", root);
        _trackBg.anchorMin = new Vector2(0f, 0f);
        _trackBg.anchorMax = new Vector2(1f, 0f);
        _trackBg.pivot = new Vector2(0.5f, 0f);
        _trackBg.anchoredPosition = Vector2.zero;
        _trackBg.sizeDelta = new Vector2(0f, BarH);
        var bgImg = _trackBg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.62f);
        bgImg.raycastTarget = false;
        _trackBgCg = _trackBg.gameObject.AddComponent<CanvasGroup>();
        _trackBgCg.alpha = 0f;

        var frame = bar.gameObject.AddComponent<Image>();
        frame.color = new Color(1f, 1f, 1f, 0f);
        frame.raycastTarget = false;

        var track = NewRect("Track", bar);
        Stretch(track, Border);
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = new Color(0f, 0f, 0f, 0f);   // 底色改由 CleanBG_BarTrack 画
        trackImg.raycastTarget = false;

        _chip = NewBarImage("Chip", _trackBg, ChipColor);   // 在 CleanBG_ 背板下：深红不会被色差模块的亮度透明度吃掉
        _fill = NewBarImage("Fill", track, Color.white);

        // 实血右端的纵向发光：窄而高的光斑 + 淡淡的泛光，位置跟着当前血量。
        // 名字以 CleanFG_ 开头 = 色差模块不把它抓进离屏 RT(进了 RT 按亮度算透明度，半透明白光会变成带黑边的灰斑)，
        // 作为模块根的兄弟节点、盖在色差输出之上，用加算材质直接画。
        var glowRt = NewRect("CleanFG_EdgeGlow", root);
        glowRt.anchorMin = glowRt.anchorMax = new Vector2(0f, 0f);
        glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.sizeDelta = new Vector2(30f, 66f);
        _edgeGlow = glowRt.gameObject.AddComponent<RawImage>();
        _edgeGlow.texture = GetGlowTexture();
        _edgeGlow.raycastTarget = false;
        var addSh = Resources.Load<Shader>("Shaders/UIAdditive");
        if (addSh != null) _edgeGlow.material = new Material(addSh);
        _edgeGlow.color = new Color(1f, 1f, 1f, 0f);

        // 左右两端的白色小矩形(比血条高一截，黑色细描边)，和血条之间留一道缝
        const float CapW = 7f, CapH = 30f, CapGap = 8f, CapOutline = 2f;
        bar.offsetMin = new Vector2(CapW + CapOutline * 2f + CapGap, bar.offsetMin.y);
        bar.offsetMax = new Vector2(-(CapW + CapOutline * 2f + CapGap), bar.offsetMax.y);
        _trackBg.offsetMin = new Vector2(bar.offsetMin.x, _trackBg.offsetMin.y);
        _trackBg.offsetMax = new Vector2(bar.offsetMax.x, _trackBg.offsetMax.y);
        _capL = NewCap(root, false, CapW, CapH, CapOutline, BarH * 0.5f);
        _capR = NewCap(root, true, CapW, CapH, CapOutline, BarH * 0.5f);
        _barInsetL = bar.offsetMin.x;
        _barW = root.sizeDelta.x - bar.offsetMin.x + bar.offsetMax.x;
        BuildGrid(root, bar.offsetMin.x, -bar.offsetMax.x, BarH);

        // 色差模块的离屏相机只渲染这一层，整棵树必须在 UI 层
        SetLayerRecursive(gameObject, LayerMask.NameToLayer("UI"));
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }

    // 和玩家 HP / LP 一样的色差：借血条区那个模块渲染器的源材质和混合模式，给 BOSS 血条也挂一个同样的(整块先渲染进 RT，再对 RT 做真 RGB 通道偏移)。
    // 以后调 HP/LP 的色差参数，BOSS 血条自动跟着变。HUD 还没建好就稍后重试；一直找不到就没有色差，血条照常显示。
    private void TryAttachChromatic()
    {
        if (_chromaAttached || Time.unscaledTime < _nextAttachTry) return;
        _nextAttachTry = Time.unscaledTime + 1f;
        var driver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
        GameObject hud = driver != null ? driver.HudInstance : null;
        if (hud == null) return;

        SkyPrisonHUDModulePostProcessRenderer_V87 template = null;
        foreach (var r in hud.GetComponentsInChildren<SkyPrisonHUDModulePostProcessRenderer_V87>(true))
        {
            if (r == null || r.SourceMaterial == null) continue;
            if (template == null || r.name == "PlayerStatusArea") template = r;
            if (r.name == "PlayerStatusArea") break;
        }
        if (template == null) return;
        SkyPrisonHUDModulePostProcessRenderer_V87.Attach(_moduleRoot.gameObject, template.SourceMaterial, template.BlendMode);
        _chromaAttached = true;
    }

    private static RectTransform NewCap(RectTransform root, bool right, float w, float h, float outline, float centerY)
    {
        float x = right ? 1f : 0f;
        var rt = NewRect(right ? "CapR" : "CapL", root);
        rt.anchorMin = rt.anchorMax = new Vector2(x, 0f);
        rt.pivot = new Vector2(x, 0.5f);
        rt.anchoredPosition = new Vector2(0f, centerY);
        rt.sizeDelta = new Vector2(w + outline * 2f, h + outline * 2f);
        var edge = rt.gameObject.AddComponent<Image>();
        edge.color = new Color(0f, 0f, 0f, 0.95f);
        edge.raycastTarget = false;
        var inner = NewRect("Fill", rt);
        Stretch(inner, outline);
        var img = inner.gameObject.AddComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;
        return rt;
    }

    // 登场用的走线场 + 点亮用的单行方格；另外在损失血量的深色底上留一层很淡的方格分隔线(单元基本是正方形，边长≈血条高度)
    private void BuildGrid(RectTransform root, float insetL, float insetR, float barH)
    {
        float W = root.sizeDelta.x - insetL - insetR;
        _cellN = Mathf.Max(1, Mathf.FloorToInt(W / CellPx));
        float cw = W / _cellN;                                   // 余数摊进单元宽度，刚好铺满
        _fieldW = W;

        _gridRoot = NewRect("IntroGrid", root);
        _gridRoot.anchorMin = new Vector2(0f, 0f); _gridRoot.anchorMax = new Vector2(1f, 0f);
        _gridRoot.pivot = new Vector2(0.5f, 0f);
        _gridRoot.offsetMin = new Vector2(insetL, FieldBottom);  // 走线场：以血条为中线向上下铺开
        _gridRoot.offsetMax = new Vector2(-insetR, FieldBottom + FieldH);
        _gridCg = _gridRoot.gameObject.AddComponent<CanvasGroup>();

        _tr = new RectTransform[TraceCount]; _trImg = new Image[TraceCount];
        _trDelay = new float[TraceCount]; _trLife = new float[TraceCount]; _trAlpha = new float[TraceCount];
        _trVel = new float[TraceCount]; _trBaseX = new float[TraceCount]; _trBaseY = new float[TraceCount]; _trDir = new Vector2[TraceCount];
        for (int k = 0; k < TraceCount; k++)
        {
            var rt = NewRect("T" + k, _gridRoot);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            var im = rt.gameObject.AddComponent<Image>(); im.raycastTarget = false; im.enabled = false;
            _tr[k] = rt; _trImg[k] = im;
        }
        _dots = new RectTransform[DotCount]; _dotImg = new Image[DotCount]; _dotDelay = new float[DotCount];
        for (int d = 0; d < DotCount; d++)
        {
            var rt = NewRect("D" + d, _gridRoot);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            var im = rt.gameObject.AddComponent<Image>(); im.color = Color.white; im.raycastTarget = false; im.enabled = false;
            _dots[d] = rt; _dotImg[d] = im;
        }
        _blocks = new RectTransform[BlockCount]; _blockImg = new Image[BlockCount]; _blockDelay = new float[BlockCount]; _blockAlpha = new float[BlockCount];
        for (int q = 0; q < BlockCount; q++)
        {
            var rt = NewRect("B" + q, _gridRoot);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); rt.pivot = new Vector2(0f, 0f);
            var im = rt.gameObject.AddComponent<Image>(); im.raycastTarget = false; im.enabled = false;
            _blocks[q] = rt; _blockImg[q] = im;
        }
        RerollCircuit();

        // 点亮用的方格(一行)
        _cellsRoot = NewRect("IntroCells", root);
        _cellsRoot.anchorMin = new Vector2(0f, 0f); _cellsRoot.anchorMax = new Vector2(1f, 0f);
        _cellsRoot.pivot = new Vector2(0.5f, 0f);
        _cellsRoot.offsetMin = new Vector2(insetL, 0f);
        _cellsRoot.offsetMax = new Vector2(-insetR, barH);
        _cellsCg = _cellsRoot.gameObject.AddComponent<CanvasGroup>();
        _cells = new Image[_cellN]; _cellDelay = new float[_cellN];
        for (int i = 0; i < _cellN; i++)
        {
            var c = NewRect("C" + i, _cellsRoot);
            c.anchorMin = c.anchorMax = new Vector2(0f, 0f); c.pivot = new Vector2(0f, 0f);
            c.anchoredPosition = new Vector2(i * cw, 0f); c.sizeDelta = new Vector2(cw - 1f, barH);
            var im = c.gameObject.AddComponent<Image>(); im.color = Color.white; im.raycastTarget = false; im.enabled = false;
            _cells[i] = im; _cellDelay[i] = 0.30f + 0.55f * i / _cellN + Random.value * 0.12f;
        }

        // 损失血量深色底上的淡方格分隔线(CleanBG_ 背板的子物体，不进色差 RT)
        for (int i = 1; i < _cellN; i++)
        {
            var d = NewRect("Div" + i, _trackBg);
            d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f); d.pivot = new Vector2(0.5f, 0.5f);
            d.anchoredPosition = new Vector2(i * cw, 0f); d.sizeDelta = new Vector2(1f, barH);
            var im = d.gameObject.AddComponent<Image>(); im.color = new Color(1f, 1f, 1f, 0.09f); im.raycastTarget = false;
        }
    }

    // 随机生成一套走线：每条线段沿自己的方向快速滑过，寿命到了在别处重生——整体是密集交汇、持续流动的数据流
    private void RerollCircuit()
    {
        if (_tr == null) return;
        float W = _fieldW;
        for (int k = 0; k < TraceCount; k++) RespawnTrace(k, Random.Range(0f, 0.7f) + Random.Range(-0.4f, 0f));
        for (int d = 0; d < DotCount; d++)
        {
            float sz = Random.Range(2f, 4f);
            _dots[d].pivot = new Vector2(0.5f, 0.5f);
            _dots[d].anchoredPosition = new Vector2(Random.Range(0f, W), Mathf.Round(Random.Range(0f, FieldH) / 2f) * 2f);
            _dots[d].sizeDelta = new Vector2(sz, sz);
            _dotDelay[d] = Random.Range(0.1f, 0.7f);
        }
        for (int q = 0; q < BlockCount; q++)
        {
            _blocks[q].anchoredPosition = new Vector2(Random.Range(0f, W - 160f), Random.Range(0f, FieldH - 30f));
            _blocks[q].sizeDelta = new Vector2(Random.Range(40f, 160f), Random.Range(10f, 36f));
            _blockDelay[q] = Random.Range(0.1f, 0.6f);
            _blockAlpha[q] = Random.Range(0.06f, 0.15f);
        }
    }

    // 给第 k 条线段重新随机：横线(多数，快)、竖线(短，交叉用)、45° 斜线(少)；线宽 1~3px
    private void RespawnTrace(int k, float startTime)
    {
        float W = _fieldW;
        float[] thick = { 1f, 1f, 1.5f, 2f, 3f };
        float r = Random.value;
        float angle, len, vel, life;
        if (r < 0.62f)
        {
            angle = Random.value < 0.5f ? 0f : 180f;
            len = Random.Range(60f, 420f);
            vel = Random.Range(500f, 1800f);
            life = Random.Range(0.25f, 0.6f);
        }
        else if (r < 0.92f)
        {
            angle = Random.value < 0.5f ? 90f : -90f;
            len = Random.Range(12f, 70f);
            vel = Random.Range(120f, 420f);
            life = Random.Range(0.2f, 0.5f);
        }
        else
        {
            angle = Random.value < 0.5f ? 45f : -45f;
            len = Random.Range(40f, 120f);
            vel = Random.Range(200f, 600f);
            life = Random.Range(0.25f, 0.5f);
        }
        var rt = _tr[k];
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(len, thick[Random.Range(0, thick.Length)]);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        rt.localScale = Vector3.one;
        float rad = angle * Mathf.Deg2Rad;
        _trDir[k] = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        _trBaseX[k] = Random.Range(0f, W);
        _trBaseY[k] = Mathf.Round(Random.Range(0f, FieldH) / 2f) * 2f;
        _trVel[k] = vel; _trLife[k] = life; _trDelay[k] = startTime;
        _trAlpha[k] = Random.Range(0.3f, 0.9f);
    }

    // 程序生成：中间一道窄而高的亮光 + 外面一圈很淡的椭圆泛光(参考图)
    private static Texture2D GetGlowTexture()
    {
        if (_glowTex != null) return _glowTex;
        const int W = 32, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float ny = (y / (float)(H - 1) - 0.5f) * 2f;
            for (int x = 0; x < W; x++)
            {
                float nx = (x / (float)(W - 1) - 0.5f) * 2f;
                float core = Mathf.Exp(-(nx * nx) / 0.012f) * Mathf.Exp(-(ny * ny) / 0.55f);         // 窄而高的核心
                float halo = 0.32f * Mathf.Exp(-(nx * nx + ny * ny * 0.3f) * 4.5f);                  // 椭圆泛光
                float a = Mathf.Clamp01(core + halo);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        _glowTex = tex;
        return tex;
    }

    private static Image NewBarImage(string name, RectTransform parent, Color c)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private void TryBindFont()
    {
        if (_fontBound) return;
        // 名字要支持中日文(动态字体)，数字用 HUD 的 MRT 字体；直接走 Resources，不依赖场景里有没有样式设置物体
        var nameFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/ZhouFangRiMingTi-2 SDF");
        var numFont = Resources.Load<TMP_FontAsset>("UI/HUD/MRT-すくてらむ SDF");
        if (nameFont == null)
        {
            var style = FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
            if (style != null) nameFont = style.defaultTextFont;
        }
        if (nameFont == null) return;
        _nameText.font = nameFont;
        // 名字/标签：普通混合 + 淡红。不用加算：整块 UI 走色差模块的离屏 RT，加算叠在黑底上会被冲淡成发白
        var tacFont = numFont != null ? numFont : nameFont;
        _labelText.font = tacFont;
        _hpText.font = tacFont;
        _fontBound = true;
    }
}
