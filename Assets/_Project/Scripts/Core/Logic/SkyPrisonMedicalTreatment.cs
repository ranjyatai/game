using UnityEngine;

/// <summary>
/// 医疗站的"进行治疗"：刷新玩家全部状态。
///   · 清掉所有异常累积和状态效果(灼烧/感电/腐蚀/冻结等)；
///   · 清掉灰炉的灰色损失(最大生命恢复)；
///   · 血量补满、LP 补满。
/// 不动装备耐久(那是工房的事)，不动背包。
/// </summary>
public static class SkyPrisonMedicalTreatment
{
    /// <summary>治疗当前玩家。返回是否真的执行了治疗(没有玩家/已死亡返回 false)。</summary>
    public static bool TreatPlayer()
    {
        var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (unit == null) return false;
        return Treat(unit.gameObject);
    }

    public static bool Treat(GameObject root)
    {
        if (root == null) return false;

        var health = root.GetComponentInChildren<UnitHealthController>(true);
        if (health == null || health.IsDead) return false;

        // 1) 灰色损失先清：最大生命恢复到正常，后面补血才能补到真正的满值
        var gray = root.GetComponent<SkyPrisonGrayLoss>();
        if (gray != null) gray.Clear();

        // 2) 异常累积 + 状态效果
        var anomaly = root.GetComponentInChildren<UnitAnomalyController>(true);
        if (anomaly != null) anomaly.ClearAllAccumulations();
        var status = root.GetComponentInChildren<UnitStatusController>(true);
        if (status != null) status.ClearAllStatuses();

        // 3) 血量、LP 补满
        health.FullHeal();
        var load = root.GetComponentInChildren<UnitLoadRuntime>(true);
        if (load != null) load.RestoreToFull();

        return true;
    }
}
