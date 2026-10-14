using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AFP 购买区 / 「ADAPTATION SYSTEM」购买窗口里的文字，补进多语言字典表（UILocalizationTable）：中 / 日 / 英。
/// 只补缺的 key、只填空着的语言，已经有内容的不会覆盖。装饰性英文标题(ADAPTATION SYSTEM / POWERED BY KYOGOKU)不进字典。
/// 编译后自动跑一次；也可以手动点菜单「天空囚笼/本地化/补全AFP购买窗口文字」。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonAfpLocalizationSeeder
{
    const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
    static readonly string[] Codes = { "zh-CN", "ja", "en" };

    // key, 中, 日, 英
    static readonly string[][] Rows =
    {
        new[] { "ui_afp_shop_sub",    "适应性调整仅在本次部署内有效。每完成一级强化，后续所有项目的 AFP 消耗同步上调。离开作业区或检测到交战时，系统自动断开。",
                                      "適応調整は今回の展開中のみ有効。強化を1段階行うごとに、以降の全項目のAFP消費が連動して上昇する。作業区域を離れるか交戦を検知すると、システムは自動的に切断される。",
                                      "Adaptive adjustments last for this deployment only. Each level purchased raises the AFP cost of all later items. The system disconnects automatically when you leave the work zone or combat is detected." },
        new[] { "ui_afp_hint_select", "选择",             "選択",               "Select" },
        new[] { "ui_afp_hint_adjust", "预加点 A−/D+",     "事前配分 A−/D+",     "Allocate A−/D+" },
        new[] { "ui_afp_hint_confirm","完成部署",         "展開完了",           "Complete Deployment" },
        new[] { "ui_afp_hint_close",  "关闭",             "閉じる",             "Close" },
        new[] { "training_title", "战术研修", "戦術研修", "Field Training" },
        new[] { "training_points", "研修点", "研修ポイント", "Training Points" },
        new[] { "training_tab_seeker", "探索者", "探索者", "Seeker" },
        new[] { "training_tab_strategist", "战略家", "戦略家", "Strategist" },
        new[] { "training_hint_select", "选择", "選択", "Select" },
        new[] { "training_hint_tab", "切换课程", "コース切替", "Switch Course" },
        new[] { "ui_afp_up_drone_count", "浮游炮数量", "ドローン砲の数", "Drone Count" },
        new[] { "ui_afp_up_drone_mag", "浮游炮弹仓", "ドローン砲の装弾数", "Drone Magazine" },
        new[] { "ui_afp_up_drone_cd", "浮游炮冷却缩短", "ドローン砲のクールダウン短縮", "Drone Cooldown Cut" },
        new[] { "training_hint_zoom", "缩放", "ズーム", "Zoom" },
        new[] { "training_hint_learn", "按住研修", "長押しで研修", "Hold to Train" },
        new[] { "training_hold", "按住研修", "長押しで研修", "Hold to Train" },
        new[] { "training_prereq", "前置条件", "前提条件", "Prerequisites" },
        new[] { "training_done", "研修完成", "研修完了", "Training Complete" },
        new[] { "training_reason_night", "夜间模式尚未出现", "夜間モードがまだ出現していない", "Night mode has not appeared yet" },
        new[] { "training_reason_condition", "条件未满足", "条件を満たしていない", "Requirements not met" },
        new[] { "training_reason_maxed", "已达最高等级", "最大レベルに到達済み", "Already at max level" },
        new[] { "training_reason_need_unlock_fmt", "需先解锁「{0}」", "先に「{0}」を解放する必要がある", "Requires \"{0}\" first" },
        new[] { "training_reason_need_level_fmt", "需「{0}」达到 LV{1}（当前 LV{2}）", "「{0}」がLV{1}に達する必要がある（現在LV{2}）", "Requires \"{0}\" LV{1} (currently LV{2})" },
        new[] { "training_reason_points_fmt", "研修点不足（需要 {0}，剩余 {1}）", "研修ポイント不足（必要 {0}、残り {1}）", "Not enough Training Points (need {0}, have {1})" },
        new[] { "training_cost_fmt", "下一级需要 {0} 研修点", "次のレベルに研修ポイント{0}必要", "Next level costs {0} Training Points" },
        new[] { "ui_afp_confirm",     "完成部署",         "展開完了",           "Complete Deployment" },
        new[] { "ui_afp_clear",       "清除",             "クリア",             "Clear" },
        new[] { "ui_afp_effect_fmt",  "每级 +{0}%　当前 {1}", "1段階 +{0}%　現在 {1}", "Per Level +{0}%　Current {1}" },
        new[] { "ui_afp_total_fmt",   "待强化 {0} 级　合计 {1} AFP", "強化待ち {0} 段階　合計 {1} AFP", "Pending {0} Levels　Total {1} AFP" },
        new[] { "ui_afp_max",         "MAX",              "MAX",                "MAX" },
        new[] { "ui_afp_next_fmt",    "下一级 +{0}%",     "次のレベル +{0}%",   "Next Level +{0}%" },
        new[] { "ui_afp_summary",     "本次提升",         "今回の上昇",         "This Upgrade" },
        new[] { "ui_afp_cost_fmt",    "花费 {0} AFP",     "消費 {0} AFP",       "Cost {0} AFP" },
        new[] { "ui_afp_dmg_heat",     "灼热", "灼熱", "Heat" },
        new[] { "ui_afp_dmg_shock",    "电磁", "電磁", "Shock" },
        new[] { "ui_afp_ab_field_fmt", "特效力场：{0}", "特効フィールド：{0}", "Effect Field: {0}" },
        new[] { "ui_afp_ab_shield_eff_fmt", "吸收 {0} 点{1}伤害", "{1}ダメージを{0}吸収", "Absorbs {0} {1} damage" },
        new[] { "ui_afp_ab_insurance", "生命保险", "ライフ保険", "Life Insurance" },
        new[] { "ui_afp_ab_insurance_eff", "致命一击锁 1 点血", "致命傷をHP1で耐える", "Survive a lethal hit at 1 HP" },
        new[] { "ui_afp_ab_need_fmt",   "再 {0} 级解锁", "あと{0}段階で解放", "{0} more levels to unlock" },
        new[] { "ui_afp_ab_need_tech", "需科技树解锁", "テックツリーで解放", "Unlock in Tech Tree" },
        new[] { "status_afp_insurance_ready",      "生命保险", "ライフ保険", "Life Insurance" },
        new[] { "status_afp_insurance_ready_desc", "受到致命伤害时锁在 1 点血，随后进入冷却。", "致命傷を受けるとHP1で耐え、その後クールダウンに入る。", "A lethal hit leaves you at 1 HP, then the insurance goes on cooldown." },
        new[] { "status_afp_insurance_cd",         "生命保险·冷却", "ライフ保険·クールダウン", "Life Insurance · Cooldown" },
        new[] { "status_afp_insurance_cd_desc",    "生命保险冷却中，结束后恢复可用。", "ライフ保険はクールダウン中。終了後に再び使用可能。", "Life Insurance is cooling down and becomes available again afterwards." },
        new[] { "status_afp_field_cd_suffix", "·恢复中", "·回復中", " · Recharging" },
        new[] { "status_afp_field_desc",      "吸收对应属性的伤害；档位(层数)越高吸收越多。", "対応属性のダメージを吸収する。段階(スタック数)が高いほど吸収量が増える。", "Absorbs damage of its element; higher tiers absorb more." },
        new[] { "status_afp_field_cd_desc",   "力场被打空，恢复中，结束后重新生效。", "フィールドが破られた。回復後に再び有効になる。", "The field was broken and is recharging." },
        new[] { "charpanel_group_abilities",  "战局能力", "戦局アビリティ", "Raid Abilities" },
        new[] { "charpanel_ability_absorb_fmt", "吸收 {0}", "吸収 {0}", "Absorb {0}" },
        new[] { "charpanel_ability_ready",    "就绪", "待機中", "Ready" },
        new[] { "ui_afp_ab_field_eff_fmt",    "豁免一次{0}异常 · {1}秒恢复", "{0}異常を1回無効化 · {1}秒で回復", "Negates one {0} anomaly · {1}s recharge" },
        new[] { "status_afp_field_desc_v2",   "豁免一次对应属性的异常赋予，随后进入恢复；档位(层数)越高恢复越快，但有下限。", "対応属性の異常付与を1回無効化し、その後回復に入る。段階が高いほど回復が速いが下限がある。", "Negates one anomaly of its element, then recharges; higher tiers recharge faster, down to a floor." },
        new[] { "status_afp_field_cd_desc_v2", "力场已豁免一次异常，恢复中，结束后重新生效。", "フィールドが異常を無効化した。回復後に再び有効になる。", "The field negated an anomaly and is recharging." },
        new[] { "charpanel_ability_exempt",    "豁免 1 次", "1回無効化", "Negate x1" },
        new[] { "ui_afp_up_magazine",  "扩容弹匣", "マガジン拡張", "Extended Magazine" },
        new[] { "ui_afp_up_lp",        "负荷耐受", "負荷耐性", "Load Tolerance" },
        new[] { "ui_afp_up_weak",      "弱点特效", "弱点特効", "Weakness Exploit" },
        new[] { "ui_afp_up_crit",      "暴击特效", "会心特効", "Critical Boost" },
        new[] { "ui_afp_ab_armorbreak",       "破甲冲击", "破甲衝撃", "Armor Break" },
        new[] { "ui_afp_ab_armorbreak_short", "弱点暴击 失衡×3 · 无视霸体", "弱点会心 崩し×3・スーパーアーマー無視", "Weak-point crits: stagger x3, ignore super armor" },
        new[] { "ui_afp_ab_critrecover",      "会心回收", "会心回収", "Critical Recovery" },
        new[] { "ui_afp_ab_critrecover_short", "枪暴击 40% 退弹 · 剑暴击回 4% LP", "銃会心 40%で弾薬返還・剣会心 LP4%回復", "Gun crits: 40% ammo refund / Sword crits: +4% LP" },
        new[] { "charpanel_ability_armorbreak_desc", "命中目标弱点并触发暴击时，失衡累积 ×3。失衡累积满时，无视霸体打断目标的冲刺、扑击或蓄力。", "対象の弱点に命中し会心が発生した時、崩し蓄積が×3になる。崩しが満タンになると、スーパーアーマーを無視して対象の突進・飛びかかり・溜めを中断させる。", "Critical hits on a target's weakness build stagger x3. When the stagger gauge fills, the target's charge, pounce or wind-up is interrupted even through super armor." },
        new[] { "charpanel_ability_armorbreak_source", "来源：适应强化「弱点特效」3 级 + 科技树节点", "入手：適応強化「弱点特効」Lv3 + 研修ノード", "Source: Adaptation \"Weakness Exploit\" Lv3 + tech node" },
        new[] { "charpanel_ability_critrecover_desc", "枪械暴击时，有 40% 概率不消耗这发弹药。剑暴击时，恢复最大 LP 的 4%。", "銃器の会心時、40%の確率でその弾薬を消費しない。剣の会心時、最大LPの4%を回復する。", "Firearm crits have a 40% chance not to consume the round. Sword crits restore 4% of max LP." },
        new[] { "charpanel_ability_critrecover_source", "来源：适应强化「暴击特效」3 级 + 科技树节点", "入手：適応強化「会心特効」Lv3 + 研修ノード", "Source: Adaptation \"Critical Boost\" Lv3 + tech node" },
        new[] { "charpanel_ability_cycle_desc_v6", "仅限剑。击杀敌人时，恢复最大 LP 的 {0}%。击杀生命上限 300 以上的目标，恢复量 ×3。破坏物不触发。", "剣専用。敵を撃破すると、最大LPの{0}%を回復する。最大HP300以上の対象を撃破した場合、回復量は×3。破壊物は対象外。", "Sword only. Killing an enemy restores {0}% of max LP. Targets with 300 or more max HP restore x3. Destructibles do not count." },
        new[] { "ui_afp_unit_rounds",  " 发", " 発", " rounds" },
        new[] { "charpanel_page_stats",       "属性", "ステータス", "Stats" },
        new[] { "charpanel_page_abilities",   "战局能力", "戦局アビリティ", "Raid Abilities" },
        new[] { "charpanel_hint_prev_page",   "上一页", "前のページ", "Prev Page" },
        new[] { "charpanel_hint_next_page",   "下一页", "次のページ", "Next Page" },
        new[] { "charpanel_ability_detail",   "详情", "詳細", "Details" },
        new[] { "charpanel_ability_state_ready", "就绪", "待機中", "Ready" },
        new[] { "charpanel_ability_recharging_fmt", "恢复中 {0}", "回復中 {0}", "Recharging {0}" },
        new[] { "charpanel_ability_maxtier",  "已达最高档", "最高段階に到達", "Max tier reached" },
        new[] { "charpanel_ability_next_fmt", "再 {0} 级升至 {1}：恢复 {2} 秒", "あと{0}段階で{1}へ：回復{2}秒", "{0} more levels to {1}: {2}s recharge" },
        new[] { "charpanel_ability_field_desc", "这个属性的异常累计满值、即将赋予{0}异常状态时，豁免这一次，随后进入恢复。档位越高恢复越快，但不会一直豁免。", "この属性の異常蓄積が満タンになり{0}異常が付与される直前に、1回だけ無効化して回復に入る。段階が高いほど回復は速いが、常時無効にはならない。", "When this element's anomaly gauge fills and a {0} anomaly is about to be applied, negate it once, then recharge. Higher tiers recharge faster, but never permanently." },
        new[] { "charpanel_ability_field_source", "来源：AFP 强化「{0}抗性」每 5 级一档 + 科技树节点", "入手元：AFP強化「{0}耐性」5段階ごとに1段 + テックツリー", "Source: AFP upgrade \"{0} Resistance\" (tier every 5 levels) + Tech Tree node" },
        new[] { "ui_afp_ab_insurance_desc", "受到会致命的伤害时，改为锁在 1 点血，随后进入冷却。冷却结束前再受致命伤害会正常死亡。", "致命的なダメージを受けるとHP1で耐え、クールダウンに入る。クールダウン中に再び致命傷を受けると通常通り死亡する。", "A lethal hit leaves you at 1 HP, then the insurance cools down. A lethal hit during cooldown kills you normally." },
        new[] { "charpanel_ability_ins_source", "来源：AFP 强化「追加生命」5 级 + 科技树节点", "入手元：AFP強化「追加ライフ」5段階 + テックツリー", "Source: AFP upgrade \"Bonus Vitality\" level 5 + Tech Tree node" },
        new[] { "charpanel_ability_cd_fmt", "恢复时间：{0} 秒", "回復時間：{0}秒", "Recharge time: {0}s" },
        new[] { "charpanel_ability_tier_fmt", "当前档位：{0} / {1}", "現在の段階：{0} / {1}", "Current tier: {0} / {1}" },
        new[] { "charpanel_ability_empty", "本局还没有获得任何能力。在 AFP 购买区强化，并在探索者科技树里解锁对应能力后，会出现在这里。", "今回の戦局ではまだアビリティがありません。AFP購入エリアで強化し、探索者テックツリーで解放するとここに表示されます。", "No abilities yet this raid. Upgrade at an AFP zone and unlock the matching Seeker Tech Tree node to see them here." },
        new[] { "charpanel_upgrades_title", "AFP 强化", "AFP強化", "AFP Upgrades" },
        new[] { "charpanel_upgrades_none", "本局还没有强化过任何项目。", "今回の戦局ではまだ強化していません。", "Nothing upgraded this raid." },
        new[] { "ui_afp_up_phys",      "物理攻击强化", "物理攻撃強化", "Physical Damage" },
        new[] { "ui_afp_up_attr",      "属性攻击强化", "属性攻撃強化", "Elemental Damage" },
        new[] { "charpanel_page_abilities_v2", "适应系统", "適応システム", "Adaptation System" },
        new[] { "charpanel_group_abilities_v2", "适应能力", "適応アビリティ", "Adaptation Abilities" },
        new[] { "charpanel_upgrades_title_v2", "适应强化", "適応強化", "Adaptation Upgrades" },
        new[] { "charpanel_upgrades_none_v2", "本局还没有进行过适应强化。", "今回の戦局ではまだ適応強化を行っていません。", "No adaptation upgrades yet this raid." },
        new[] { "charpanel_ability_empty_v2", "本局还没有获得任何适应能力。在适应系统(AFP 购买区)里强化，并在探索者科技树里解锁对应能力后，会出现在这里。", "今回の戦局ではまだ適応アビリティがありません。適応システム(AFP購入エリア)で強化し、探索者テックツリーで解放するとここに表示されます。", "No adaptation abilities yet this raid. Upgrade in the Adaptation System (AFP zone) and unlock the matching Seeker Tech Tree node to see them here." },
        new[] { "ui_afp_ab_exempt",       "死亡豁免", "死亡免除", "Death Exemption" },
        new[] { "ui_afp_ab_exempt_eff",   "豁免一次致命伤害", "致命傷を1回免除", "Exempts one lethal hit" },
        new[] { "ui_afp_ab_exempt_desc",  "受到会致命的伤害时，豁免这一次死亡，血量保留 1 点，随后进入冷却。冷却结束前再受致命伤害会正常死亡。", "致命的なダメージを受けた時、その死亡を1回免除しHP1で耐え、クールダウンに入る。クールダウン中に再び致命傷を受けると通常通り死亡する。", "A lethal hit is exempted once, leaving you at 1 HP, then the ability goes on cooldown. A lethal hit during cooldown kills you normally." },
        new[] { "status_afp_exempt_ready",      "死亡豁免", "死亡免除", "Death Exemption" },
        new[] { "status_afp_exempt_ready_desc", "受到致命伤害时豁免这一次死亡，血量保留 1 点，随后进入冷却。", "致命傷を受けた時、その死亡を1回免除しHP1で耐え、クールダウンに入る。", "A lethal hit is exempted once, leaving you at 1 HP, then it cools down." },
        new[] { "status_afp_exempt_cd",        "死亡豁免·冷却", "死亡免除·クールダウン", "Death Exemption · Cooldown" },
        new[] { "status_afp_exempt_cd_desc",   "死亡豁免冷却中，结束后恢复可用。", "死亡免除はクールダウン中。終了後に再び使用可能。", "Death Exemption is cooling down and becomes available again afterwards." },
        new[] { "ui_afp_ab_burst",               "异常追伤", "異常追撃", "Anomaly Burst" },
        new[] { "ui_afp_ab_burst_eff_fmt",       "异常触发时追伤 攻击力×{0}", "異常発動時に攻撃力×{0}の追撃", "On anomaly: bonus damage = ATK ×{0}" },
        new[] { "charpanel_ability_burst_state_fmt", "×{0}", "×{0}", "×{0}" },
        new[] { "charpanel_ability_burst_desc",  "你的攻击让敌人的某种属性异常累计满值、异常真正赋予的那一刻，额外打出一笔同属性的追伤，大小是你当前攻击力的倍数。追伤不会再累计异常；双属性武器两种异常各追伤一次。", "攻撃で敵の属性異常が満タンになり、異常が実際に付与された瞬間に、同属性の追撃ダメージを与える。大きさは現在の攻撃力の倍率。追撃自体は異常を蓄積しない。属性を2つ持つ武器は、両方の異常でそれぞれ追撃する。", "When your attack fills an enemy's anomaly gauge and the anomaly is actually applied, you deal an extra hit of that element equal to a multiple of your ATK. The extra hit does not build anomaly; dual-element weapons trigger it once per anomaly." },
        new[] { "charpanel_ability_burst_source",  "来源：适应强化「属性攻击强化」每 5 级一档 + 科技树节点", "入手元：適応強化「属性攻撃強化」5段階ごとに1段 + テックツリー", "Source: Adaptation upgrade \"Elemental Damage\" (tier every 5 levels) + Tech Tree node" },
        new[] { "charpanel_ability_burst_next_fmt", "再 {0} 级升至 {1}：追伤 ×{2}", "あと{0}段階で{1}へ：追撃×{2}", "{0} more levels to {1}: burst ×{2}" },
        new[] { "ui_afp_ab_mastery",             "物理专精", "物理熟練", "Physical Mastery" },
        new[] { "ui_afp_ab_mastery_eff_fmt",     "大剑：斩杀 ≤{0} 血 · 枪械：末 {1} 发 +{2}%", "大剣：HP{0}以下を処刑 · 銃：最後の{1}発+{2}%", "Greatsword: execute ≤{0} HP · Gun: last {1} rounds +{2}%" },
        new[] { "charpanel_ability_mastery_state", "已生效", "有効", "Active" },
        new[] { "charpanel_ability_mastery_desc_fmt", "大剑：这一击打完后目标血量 ≤ {0} 点就直接斩杀(血量上限 ≥ 250 的敌人不会被斩杀，改为血量低于 25% 时物理伤害 +{1}%)。枪械：弹匣里最后 {2} 发子弹伤害 +{3}%。", "大剣：この一撃の後に対象のHPが{0}以下なら即座に処刑(最大HP250以上の敵は処刑されず、HP25%未満の時に物理ダメージ+{1}%)。銃：マガジン最後の{2}発のダメージ+{3}%。", "Greatsword: if the target's HP is ≤ {0} after this hit, execute it (enemies with max HP ≥ 250 can't be executed; below 25% HP they take +{1}% physical damage). Gun: the last {2} rounds in the magazine deal +{3}% damage." },
        new[] { "charpanel_ability_mastery_source", "来源：适应强化「物理攻击强化」每 5 级一档 + 科技树节点", "入手元：適応強化「物理攻撃強化」5段階ごとに1段 + テックツリー", "Source: Adaptation upgrade \"Physical Damage\" (tier every 5 levels) + Tech Tree node" },
        new[] { "charpanel_ability_mastery_next_fmt", "再 {0} 级升至 {1}：斩杀 ≤{2} 血 · 末 {3} 发 +{4}%", "あと{0}段階で{1}へ：HP{2}以下を処刑 · 最後{3}発+{4}%", "{0} more levels to {1}: execute ≤{2} HP · last {3} rounds +{4}%" },
        new[] { "charpanel_ability_burst_vuln_fmt", "另外，处于异常状态中的敌人受到你的伤害 +{0}%。", "さらに、異常状態の敵が受ける自分のダメージ+{0}%。", "Also, enemies under an anomaly take +{0}% damage from you." },
        new[] { "ui_afp_ab_mastery_eff_v3",      "回转：击杀回 {0}% LP · 枪：末 {1} 发 +{2}%", "回転：撃破でLP{0}%回復 · 銃：最後の{1}発+{2}%", "Cycle: kills restore {0}% LP · Gun: last {1} rounds +{2}%" },
        new[] { "charpanel_ability_mastery_desc_v3", "回转：击杀敌人时回复最大 LP 的 {0}%(击杀血量上限 ≥ 250 的大血量敌人回复 ×3)，让你敢于冲进敌群；箱子等可破坏物不算。致命弹药(枪械)：弹匣里最后 {1} 发子弹伤害 +{2}%。", "回転：敵を倒すと最大LPの{0}%を回復(最大HP250以上の大型敵は×3)。敵群に飛び込みやすくなる。木箱などの破壊物は対象外。致命弾(銃)：マガジン最後の{1}発のダメージ+{2}%。", "Cycle: killing an enemy restores {0}% of max LP (x3 for large enemies with max HP >= 250), so you can dive into groups; crates don't count. Deadly Ammo (guns): the last {1} rounds in the magazine deal +{2}% damage." },
        new[] { "ui_afp_ab_mastery_eff_v4",      "回转：击杀回 {0}% LP · 致命子弹：末 {1} 发 +{2}%", "回転：撃破でLP{0}%回復 · 致命弾：最後の{1}発+{2}%", "Cycle: kills restore {0}% LP · Lethal Rounds: last {1} rounds +{2}%" },
        new[] { "charpanel_ability_mastery_desc_v4", "回转：击杀敌人时回复最大 LP 的 {0}%(击杀血量上限 ≥ 250 的大血量敌人回复 ×3)，让你敢于冲进敌群；箱子等可破坏物不算。致命子弹(双枪)：弹匣里最后 {1} 发子弹伤害 +{2}%。", "回転：敵を倒すと最大LPの{0}%を回復(最大HP250以上の大型敵は×3)。敵群に飛び込みやすくなる。木箱などの破壊物は対象外。致命弾(双銃)：マガジン最後の{1}発のダメージ+{2}%。", "Cycle: killing an enemy restores {0}% of max LP (x3 for large enemies with max HP >= 250), so you can dive into groups; crates don't count. Lethal Rounds (dual guns): the last {1} rounds in the magazine deal +{2}% damage." },
        new[] { "charpanel_ability_mastery_next_v3", "再 {0} 级升至 {1}：击杀回 {2}% LP · 致命子弹 末 {3} 发 +{4}%", "あと{0}段階で{1}へ：撃破でLP{2}%回復 · 致命弾 最後{3}発+{4}%", "{0} more levels to {1}: kills restore {2}% LP · Lethal Rounds last {3} rounds +{4}%" },
        new[] { "afp_zone_prompt_v2",     "进行AFP部署", "AFP展開を行う", "Deploy AFP" },
        new[] { "ui_afp_ab_need_short_fmt",       "再 {0} 级", "あと{0}段階", "{0} more lv" },
        new[] { "ui_afp_ab_burst_short_fmt",      "追伤 攻击力×{0} · 异常增伤 +{1}%", "追撃 攻撃力×{0} · 異常増ダメ+{1}%", "Burst ATK×{0} · anomaly +{1}% dmg" },
        new[] { "ui_afp_ab_mastery_short_fmt",    "回转 {0}% LP · 致命子弹 末{1}发 +{2}%", "回転 LP{0}% · 致命弾 最後{1}発+{2}%", "Cycle {0}% LP · Lethal Rounds last {1} +{2}%" },
        new[] { "ui_afp_ab_mastery_v2",          "武器专精", "武器熟練", "Weapon Mastery" },
        new[] { "ui_afp_ab_mastery_eff_v2",      "续战：击杀回 {0}% LP · 枪：末 {1} 发 +{2}%", "継戦：撃破でLP{0}%回復 · 銃：最後の{1}発+{2}%", "Sustain: kills restore {0}% LP · Gun: last {1} rounds +{2}%" },
        new[] { "charpanel_ability_mastery_desc_v2", "续战：击杀敌人时回复最大 LP 的 {0}%(击杀血量上限 ≥ 250 的大血量敌人回复 ×3)，让你敢于冲进敌群；箱子等可破坏物不算。致命弹药(枪械)：弹匣里最后 {1} 发子弹伤害 +{2}%。", "継戦：敵を倒すと最大LPの{0}%を回復(最大HP250以上の大型敵は×3)。敵群に飛び込みやすくなる。木箱などの破壊物は対象外。致命弾(銃)：マガジン最後の{1}発のダメージ+{2}%。", "Sustain: killing an enemy restores {0}% of max LP (x3 for large enemies with max HP >= 250), so you can dive into groups; crates don't count. Deadly Ammo (guns): the last {1} rounds in the magazine deal +{2}% damage." },
        new[] { "charpanel_ability_mastery_next_v2", "再 {0} 级升至 {1}：击杀回 {2}% LP · 枪末 {3} 发 +{4}%", "あと{0}段階で{1}へ：撃破でLP{2}%回復 · 銃最後{3}発+{4}%", "{0} more levels to {1}: kills restore {2}% LP · gun last {3} rounds +{4}%" },
        new[] { "ui_afp_up_hp",        "追加生命", "追加ライフ", "Bonus Vitality" },
        new[] { "ui_afp_up_slash",     "斩击抗性", "斬撃耐性", "Slash Resistance" },
        new[] { "ui_afp_up_strike",    "打击抗性", "打撃耐性", "Strike Resistance" },
        new[] { "ui_afp_up_impact",    "冲击抗性", "衝撃耐性", "Impact Resistance" },
        new[] { "ui_afp_up_heat",      "灼热抗性", "灼熱耐性", "Heat Resistance" },
        new[] { "ui_afp_up_shock",     "电磁抗性", "電磁耐性", "Electromagnetic Resistance" },
        new[] { "ui_afp_up_corrosion", "腐蚀抗性", "腐食耐性", "Corrosion Resistance" },
        new[] { "ui_afp_up_freeze",    "冻结抗性", "凍結耐性", "Freeze Resistance" },
        new[] { "afp_zone_prompt",     "适应系统",       "適応システム",     "Adaptation System" },
        new[] { "ui_afp_ab_usurp", "篡夺者", "簒奪者", "Usurper" },
        new[] { "ui_afp_ab_usurp_short", "攻击回复红色血量", "攻撃で赤いHPを回復", "Attacks restore red HP" },
        new[] { "ui_afp_ab_usurp_state", "红色回复", "赤回復", "Red Regain" },
        new[] { "charpanel_ability_usurp_desc_v5", "受到的伤害，有 {0}% 转为血条上的红色部分，随时间消退。攻击敌人时，造成伤害的一部分转化为生命回复，只能回复红色部分：近战 {1}%，远程 {2}%。", "受けたダメージの{0}%がHPバーの赤い部分に変わり、時間とともに消える。敵を攻撃すると、与えたダメージの一部が生命回復に変換される。回復できるのは赤い部分のみ：近接{1}%、遠隔{2}%。", "{0}% of damage taken becomes a red portion of the HP bar that fades over time. Damage dealt to enemies is converted into healing, but only restores the red portion: {1}% melee, {2}% ranged." },
        new[] { "charpanel_ability_usurp_source", "来源：探索者科技树", "入手：探索者テックツリー", "Source: Seeker tech tree" },
        new[] { "ui_afp_up_usurp", "篡夺者", "簒奪者", "Usurper" },
        new[] { "ui_afp_usurp_need_fmt", "需要 {0}　{1}", "必要：{0}　{1}", "Requires {0}  {1}" },
        new[] { "ui_afp_ab_usurp_state_fmt", "近战 {0}% · 远程 {1}%", "近接{0}% · 遠隔{1}%", "Melee {0}% · Ranged {1}%" },
        new[] { "charpanel_ability_usurp_source_v6", "来源：适应强化「篡夺者」(需追加生命、物理攻击强化各 10 级) + 科技树节点", "入手：適応強化「簒奪者」(追加ライフ・物理攻撃強化が各10段階必要) + 技術ツリーノード", "Source: Usurper upgrade (requires Bonus Vitality and Physical Attack at level 10) + tech tree node" },
        new[] { "charpanel_ability_usurp_next_v6", "下一级：近战 {0}% · 远程 {1}%", "次の段階：近接{0}% · 遠隔{1}%", "Next level: melee {0}% · ranged {1}%" },
        new[] { "ui_afp_ab_lethal_short_fmt_v2", "每第 {0} 发 伤害 +{1}%", "{0}発ごと ダメージ+{1}%", "Every {0}th round: +{1}% damage" },
        new[] { "ui_afp_ab_lethal_state_fmt_v2", "每 {0} 发 +{1}%", "{0}発ごと +{1}%", "Every {0}th +{1}%" },
        new[] { "charpanel_ability_lethal_desc_v6", "仅限双枪。每第 {0} 发命中的子弹，伤害 +{1}%。", "双銃専用。{0}発目の命中ごとに、ダメージ+{1}%。", "Dual guns only. Every {0}th round that hits deals +{1}% damage." },
        new[] { "charpanel_ability_lethal_next_v6", "再 {0} 级升至 {1}：每第 {2} 发 +{3}%", "あと{0}段階で{1}へ：{2}発ごと +{3}%", "{0} more levels to {1}: every {2}th round +{3}%" },
        new[] { "ui_afp_dmg_corrosion", "腐蚀", "腐食", "Corrosion" },
        new[] { "ui_afp_dmg_freeze", "冻结", "凍結", "Freeze" },
        new[] { "afp_zone_combat",     "战斗中无法使用", "戦闘中は使用不可", "Unavailable in Combat" },
        new[] { "charpanel_ability_slot_fmt", "适应能力 {0}", "適応能力 {0}", "Adaptation {0}" },
        new[] { "charpanel_ability_empty_v3", "尚未获得适应能力", "適応能力は未取得", "No adaptations acquired" },
        new[] { "charpanel_upgrades_none_v3", "尚未进行适应强化", "適応強化は未実施", "No upgrades applied" },
        new[] { "charpanel_equip_weight", "装备总重", "装備総重量", "Equipment Weight" },
        new[] { "charpanel_tier_light", "轻装", "軽装", "Light" },
        new[] { "charpanel_tier_normal", "标准", "標準", "Standard" },
        new[] { "charpanel_tier_heavy", "重装", "重装", "Heavy" },
        new[] { "charpanel_tier_overload", "超重装", "超重装", "Overweight" },
        new[] { "ui_afp_ab_cycle", "回转", "回転", "Cycle" },
        new[] { "ui_afp_ab_cycle_short_fmt", "击杀回复 {0}% LP", "撃破でLP{0}%回復", "Kill: +{0}% LP" },
        new[] { "ui_afp_ab_cycle_state_fmt", "击杀 +{0}% LP", "撃破 +{0}% LP", "Kill +{0}% LP" },
        new[] { "ui_afp_ab_lethal", "致命子弹", "致命弾", "Lethal Rounds" },
        new[] { "ui_afp_ab_lethal_short_fmt", "弹匣末 {0} 发 伤害 +{1}%", "マガジン末尾{0}発 ダメージ+{1}%", "Last {0} rounds: +{1}% damage" },
        new[] { "ui_afp_ab_lethal_state_fmt", "末 {0} 发 +{1}%", "末尾{0}発 +{1}%", "Last {0} +{1}%" },
        new[] { "charpanel_ability_cycle_desc_v5", "仅限剑。击杀敌人时，恢复最大 LP 的 {0}%。击杀生命上限 250 以上的目标，恢复量 ×3。破坏物不触发。", "剣専用。敵を撃破すると、最大LPの{0}%を回復する。最大HP250以上の対象を撃破した場合、回復量は×3。破壊物は対象外。", "Sword only. Killing an enemy restores {0}% of max LP. Targets with 250 or more max HP restore x3. Destructibles do not count." },
        new[] { "charpanel_ability_lethal_desc_v5", "仅限双枪。弹匣最后 {0} 发子弹的伤害 +{1}%。", "双銃専用。マガジン最後の{0}発のダメージ+{1}%。", "Dual guns only. The last {0} rounds in the magazine deal +{1}% damage." },
        new[] { "charpanel_ability_phys_source_v5", "来源：适应强化「物理攻击强化」每 5 级 + 科技树节点", "入手：適応強化「物理攻撃強化」5段階ごと + 技術ツリーノード", "Source: Physical Attack upgrade, every 5 levels + tech tree node" },
        new[] { "charpanel_ability_cycle_next_v5", "再 {0} 级升至 {1}：击杀回复 {2}% LP", "あと{0}段階で{1}へ：撃破でLP{2}%回復", "{0} more levels to {1}: kill restores {2}% LP" },
        new[] { "charpanel_ability_lethal_next_v5", "再 {0} 级升至 {1}：弹匣末 {2} 发 +{3}%", "あと{0}段階で{1}へ：マガジン末尾{2}発 +{3}%", "{0} more levels to {1}: last {2} rounds +{3}%" },
        new[] { "charpanel_ability_field_desc_v5", "目标属性的异常累计即将触发{0}异常时，豁免一次，随后进入恢复。档位越高，恢复越快。", "対象属性の異常蓄積が{0}異常に達する際、一度だけ無効化し、その後回復に入る。段階が高いほど回復が早い。", "When accumulation of this attribute is about to inflict {0} status, negate it once, then recharge. Higher tiers recharge faster." },
        new[] { "charpanel_ability_field_source_v5", "来源：适应强化「{0}抗性」每 5 级 + 科技树节点", "入手：適応強化「{0}耐性」5段階ごと + 技術ツリーノード", "Source: {0} Resistance upgrade, every 5 levels + tech tree node" },
        new[] { "charpanel_ability_burst_desc_v5", "命中令敌人触发属性异常时，追加一次同属性追伤，伤害为当前攻击力的倍数。追伤不再累计异常；双属性武器两种异常各追伤一次。处于异常状态的敌人，受到的伤害 +{0}%。", "命中で敵に属性異常が発生した際、同属性の追撃を1回追加する。ダメージは現在の攻撃力の倍率。追撃は異常を蓄積しない。二属性武器は両方の異常で追撃する。異常状態の敵への与ダメージ+{0}%。", "When a hit inflicts an attribute status, deal an additional hit of the same attribute, scaled by your current ATK. It does not build up status. Dual-attribute weapons trigger once per status. Enemies under a status take +{0}% damage." },
        new[] { "charpanel_ability_burst_source_v5", "来源：适应强化「属性攻击强化」每 5 级 + 科技树节点", "入手：適応強化「属性攻撃強化」5段階ごと + 技術ツリーノード", "Source: Attribute Attack upgrade, every 5 levels + tech tree node" },
        new[] { "ui_afp_ab_exempt_desc_v5", "受到致命伤害时，豁免一次死亡，生命保留 1 点，随后进入冷却。冷却期间再受致命伤害，将正常死亡。", "致命ダメージを受けた際、一度だけ死亡を無効化し、HPを1残す。その後クールダウンに入る。クールダウン中に再び致命ダメージを受けると死亡する。", "When taking lethal damage, negate death once and keep 1 HP, then enter cooldown. Lethal damage during cooldown is fatal." },
        new[] { "charpanel_ability_ins_source_v5", "来源：适应强化「追加生命」5 级 + 科技树节点", "入手：適応強化「追加ライフ」5段階 + 技術ツリーノード", "Source: Bonus Vitality upgrade, 5 levels + tech tree node" },
    };

    static SkyPrisonAfpLocalizationSeeder()
    {
        EditorApplication.delayCall += () => Seed(false);
    }

    [MenuItem("天空囚笼/本地化/补全AFP购买窗口文字", false, 2101)]
    static void Menu() { Seed(true); }

    static void Seed(bool verbose)
    {
        var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
        if (table == null) { if (verbose) Debug.LogWarning("[AfpLoc] 找不到 " + TablePath); return; }

        var codes = new List<string>(Codes);
        int changed = 0;
        foreach (var row in Rows)
        {
            var entry = table.EnsureEntry(row[0], codes);
            for (int i = 0; i < Codes.Length; i++)
            {
                foreach (var t in entry.texts)
                {
                    if (t.languageCode != Codes[i] || !string.IsNullOrEmpty(t.text)) continue;
                    t.text = row[i + 1]; changed++;
                }
            }
        }
        if (changed > 0)
        {
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("[AfpLoc] AFP购买窗口文字已补进字典表，新增 " + changed + " 条译文（中/日/英）。");
        }
        else if (verbose) Debug.Log("[AfpLoc] 字典表里AFP购买窗口文字已齐全。");
    }
}
