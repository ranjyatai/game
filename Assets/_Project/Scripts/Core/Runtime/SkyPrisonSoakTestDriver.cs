using System;
using UnityEngine;

/// <summary>
/// 无人浸测机器人——不需要人坐在那里玩。自动、持续地喂移动/冲刺/跳跃/攻击输入，
/// 挂后台跑几十分钟甚至一整晚，配合 SkyPrisonFrameSpikeWatchdog 的定时自动上报，
/// 睡一觉起来 Discord 里就有一整晚的性能数据，不用人工守着卡顿的那一刻按F10。
///
/// 直接调用 UnitActionController 的公开方法（SubmitMoveIntent/RequestJump/RequestAttack），
/// 不模拟原始按键——不依赖窗口焦点、不依赖 OS 级输入注入，更稳定可靠。
///
/// 默认只在命令行带 -soaktest 参数时生效，不会意外出现在正常发布的Build里、
/// 也不会干扰你自己手动测试的场次。
/// </summary>
public class SkyPrisonSoakTestDriver : MonoBehaviour
{
    [Tooltip("只有命令行带这个参数启动时才生效。关掉这个开关＝随时都跑（仅用于专门的浸测Build）。")]
    [SerializeField] private bool requireCommandLineArg = true;
    [SerializeField] private string commandLineArg = "-soaktest";

    [Header("决策节奏")]
    [Tooltip("每次换方向/换动作之间维持多久，取这个区间内的随机值。")]
    [SerializeField] private float minHoldSeconds = 1.2f;
    [SerializeField] private float maxHoldSeconds = 4f;

    [Header("动作概率（每次换决策时各自独立判定）")]
    [SerializeField, Range(0f, 1f)] private float sprintChance = 0.5f;
    [SerializeField, Range(0f, 1f)] private float jumpChance = 0.35f;
    [SerializeField, Range(0f, 1f)] private float lightAttackChance = 0.2f;
    [SerializeField, Range(0f, 1f)] private float heavyAttackChance = 0.1f;
    [SerializeField, Range(0f, 1f)] private float idleChance = 0.15f; // 偶尔完全站着不动，别一直在动

    [Tooltip("找不到玩家/ActionController时，隔多久重新找一次。")]
    [SerializeField] private float playerSearchInterval = 2f;

    private UnitActionController _actionController;
    private float _nextPlayerSearchTime;
    private float _nextDecisionTime;
    private Vector2 _currentDirection;
    private bool _currentRunHeld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isPlaying)
            return;

        if (FindObjectOfType<SkyPrisonSoakTestDriver>() != null)
            return;

        var go = new GameObject("[SkyPrisonSoakTestDriver]");
        go.AddComponent<SkyPrisonSoakTestDriver>();
    }

    private void Awake()
    {
        if (requireCommandLineArg && !HasCommandLineArg())
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        Debug.Log("[SkyPrisonSoakTestDriver] 浸测机器人已启动——自动喂移动/跳跃/攻击输入，挂后台长时间跑。");
    }

    private bool HasCommandLineArg()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], commandLineArg, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void Update()
    {
        if (_actionController == null)
        {
            if (Time.unscaledTime < _nextPlayerSearchTime)
                return;

            _nextPlayerSearchTime = Time.unscaledTime + playerSearchInterval;
            TryFindActionController();
            if (_actionController == null)
                return;
        }

        if (Time.unscaledTime >= _nextDecisionTime)
            PickNewDecision();

        _actionController.SubmitMoveIntent(_currentDirection, _currentRunHeld, false);
    }

    private void TryFindActionController()
    {
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO == null)
            return;

        _actionController = playerGO.GetComponentInChildren<UnitActionController>(true);
    }

    private void PickNewDecision()
    {
        _nextDecisionTime = Time.unscaledTime + UnityEngine.Random.Range(minHoldSeconds, maxHoldSeconds);

        if (UnityEngine.Random.value < idleChance)
        {
            _currentDirection = Vector2.zero;
            _currentRunHeld = false;
            return;
        }

        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        _currentDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        _currentRunHeld = UnityEngine.Random.value < sprintChance;

        if (UnityEngine.Random.value < jumpChance)
            _actionController.RequestJump();

        if (UnityEngine.Random.value < lightAttackChance)
            _actionController.RequestLightAttack();
        else if (UnityEngine.Random.value < heavyAttackChance)
            _actionController.RequestHeavyAttack();
    }
}
