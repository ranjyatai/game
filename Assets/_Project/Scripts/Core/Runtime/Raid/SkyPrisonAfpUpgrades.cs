using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 局内 AFP 升级（第一版：只做属性类——七种抗性）。
/// 规则（用户 10-06 定）：每级等额、单级不明显，同一项叠多级才有体感；每买一次，所有升级的价格一起涨（全局涨价），
/// 所以偏科有代价。数值(10-10 用户要求每项能升 20 来次)：每级 +0.5% 抗性、上限 20 级(=10%，只是装备之外的小补充，不抢装备的位置；原 4%/80% 10-10 用户嫌太高)、起价 100、每次购买后全局 ×1.06(买 40 次约 ×10)。
/// 只在一局(AFP 会话)内有效：进本清零，出本/死亡清零。效果通过 UnitBattleStatRuntime 的外部加成(来源 "afp_upgrade")叠到玩家身上。
/// </summary>
public static class SkyPrisonAfpUpgrades
{
    public sealed class Def
    {
        public string key;          // 升级项 key，同时是属性 key 的前缀
        public string statKey;      // 实际改的属性：xxxResist(百分点，+8 = +8% 抗性)
        public string labelKey, labelFallback;
        public float perLevel;      // 每级加多少(抗性是百分点；追加生命是点数)
        public string unit = "%";   // 数值后缀
        public bool multiply;       // true=按百分比乘(追加生命：最大生命 ×(1+n%))；false=直接加(抗性百分点)
        public int maxLevel;
        public int basePrice;       // 这一项自己的起价；0 = 用默认 BasePrice(100)。全局涨价倍率照样叠在上面
        public int StartPrice => basePrice > 0 ? basePrice : BasePrice;
        public int[] fixedPrices;   // 非 null = 固定价目表：第 N 次买就是表里第 N 个价格，不参与也不受全局涨价影响(扩容弹匣用)
        public Func<int, float> valueAt;   // 非 null = 窗口里大号数值按这个函数算(篡夺者：每级数值不是等差)
        public Func<bool> visible;         // 非 null = 返回 false 时整行不显示(篡夺者：科技树节点没点亮)
        public Func<bool> unlocked;        // 非 null = 返回 false 时这一行灰着、不能买(篡夺者：追加生命 / 物理攻击强化没到 10 级)
        public Func<string> lockedHint;    // 灰着时左下小字显示的条件说明
    }

    public static float ValueAt(Def d, int level) => d.valueAt != null ? d.valueAt(level) : level * d.perLevel;
    public static bool IsVisible(Def d) => d.visible == null || d.visible();
    public static bool IsUnlocked(Def d) => d.unlocked == null || d.unlocked();

    public const int BasePrice = 100;
    public const float Inflation = 1.06f;
    public const string ModifierSource = "afp_upgrade";

