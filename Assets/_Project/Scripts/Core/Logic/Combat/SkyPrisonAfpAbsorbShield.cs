using System;
using UnityEngine;

/// <summary>
/// AFP 里程碑效果：「特效力场：xx」。
/// 玩家在 AFP 购买窗口里把某一项属性抗性(灼热/电磁...)点到每满 5 级一档，且在 Seeker 科技树里点亮了对应节点，
/// 就获得一层力场：这个属性的异常累计满值、即将赋予异常状态(灼烧/触电…)时，豁免这一次(累计清零但不生成状态)；
/// 用掉后进入恢复，恢复时间随档位缩短(I~IV：150/120/90/60 秒)。
/// 实现 IAnomalyApplyGuard，由 UnitAnomalyController 在赋予异常状态前查询。
/// 一局内有效：进本/出本/死亡结算时 AFP 等级清零，档位变 0，力场自然消失。
/// </summary>
// 注意：不能加 [DisallowMultipleComponent]——灼热和电磁各挂一个，同一个物体上要有两个这个组件
public sealed class SkyPrisonAfpAbsorbShield : MonoBehaviour, IAnomalyApplyGuard
{
    public const int   LevelsPerTier   = 5;
    public const float RechargeSeconds = 120f;
    /// <summary>第 1~4 档(抗性 5/10/15/20 级)用掉豁免后的恢复秒数：档位越高恢复越快。</summary>
    public const int MaxTier = 4;
    /// <summary>第 1~4 档(抗性 5/10/15/20 级)用掉豁免后的恢复秒数：档位越高恢复越快，最快 60 秒，不会变成一直豁免。</summary>
    public static readonly float[] TierRechargeSeconds = { 150f, 120f, 90f, 60f };

    public static float RechargeSecondsForTier(int tier)
    {
        if (tier <= 0) return TierRechargeSeconds[0];
        return TierRechargeSeconds[Mathf.Min(tier, MaxTier) - 1];
    }

    /// <summary>档位的罗马数字写法(I、II … XIX)，名字后面用。</summary>
    public static string TierName(int tier)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        string res = "";
        while (tier >= 10) { res += "X"; tier -= 10; }
        return res + r[Mathf.Clamp(tier, 0, 9)];
    }

    /// <summary>测试开关：为 true 时跳过科技树解锁检查(科技树节点还没发放途径时用来验证效果)。</summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool DebugForceUnlock = false;     // 科技树(战术研修)已接通：默认按真实科技树解锁；测试时按 F7 临时全放开
#else
    public static bool DebugForceUnlock;
