using UnityEngine;

/// <summary>
/// 灰炉：不会移动的瘴气装置。
///   · 玩家处于瘴气半径内，每秒累加「灰色损失」（最大生命被灰色吞掉，满了即死），离开不消失；
///   · 能被打死，但是「集火检测」：一直按最大生命的比例回血(挨打时也回)，停火超过 regenDelaySeconds 回得更快，
///     所以不能先去清周围的怪再回头打它；前期火力低于回血速度，站在瘴气里把子弹打空也打不死(用户 10-12 定)；
///   · 血量由单位定义的 maxHp 决定(10-12：以前组件自己 SetMaxHealth(450)，但随后被属性系统按单位定义的 100 覆盖，实际一直是 100 血)；
///   · 瘴气半径要比枪的射程大，枪也得走进瘴气才打得到。
/// 占位阶段：瘴气边缘用地面圆环表示，美术(特效/模型)做好后再换。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonGrayFurnace : MonoBehaviour
{
    [Header("瘴气")]
    [Tooltip("瘴气半径(米，水平)。要大于玩家枪的射程(约16.5m)。")]
    [SerializeField] private float radius = 20f;
    [Tooltip("一直待在瘴气里，多少秒灰色占满血条(致死)。")]
    [SerializeField] private float secondsToFull = 25f;

    [Header("集火检测（回血）")]
    [Tooltip("持续回血：每秒回最大生命的这个比例，挨打时也回。2000 血 × 2.5% = 50/秒，前期火力(≈56/秒)净伤害只剩 ≈6/秒，25 秒吞满前打不死。")]
    [SerializeField] private float regenFractionPerSecond = 0.025f;
    [Tooltip("最后一次受击后，多少秒没被打就进入快速回血。")]
    [SerializeField] private float regenDelaySeconds = 2f;
    [Tooltip("停火后的回血倍率。")]
    [SerializeField] private float idleRegenMultiplier = 3f;

    [Header("地面圆环（占位）")]
    [SerializeField] private bool showRing = true;
    [SerializeField] private Color ringColor = new Color(0.62f, 0.64f, 0.66f, 0.55f);
    [SerializeField] private float ringWidth = 0.18f;

    private UnitHealthController _health;
    private UnitDeathController _death;
    private float _lastDamageTime = -999f;
    private LineRenderer _ring;

    private bool _healthInit;

    // 摆在场景里的单位：血量/死亡控制器是运行时才创建的，Awake/Start 时多半还没有——补取，
    // 取到后只初始化一次血量并挂受伤回调。
    private void EnsureRefs()
    {
        if (_death == null) _death = GetComponentInChildren<UnitDeathController>(true);
        if (_health == null) _health = GetComponentInChildren<UnitHealthController>(true);
        if (!_healthInit && _health != null)
        {
            _healthInit = true;
            _health.OnDamaged += OnDamaged;
        }
    }

    private void Awake() { EnsureRefs(); }

    private void Start()
    {
        EnsureRefs();
        if (showRing) BuildRing();
    }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDamaged -= OnDamaged;
    }

    private void OnDamaged(UnitHealthController h, float amount)
    {
        _lastDamageTime = Time.time;
    }


    private bool IsDead => _death != null && _death.IsDeadLike;

    private void Update()
    {
        EnsureRefs();
        if (IsDead)
        {
            if (_ring != null) _ring.enabled = false;
            return;
        }

        TickRegen(Time.deltaTime);
        TickMiasma(Time.deltaTime);
    }

    private void TickRegen(float dt)
    {
        if (_health == null || _health.IsDead) return;
        if (_health.CurrentHealth >= _health.MaxHealth) return;
        float rate = _health.MaxHealth * regenFractionPerSecond;
        if (Time.time - _lastDamageTime >= regenDelaySeconds) rate *= idleRegenMultiplier;
        _health.ApplyHeal(rate * dt);
    }

    private void TickMiasma(float dt)
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return;

        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > radius * radius) return;

        var gray = SkyPrisonGrayLoss.EnsureOn(player.gameObject);
        if (gray == null) return;

        // 每秒损失 = 基础最大生命 / secondsToFull
        float perSecond = gray.BaseMaxHealth / Mathf.Max(1f, secondsToFull);
        gray.AddLoss(perSecond * dt);
    }

    private void BuildRing()
    {
        var go = new GameObject("GrayFurnaceRing");
        go.transform.SetParent(transform, false);
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;

        _ring = go.AddComponent<LineRenderer>();
        _ring.useWorldSpace = true;
        _ring.loop = true;
        _ring.alignment = LineAlignment.View;
        _ring.startWidth = _ring.endWidth = ringWidth;
        _ring.material = new Material(Shader.Find("Sprites/Default")) { color = ringColor };
        _ring.startColor = _ring.endColor = ringColor;
        _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring.receiveShadows = false;

        const int segments = 128;
        _ring.positionCount = segments;
        Vector3 c = transform.position;
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            _ring.SetPosition(i, new Vector3(c.x + Mathf.Cos(a) * radius, c.y + 0.06f, c.z + Mathf.Sin(a) * radius));
        }
    }
}
