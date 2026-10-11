using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AFP（适应系数 Adaptability Coefficient Point）运行时。
/// 局内专属积分：进本时初始化，击杀敌人累积，用于购买局内增益。
/// 出本（撤离或死亡）后自动重置。
/// </summary>
public class AFPRuntime : MonoBehaviour
{
    public static AFPRuntime Instance { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────
    /// <summary>AFP 数值变化时触发，传入当前值。供 HUD 刷新。</summary>
    public static event Action<int> OnAFPChanged;
    /// <summary>一局(AFP 会话)开始/结束：局内升级这类按局清零的东西订阅它。</summary>
    public static event Action OnSessionBegan;
    public static event Action OnSessionEnded;

    // ── 常量 ──────────────────────────────────────────────────────────────
    public const int BaseStartingAFP = 300;
    public const int AFPCap          = 999999;

    // 探究者科技树节点 ID（每升一级增加起始 AFP）
    private const string SeekerStartingAFPNodeId = "seeker_starting_afp";
    // 探究者每级额外起始 AFP
    private const int SeekerStartingAFPPerLevel  = 50;

    // ── 状态 ──────────────────────────────────────────────────────────────
    public int CurrentAFP   { get; private set; }
    public int StartingAFP  { get; private set; }
    public bool IsInSession { get; private set; }

    // ── Unity 生命周期 ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnEnable()  => UnitDeathController.OnAnyEnemyDied += HandleEnemyDied;
    private void OnDisable() => UnitDeathController.OnAnyEnemyDied -= HandleEnemyDied;

    // 场景加载时检测是否为章节场景，自动开始会话
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsInSession) return;
        if (SaveManager.Player?.activeSession != null)
            BeginSession();
#if UNITY_EDITOR
        else
            TryEditorAutoSession(scene);
#endif
    }

#if UNITY_EDITOR
    /// <summary>仅编辑器：直接在编辑器里打开 RAID 地图 Play（没走选关出发流程、没有 activeSession）时，
    /// 自动开一个测试会话，AFP HUD 才会显示。打包后不存在这段。</summary>
    private void TryEditorAutoSession(Scene scene)
    {
        if (IsInSession) return;
        string path = scene.path ?? "";
        if (path.Contains("/Maps/Raid/"))   // Unity 的 Scene.path 恒为正斜杠
        {
            BeginSession();
            Debug.Log("[AFPRuntime] 编辑器直接进 RAID 地图：自动开始测试 AFP 会话（起始 " + CurrentAFP + "）");
        }
    }

    private void Start()
    {
        // 初始场景里 sceneLoaded 可能早于本对象订阅，补一次检查
        if (!IsInSession && SaveManager.Player?.activeSession == null)
            TryEditorAutoSession(SceneManager.GetActiveScene());
    }
#endif

    // ── 会话控制 ──────────────────────────────────────────────────────────

    /// <summary>进本时调用。初始化 AFP 为起始值。</summary>
    public void BeginSession()
    {
        StartingAFP = BaseStartingAFP + GetSeekerStartingBonus();
        CurrentAFP  = StartingAFP;
        IsInSession = true;
        OnAFPChanged?.Invoke(CurrentAFP);
        OnSessionBegan?.Invoke();
    }

    /// <summary>出本时调用（撤离或死亡结算完成后）。重置状态。</summary>
    public void EndSession()
    {
        CurrentAFP  = 0;
        StartingAFP = 0;
        IsInSession = false;
        OnAFPChanged?.Invoke(CurrentAFP);
        OnSessionEnded?.Invoke();
    }

    // ── AFP 操作 ──────────────────────────────────────────────────────────

    public void AddAFP(int amount)
    {
        if (!IsInSession || amount <= 0) return;
        CurrentAFP = Mathf.Min(CurrentAFP + amount, AFPCap);
        OnAFPChanged?.Invoke(CurrentAFP);
    }

    /// <summary>尝试消耗 AFP。返回 true 表示消耗成功。</summary>
    public bool TrySpendAFP(int cost)
    {
        if (!IsInSession || CurrentAFP < cost) return false;
        CurrentAFP -= cost;
        OnAFPChanged?.Invoke(CurrentAFP);
        return true;
    }

    public bool HasAFP(int amount) => IsInSession && CurrentAFP >= amount;

    // ── 击杀监听 ──────────────────────────────────────────────────────────

    private void HandleEnemyDied(UnitDeathController dc)
    {
        AddAFP(Mathf.RoundToInt(GetAFPYield(dc) * TechTreePerks.Mult("afpGainRate")));      // 科技树「自适应」：AFP 获取率
    }

    private static int GetAFPYield(UnitDeathController dc)
    {
        var binder = dc.GetComponent<UnitDefinitionRuntimeBinder>();
        var def = binder?.UnitDefinitionAsset;
        if (def == null) return 0;
        var id = def.characterIdentity;
        if (id != CharacterIdentity.Enemy && id != CharacterIdentity.Elite && id != CharacterIdentity.Boss)
            return 0;
        return Mathf.Max(0, def.afpYield);
    }

    // ── 探究者科技树加成 ──────────────────────────────────────────────────

    private static int GetSeekerStartingBonus()
    {
        if (TechTreeRuntime.Instance == null) return 0;
        int level = TechTreeRuntime.Instance.GetNodeLevel(SeekerStartingAFPNodeId);
        return level * SeekerStartingAFPPerLevel;
    }
}
