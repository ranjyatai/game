using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 角色信息面板的第二页「战局能力」：左边是这一局获得的能力列表，选中一项，右边显示详情
/// (名字+档位、效果说明、恢复/冷却时间、当前状态与倒计时、来源、下一档要求)，下面列出 AFP 强化已点的项目。
/// 能力只要做成状态+组件，这里自动多一行；没有任何能力时写一句提示。
/// 自己负责：每 0.2 秒读一次数据、数量/状态变了才重建，倒计时每帧刷新；W/S/方向键/手柄方向键选择，鼠标悬停或点击选择。
/// </summary>
public sealed class CharacterPanelAbilityPage : MonoBehaviour
{
    public sealed class Entry
    {
        public string id, name, effect, detail, source, nextHint;
        public int icon, tier, maxTier;
        public bool cooling;
        public float cooldownLeft, cooldownTotal;
    }

    static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);
    const float RowGap = 8f;
    const float FontScale = 1.2f;       // 这一页的字整体放大(窗口标题「角色信息」不在这页里，不受影响)

    // 多档能力满档：淡紫；否则冷绿。冷却中的暗色另算
    static bool IsMaxed(Entry e) => e.maxTier > 1 && e.tier >= e.maxTier;
    static Color Accent(Entry e) => IsMaxed(e) ? SkyPrisonAfpIcons.MaxTierColor : ColdGreen;

    TMP_FontAsset _font, _phFont;   // _phFont：NOT EQUIPPED 那类占位字用的"东亚重工"风格字体
    RectTransform _root;
    float _scale, _width, _height;
    int _selected;
    bool _built;
    float _nextPoll, _prevNav;
    string _signature = "";
    readonly List<Entry> _entries = new List<Entry>();
    readonly List<Image> _rowBgs = new List<Image>();
    TMP_Text _detailTimer;
    readonly List<TMP_Text> _rowTimers = new List<TMP_Text>();
    readonly List<int> _rowTimerIndex = new List<int>();

    static string Loc(string key, string fallback) => DoorText.Get(key, fallback);

    public static CharacterPanelAbilityPage Create(RectTransform box, TMP_FontAsset font, Vector2 offsetMin, Vector2 offsetMax, float scale, TMP_FontAsset placeholderFont = null)
    {
        var go = new GameObject("AbilityPage", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(box, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var p = go.AddComponent<CharacterPanelAbilityPage>();
        p._font = font; p._phFont = placeholderFont != null ? placeholderFont : font; p._root = rt; p._scale = scale;
        go.SetActive(false);
        return p;
    }

    void OnEnable() { _nextPoll = 0f; _signature = null; }

    void Update()
    {
        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + 0.2f;
            Collect();
            string sig = Signature();
            if (sig != _signature) { _signature = sig; Rebuild(); }
        }
        HandleNavigation();
        for (int i = 0; i < _rowTimers.Count; i++)
        {
            int idx = _rowTimerIndex[i];
            if (_rowTimers[i] != null && idx < _entries.Count && _entries[idx].cooling) _rowTimers[i].text = FormatTimer(_entries[idx].cooldownLeft);
        }
        if (_detailTimer != null && _selected < _entries.Count && _entries[_selected].cooling)
            _detailTimer.text = string.Format(Loc("charpanel_ability_recharging_fmt", "恢复中 {0}"), FormatTimer(_entries[_selected].cooldownLeft));
    }

    void HandleNavigation()
    {
        if (_entries.Count == 0) return;
        bool up = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
        bool down = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
        float dp = 0f;
        try { dp = Input.GetAxisRaw("DPadVertical"); } catch { }
        if (dp > 0.6f && _prevNav <= 0.6f) up = true;
        if (dp < -0.6f && _prevNav >= -0.6f) down = true;
        _prevNav = dp;
        int n = _selected;
        if (up) n = Mathf.Max(0, _selected - 1);
        if (down) n = Mathf.Min(_entries.Count - 1, _selected + 1);
        if (n != _selected) { _selected = n; _signature = null; _nextPoll = 0f; SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); }
    }

    static string FormatTimer(float s) { int t = Mathf.CeilToInt(Mathf.Max(0f, s)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_selected).Append('#');
        foreach (var e in _entries) sb.Append(e.id).Append('|').Append(e.name).Append('|').Append(e.tier).Append('|').Append(e.cooling ? '1' : '0').Append(';');
        foreach (var d in SkyPrisonAfpUpgrades.All) sb.Append(SkyPrisonAfpUpgrades.Level(d)).Append(',');
        return sb.ToString();
    }

    // ── 数据 ─────────────────────────────────────────────────
    void Collect()
    {
        var list = new List<Entry>();
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player != null)
        {
            foreach (var s in player.GetComponentsInChildren<SkyPrisonAfpAbsorbShield>(true))
            {
                int tier = s.Tier;
                if (tier <= 0 || s.Capacity <= 0f) continue;
                string dmg = SkyPrisonAfpAbsorbShield.DamageNameOf(s.DamageKey);
                float total = SkyPrisonAfpAbsorbShield.RechargeSecondsForTier(tier);
                int lvl = 0;
                foreach (var d in SkyPrisonAfpUpgrades.All) if (d.key == s.DamageKey) lvl = SkyPrisonAfpUpgrades.Level(d);
                int toNext = SkyPrisonAfpAbsorbShield.LevelsToNextTier(lvl);
                string next = toNext < 0
                    ? Loc("charpanel_ability_maxtier", "已达最高档")
                    : string.Format(Loc("charpanel_ability_next_fmt", "再 {0} 级升至 {1}：恢复 {2} 秒"), toNext,
                        SkyPrisonAfpAbsorbShield.TierName(tier + 1), Mathf.RoundToInt(SkyPrisonAfpAbsorbShield.RechargeSecondsForTier(tier + 1)));
                list.Add(new Entry
                {
                    id = "field_" + s.DamageKey,
                    name = string.Format(Loc("ui_afp_ab_field_fmt", "特效力场：{0}"), dmg) + " " + SkyPrisonAfpAbsorbShield.TierName(tier),
                    effect = Loc("charpanel_ability_exempt", "豁免 1 次"),
                    detail = string.Format(Loc("charpanel_ability_field_desc_v5", "目标属性的异常累计即将触发{0}异常时，豁免一次，随后进入恢复。档位越高，恢复越快。"), dmg),
                    source = string.Format(Loc("charpanel_ability_field_source_v5", "来源：适应强化「{0}抗性」每 5 级 + 科技树节点"), dmg),
                    nextHint = next,
                    icon = SkyPrisonAfpIcons.Shield, tier = tier, maxTier = SkyPrisonAfpAbsorbShield.MaxTier,
                    cooling = s.RechargeLeft > 0f, cooldownLeft = s.RechargeLeft, cooldownTotal = total,
                });
            }
            int burstTier = SkyPrisonAfpAnomalyBurst.Tier;
            if (burstTier > 0)
            {
                int bl = 0;
                foreach (var d in SkyPrisonAfpUpgrades.All) if (d.key == "attrAtk") bl = SkyPrisonAfpUpgrades.Level(d);
                int toNext = SkyPrisonAfpAnomalyBurst.LevelsToNextTier(bl);
                list.Add(new Entry
                {
                    id = "burst",
                    name = Loc("ui_afp_ab_burst", "异常追伤") + " " + SkyPrisonAfpAbsorbShield.TierName(burstTier),
                    effect = string.Format(Loc("charpanel_ability_burst_state_fmt", "×{0}"), SkyPrisonAfpAnomalyBurst.MultiplierForTier(burstTier).ToString("0.0")),
                    detail = string.Format(Loc("charpanel_ability_burst_desc_v5", "命中令敌人触发属性异常时，追加一次同属性追伤，伤害为当前攻击力的倍数。追伤不再累计异常；双属性武器两种异常各追伤一次。处于异常状态的敌人，受到的伤害 +{0}%。"), Mathf.RoundToInt(SkyPrisonAfpAnomalyBurst.VulnerabilityPerTier * 100f * burstTier)),
                    source = Loc("charpanel_ability_burst_source_v5", "来源：适应强化「属性攻击强化」每 5 级 + 科技树节点"),
                    nextHint = toNext < 0 ? Loc("charpanel_ability_maxtier", "已达最高档")
                        : string.Format(Loc("charpanel_ability_burst_next_fmt", "再 {0} 级升至 {1}：追伤 ×{2}"), toNext, SkyPrisonAfpAbsorbShield.TierName(burstTier + 1), SkyPrisonAfpAnomalyBurst.MultiplierForTier(burstTier + 1).ToString("0.0")),
                    icon = SkyPrisonAfpIcons.Bolt, tier = burstTier, maxTier = SkyPrisonAfpAnomalyBurst.MaxTier,
                    cooling = false, cooldownLeft = 0f, cooldownTotal = 0f,
                });
            }
            int physLv = 0;
            foreach (var d in SkyPrisonAfpUpgrades.All) if (d.key == "physAtk") physLv = SkyPrisonAfpUpgrades.Level(d);
            int pNext = SkyPrisonAfpPhysicalMastery.LevelsToNextTier(physLv);
            int cycleTier = SkyPrisonAfpPhysicalMastery.CycleTier;
            if (cycleTier > 0)
            {
                int ci = cycleTier - 1, cn = Mathf.Min(ci + 1, 3);
                list.Add(new Entry
                {
                    id = "cycle",
                    name = Loc("ui_afp_ab_cycle", "回转") + " " + SkyPrisonAfpAbsorbShield.TierName(cycleTier),
                    effect = string.Format(Loc("ui_afp_ab_cycle_state_fmt", "击杀 +{0}% LP"), Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.KillLpFraction[ci] * 100f)),
                    detail = string.Format(Loc("charpanel_ability_cycle_desc_v6", "仅限剑。击杀敌人时，恢复最大 LP 的 {0}%。击杀生命上限 300 以上的目标，恢复量 ×3。破坏物不触发。"),
                        Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.KillLpFraction[ci] * 100f)),
                    source = Loc("charpanel_ability_phys_source_v5", "来源：适应强化「物理攻击强化」每 5 级 + 科技树节点"),
                    nextHint = pNext < 0 ? Loc("charpanel_ability_maxtier", "已达最高档")
                        : string.Format(Loc("charpanel_ability_cycle_next_v5", "再 {0} 级升至 {1}：击杀回复 {2}% LP"), pNext, SkyPrisonAfpAbsorbShield.TierName(cycleTier + 1),
                            Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.KillLpFraction[cn] * 100f)),
                    icon = SkyPrisonAfpIcons.Cycle, tier = cycleTier, maxTier = SkyPrisonAfpPhysicalMastery.MaxTier,
                    cooling = false, cooldownLeft = 0f, cooldownTotal = 0f,
                });
            }
            int lethalTier = SkyPrisonAfpPhysicalMastery.LethalTier;
            if (lethalTier > 0)
            {
                int li = lethalTier - 1, ln = Mathf.Min(li + 1, 3);
                list.Add(new Entry
                {
                    id = "lethal",
                    name = Loc("ui_afp_ab_lethal", "致命子弹") + " " + SkyPrisonAfpAbsorbShield.TierName(lethalTier),
                    effect = string.Format(Loc("ui_afp_ab_lethal_state_fmt_v2", "每 {0} 发 +{1}%"), SkyPrisonAfpPhysicalMastery.DeadlyEvery, Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.DeadlyBonus[li] * 100f)),
                    detail = string.Format(Loc("charpanel_ability_lethal_desc_v6", "仅限双枪。每第 {0} 发命中的子弹，伤害 +{1}%。"),
                        SkyPrisonAfpPhysicalMastery.DeadlyEvery, Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.DeadlyBonus[li] * 100f)),
                    source = Loc("charpanel_ability_phys_source_v5", "来源：适应强化「物理攻击强化」每 5 级 + 科技树节点"),
                    nextHint = pNext < 0 ? Loc("charpanel_ability_maxtier", "已达最高档")
                        : string.Format(Loc("charpanel_ability_lethal_next_v6", "再 {0} 级升至 {1}：每第 {2} 发 +{3}%"), pNext, SkyPrisonAfpAbsorbShield.TierName(lethalTier + 1),
                            SkyPrisonAfpPhysicalMastery.DeadlyEvery, Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.DeadlyBonus[ln] * 100f)),
                    icon = SkyPrisonAfpIcons.Bullet, tier = lethalTier, maxTier = SkyPrisonAfpPhysicalMastery.MaxTier,
                    cooling = false, cooldownLeft = 0f, cooldownTotal = 0f,
                });
            }
            if (SkyPrisonAfpWeakCrit.WeakLevel >= SkyPrisonAfpWeakCrit.MaxLevel && SkyPrisonAfpAbsorbShield.NodeUnlocked(SkyPrisonAfpWeakCrit.WeakNodeId))
            {
                list.Add(new Entry
                {
                    id = "armorbreak",
                    name = Loc("ui_afp_ab_armorbreak", "破甲冲击"),
                    effect = Loc("ui_afp_ab_armorbreak_short", "弱点暴击 失衡×3 · 无视霸体"),
                    detail = Loc("charpanel_ability_armorbreak_desc", "命中目标弱点并触发暴击时，失衡累积 ×3。失衡累积满时，无视霸体打断目标的冲刺、扑击或蓄力。"),
                    source = Loc("charpanel_ability_armorbreak_source", "来源：适应强化「弱点特效」3 级 + 科技树节点"),
                    nextHint = Loc("charpanel_ability_maxtier", "已达最高档"),
                    icon = SkyPrisonAfpIcons.Bolt, tier = 1, maxTier = 1,
                    cooling = false, cooldownLeft = 0f, cooldownTotal = 0f,
                });
            }
            if (SkyPrisonAfpWeakCrit.CritLevel >= SkyPrisonAfpWeakCrit.MaxLevel && SkyPrisonAfpAbsorbShield.NodeUnlocked(SkyPrisonAfpWeakCrit.CritNodeId))
            {
                list.Add(new Entry
                {
                    id = "critrecover",
                    name = Loc("ui_afp_ab_critrecover", "会心回收"),
                    effect = Loc("ui_afp_ab_critrecover_short", "枪暴击 40% 退弹 · 剑暴击回 4% LP"),
                    detail = Loc("charpanel_ability_critrecover_desc", "枪械暴击时，有 40% 概率不消耗这发弹药。剑暴击时，恢复最大 LP 的 4%。"),
                    source = Loc("charpanel_ability_critrecover_source", "来源：适应强化「暴击特效」3 级 + 科技树节点"),
                    nextHint = Loc("charpanel_ability_maxtier", "已达最高档"),
                    icon = SkyPrisonAfpIcons.Bullet, tier = 1, maxTier = 1,
                    cooling = false, cooldownLeft = 0f, cooldownTotal = 0f,
                });
            }
            if (SkyPrisonAfpUsurper.Active)
            {
                int ul = SkyPrisonAfpUsurper.EffectiveLevel;
                int pool = Mathf.RoundToInt(SkyPrisonAfpUsurper.PoolRatioAt(ul) * 100f), melee = Mathf.RoundToInt(SkyPrisonAfpUsurper.MeleeAt(ul) * 100f), ranged = Mathf.RoundToInt(SkyPrisonAfpUsurper.RangedAt(ul) * 100f);
                bool uMax = ul >= SkyPrisonAfpUsurper.MaxLevel;
                list.Add(new Entry
                {
                    id = "usurp",
                    name = Loc("ui_afp_ab_usurp", "篡夺者") + " " + ToRoman(ul),
                    effect = string.Format(Loc("ui_afp_ab_usurp_state_fmt", "近战 {0}% · 远程 {1}%"), melee, ranged),
                    detail = string.Format(Loc("charpanel_ability_usurp_desc_v5", "受到的伤害，有 {0}% 转为血条上的红色部分，随时间消退。攻击敌人时，造成伤害的一部分转化为生命回复，只能回复红色部分：近战 {1}%，远程 {2}%。"), pool, melee, ranged),
                    source = Loc("charpanel_ability_usurp_source_v6", "来源：适应强化「篡夺者」(需追加生命、物理攻击强化各 10 级) + 科技树节点"),
                    nextHint = uMax ? Loc("charpanel_ability_maxtier", "已达最高档")
                        : string.Format(Loc("charpanel_ability_usurp_next_v6", "下一级：近战 {0}% · 远程 {1}%"), Mathf.RoundToInt(SkyPrisonAfpUsurper.MeleeAt(ul + 1) * 100f), Mathf.RoundToInt(SkyPrisonAfpUsurper.RangedAt(ul + 1) * 100f)),
                    icon = SkyPrisonAfpIcons.Drop, tier = ul, maxTier = SkyPrisonAfpUsurper.MaxLevel,
                    cooling = false, cooldownLeft = 0f, cooldownTotal = 0f,
                });
            }
            var ins = SkyPrisonAfpLifeInsurance.FindOnPlayer();
            if (ins != null && ins.Active)
            {
                list.Add(new Entry
                {
                    id = "insurance",
                    name = Loc("ui_afp_ab_insurance", "生命保险"),
                    effect = Loc("ui_afp_ab_exempt", "死亡豁免"),
                    detail = Loc("ui_afp_ab_exempt_desc_v5", "受到致命伤害时，豁免一次死亡，生命保留 1 点，随后进入冷却。冷却期间再受致命伤害，将正常死亡。"),
                    source = Loc("charpanel_ability_ins_source_v5", "来源：适应强化「追加生命」5 级 + 科技树节点"),
                    nextHint = "",
                    icon = SkyPrisonAfpIcons.Insurance, tier = 1, maxTier = 1,
                    cooling = ins.CooldownLeft > 0f, cooldownLeft = ins.CooldownLeft, cooldownTotal = SkyPrisonAfpLifeInsurance.CooldownSeconds,
                });
            }
        }
        _entries.Clear(); _entries.AddRange(list);
        if (_selected >= _entries.Count) _selected = Mathf.Max(0, _entries.Count - 1);
    }

    // ── 构建 ─────────────────────────────────────────────────
    void Rebuild()
    {
        for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
        _rowBgs.Clear(); _rowTimers.Clear(); _rowTimerIndex.Clear(); _detailTimer = null;

        _width = _root.rect.width; _height = _root.rect.height;
        if (_width < 10f) { Canvas.ForceUpdateCanvases(); _width = _root.rect.width; _height = _root.rect.height; }
        float s = _scale;
        float leftW = _width * 0.36f, gap = 50f * s, rightX = leftW + gap;

        BuildHeader(_root, 0f, leftW, string.Format("{0} · {1}", Loc("charpanel_group_abilities_v2", "适应能力"), _entries.Count));
        // 左右两栏之间一条竖线：空的时候也看得出是「左边能力槽 / 右边详情」两块
        var divider = Rect("Divider", _root, new Vector2(0f, 0f), new Vector2(0f, 1f));
        divider.pivot = new Vector2(0f, 0.5f); divider.anchoredPosition = new Vector2(leftW + gap * 0.5f, 0f); divider.sizeDelta = new Vector2(1.4f, 0f);
        var divImg = divider.gameObject.AddComponent<Image>(); divImg.color = new Color(1f, 1f, 1f, 0.12f); divImg.raycastTarget = false;

        // 左栏：已获得的能力一行一个槽，后面永远只跟一个空槽(NOT ADAPTED)；每多获得一个，就多出一个槽位。全部拿到就不再留空槽
        float rowH = 74f * s;
        float y = 44f * s + 10f * s;
        int slotCount = _entries.Count + (_entries.Count < SkyPrisonAfpAbilityTable.All.Length ? 1 : 0);
        for (int i = 0; i < slotCount; i++)
        {
            if (i < _entries.Count) BuildRow(_root, i, y, leftW, rowH);
            else BuildEmptySlot(_root, i, y, leftW, rowH);
            y += rowH + RowGap * s;
        }

        // 右栏：有能力画详情；一个都没有时只留标题和一句简短提示，不画槽
        if (_entries.Count == 0) BuildEmptyDetail(_root, rightX, _width - rightX);
        else BuildDetail(_root, _entries[Mathf.Clamp(_selected, 0, _entries.Count - 1)], rightX, _width - rightX);

        BuildUpgradeList(_root, rightX, _width - rightX);
    }

    void BuildRow(RectTransform parent, int index, float y, float w, float h)
    {
        var e = _entries[index];
        var row = Rect("Row" + index, parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
        row.pivot = new Vector2(0f, 1f); row.anchoredPosition = new Vector2(0f, -y); row.sizeDelta = new Vector2(w, h);
        var bg = row.gameObject.AddComponent<Image>();
        AddSlotFrame(row);
        bool sel = index == _selected;
        bg.color = sel ? new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.16f) : new Color(1f, 1f, 1f, 0.04f);
        _rowBgs.Add(bg);
        if (sel)
        {
            var bar = Rect("Sel", row, new Vector2(0f, 0f), new Vector2(0f, 1f));
            bar.pivot = new Vector2(0f, 0.5f); bar.sizeDelta = new Vector2(5f * _scale, 0f);
            var bi = bar.gameObject.AddComponent<Image>(); bi.color = ColdGreen; bi.raycastTarget = false;
        }
        int captured = index;
        var trig = row.gameObject.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; enter.callback.AddListener(_ => Select(captured)); trig.triggers.Add(enter);
        var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick }; click.callback.AddListener(_ => Select(captured)); trig.triggers.Add(click);

        float icon = h * 0.7f;
        var iconRt = Rect("Icon", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        iconRt.pivot = new Vector2(0f, 0.5f); iconRt.sizeDelta = new Vector2(icon, icon); iconRt.anchoredPosition = new Vector2(16f * _scale, 0f);
        var img = iconRt.gameObject.AddComponent<Image>(); img.sprite = SkyPrisonAfpIcons.GetSprite(e.icon); img.raycastTarget = false;
        img.color = e.cooling ? new Color(1f, 1f, 1f, 0.35f) : Accent(e);

        var name = Txt(row, "Name", e.name, SkyPrisonFloatingWindowKit.PrimaryFontSize * 1.02f, FontStyles.Normal, new Color(1f, 1f, 1f, e.cooling ? 0.5f : 1f), TextAlignmentOptions.MidlineLeft);
        Stretch(name.rectTransform, 16f * _scale + icon + 12f * _scale, 0f, w * 0.34f, 0f);
        name.enableWordWrapping = false; name.overflowMode = TextOverflowModes.Ellipsis;

        var st = Txt(row, "State", e.cooling ? FormatTimer(e.cooldownLeft) : e.effect, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.68f, FontStyles.Normal,
            e.cooling ? new Color(1f, 1f, 1f, 0.6f) : new Color(Accent(e).r, Accent(e).g, Accent(e).b, 0.95f), TextAlignmentOptions.MidlineRight);
        Stretch(st.rectTransform, w * 0.66f, 0f, 14f * _scale, 0f);
        st.enableWordWrapping = false;
        if (e.cooling) { _rowTimers.Add(st); _rowTimerIndex.Add(index); }
    }

    // 跟角色面板武器 / 装备槽同一种框：四个角各一个小方块，左右各一条细竖线
    void AddSlotFrame(RectTransform row)
    {
        float cs = 6f * _scale;
        var cc = new Color(1f, 1f, 1f, 0.4f);
        Vector2[] anchors = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        for (int i = 0; i < 4; i++)
        {
            var c = Rect("Corner", row, anchors[i], anchors[i]);
            c.pivot = anchors[i]; c.sizeDelta = new Vector2(cs, cs);
            var ci = c.gameObject.AddComponent<Image>(); ci.color = cc; ci.raycastTarget = false;
        }
        for (int side = 0; side < 2; side++)
        {
            var l = Rect(side == 0 ? "EdgeL" : "EdgeR", row, new Vector2(side, 0f), new Vector2(side, 1f));
            l.pivot = new Vector2(side, 0.5f); l.sizeDelta = new Vector2(0.9f * _scale, 0f);
            var li = l.gameObject.AddComponent<Image>(); li.color = new Color(1f, 1f, 1f, 0.18f); li.raycastTarget = false;
        }
    }

    // 空槽：小字「适应能力 N」+ 大号占位字 NOT ADAPTED（固定英文，不跟语言走，和 NOT EQUIPPED 同款）
    void BuildEmptySlot(RectTransform parent, int index, float y, float w, float h)
    {
        var row = Rect("Slot" + index, parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
        row.pivot = new Vector2(0f, 1f); row.anchoredPosition = new Vector2(0f, -y); row.sizeDelta = new Vector2(w, h);
        var bg = row.gameObject.AddComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.03f); bg.raycastTarget = false;
        AddSlotFrame(row);

        var label = Txt(row, "SlotLabel", string.Format(Loc("charpanel_ability_slot_fmt", "适应能力 {0}"), index + 1),
            SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.5f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.TopLeft);
        Place(label.rectTransform, 22f * _scale, -(6f * _scale), w - 40f * _scale, h * 0.4f);

        var big = Txt(row, "SlotEmpty", "NOT ADAPTED", SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.95f, FontStyles.Bold, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.TopLeft);
        big.font = _phFont; big.enableWordWrapping = false;
        Place(big.rectTransform, 22f * _scale, -(h * 0.34f), w - 40f * _scale, h * 0.66f);
    }

    // 右栏空态：只有标题和一句简短提示，不画槽
    void BuildEmptyDetail(RectTransform parent, float x, float w)
    {
        float s = _scale;
        BuildHeader(parent, x, w, Loc("charpanel_ability_detail", "详情"));
        var hint = Txt(parent, "Hint", Loc("charpanel_ability_empty_v3", "尚未获得适应能力"),
            SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.66f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.TopLeft);
        hint.enableWordWrapping = false;
        Place(hint.rectTransform, x, -(60f * s), w, 50f * s);
    }

    void Select(int index)
    {
        if (index == _selected || index < 0 || index >= _entries.Count) return;
        _selected = index; _signature = null; _nextPoll = 0f;
    }

    void BuildDetail(RectTransform parent, Entry e, float x, float w)
    {
        float s = _scale;
        BuildHeader(parent, x, w, Loc("charpanel_ability_detail", "详情"));
        float y = 44f * s + 14f * s;

        float icon = 84f * s;
        var iconRt = Rect("DetailIcon", parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
        iconRt.pivot = new Vector2(0f, 1f); iconRt.sizeDelta = new Vector2(icon, icon); iconRt.anchoredPosition = new Vector2(x, -y);
        var img = iconRt.gameObject.AddComponent<Image>(); img.sprite = SkyPrisonAfpIcons.GetSprite(e.icon); img.raycastTarget = false;
        img.color = e.cooling ? new Color(1f, 1f, 1f, 0.4f) : Accent(e);

        var title = Txt(parent, "DetailName", e.name, SkyPrisonFloatingWindowKit.PrimaryFontSize * 1.2f, FontStyles.Bold, Color.white, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, x + icon + 18f * s, -y, w - icon - 18f * s, icon * 0.55f);

        string stateText = e.cooling ? string.Format(Loc("charpanel_ability_recharging_fmt", "恢复中 {0}"), FormatTimer(e.cooldownLeft)) : Loc("charpanel_ability_state_ready", "就绪");
        _detailTimer = Txt(parent, "DetailState", stateText, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.78f, FontStyles.Bold,
            e.cooling ? new Color(1f, 1f, 1f, 0.6f) : Accent(e), TextAlignmentOptions.TopLeft);
        Place(_detailTimer.rectTransform, x + icon + 18f * s, -(y + icon * 0.55f), w - icon - 18f * s, icon * 0.45f);
        if (!e.cooling) _detailTimer = null;
        y += icon + 22f * s;

        var desc = Txt(parent, "Desc", e.detail, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.74f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        desc.enableWordWrapping = true;
        // 说明文字按实际行数算高度(不再固定留一大块)，下面的内容才不会和底部「适应强化」撞在一起
        float descH = Mathf.Max(40f * s, desc.GetPreferredValues(e.detail, w, 0f).y + 4f * s);
        Place(desc.rectTransform, x, -y, w, descH);
        y += descH + 14f * s;

        // 档位阶梯：已达成的各档(冷绿实心点)，加上下一档(暗色空心点)；下一档的科技树节点没点亮就不显示
        string ladder = BuildLadder(e);
        if (!string.IsNullOrEmpty(ladder))
        {
            int lines = ladder.Split('\n').Length;
            var lt = Txt(parent, "Ladder", ladder, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.66f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.TopLeft);
            lt.enableWordWrapping = true;
            Place(lt.rectTransform, x, -y, w, lines * 50f * s);
            y += lines * 48f * s + 4f * s;
        }

        // 数据行：恢复/冷却时间、档位、下一档
        if (e.cooldownTotal > 0f) y = Line(parent, x, w, y, string.Format(Loc("charpanel_ability_cd_fmt", "恢复时间：{0} 秒"), Mathf.RoundToInt(e.cooldownTotal)));
        if (e.maxTier > 1) y = Line(parent, x, w, y, string.Format(Loc("charpanel_ability_tier_fmt", "当前档位：{0} / {1}"), e.tier, e.maxTier));
        if (!string.IsNullOrEmpty(e.nextHint)) y = Line(parent, x, w, y, e.nextHint, new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.9f));
        Line(parent, x, w, y, e.source, new Color(1f, 1f, 1f, 0.5f));
    }

    static string ToRoman(int n)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return r[Mathf.Clamp(n, 0, 10)];
    }

    static string BuildLadder(Entry e)
    {
        var def = SkyPrisonAfpAbilityTable.Find(e.id);
        if (def == null || !def.MultiTier) return "";
        var sb = new System.Text.StringBuilder();
        int last = Mathf.Min(e.tier + 1, def.maxTier);
        for (int t = 1; t <= last; t++)
        {
            bool done = t <= e.tier;
            if (!done && !SkyPrisonAfpAbilityTable.TierUnlocked(def, t)) break;
            if (sb.Length > 0) sb.Append('\n');
            string label = SkyPrisonAfpAbilityTable.TierName(t) + "  " + def.shortEffect(t);
            if (done) sb.Append("<color=#7CFFC0FF>●</color> ").Append(label);
            else sb.Append("<color=#FFFFFF66>○ ").Append(label).Append("</color>");
        }
        return sb.ToString();
    }

    float Line(RectTransform parent, float x, float w, float y, string text, Color? color = null)
    {
        float s = _scale;
        var t = Txt(parent, "Line", text, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.68f, FontStyles.Normal, color ?? new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.TopLeft);
        t.enableWordWrapping = true;
        Place(t.rectTransform, x, -y, w, 48f * s);
        return y + 46f * s;
    }

    // AFP 强化已点的项目(等级>0)：名字 + 等级 + 加成，右下角一块
    void BuildUpgradeList(RectTransform parent, float x, float w)
    {
        float s = _scale;
        var rows = new List<string>();
        foreach (var d in SkyPrisonAfpUpgrades.All)
        {
            int lv = SkyPrisonAfpUpgrades.Level(d);
            if (lv <= 0) continue;
            string unit = d.unit == "{r}" ? Loc("ui_afp_unit_rounds", " 发") : d.unit;
            rows.Add(string.Format("{0}   Lv.{1}   +{2}{3}", Loc(d.labelKey, d.labelFallback), lv, (lv * d.perLevel).ToString("0.#"), unit));
        }
        float lineH = 42f * s;
        int cols = rows.Count > 5 ? 2 : 1;                                   // 项目多时分两列，别和上面的详情撞在一起
        int perCol = Mathf.Max(1, Mathf.CeilToInt(rows.Count / (float)cols));
        float blockH = 44f * s + 10f * s + perCol * lineH;
        var host = Rect("Upgrades", parent, new Vector2(0f, 0f), new Vector2(0f, 0f));
        host.pivot = new Vector2(0f, 0f); host.anchoredPosition = new Vector2(x, 0f); host.sizeDelta = new Vector2(w, blockH);
        BuildHeader(host, 0f, w, Loc("charpanel_upgrades_title_v2", "适应强化"), true);
        if (rows.Count == 0)
        {
            var t = Txt(host, "None", Loc("charpanel_upgrades_none_v3", "尚未进行适应强化"), SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.62f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.TopLeft);
            Place(t.rectTransform, 0f, -(52f * s), w, lineH);
            return;
        }
        float colW = w / cols;
        for (int i = 0; i < rows.Count; i++)
        {
            int col = i / perCol, row = i % perCol;
            var t = Txt(host, "U" + i, rows[i], SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.62f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.TopLeft);
            t.enableWordWrapping = false;
            Place(t.rectTransform, col * colW, -(52f * s + row * lineH), colW - 10f * s, lineH);
        }
    }

    void BuildHeader(RectTransform parent, float x, float w, string label, bool fromTop = false)
    {
        float s = _scale;
        var header = Rect("Header", parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
        header.pivot = new Vector2(0f, 1f); header.anchoredPosition = new Vector2(x, 0f); header.sizeDelta = new Vector2(w, 44f * s);
        var t = Txt(header, "Label", label, SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.7f, FontStyles.Bold, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.MidlineLeft);
        Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
        var line = Rect("Line", header, new Vector2(0f, 0f), new Vector2(1f, 0f));
        line.pivot = new Vector2(0f, 0f); line.sizeDelta = new Vector2(0f, 1.4f);
        var li = line.gameObject.AddComponent<Image>(); li.color = new Color(1f, 1f, 1f, 0.25f); li.raycastTarget = false;
        if (fromTop) { header.anchorMin = header.anchorMax = new Vector2(0f, 1f); header.anchoredPosition = new Vector2(x, 0f); }
    }

    // ── 小工具 ───────────────────────────────────────────────
    TMP_Text Txt(Transform parent, string name, string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var t = SkyPrisonFloatingWindowKit.MakeText(parent, name, text, size * FontScale, style, _font);
        t.alignment = align; t.color = color; t.raycastTarget = false;
        return t;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
    }
}
