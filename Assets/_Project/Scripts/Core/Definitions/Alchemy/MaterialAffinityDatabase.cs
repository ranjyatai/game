using System;
using System.Collections.Generic;
using UnityEngine;

// 素材/装备共用的"标签+权重"——素材用它表达"我携带哪些相性标签、各自多强"，
// 装备用它表达"我偏好哪些标签"。两边共用同一个类型，强化计算时直接拿这两份列表
// 按标签key对齐算契合度，不用为素材和装备分别定义结构。
[Serializable]
public class MaterialAffinityTagWeight
{
    public string tagKey = "";
    [Range(0f, 1f)] public float weight = 1f;
}

[Serializable]
public class AlchemyTagDefinition
{
    public string key = "";
    public string displayName = "";
}

// 标签跟标签之间的相性——正值=放一起加分，负值=互相拖累减分。查询时不区分
// tagAKey/tagBKey 顺序（A×B 跟 B×A 是同一条规则），维护的时候只需要填一次。
[Serializable]
public class TagAffinityRule
{
    public string tagAKey = "";
    public string tagBKey = "";
    [Range(-1f, 1f)] public float affinity = 0f;
}

// 特定标签组合直接命中的隐藏配方——不算在常规标签相性计算里，是额外的"发现了就
// 很爽"的例外规则（比如同时出现两个特定标签，直接把成功率锁到某个高位）。
[Serializable]
public class SpecialComboRule
{
    public string comboName = "";
    public List<string> requiredTagKeys = new List<string>();
    [Tooltip("命中这个配方时直接锁定的成功率（0~1）。")]
    [Range(0f, 1f)] public float forcedSuccessRate = 1f;
}

[CreateAssetMenu(
    fileName = "MaterialAffinityDatabase",
    menuName = "Sky Prison/Material Affinity Database",
    order = 1300)]
public class MaterialAffinityDatabase : ScriptableObject
{
    public string databaseId = "material_affinity_main";
    public string displayName = "素材相性库";
    [TextArea(2, 4)]
    public string note = "";

    [Header("Runtime")]
    public bool isRuntimeActive = true;

    public List<AlchemyTagDefinition> tags = new List<AlchemyTagDefinition>();
    public List<TagAffinityRule> tagAffinities = new List<TagAffinityRule>();
    public List<SpecialComboRule> specialCombos = new List<SpecialComboRule>();

    public AlchemyTagDefinition FindTag(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        for (int i = 0; i < tags.Count; i++)
            if (tags[i] != null && tags[i].key == key) return tags[i];
        return null;
    }

    // A×B 和 B×A 是同一条规则，查询时两个方向都要试——维护的人只用填一次，
    // 不用两条方向对称的重复数据。找不到规则时返回0（中性，不加不减）。
    public float GetTagAffinity(string tagKeyA, string tagKeyB)
    {
        if (string.IsNullOrWhiteSpace(tagKeyA) || string.IsNullOrWhiteSpace(tagKeyB))
            return 0f;

        for (int i = 0; i < tagAffinities.Count; i++)
        {
            TagAffinityRule rule = tagAffinities[i];
            if (rule == null) continue;

            bool forward  = rule.tagAKey == tagKeyA && rule.tagBKey == tagKeyB;
            bool backward = rule.tagAKey == tagKeyB && rule.tagBKey == tagKeyA;
            if (forward || backward) return rule.affinity;
        }
        return 0f;
    }

    // 命中的标签集合里，只要包含某条特殊配方要求的全部标签（可以有多余的标签，
    // 但不能少），就算命中——多条同时命中时取成功率最高的那条，最大化玩家"抓到彩蛋"
    // 的体验，不会因为凑巧同时满足两条配方结果反而更差。
    public SpecialComboRule FindBestMatchingCombo(IEnumerable<string> presentTagKeys)
    {
        if (specialCombos == null || specialCombos.Count == 0) return null;

        var present = new HashSet<string>(presentTagKeys);
        SpecialComboRule best = null;

        foreach (var combo in specialCombos)
        {
            if (combo?.requiredTagKeys == null || combo.requiredTagKeys.Count == 0) continue;

            bool allPresent = true;
            foreach (var required in combo.requiredTagKeys)
            {
                if (!present.Contains(required)) { allPresent = false; break; }
            }
            if (!allPresent) continue;

            if (best == null || combo.forcedSuccessRate > best.forcedSuccessRate)
                best = combo;
        }
        return best;
    }
}
