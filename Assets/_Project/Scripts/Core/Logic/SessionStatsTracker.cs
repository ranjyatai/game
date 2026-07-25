using UnityEngine;

/// <summary>
/// 本局统计追踪器。挂在玩家 GameObject 上（由 UnitDefinitionRuntimeApplier 自动挂载）。
/// 监听场上所有敌方单位的死亡事件，统计击杀数。
/// 玩家死亡结算时将数据传给 DeathSettlementUI。
/// </summary>
public class SessionStatsTracker : MonoBehaviour
{
    public int KillCount { get; private set; }

    private void Awake()
    {
        KillCount = 0;
    }

    private void OnEnable()
    {
        UnitDeathController.OnAnyEnemyDied += HandleEnemyDied;
    }

    private void OnDisable()
    {
        UnitDeathController.OnAnyEnemyDied -= HandleEnemyDied;
    }

    private void HandleEnemyDied(UnitDeathController dc)
    {
        KillCount++;
    }

    /// <summary>回到基地后重置本局统计。</summary>
    public void ResetSession()
    {
        KillCount = 0;
    }
}