    public static readonly Def[] All =
    {
        new Def { key = "hp", statKey = "maxHp", labelKey = "ui_afp_up_hp", labelFallback = "追加生命", perLevel = 4f, maxLevel = 99, unit = "" },
        new Def { key = "magazine", statKey = "", labelKey = "ui_afp_up_magazine", labelFallback = "扩容弹匣", perLevel = 10f, maxLevel = 3, unit = "{r}", fixedPrices = new[] { 350, 2200, 7500 } },
        new Def { key = "physAtk", statKey = "", labelKey = "ui_afp_up_phys", labelFallback = "物理攻击强化", perLevel = 0.5f, maxLevel = 99 },
        new Def { key = "attrAtk", statKey = "", labelKey = "ui_afp_up_attr", labelFallback = "属性攻击强化", perLevel = 0.5f, maxLevel = 99 },
        Make("slash",     "ui_afp_up_slash",     "斩击抗性"),
        Make("strike",    "ui_afp_up_strike",    "打击抗性"),
        Make("impact",    "ui_afp_up_impact",    "冲击抗性"),
        Make("heat",      "ui_afp_up_heat",      "灼热抗性"),
        Make("shock",     "ui_afp_up_shock",     "电磁抗性"),
        Make("corrosion", "ui_afp_up_corrosion", "腐蚀抗性"),
        Make("freeze",    "ui_afp_up_freeze",    "冻结抗性"),
        new Def { key = SkyPrisonAfpUsurper.UpgradeKey, statKey = "", labelKey = "ui_afp_up_usurp", labelFallback = "篡夺者", perLevel = 6.7f, maxLevel = SkyPrisonAfpUsurper.MaxLevel, basePrice = 150,
            valueAt = lv => Mathf.Round(SkyPrisonAfpUsurper.MeleeAt(lv) * 100f),
            visible = () => SkyPrisonAfpUsurper.NodeOn,
            unlocked = () => LevelOf("hp") >= SkyPrisonAfpUsurper.NeedHpLevel && LevelOf("physAtk") >= SkyPrisonAfpUsurper.NeedPhysLevel,
            lockedHint = UsurperNeedText },
        // 浮游炮(科技树节点点亮才出现)：数量 1~4(阶梯昂贵、固定价)；弹仓每级 +2 发；冷却每级 -10%
        new Def { key = SkyPrisonAfpDrones.CountKey, statKey = "", labelKey = "ui_afp_up_drone_count", labelFallback = "浮游炮数量", perLevel = 1f, maxLevel = 4, unit = "", fixedPrices = new[] { 500, 1800, 5000, 12000 },
            visible = () => SkyPrisonAfpDrones.NodeOn },
        new Def { key = SkyPrisonAfpDrones.MagKey, statKey = "", labelKey = "ui_afp_up_drone_mag", labelFallback = "浮游炮弹仓", perLevel = 3f, maxLevel = 5, unit = "{r}", basePrice = 150,
            visible = () => SkyPrisonAfpDrones.NodeOn },
        new Def { key = SkyPrisonAfpDrones.CdKey, statKey = "", labelKey = "ui_afp_up_drone_cd", labelFallback = "浮游炮冷却缩短", perLevel = 10f, maxLevel = 6, basePrice = 150,
            visible = () => SkyPrisonAfpDrones.NodeOn },
    };

