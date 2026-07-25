using System;
using UnityEngine;

/// <summary>
/// 全局"玩家是否处于战斗中"信号源。
///
/// 判定依据：任意敌对阵营（Enemy/Elite/Boss）单位的 UnitPerceptionRuntime.CanSeePlayer
/// 为true，即视为"进入战斗"；所有敌对单位都看不见玩家之后，维持 combatExitHoldDuration
/// 秒再判定"脱离战斗"（跟 SkyPrisonHUDCombatVisibilityController 里已有的那套"战斗后
/// 持续显示N秒"衰减手感保持一致，只是那边一直没人真正驱动过）。
///
/// 地图BGM切换、HUD战斗可见性等任何需要"现在算不算战斗"的系统，都订阅这一个共享信号，
/// 不用各自重复扫一遍 UnitPerceptionRuntime.ActiveInstances。
/// </summary>
public sealed class PlayerCombatStateRuntime : MonoBehaviour
{
    private const string BootstrapObjectName = "SkyPrison_PlayerCombatStateRuntime";
    private const float PollInterval = 0.15f;

    [SerializeField] private float combatExitHoldDuration = 2f;

    public static bool IsInCombat { get; private set; }
    public static event Action<bool> OnCombatStateChanged;

    private static float s_lastSeenTime = -9999f;
    private float _nextPollTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<PlayerCombatStateRuntime>() != null)
            return;

        GameObject go = new GameObject(BootstrapObjectName);
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<PlayerCombatStateRuntime>();
    }

    private void Update()
    {
        if (Time.time < _nextPollTime)
            return;
        _nextPollTime = Time.time + PollInterval;

        bool seenNow = AnyHostileUnitSeesPlayer();
        if (seenNow)
            s_lastSeenTime = Time.time;

        bool shouldBeInCombat = seenNow || (Time.time - s_lastSeenTime < combatExitHoldDuration);
        if (shouldBeInCombat != IsInCombat)
        {
            IsInCombat = shouldBeInCombat;
            OnCombatStateChanged?.Invoke(IsInCombat);
        }
    }

    private static bool AnyHostileUnitSeesPlayer()
    {
        var instances = UnitPerceptionRuntime.ActiveInstances;
        for (int i = 0; i < instances.Count; i++)
        {
            UnitPerceptionRuntime perception = instances[i];
            if (perception == null || !perception.CanSeePlayer)
                continue;

            UnitDefinition def = perception.UnitDefinition;
            if (def == null)
                continue;

            if (def.characterIdentity == CharacterIdentity.Enemy
                || def.characterIdentity == CharacterIdentity.Elite
                || def.characterIdentity == CharacterIdentity.Boss)
                return true;
        }

        return false;
    }
}
