using UnityEngine;
/// <summary>
/// 受击修正：单位身上有实现这个接口的组件时，伤害结算(ResolveSkillHit)在把伤害真正扣到血量前，
/// 会先让它改一下数值——守卫者的盾就是这么拦截的(物理被盾减伤、属性伤害更伤盾)。
/// </summary>
public interface IIncomingHitModifier
{
    /// <param name="isPhysical">true=物理伤害(斩击/打击/冲击)，false=属性伤害(灼热/电磁/腐蚀/冻结)</param>
    /// <param name="damageKey">伤害类型 key，例如 strike / heat</param>
    /// <param name="amount">本次将要扣的伤害；可以改小/改大/改成 0</param>
    /// <param name="attackerX">攻击者的世界 X 坐标（横版里只有左右两侧，盾据此判断是正面还是背后）</param>
    void ModifyHit(bool isPhysical, string damageKey, float attackerX, ref float amount);
}


/// <summary>对目标单位身上（含父级）所有 IIncomingHitModifier 依次调用——守卫者的盾、拴绳无敌这类可以同时存在，
/// 之前只取第一个，后面的会被漏掉。</summary>
public static class IncomingHitModifiers
{
    private static readonly System.Collections.Generic.List<IIncomingHitModifier> Buffer = new System.Collections.Generic.List<IIncomingHitModifier>();

    public static void Apply(Component target, bool isPhysical, string damageKey, float attackerX, ref float amount)
    {
        if (target == null || amount <= 0f) return;
        Buffer.Clear();
        target.GetComponents(Buffer);
        if (Buffer.Count == 0) target.GetComponentsInParent(true, Buffer);
        for (int i = 0; i < Buffer.Count; i++)
        {
            Buffer[i].ModifyHit(isPhysical, damageKey, attackerX, ref amount);
            if (amount <= 0f) break;
        }
        Buffer.Clear();
    }
}

/// <summary>致命伤守卫：伤害真正扣到血量的最后一道入口(UnitHealthController.ApplyDamage)，不管伤害走的是哪条路径(普通命中、灼烧、触发器…)都会经过。
/// 返回值是最终要扣的伤害；「生命保险」这类「致命时锁 1 点血」的效果实现它。</summary>
public interface ILethalDamageGuard
{
    float GuardLethal(float currentHealth, float amount);
}

/// <summary>异常赋予守卫：属性异常累计满值、即将赋予异常状态(灼烧/触电…)的瞬间，让它有机会「豁免这一次」。
/// 返回 true = 这次异常不赋予(累计条仍然清零，只是不生成状态)。「特效力场」实现它。</summary>
public interface IAnomalyApplyGuard
{
    bool TryExemptAnomaly(string anomalyKey);
}