    static int LevelOf(string key)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].key == key) return Level(All[i]);
        return 0;
    }

    static string UsurperNeedText()
    {
        bool hp = LevelOf("hp") >= SkyPrisonAfpUsurper.NeedHpLevel, ph = LevelOf("physAtk") >= SkyPrisonAfpUsurper.NeedPhysLevel;
        string a = DoorText.Get("ui_afp_up_hp", "追加生命") + " Lv." + SkyPrisonAfpUsurper.NeedHpLevel;
        string b = DoorText.Get("ui_afp_up_phys", "物理攻击强化") + " Lv." + SkyPrisonAfpUsurper.NeedPhysLevel;
        if (hp) a = "<color=#7CFFC0FF>" + a + "</color>";
        if (ph) b = "<color=#7CFFC0FF>" + b + "</color>";
        return string.Format(DoorText.Get("ui_afp_usurp_need_fmt", "需要 {0}　{1}"), a, b);
    }

    static Def Make(string k, string lk, string lf) => new Def { key = k, statKey = k + "Resist", labelKey = lk, labelFallback = lf, perLevel = 0.5f, maxLevel = 99 };

    static readonly Dictionary<string, int> levels = new Dictionary<string, int>();
    static int purchases;

    /// <summary>升级项等级/购买次数变化(买了或清零)。</summary>
    public static event Action OnChanged;

    public static int Level(Def d) => levels.TryGetValue(d.key, out int l) ? l : 0;
    public static bool IsMax(Def d) => Level(d) >= d.maxLevel;
    public static int NextPrice => Mathf.RoundToInt(BasePrice * Mathf.Pow(Inflation, purchases));
    /// <summary>这一项第 idx 个待买等级的价格：自己的起价 × 全局涨价(已买次数 + 它在本次预加点里的排位)。</summary>
    public static int PriceFor(Def d, int idx, int levelIndex = 0)
    {
        if (d.fixedPrices != null && d.fixedPrices.Length > 0)
        {
            int fixedPrice = d.fixedPrices[Mathf.Clamp(levelIndex, 0, d.fixedPrices.Length - 1)];
            if (d.key == "magazine" && MagazineDiscountActive) fixedPrice = Mathf.RoundToInt(fixedPrice * MagazineDiscountMultiplier);
            return fixedPrice;
        }
        return Mathf.RoundToInt(d.StartPrice * Mathf.Pow(Inflation, purchases + idx));
    }
    /// <summary>科技树「扩容折扣」节点：把扩容弹匣的价目整体打到 300/350(第一次正好是开局 300 AFP 买得起的价格)。</summary>
    public const string MagazineDiscountNodeId = "seeker_afp_magazine_discount";
    public const float MagazineDiscountMultiplier = 300f / 350f;
    /// <summary>测试开关(F8)：不依赖科技树，直接打开扩容折扣。</summary>
    public static bool DebugMagazineDiscount;
    public static bool MagazineDiscountActive => DebugMagazineDiscount || (TechTreeRuntime.Instance != null && TechTreeRuntime.Instance.GetNodeLevel(MagazineDiscountNodeId) > 0);

    static bool IsFixed(Def d) => d.fixedPrices != null && d.fixedPrices.Length > 0;
    public static float CurrentBonus(Def d) => Level(d) * d.perLevel;

    /// <summary>物理 / 属性攻击强化：玩家造成的物理伤害(斩击/打击/冲击)或属性伤害(灼热/电磁/腐蚀/冻结)乘上 (1 + n%)。
    /// 只对玩家自己生效(敌人没有 AFP)，伤害公式里把攻击者传进来判断；一局内有效。</summary>
    public static float AttackMultiplier(UnitBattleStatRuntime attacker, bool physical)
    {
        if (attacker == null) return 1f;
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null || !(attacker.transform == player.transform || attacker.transform.IsChildOf(player.transform) || player.transform.IsChildOf(attacker.transform))) return 1f;
        string key = physical ? "physAtk" : "attrAtk";
        for (int i = 0; i < All.Length; i++)
            if (All[i].key == key) return 1f + CurrentBonus(All[i]) / 100f;
        return 1f;
    }

    /// <summary>扩容弹匣带来的额外弹匣容量(发)：玩家所有弹匣武器的容量统一加这个数。一局内有效。</summary>
    public static int MagazineBonus
    {
        get
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].key == "magazine") return Mathf.RoundToInt(CurrentBonus(All[i]));
            return 0;
        }
    }

    public static bool CanAfford => AFPRuntime.Instance != null && AFPRuntime.Instance.HasAFP(NextPrice);

    // ── 预加点(购买窗口里先分配、最后统一「强化」才真正扣钱生效，避免点错) ─────────────
    static readonly Dictionary<string, int> pending = new Dictionary<string, int>();

    public static int Pending(Def d) => pending.TryGetValue(d.key, out int p) ? p : 0;
    public static int PendingLevels { get { int n = 0; foreach (var kv in pending) n += kv.Value; return n; } }
    /// <summary>预加点之后的等级(当前等级 + 待强化)。</summary>
    public static int PreviewLevel(Def d) => Level(d) + Pending(d);
    public static float PreviewBonus(Def d) => PreviewLevel(d) * d.perLevel;

    /// <summary>第 k 级(从 0 起，接在已买次数之后)的价格：全局涨价按「已买次数 + 已预加的级数」逐级累乘。</summary>
    public static int PriceAt(int k) => Mathf.RoundToInt(BasePrice * Mathf.Pow(Inflation, purchases + k));

    /// <summary>某组预加点的合计价格：按升级表顺序依次排位，排位越靠后全局涨价越多(各项自己的起价不同，所以不能只看总级数)。</summary>
    static int CostOf(Dictionary<string, int> map, string extraKey = null)
    {
        int idx = 0, sum = 0;
        for (int i = 0; i < All.Length; i++)
        {
            int c = map.TryGetValue(All[i].key, out int v) ? v : 0;
            if (All[i].key == extraKey) c++;
            for (int k = 0; k < c; k++)
            {
                if (IsFixed(All[i])) sum += PriceFor(All[i], 0, Level(All[i]) + k);       // 固定价目表：不占全局涨价的排位
                else sum += PriceFor(All[i], idx++);
            }
        }
        return sum;
    }

    /// <summary>当前所有预加点的合计价格(逐级累加，后加的更贵)。</summary>
    public static int PendingCost => CostOf(pending);

    /// <summary>再给这一项预加一级，合计价格会增加多少(窗口里「下一级」小字显示的价格)。</summary>
    public static int MarginalPrice(Def d) => CostOf(pending, d.key) - CostOf(pending);
    public static int AfpNow => AFPRuntime.Instance != null ? AFPRuntime.Instance.CurrentAFP : 0;
    public static bool PendingAffordable => PendingCost <= AfpNow;

    /// <summary>预加一级：没满级、且加上之后合计价格仍在 AFP 范围内才成功(买不起的不让加，避免点了又提示不够)。</summary>
    public static bool TryAddPending(Def d)
    {
        if (d == null || PreviewLevel(d) >= d.maxLevel || !IsVisible(d) || !IsUnlocked(d)) return false;
        if (CostOf(pending, d.key) > AfpNow) return false;
        pending[d.key] = Pending(d) + 1;
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>这一项现在还能不能再预加一级(没满级且合计价格买得起)——窗口用它决定「+」亮不亮。</summary>
    public static bool CanAddPending(Def d)
    {
        if (d == null || PreviewLevel(d) >= d.maxLevel || !IsVisible(d) || !IsUnlocked(d)) return false;
        return CostOf(pending, d.key) <= AfpNow;
    }

    public static bool TryRemovePending(Def d)
    {
        if (d == null || Pending(d) <= 0) return false;
        int v = Pending(d) - 1;
        if (v <= 0) pending.Remove(d.key); else pending[d.key] = v;
        OnChanged?.Invoke();
        return true;
    }

    public static void ClearPending()
    {
        if (pending.Count == 0) return;
        pending.Clear();
        OnChanged?.Invoke();
    }

    /// <summary>统一强化：把所有预加点一次买下(一次性扣合计价格)。预加点为空或钱不够返回 false。</summary>
    public static bool CommitPending()
    {
        int n = PendingLevels;
        if (n <= 0) return false;
        int cost = PendingCost;
        var afp = AFPRuntime.Instance;
        if (afp == null || !afp.TrySpendAFP(cost)) return false;
        foreach (var kv in pending) levels[kv.Key] = (levels.TryGetValue(kv.Key, out int l) ? l : 0) + kv.Value;
        for (int i = 0; i < All.Length; i++)
            if (!IsFixed(All[i]) && pending.TryGetValue(All[i].key, out int pv)) purchases += pv;       // 固定价目的项不推高全局涨价
        pending.Clear();
        ApplyToPlayer();
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>买一级：够钱且没满级才成功。成功返回 true。</summary>
    public static bool TryBuy(Def d)
    {
        if (d == null || IsMax(d)) return false;
        var afp = AFPRuntime.Instance;
        if (afp == null || !afp.TrySpendAFP(PriceFor(d, 0, Level(d)))) return false;
        levels[d.key] = Level(d) + 1;
        if (!IsFixed(d)) purchases++;
        ApplyToPlayer();
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>把当前所有等级写到玩家的属性外部加成上(重复调用安全：同一来源标签会被替换)。</summary>
    public static void ApplyToPlayer()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return;
        var stats = player.GetComponentInChildren<UnitBattleStatRuntime>(true);
        if (stats == null) return;
        var mods = new List<StatModifier>();
        for (int i = 0; i < All.Length; i++)
        {
            if (string.IsNullOrEmpty(All[i].statKey)) continue;            // 没有对应属性的升级项(扩容弹匣)不写属性加成
            float v = CurrentBonus(All[i]);
            if (v <= 0f) continue;
            mods.Add(All[i].multiply ? new StatModifier(All[i].statKey, 1f + v / 100f, StatModifierOp.Multiply)
                                     : new StatModifier(All[i].statKey, v, StatModifierOp.Add));
        }
        var health = player.GetComponentInChildren<UnitHealthController>(true);
        float oldMax = health != null ? health.MaxHealth : 0f;
        stats.SetExternalModifiers(ModifierSource, mods);   // 空列表 = 清除
        // 追加生命：上限涨了多少，当前血量就同步回多少(只涨上限不回血的话，玩家点了没感觉)；上限降(清零)时 clamp 自己处理
        if (health != null && !health.IsDead && health.MaxHealth > oldMax + 0.01f && oldMax > 1f)
            health.ApplyHeal(health.MaxHealth - oldMax);
    }

    public static void ResetAll()
    {
        levels.Clear();
        pending.Clear();
        purchases = 0;
        SkyPrisonAfpPhysicalMastery.ResetCounter();
        ApplyToPlayer();
        SkyPrisonAfpLifeInsurance.FindOnPlayer()?.ResetCooldown();    // 新的一局，生命保险冷却清零
        foreach (var fk in new[] { "heat", "shock", "corrosion", "freeze" }) SkyPrisonAfpAbsorbShield.FindOnPlayer(fk)?.RestoreNow();
        OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        levels.Clear();
        pending.Clear();
        purchases = 0;
        OnChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        // 一局开始/结束都清零(出本、死亡结算后也是)
        AFPRuntime.OnSessionBegan -= ResetAll; AFPRuntime.OnSessionBegan += ResetAll;
        AFPRuntime.OnSessionEnded -= ResetAll; AFPRuntime.OnSessionEnded += ResetAll;
    }
}
