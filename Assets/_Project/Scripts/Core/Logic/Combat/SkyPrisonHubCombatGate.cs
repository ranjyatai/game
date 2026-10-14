using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 主城（Hub 地图）里"战斗才扣 LP、才显示 HP/LP"的闸门。
///
/// 规则：在 Hub 地图里，玩家平时是安全状态——LP 不消耗（奔跑、闪避随便用），HUD 的 HP/LP 整块隐藏。
/// 一旦玩家出招（轻/重/空中攻击、突刺、机枪每一发），或者玩家受到伤害，就进入"主城战斗"：
/// HP/LP 显示出来，LP 正常消耗。最后一次出招/受击之后 <see cref="CombatHoldSeconds"/> 秒没有新的战斗行为，
/// 就回到安全状态（HP/LP 再次隐藏、LP 不再消耗）。
///
/// 判定"是不是 Hub 地图"：当前活动场景路径含 /Maps/Hub/，或场景名是 Hub_Base。
/// 局内地图完全不受影响（<see cref="IsSafeNow"/> 恒为 false）。
/// </summary>
public static class SkyPrisonHubCombatGate
{
    /// <summary>最后一次战斗行为之后，保持"战斗"状态多少秒。</summary>
    public const float CombatHoldSeconds = 6f;

    private static float s_combatUntil = -1f;
    private static int s_cachedSceneHandle = int.MinValue;
    private static bool s_cachedIsHub;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_combatUntil = -1f;
        s_cachedSceneHandle = int.MinValue;
        s_cachedIsHub = false;
    }

    /// <summary>当前活动场景是不是主城地图。</summary>
    public static bool IsHubScene
    {
        get
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.handle == s_cachedSceneHandle)
                return s_cachedIsHub;

            string path = scene.path ?? string.Empty;
            s_cachedIsHub = path.Contains("/Maps/Hub/") || scene.name == "Hub_Base";
            s_cachedSceneHandle = scene.handle;
            return s_cachedIsHub;
        }
    }

    /// <summary>主城里最近有没有战斗行为（出招/受击）。</summary>
    public static bool HubCombatActive => Time.unscaledTime < s_combatUntil;

    /// <summary>主城里的安全状态：LP 不消耗、HUD 隐藏。局内地图永远是 false。</summary>
    public static bool IsSafeNow => IsHubScene && !HubCombatActive;

    /// <summary>玩家出招或受击时调用。局内地图里什么都不做。</summary>
    public static void NotifyCombat()
    {
        if (!IsHubScene)
            return;
        s_combatUntil = Time.unscaledTime + CombatHoldSeconds;
    }
}
