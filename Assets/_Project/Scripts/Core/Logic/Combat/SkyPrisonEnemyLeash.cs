using System.Collections;
using UnityEngine;

/// <summary>敌人被拉回出生点后需要重置自己的状态（休眠、清试探/出招状态）时实现这个接口。</summary>
public interface ILeashListener
{
    void OnLeashReturnedHome();
}

/// <summary>
/// 敌人的"活动范围"（拴绳）：防止被玩家一路拖到地图另一头，也不留"走回家时最脆弱"这种白嫖窗口。
///   · 软范围 softRadius：离出生点超过它，追击意愿 Willingness01 从 1 线性降到 0（到硬范围）。
///     具体怎么影响由各怪自己的脚本读 Willingness01 决定（织面者：加速幅度变小、试探/冷却变长）；
///   · 硬范围 hardRadius：超过它就放弃追击——**统一走「无敌 + 传送回家」**，不走路回去：
///       立刻进入无敌（所有伤害归零）→ 停手 → 从脚到头淡出 → 传回出生点 → 在家里从脚到头淡入 → 血回满 → 重置成休眠。
///     走回去会让它背对玩家白挨打，被风筝一拉一打就能白嫖；卡在地形里回不去也一并解决了；
///   · 最近 hurtExtendSeconds 秒内被打过：硬范围放宽 hurtHardRadiusScale 倍，不让它被风筝着打还放弃；
///   · 传送写刚体位置并同步物理、清掉地形马达速度——只改 transform.position 会被马达缓存的刚体位置冲掉。
/// 摆在场景里的单位：组件引用在用到时补取。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonEnemyLeash : MonoBehaviour, IIncomingHitModifier
{
    [Header("活动范围（离出生点的水平距离，米）")]
    [SerializeField] private float softRadius = 22f;
    [SerializeField] private float hardRadius = 35f;

    [Header("被打时放宽")]
    [Tooltip("最近这么多秒内被打过，硬范围放宽（不让它被风筝着打还放弃）。")]
    [SerializeField] private float hurtExtendSeconds = 4f;
    [SerializeField] private float hurtHardRadiusScale = 1.5f;

    [Header("回归（无敌 + 淡出 → 传送 → 淡入）")]
    [Tooltip("从脚到头淡出的时间(秒)。0 = 不淡出直接传。")]
    [SerializeField] private float fadeOutSeconds = 1.1f;
    [Tooltip("到家后从脚到头淡入的时间(秒)。")]
    [SerializeField] private float fadeInSeconds = 0.8f;
    [Tooltip("淡出/淡入的高度范围(米)：从脚底往上这么高。比单位身高略大一点，头顶才会彻底消失。")]
    [SerializeField] private float fadeHeight = 3f;
    [Tooltip("溶解边缘混入多少噪声(0=整齐的水平线，越大边缘越碎、越有机)。")]
    [SerializeField, Range(0f, 1f)] private float edgeNoiseMix = 0.4f;
    [SerializeField] private float edgeNoiseScale = 0.9f;
    [Header("消失/出现处的尘雾（遮住硬切，可选）")]
    [Tooltip("开始淡出时在原位、到家淡入时在出生点各冒一团的尘雾预制体。留空 = 不出。")]
    [SerializeField] private GameObject[] puffPrefabs;
    [SerializeField] private float puffScale = 1.8f;
    [SerializeField] private float puffLifetime = 3f;
    [Tooltip("无敌期间(淡出、传送、淡入)，受到的伤害乘这个倍率。0 = 完全无敌。")]
    [SerializeField, Range(0f, 1f)] private float invulnerableDamageScale = 0f;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitHealthController _health;
    private UnitDeathController _death;
    private Behaviour _aiPackage;
    private bool _refsReady;

    private Vector3 _home;
    private bool _homeSet;
    private bool _returning;
    private bool _invulnerable;
    private float _lastHurt = -999f;
    private bool _aiPausedByMe;

    private static readonly int DissolveAmountId = Shader.PropertyToID("_SkyPrison_DissolveAmount");
    private static readonly int DissolveBottomUpId = Shader.PropertyToID("_SkyPrison_DissolveBottomUp");
    private static readonly int DissolveBaseYId = Shader.PropertyToID("_SkyPrison_DissolveBaseY");
    private static readonly int DissolveHeightId = Shader.PropertyToID("_SkyPrison_DissolveHeight");
    private static readonly int DissolveEdgeWidthId = Shader.PropertyToID("_SkyPrison_DissolveEdgeWidth");
    private static readonly int DissolveEdgeColorId = Shader.PropertyToID("_SkyPrison_DissolveEdgeColor");
    private static readonly int DissolveNoiseMixId = Shader.PropertyToID("_SkyPrison_DissolveBottomUpNoise");
    private static readonly int DissolveNoiseTexId = Shader.PropertyToID("_SkyPrison_DissolveNoiseTex");
    private static readonly int DissolveNoiseScaleId = Shader.PropertyToID("_SkyPrison_DissolveNoiseScale");

    private static Texture2D s_noise;
    // 溶解边缘用的柔和噪声（运行时生成一次，不依赖项目里有没有现成的噪声贴图）
    private static Texture2D GetNoise()
    {
        if (s_noise != null) return s_noise;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.18f + 31f, y * 0.18f + 7f) * 0.4f;
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                px[y * n + x] = new Color32(b, b, b, 255);
            }
        tex.SetPixels32(px); tex.Apply();
        return s_noise = tex;
    }

    private static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }      // 缓入缓出

    /// <summary>0~1：1 = 在软范围内意愿完整；到硬范围线性降到 0。</summary>
    public float Willingness01 { get; private set; } = 1f;
    /// <summary>正在回归流程中（无敌、淡出/传送/淡入）：各怪的脚本据此让位、不出招。</summary>
    public bool IsReturning => _returning;
    public Vector3 Home => _home;

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;
        _action = GetComponentInChildren<UnitActionController>(true);
        _health = GetComponentInChildren<UnitHealthController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _aiPackage = GetComponentInChildren<AIBehaviorPackageRuntimeController>(true);
        if (_health != null) _health.OnDamaged += OnDamaged;
        if (!_homeSet) { _home = transform.position; _homeSet = true; }
        _refsReady = true;
    }

    private void Awake() { if (!_homeSet) { _home = transform.position; _homeSet = true; } EnsureRefs(); }
    private void Start() { EnsureRefs(); }
    private void OnDestroy() { if (_health != null) _health.OnDamaged -= OnDamaged; }
    private void OnDamaged(UnitHealthController h, float amount) { _lastHurt = Time.time; }

    /// <summary>外部（比如关卡脚本）重设出生点。</summary>
    public void SetHome(Vector3 home) { _home = home; _homeSet = true; }

    // IIncomingHitModifier：回归流程中无敌
    public void ModifyHit(bool isPhysical, string damageKey, float attackerX, ref float amount)
    {
        if (_invulnerable) amount *= invulnerableDamageScale;
    }

    private float FlatDistToHome()
    {
        Vector3 p = transform.position;
        float dx = p.x - _home.x, dz = p.z - _home.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void Update()
    {
        EnsureRefs();
        if (!_refsReady) return;
        if (_death != null && _death.IsDeadLike) return;
        if (_returning) return;                                  // 回归流程由协程接管

        float dist = FlatDistToHome();
        bool recentlyHurt = Time.time - _lastHurt < hurtExtendSeconds;
        float hard = recentlyHurt ? hardRadius * hurtHardRadiusScale : hardRadius;
        float soft = Mathf.Min(softRadius, hard - 1f);

        Willingness01 = dist <= soft ? 1f : Mathf.Clamp01(1f - (dist - soft) / Mathf.Max(0.01f, hard - soft));

        if (dist > hard) StartCoroutine(ReturnHomeRoutine());
    }

    // ── 回归：无敌 → 淡出 → 传送 → 淡入 → 回满血 → 重置 ──────────────

    private IEnumerator ReturnHomeRoutine()
    {
        _returning = true;
        _invulnerable = true;

        // 暂停 AI 包（追击会抢移动指令）；守卫者护卫期间它本来就是暂停的，别误恢复
        if (_aiPackage != null && _aiPackage.enabled) { _aiPackage.enabled = false; _aiPausedByMe = true; }
        _movement.ConfigureAsExternalExecutor(false);
        SubmitMove(Vector2.zero, false);
        var tactical = GetComponent<SkyPrisonTacticalActions>();
        if (tactical != null) tactical.Cancel();

        var renderers = GetComponentsInChildren<Renderer>(true);
        var block = new MaterialPropertyBlock();
        float baseY = transform.position.y;

        SpawnPuff(transform.position);                                  // 消失处冒一团尘雾，遮住硬切

        // 淡出
        for (float t = 0f; t < fadeOutSeconds; t += Time.deltaTime)
        {
            if (_death != null && _death.IsDeadLike) { Finish(false); yield break; }
            SubmitMove(Vector2.zero, false);
            SetFade(renderers, block, Ease(t / fadeOutSeconds), baseY);
            yield return null;
        }
        SetFade(renderers, block, 1f, baseY);

        // 传送（此时完全看不见）
        TeleportHome();
        baseY = transform.position.y;
        if (_health != null && !_health.IsDead) _health.SetCurrentHealth(_health.MaxHealth);     // 回满血，不留残血白嫖
        yield return null;
        SpawnPuff(transform.position);                                  // 出现处也冒一团

        // 淡入
        for (float t = 0f; t < fadeInSeconds; t += Time.deltaTime)
        {
            SetFade(renderers, block, 1f - Ease(t / fadeInSeconds), baseY);
            yield return null;
        }
        SetFade(renderers, block, 0f, baseY);
        Finish(true);
    }

    private void Finish(bool arrived)
    {
        _invulnerable = false;
        _returning = false;
        _lastHurt = -999f;
        Willingness01 = 1f;
        _movement?.ClearFacingOverride();
        if (_aiPackage != null && _aiPausedByMe)
        {
            var escort = GetComponent<SkyPrisonGuardianEscort>();
            if (escort == null || !escort.IsEscorting) _aiPackage.enabled = true;
            _aiPausedByMe = false;
        }
        if (arrived)
            foreach (var l in GetComponents<ILeashListener>()) l.OnLeashReturnedHome();
    }

    private void SetFade(Renderer[] renderers, MaterialPropertyBlock block, float amount, float baseY)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(DissolveAmountId, amount);
            block.SetFloat(DissolveBottomUpId, amount > 0f ? 1f : 0f);
            block.SetFloat(DissolveBaseYId, baseY);
            block.SetFloat(DissolveHeightId, Mathf.Max(0.5f, fadeHeight));
            block.SetFloat(DissolveEdgeWidthId, 0.06f);
            block.SetColor(DissolveEdgeColorId, new Color(0f, 0f, 0f, 1f));      // 淡出不要发光边
            block.SetTexture(DissolveNoiseTexId, GetNoise());
            block.SetFloat(DissolveNoiseScaleId, edgeNoiseScale);
            block.SetFloat(DissolveNoiseMixId, edgeNoiseMix);
            r.SetPropertyBlock(block);
        }
    }

    private void SpawnPuff(Vector3 pos)
    {
        if (puffPrefabs == null || puffPrefabs.Length == 0) return;
        var vfx = FootstepVFXManager.Instance;
        if (vfx == null) return;
        int sorting = Mathf.RoundToInt(pos.z * 100f);
        for (int i = 0; i < puffPrefabs.Length; i++)
            if (puffPrefabs[i] != null)
                vfx.SpawnDust(new[] { puffPrefabs[i] }, pos, Quaternion.identity, puffScale, sorting, puffLifetime);
    }

    private void TeleportHome()
    {
        Rigidbody rb = _movement != null ? _movement.CachedRigidbody : null;
        Vector3 target = _home;
        if (rb != null) rb.position = target;
        transform.position = target;
        Physics.SyncTransforms();

        var motor = GetComponentInChildren<TerrainGroundMotorV5>(true);
        if (motor != null) { motor.ResetHorizontalVelocity(); motor.ResetVerticalVelocity(); }
        Debug.Log("[Leash] " + name + " 超出活动范围，无敌淡出并传送回出生点 " + target.ToString("F1"), this);
    }

    private void SubmitMove(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, run);
    }
}
