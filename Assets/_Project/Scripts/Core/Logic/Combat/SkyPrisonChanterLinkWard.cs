using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在被颂赞者连线的单位上：伤害结算时按连着它的颂赞者里最大的减伤比例削减(多只不叠加)。
/// 没有颂赞者连着时不起作用；颂赞者死亡/断线会自己把登记撤掉。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonChanterLinkWard : MonoBehaviour, IIncomingHitModifier
{
    private readonly List<SkyPrisonChanterSupport> _linkers = new List<SkyPrisonChanterSupport>();

    public bool IsLinked
    {
        get
        {
            for (int i = _linkers.Count - 1; i >= 0; i--) if (_linkers[i] == null) _linkers.RemoveAt(i);
            return _linkers.Count > 0;
        }
    }

    // 伤害结算从受击单位的 UnitHealthController 所在物体往上找 IIncomingHitModifier，所以挂在血量组件那个物体上
    public static SkyPrisonChanterLinkWard EnsureOn(Transform unitRoot)
    {
        var h = unitRoot.GetComponentInChildren<UnitHealthController>(true);
        var host = h != null ? h.gameObject : unitRoot.gameObject;
        var w = host.GetComponent<SkyPrisonChanterLinkWard>();
        return w != null ? w : host.AddComponent<SkyPrisonChanterLinkWard>();
    }

    public static SkyPrisonChanterLinkWard Find(Transform unitRoot)
    {
        var h = unitRoot.GetComponentInChildren<UnitHealthController>(true);
        return h != null ? h.GetComponent<SkyPrisonChanterLinkWard>() : unitRoot.GetComponentInChildren<SkyPrisonChanterLinkWard>(true);
    }

    public void AddLinker(SkyPrisonChanterSupport c) { if (c != null && !_linkers.Contains(c)) _linkers.Add(c); }
    public void RemoveLinker(SkyPrisonChanterSupport c) { _linkers.Remove(c); }

    public void ModifyHit(bool isPhysical, string damageKey, float attackerX, ref float amount)
    {
        float best = 0f;
        for (int i = _linkers.Count - 1; i >= 0; i--)
        {
            var c = _linkers[i];
            if (c == null) { _linkers.RemoveAt(i); continue; }
            if (c.DamageReduction > best) best = c.DamageReduction;
        }
        if (best > 0f) amount *= 1f - best;
    }
}
