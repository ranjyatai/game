using UnityEngine;

/// <summary>
/// 守卫者(ガーディアン)的盾：
///   · 盾有自己的耐久(shieldDurability)，不是单纯的减伤；盾还在时，物理伤害(斩击/打击/冲击)被大幅减免(physicalReduction)，
///     属性伤害(灼热/电磁/腐蚀/冻结)不减免(它的属性抗性本来就低)，而且更伤盾(elementShieldMultiplier 倍)；
///   · 任何一次受击都会按"未减免前"的数值消耗盾耐久；耐久打光 = 盾破，之后受到的是正常伤害；
///   · 盾不修复(用户 10-09 倾向：打爆就是奖励时刻)；
///   · 盾只挡正面：盾在面朝玩家的一侧，守卫者转身要 turnDelaySeconds 秒，期间盾还在旧的一侧；
///     从盾的背面(攻击者在盾的另一侧)打来的伤害无视盾、不耗耐久——绕到背后就是输出窗口；
///   · 占位视觉是一块半透明竖板，越破越淡，受击闪白，盾破时碎成几片飞出去。
/// 实现方式：通过 IIncomingHitModifier 被 ResolveSkillHit 调用，不改 UnitHealthController。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonGuardianShield : MonoBehaviour, IIncomingHitModifier
{
    [Header("本体")]
    [SerializeField] private float guardianMaxHealth = 120f;

    [Header("盾")]
    [SerializeField] private float shieldDurability = 150f;
    [Tooltip("盾还在时，物理伤害被减掉的比例（0.7 = 只吃 30%）。")]
    [SerializeField, Range(0f, 0.95f)] private float physicalReduction = 0.9f;
    [Tooltip("盾还在时，单次物理伤害的硬上限：再重的一击也只能是个位数。")]
    [SerializeField] private float maxPhysicalDamageWhileShielded = 9f;
    [Tooltip("属性伤害消耗盾耐久的倍率：属性是盾的弱点，打盾更快。")]
    [SerializeField] private float elementShieldMultiplier = 1.5f;

    [Tooltip("玩家换到另一侧后，守卫者多久才把盾转过去。这段时间就是绕背窗口。")]
    [SerializeField] private float turnDelaySeconds = 1f;

    [Tooltip("玩家(走/潜行/闪避)不能穿过盾：盾前后(Z 方向)多宽的范围内会挡住闪避。")]
    [SerializeField] private float dodgeBlockHalfDepth = 1.2f;

    [Header("占位视觉")]
    [SerializeField] private float shieldFrontOffset = 1.1f;
    [SerializeField] private Vector2 shieldSize = new Vector2(0.7f, 3.0f);
    [SerializeField] private float shieldHeight = 1.6f;
    [SerializeField] private Color shieldColor = new Color(0.55f, 0.85f, 1f, 0.8f);

    private float _shield;
    private UnitHealthController _health;
    private UnitDeathController _death;
    private Transform _shieldVisual;
    private SpriteRenderer _shieldRenderer;
    private float _facing = 1f;        // 盾在哪一侧：+1 右 / -1 左
    private bool _facingInit;
    private float _turnTimer;          // 玩家已经在"盾的另一侧"待了多久
    private float _hitFlash;           // 受击闪白剩余时间
    private const float HitFlashSeconds = 0.08f;

    public bool ShieldUp => _shield > 0f;
    public float ShieldRatio01 => shieldDurability > 0f ? Mathf.Clamp01(_shield / shieldDurability) : 0f;

    /// <summary>盾破时触发（给音效/特效/掉落反馈挂接）。</summary>
    public event System.Action<SkyPrisonGuardianShield> OnShieldBroken;

    private static readonly System.Collections.Generic.List<SkyPrisonGuardianShield> Active = new System.Collections.Generic.List<SkyPrisonGuardianShield>();
    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    /// <summary>
    /// 玩家位移的盾阻挡：从盾的正面一侧出发时，不管是走、潜行还是闪避，都不能穿过盾到守卫者背后(会被盾挡下)；
    /// 从背后/盾已破/守卫者已死则不受影响。由 TerrainGroundMotor 的最终位移调用。
    /// </summary>
    public static void ClampDodgeDelta(Vector3 from, ref Vector3 delta)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            var g = Active[i];
            if (g == null || !g.ShieldUp) continue;
            if (g._death != null && g._death.IsDeadLike) continue;
            Vector3 gp = g.transform.position;
            if (Mathf.Abs(from.z - gp.z) > g.dodgeBlockHalfDepth) continue;
            float f = g._facing;
            if (Mathf.Sign(from.x - gp.x) != f) continue;                 // 在盾背后：不挡
            float limit = gp.x + f * (g.shieldFrontOffset + g.shieldSize.x * 0.5f);
            float newX = from.x + delta.x;
            if (f > 0f) { float lo = Mathf.Min(from.x, limit); if (newX < lo) delta.x = lo - from.x; }
            else        { float hi = Mathf.Max(from.x, limit); if (newX > hi) delta.x = hi - from.x; }
        }
    }

    private void Awake()
    {
        _shield = shieldDurability;
        _health = GetComponentInChildren<UnitHealthController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
    }

    private bool _healthInit;

    // 摆在场景里的单位：血量/死亡控制器是运行时才创建的，Start 时多半还没有——补取，取到后只初始化一次血量。
    private void EnsureRefs()
    {
        if (_health == null) _health = GetComponentInChildren<UnitHealthController>(true);
        if (_death == null) _death = GetComponentInChildren<UnitDeathController>(true);
        if (!_healthInit && _health != null)
        {
            _healthInit = true;
            _health.SetMaxHealth(guardianMaxHealth, false);
            _health.SetCurrentHealth(guardianMaxHealth);
        }
    }

    private void Start()
    {
        EnsureRefs();
        BuildVisual();
    }

    private void BuildVisual()
    {
        var go = new GameObject("GuardianShieldVisual");
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;
        go.transform.SetParent(transform, false);
        _shieldVisual = go.transform;
        _shieldRenderer = go.AddComponent<SpriteRenderer>();
        _shieldRenderer.sprite = MakeWhiteSprite();
        _shieldRenderer.color = shieldColor;
        _shieldRenderer.sortingOrder = 50;
        _shieldVisual.localScale = new Vector3(shieldSize.x, shieldSize.y, 1f);
    }

    private static Sprite _whiteSprite;
    private static Sprite MakeWhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white); tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return _whiteSprite;
    }

    private void Update()
    {
        EnsureRefs();
        if (_shieldVisual == null) return;
        bool dead = _death != null && _death.IsDeadLike;
        bool show = ShieldUp && !dead;
        if (_shieldVisual.gameObject.activeSelf != show) _shieldVisual.gameObject.SetActive(show);
        if (!show) return;

        UpdateFacing();
        _shieldVisual.position = transform.position + new Vector3(_facing * shieldFrontOffset, shieldHeight, 0f);

        var c = shieldColor;
        c.a = shieldColor.a * Mathf.Lerp(0.3f, 1f, ShieldRatio01);   // 越破越淡
        if (_hitFlash > 0f)
        {
            _hitFlash -= Time.deltaTime;
            c = Color.Lerp(c, new Color(1f, 1f, 1f, 1f), 0.8f);
        }
        _shieldRenderer.color = c;
    }

    private void UpdateFacing()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return;
        float dx = player.transform.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.2f) return;                 // 几乎重叠：不触发转身
        float desired = Mathf.Sign(dx);
        if (!_facingInit) { _facingInit = true; _facing = desired; return; }   // 出生第一次直接朝向玩家
        if (desired == _facing) { _turnTimer = 0f; return; }
        _turnTimer += Time.deltaTime;
        if (_turnTimer >= turnDelaySeconds) { _facing = desired; _turnTimer = 0f; }
    }

    private void SpawnBreakFragments()
    {
        if (_shieldVisual == null) return;
        var sprite = MakeWhiteSprite();
        int layer = LayerMask.NameToLayer("World3D");
        for (int i = 0; i < 7; i++)
        {
            var go = new GameObject("ShieldFragment");
            if (layer >= 0) go.layer = layer;
            go.transform.position = _shieldVisual.position + new Vector3(0f, Random.Range(-1.2f, 1.2f), 0f);
            go.transform.localScale = new Vector3(Random.Range(0.15f, 0.35f), Random.Range(0.25f, 0.6f), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.color = shieldColor; sr.sortingOrder = 50;
            var frag = go.AddComponent<ShieldFragment>();
            frag.velocity = new Vector3(_facing * Random.Range(1.5f, 4.5f), Random.Range(1f, 4f), 0f);
            frag.spin = Random.Range(-360f, 360f);
        }
    }

    private sealed class ShieldFragment : MonoBehaviour
    {
        public Vector3 velocity; public float spin;
        private float _t; private SpriteRenderer _sr;
        private void Awake() { _sr = GetComponent<SpriteRenderer>(); }
        private void Update()
        {
            _t += Time.deltaTime;
            velocity.y -= 9f * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0f, 0f, spin * Time.deltaTime);
            var c = _sr.color; c.a = Mathf.Clamp01(1f - _t / 0.7f) * 0.8f; _sr.color = c;
            if (_t >= 0.7f) Destroy(gameObject);
        }
    }

    // ── IIncomingHitModifier ───────────────────────────────

    public void ModifyHit(bool isPhysical, string damageKey, float attackerX, ref float amount)
    {
        if (!ShieldUp || amount <= 0f) return;

        // 攻击者在盾的另一侧 = 从背后打：无视盾
        float attackSide = Mathf.Sign(attackerX - transform.position.x);
        if (attackSide != 0f && attackSide != _facing) return;

        _hitFlash = HitFlashSeconds;

        float raw = amount;
        if (isPhysical)
            amount = Mathf.Min(raw * (1f - physicalReduction), maxPhysicalDamageWhileShielded);

        _shield -= raw * (isPhysical ? 1f : elementShieldMultiplier);
        if (_shield <= 0f)
        {
            _shield = 0f;
            Debug.Log("[GuardianShield] " + name + " 盾破");
            SpawnBreakFragments();
            OnShieldBroken?.Invoke(this);
        }
    }
}
