using UnityEngine;

/// <summary>
/// 玩家血量跨场景继承：进 RAID、回主城、读档都带着当前血量，医疗站这类回血设施才有意义。
/// 玩家每次切场景都是重新生成的（满血），所以单独记一份「血量百分比」放在存档数据里：
///   - 玩家正常存活且场景已经稳定时，每隔一小段时间把当前血量百分比写进 SaveManager.Player；
///   - 新场景里玩家生成、启动流程结束后，把记下的百分比还原回去；
///   - 记录值 &lt;= 0（死亡）或 &gt;= 1（满血）或没有记录（-1）都当作满血，不做处理——复活 / 新游戏 / 老存档都走这条。
/// 不绑任何场景对象，运行时自己常驻。
/// </summary>
public sealed class PlayerHealthCarryover : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isPlaying) return;
        if (FindObjectOfType<PlayerHealthCarryover>() != null) return;
        var go = new GameObject("[PlayerHealthCarryover]");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerHealthCarryover>();
    }

    private const float RecordInterval = 0.5f;

    private UnitHealthController _tracked;   // 当前场景里的玩家血量控制器
    private bool _restored;                  // 当前这个玩家实例是否已经做过还原（还原之前不能记录，否则会把刚生成的满血盖掉存好的值）
    private float _timer;
    private float _stableTime;               // 玩家出现、读条结束后已经稳定了多久

    private void Update()
    {
        // 读条、冻结世界（选关界面 / 暂停）期间什么都不做
        if (SceneLoader.IsLoading || Time.timeScale <= 0f)
        {
            _stableTime = 0f;
            return;
        }

        var player = FindPlayerHealth();
        if (player != _tracked)
        {
            _tracked = player;
            _restored = false;
            _stableTime = 0f;
        }
        if (_tracked == null) return;

        _stableTime += Time.unscaledDeltaTime;
        // 等玩家的属性 / 装备修正这些启动流程跑完（上限会在启动阶段被状态系统重新设一遍），稳定一小会儿再还原
        if (!_restored)
        {
            if (_stableTime < 1.0f) return;
            Restore(_tracked);
            _restored = true;
            return;
        }

        _timer += Time.unscaledDeltaTime;
        if (_timer < RecordInterval) return;
        _timer = 0f;
        Record(_tracked);
    }

    private static UnitHealthController FindPlayerHealth()
    {
        // 用项目里的「当前玩家」权威（HUD 绑血条也是用它），不要靠单位定义里的 characterIdentity 去猜：
        // 场景里大量单位的 identity 都是 Player（实测 86 个），按它找会抓到随便一个别的单位。
        var current = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (current == null) return null;
        return current.GetComponent<UnitHealthController>() ?? current.GetComponentInChildren<UnitHealthController>(true);
    }

    private static void Restore(UnitHealthController hc)
    {
        var p = SaveManager.Player;
        if (p == null || hc.IsDead) return;
        float percent = p.playerHealthPercent;
        if (percent <= 0f || percent >= 1f) return;   // 没记录 / 死亡 / 满血：保持满血
        hc.RestoreHealthPercent(percent);
    }

    private static void Record(UnitHealthController hc)
    {
        var p = SaveManager.Player;
        if (p == null) return;
        // 死亡 / 濒死时记成「无记录」，复活或下次进图就是满血，不会带着 0 血进场景
        if (hc.IsDead || hc.CurrentHealth <= 0f) { p.playerHealthPercent = -1f; return; }

        // 灰色损失(灰炉)会把最大生命压低：必须按「没有灰色时的最大生命」算百分比，
        // 否则带着灰色撤离、血条满的会被记成 100%，回主城灰色清零后变成满血（白嫖治疗）。
        float baseMax = hc.MaxHealth;
        var gray = hc.GetComponentInParent<SkyPrisonGrayLoss>(true);
        if (gray != null) baseMax = gray.BaseMaxHealth;
        p.playerHealthPercent = baseMax > 0.01f ? Mathf.Clamp01(hc.CurrentHealth / baseMax) : hc.HealthPercent01;
    }
}
