using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 编辑器专用的战斗记录(打包后全部是空函数)：玩家每一次开枪、每一次命中、每一次打出失衡都写进 Editor.log，
/// 目标死亡时再写一行汇总——用来回答"打一只 X 用了多少子弹、掉了多少血"这类平衡问题，不用让测试的人自己数。
///   [HitLog]  每一下命中：招式、目标、物理/属性伤害、弱点、暴击、目标剩余血量
///   [ShotLog] 每次开枪消耗的弹药(只记玩家)
///   [KillLog] 目标死亡汇总：用时、从第一次命中起开了几枪、命中数、总伤害、暴击/弱点/失衡次数
/// 读法：grep "\[KillLog\]\|\[HitLog\]" Editor.log
/// </summary>
public static class SkyPrisonCombatLog
{
#if UNITY_EDITOR
    sealed class Track
    {
        public string name;
        public float firstHitTime;
        public int shotsAtFirstHit;
        public int hits, crits, weakHits, staggers;
        public float damage;
    }

    static int _shots;          // 全局开枪计数(弹药数)
    static readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();

    static Transform RootOf(Component c)
    {
        var b = c.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
        return b != null ? b.transform : c.transform;
    }

    static Track Get(Transform root, bool create)
    {
        int id = root.GetInstanceID();
        if (_tracks.TryGetValue(id, out var t)) return t;
        if (!create) return null;
        var b = root.GetComponent<UnitDefinitionRuntimeBinder>();
        t = new Track { name = b != null && b.UnitDefinitionAsset != null ? b.UnitDefinitionAsset.name : root.name, firstHitTime = Time.time, shotsAtFirstHit = _shots };
        _tracks[id] = t;
        return t;
    }
#endif

    public static void OnShot(int ammo)
    {
#if UNITY_EDITOR
        _shots += Mathf.Max(1, ammo);
        Debug.Log("[ShotLog] ammo=" + ammo + " totalShots=" + _shots);
#endif
    }

    public static void OnPlayerHit(UnitHealthController target, SkillDefinition skill, float physical, float attribute, bool weak, bool crit, bool killed)
    {
#if UNITY_EDITOR
        if (target == null) return;
        var root = RootOf(target);
        var t = Get(root, true);
        t.hits++; t.damage += physical + attribute;
        if (crit) t.crits++;
        if (weak) t.weakHits++;
        Debug.Log($"[HitLog] {t.name} skill={(skill != null ? skill.skillKey : "?")} phys={physical:F1} attr={attribute:F1} weak={(weak ? 1 : 0)} crit={(crit ? 1 : 0)} hp={target.CurrentHealth:F0}/{target.MaxHealth:F0}");
        if (killed)
        {
            int shots = _shots - t.shotsAtFirstHit;
            Debug.Log($"[KillLog] {t.name} 用时={Time.time - t.firstHitTime:F1}s 开枪={shots} 命中={t.hits} 总伤害={t.damage:F0} 暴击={t.crits} 弱点={t.weakHits} 失衡={t.staggers} 上限={target.MaxHealth:F0}");
            _tracks.Remove(root.GetInstanceID());
        }
#endif
    }

    public static void OnStagger(Component unit)
    {
#if UNITY_EDITOR
        if (unit == null) return;
        var t = Get(RootOf(unit), false);
        if (t != null) t.staggers++;
        Debug.Log("[HitLog] 失衡 " + (t != null ? t.name : unit.name));
#endif
    }
}
