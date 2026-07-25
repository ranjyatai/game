using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 装备对比预览：算"如果换上悬浮中的这件装备，各项属性会变化多少"。
/// 复用 EquipmentStatApplier.CollectEquipmentMods 同一套"收集一件装备的StatModifier"
/// 逻辑，保证跟真正装备生效之后的数值完全一致，不是另外写一份口径可能对不上的近似算法。
/// 只处理 Add 类型的加成（Multiply/Override 目前项目里装备词条没在用，真出现了也不在这个
/// 简单加减预览里体现，不会算错，只是不显示delta）。
/// </summary>
public static class EquipStatPreview
{
    public static Dictionary<string, float> ComputeDelta(InventoryItemEntry candidate, InventoryItemEntry currentlyEquipped)
    {
        var a = CollectAdditive(candidate);
        var b = CollectAdditive(currentlyEquipped);

        var result = new Dictionary<string, float>();
        var keys = new HashSet<string>(a.Keys);
        keys.UnionWith(b.Keys);
        foreach (var key in keys)
        {
            float av = a.TryGetValue(key, out float x) ? x : 0f;
            float bv = b.TryGetValue(key, out float y) ? y : 0f;
            float delta = av - bv;
            if (Mathf.Abs(delta) > 0.001f) result[key] = delta;
        }
        return result;
    }

    private static Dictionary<string, float> CollectAdditive(InventoryItemEntry entry)
    {
        var dict = new Dictionary<string, float>();
        if (entry?.definition?.equipment == null) return dict;

        var mods = new List<StatModifier>();
        EquipmentStatApplier.CollectEquipmentMods(entry, mods);
        foreach (var m in mods)
        {
            if (m.op != StatModifierOp.Add) continue;
            if (!dict.ContainsKey(m.statKey)) dict[m.statKey] = 0f;
            dict[m.statKey] += m.value;
        }
        return dict;
    }
}
