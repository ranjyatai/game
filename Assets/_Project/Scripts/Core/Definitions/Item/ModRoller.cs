using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 武器组件词条投掷器。
/// 掉落时调用 Roll()，从词条池按权重抽取 bonusCount 条并在范围内随机数值。
/// 结果是确定的（存入 rolledBonuses），鉴定只是揭示，不重投。
/// </summary>
public static class ModRoller
{
    public static List<RolledModBonus> Roll(ItemModExtension mod)
    {
        var result = new List<RolledModBonus>();
        if (mod == null || mod.bonusPool.Count == 0) return result;

        var pool = new List<PossibleModBonus>(mod.bonusPool);
        int count = Mathf.Min(mod.bonusCount, pool.Count);

        for (int i = 0; i < count; i++)
        {
            // 按权重随机抽取一条（抽完从池中移除，不重复）
            PossibleModBonus picked = PickWeighted(pool);
            if (picked == null) break;

            pool.Remove(picked);

            result.Add(new RolledModBonus
            {
                statKey     = picked.statKey,
                displayName = picked.displayName,
                value       = Random.Range(picked.minValue, picked.maxValue),
                isPercent   = picked.isPercent,
            });
        }

        return result;
    }

    private static PossibleModBonus PickWeighted(List<PossibleModBonus> pool)
    {
        float total = 0f;
        foreach (var b in pool) total += b.weight;

        float roll = Random.Range(0f, total);
        float acc  = 0f;
        foreach (var b in pool)
        {
            acc += b.weight;
            if (roll <= acc) return b;
        }
        return pool.Count > 0 ? pool[pool.Count - 1] : null;
    }
}
