using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 词条里"对象"的统一解析：玩家 / 最近敌人 / 最近友军 / 出生点 / 指定点位(X,Y 两个浮点数) / 当前目标。
/// 距离带、范围条件这类词条都走这里，不各写一份。
///   · 敌人/友军按单位阵营判断(UnitCombatHitbox.AreHostile)：敌人 = 与自己敌对的存活单位；
///     友军 = 与自己不敌对、非自己、存活的单位（玩家本人在玩家阵营看是友军，在怪物看是敌人）；
///   · 最近敌人/友军每 0.5 秒才重新扫一次（场景里单位多，不能每帧 FindObjectsOfType）；
///   · 指定点位：X、Y 两个浮点数，Y 在 2.5D 地图里代表世界 Z（跟"移动到 XY"词条同一个约定）。
/// </summary>
public static class SkyPrisonAITargetResolver
{
    public const string CurrentTarget = "CurrentTarget";
    public const string Player = "Player";
    public const string NearestHostile = "NearestHostile";
    public const string NearestAlly = "NearestAlly";
    public const string SpawnPoint = "SpawnPoint";
    public const string Point = "Point";

    /// <summary>词条下拉里的选项（id, 显示名）。</summary>
    public static readonly (string id, string label)[] Options =
    {
        (CurrentTarget, "当前目标"),
        (Player, "玩家"),
        (NearestHostile, "最近的敌人"),
        (NearestAlly, "最近的友军"),
        (SpawnPoint, "出生点"),
        (Point, "指定点位(X,Y)"),
    };

    private sealed class Cache
    {
        public float nextScan;
        public Transform found;
    }

    private static readonly Dictionary<int, Cache> HostileCache = new Dictionary<int, Cache>();
    private static readonly Dictionary<int, Cache> AllyCache = new Dictionary<int, Cache>();

    /// <summary>解析出目标的世界坐标；解析不到返回 false。</summary>
    public static bool TryResolve(string kind, Transform self, Transform currentTarget, Vector3 spawnPosition,
        Vector2 point, out Vector3 position)
    {
        position = default;
        Transform t = null;
        switch (kind)
        {
            case Player:
            {
                var p = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
                t = p != null ? p.transform : null;
                break;
            }
            case NearestHostile: t = FindNearest(self, true); break;
            case NearestAlly: t = FindNearest(self, false); break;
            case SpawnPoint: position = spawnPosition; return true;
            case Point: position = new Vector3(point.x, self != null ? self.position.y : 0f, point.y); return true;
            default: t = currentTarget; break;     // CurrentTarget / 未知 → 当前目标
        }
        if (t == null) return false;
        position = t.position;
        return true;
    }

    private static Transform FindNearest(Transform self, bool hostile)
    {
        if (self == null) return null;
        var dict = hostile ? HostileCache : AllyCache;
        int key = self.GetInstanceID();
        if (!dict.TryGetValue(key, out var c)) { c = new Cache(); dict[key] = c; }
        if (Time.time < c.nextScan && (c.found == null || IsAlive(c.found))) return c.found;
        c.nextScan = Time.time + 0.5f;

        Transform best = null;
        float bestSqr = float.MaxValue;
        var all = Object.FindObjectsOfType<UnitHealthController>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null || h.IsDead) continue;
            var root = h.gameObject;
            if (root.transform == self || root.transform.IsChildOf(self) || self.IsChildOf(root.transform)) continue;
            bool isHostile = UnitCombatHitbox.AreHostile(self.gameObject, root);
            if (isHostile != hostile) continue;
            Vector3 d = root.transform.position - self.position;
            d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = root.transform; }
        }
        c.found = best;
        return best;
    }

    private static bool IsAlive(Transform t)
    {
        if (t == null) return false;
        var h = t.GetComponentInChildren<UnitHealthController>(true);
        return h == null || !h.IsDead;
    }
}
