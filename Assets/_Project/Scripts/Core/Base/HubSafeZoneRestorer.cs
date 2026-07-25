using System.Collections;
using UnityEngine;

/// <summary>
/// 挂在基地场景的任意 GameObject 上。
/// 场景激活后等玩家角色就绪，执行满状态恢复：HP 满、清除所有 Debuff。
/// 适用于任何安全场景（Hub_Base 等）。
/// </summary>
public class HubSafeZoneRestorer : MonoBehaviour
{
    [Tooltip("等待玩家角色注册的最长秒数，超时后放弃本次恢复")]
    [SerializeField] private float waitTimeout = 5f;

    private void Start()
    {
        StartCoroutine(RestoreWhenReady());
    }

    private IEnumerator RestoreWhenReady()
    {
        float elapsed = 0f;

        // 等待 SkyPrisonPlayerAuthority 注册到玩家
        while (elapsed < waitTimeout)
        {
            var auth = SkyPrisonPlayerAuthority.Instance;
            if (auth != null && auth.CurrentPlayerGameObject != null)
            {
                RestorePlayer(auth.CurrentPlayerGameObject);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[HubSafeZoneRestorer] 超时：未找到玩家角色，跳过状态恢复。");
    }

    private static void RestorePlayer(GameObject playerGo)
    {
        // ── HP 回满 ───────────────────────────────────────────────────────
        var health = playerGo.GetComponent<UnitHealthController>()
                  ?? playerGo.GetComponentInChildren<UnitHealthController>(true);
        if (health != null)
            health.FullHeal();

        // ── 清除所有状态（Debuff / Buff 全清）────────────────────────────
        var status = playerGo.GetComponent<UnitStatusController>()
                  ?? playerGo.GetComponentInChildren<UnitStatusController>(true);
        if (status != null)
            status.ClearAllStatuses();

        // ── 重置本局击杀统计 ──────────────────────────────────────────────
        var stats = playerGo.GetComponent<SessionStatsTracker>();
        if (stats != null)
            stats.ResetSession();
    }
}