#endif

    /// <summary>力场豁免了一次异常 / 恢复完成时触发，供 HUD 和音效订阅。</summary>
    public static event Action<SkyPrisonAfpAbsorbShield> OnBroken;
    public static event Action<SkyPrisonAfpAbsorbShield> OnRecharged;

    [SerializeField] string damageKey = "heat";       // 吸收的属性伤害 key，同时是 AFP 升级项 key
    [SerializeField] string nodeId = "seeker_afp_heat_shield";

    public string DamageKey => damageKey;
    public float Current { get; private set; }
    public float Capacity { get; private set; }
    /// <summary>被打空后还要等多久才恢复(0 = 没在恢复)。</summary>
    public float RechargeLeft { get; private set; }
    public bool Unlocked => NodeUnlocked(nodeId);
    /// <summary>科技树节点是否已点亮(测试开关打开时一律算解锁)。</summary>
    public static bool NodeUnlocked(string node) => DebugForceUnlock || (TechTreeRuntime.Instance != null && TechTreeRuntime.Instance.GetNodeLevel(node) > 0);

    public void Configure(string key, string node) { damageKey = key; nodeId = node; }

    /// <summary>当前档位(0 = 没有盾)：解锁节点 && 对应抗性每满 5 级一档。</summary>
    public int Tier
    {
        get
        {
            var a = SkyPrisonAfpAbilityTable.Find("field_" + damageKey);      // 每 5 级一档、每档一个科技树节点，见能力表
            return a == null ? 0 : SkyPrisonAfpAbilityTable.CurrentTier(a);
        }
    }

    /// <summary>某个抗性等级离下一档还差几级(已经最高档返回 -1)，窗口里的提示用。</summary>
    public static int LevelsToNextTier(int level)
    {
        int tier = level / LevelsPerTier;
        if (tier >= MaxTier) return -1;
        return (tier + 1) * LevelsPerTier - level;
    }

    /// <summary>当前这一层的恢复总时长(随档位)。</summary>
    public float RechargeTotal { get { int t = Tier; return t > 0 ? RechargeSecondsForTier(t) : RechargeSeconds; } }

    void Update()
    {
        int tier = Tier;
        float cap = tier > 0 ? 1f : 0f;            // 力场只有一层：Capacity 0/1，Current 0/1(1=在豁免待命中)
        if (!Mathf.Approximately(cap, Capacity))
        {
            if (cap <= 0f) { Current = 0f; RechargeLeft = 0f; }
            else Current = 1f;                       // 新获得：直接充满
            Capacity = cap;
        }

        if (RechargeLeft > 0f)
        {
            RechargeLeft -= Time.deltaTime;
            if (RechargeLeft <= 0f)
            {
                RechargeLeft = 0f;
                Current = Capacity;
                OnRecharged?.Invoke(this);
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityFieldRecharged);
            }
        }
    }

    // ── 状态显示 ─────────────────────────────────────────────
    // 有力场：「特效力场：xx」状态(常驻，层数 = 档位 I~IV)；被打空后换成「特效力场：xx·恢复」(定时 120 秒，状态栏有倒计时)；
    // 恢复完毕换回；等级清零/科技树未解锁(Capacity=0)时两个都撤掉。状态定义是运行时创建的，不需要资产文件。
    static readonly System.Collections.Generic.Dictionary<string, StatusDefinition> _defs = new System.Collections.Generic.Dictionary<string, StatusDefinition>();

    string ReadyId => "afp_field_" + damageKey + "_ready";
    string CooldownId => "afp_field_" + damageKey + "_cd";

    string DamageName => DamageNameOf(damageKey);

    public static string DamageNameOf(string key)
    {
        switch (key)
        {
            case "heat": return DoorText.Get("ui_afp_dmg_heat", "灼热");
            case "shock": return DoorText.Get("ui_afp_dmg_shock", "电磁");
            case "corrosion": return DoorText.Get("ui_afp_dmg_corrosion", "腐蚀");
            case "freeze": return DoorText.Get("ui_afp_dmg_freeze", "冻结");
            default: return key;
        }
    }

    StatusDefinition GetDef(bool cooldown)
    {
        string id = cooldown ? CooldownId : ReadyId;
        if (_defs.TryGetValue(id, out var d) && d != null) return d;
        var asset = Resources.Load<StatusDefinition>("AFP/Status/" + id);
        if (asset != null) { _defs[id] = asset; return asset; }
        d = ScriptableObject.CreateInstance<StatusDefinition>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.statusId = id;
        string fieldName = string.Format(DoorText.Get("ui_afp_ab_field_fmt", "特效力场：{0}"), DamageName);
        d.displayName = cooldown ? fieldName + DoorText.Get("status_afp_field_cd_suffix", "·恢复中") : fieldName;
        d.description = cooldown ? DoorText.Get("status_afp_field_cd_desc_v2", "力场已豁免一次异常，恢复中，结束后重新生效。")
                                 : DoorText.Get("status_afp_field_desc_v2", "豁免一次对应属性的异常赋予，随后进入恢复；档位(层数)越高恢复越快。");
        d.isBuff = !cooldown; d.isHidden = false; d.showInHud = true;
        d.icon = SkyPrisonAfpIcons.GetSprite(SkyPrisonAfpIcons.Shield);
        d.durationType = cooldown ? StatusDurationType.Timed : StatusDurationType.Permanent;
        d.baseDuration = cooldown ? TierRechargeSeconds[0] : 0f;
        d.canStack = !cooldown; d.baseStack = 1; d.maxStack = MaxTier;
        d.stackUpdateMode = StatusStackUpdateMode.Override;
        d.durationUpdateMode = StatusDurationUpdateMode.Override;
        _defs[id] = d;
        return d;
    }

    UnitStatusController _status;
    int _shownTier;

    UnitStatusController Status()
    {
        if (_status != null) return _status;
        _status = GetComponentInParent<UnitStatusController>() ?? GetComponentInChildren<UnitStatusController>(true);
        if (_status == null)
        {
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (player != null) _status = player.GetComponentInChildren<UnitStatusController>(true) ?? UnitStatusController.EnsureOnRoot(player.gameObject);
        }
        return _status;
    }

    void SyncStatuses()
    {
        var sc = Status();
        if (sc == null) return;
        bool has = Capacity > 0f;
        bool wantReady = has && RechargeLeft <= 0f;
        bool wantCd = has && RechargeLeft > 0f;
        int tier = Tier;
        if (sc.HasStatus(ReadyId) != wantReady)
        {
            if (wantReady) { sc.ApplyStatus(GetDef(false), gameObject, tier); _shownTier = tier; }
            else sc.RemoveStatus(ReadyId);
        }
        else if (wantReady && tier != _shownTier)        // 升档：刷新层数
        {
            sc.ApplyStatus(GetDef(false), gameObject, tier); _shownTier = tier;
        }
        if (sc.HasStatus(CooldownId) != wantCd)
        {
            if (wantCd) sc.ApplyStatus(GetDef(true), gameObject, 1, RechargeLeft);
            else sc.RemoveStatus(CooldownId);
        }
    }

    void OnDisable()
    {
        var sc = _status;
        if (sc == null) return;
        sc.RemoveStatus(ReadyId); sc.RemoveStatus(CooldownId);
    }

    void LateUpdate() { SyncStatuses(); }

    public bool TryExemptAnomaly(string anomalyKey)
    {
        if (anomalyKey != damageKey || Capacity <= 0f || Current <= 0f) return false;
        Current = 0f;
        RechargeLeft = RechargeTotal;
        OnBroken?.Invoke(this);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityFieldBlock);
        return true;
    }

    /// <summary>测试用：立刻恢复满。</summary>
    public void RestoreNow() { RechargeLeft = 0f; Current = Capacity; }

    // ── 给玩家挂盾 ─────────────────────────────────────────────
    // 钩子只会在「受击目标的 UnitHealthController 所在物体(找不到才找父级)」上找 IIncomingHitModifier，所以盾必须挂在玩家血量组件的物体上。
    // 用一个常驻的小驱动每 0.5 秒检查：一局进行中、玩家存在、盾还没挂就补上(玩家被重新生成也会自动重挂)。
    sealed class Driver : MonoBehaviour
    {
        float _next;
        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var afp = AFPRuntime.Instance;
            if (afp == null || !afp.IsInSession) return;
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (player == null) return;
            var health = player.GetComponentInChildren<UnitHealthController>(true);
            if (health == null) return;
            Ensure(health.gameObject, "heat", "seeker_afp_heat_shield");
            Ensure(health.gameObject, "shock", "seeker_afp_shock_shield");
            Ensure(health.gameObject, "corrosion", "seeker_afp_corrosion_shield");
            Ensure(health.gameObject, "freeze", "seeker_afp_freeze_shield");
            if (health.GetComponent<SkyPrisonAfpLifeInsurance>() == null) health.gameObject.AddComponent<SkyPrisonAfpLifeInsurance>();
        }

        static void Ensure(GameObject go, string key, string node)
        {
            foreach (var s in go.GetComponents<SkyPrisonAfpAbsorbShield>()) if (s.damageKey == key) return;
            go.AddComponent<SkyPrisonAfpAbsorbShield>().Configure(key, node);
        }
    }

    /// <summary>场景里玩家身上当前的某一属性盾(HUD / 窗口读取)。</summary>
    public static SkyPrisonAfpAbsorbShield FindOnPlayer(string key)
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return null;
        foreach (var s in player.GetComponentsInChildren<SkyPrisonAfpAbsorbShield>(true)) if (s.damageKey == key) return s;
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("[AfpShieldDriver]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<Driver>();
    }
}
