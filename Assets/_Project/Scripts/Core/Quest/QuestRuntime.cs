using System;
using UnityEngine;

/// <summary>
/// 任务运行时单例（与 CurrencyRuntime/InventoryRuntimeBootstrap 同模式，由
/// SkyPrisonRuntimeSystemsBootstrapper 创建）。定时扫描 QuestDatabase 里的全部
/// 任务：解锁条件成立就自动开始(不需要玩家手动接取)；进行中的任务全部必做目标
/// 达成就自动完成，并执行 onCompleteActions(可能设置阶段flag/给奖励)。
///
/// 任务的"进行中/已完成"单独记账(不复用 completedQuestFlags)，但完成时会顺带
/// SetQuestFlag(questId)，这样已有的对话变体条件/触发器条件可以直接拿 questId
/// 当flag用，两边判定同一套东西。
/// </summary>
public class QuestRuntime : MonoBehaviour
{
    public static QuestRuntime Instance { get; private set; }

    private const string DatabaseResourcesPath = "QuestDatabase";

    [SerializeField] private float scanIntervalSeconds = 1f;
    [SerializeField] private bool debugLogs = false;

    [Tooltip("玩家死亡判定时必须在死亡敌人这个半径内，击杀才计入任务计数器——固定值，" +
             "不按每个单位的视野/听觉各自浮动(视野是AI感知用的字段，跟\"这次死算不算" +
             "玩家打的\"是两件不相关的事，瞎眼/无视野的单位一样应该能正常记入击杀)。" +
             "比听觉最大范围(30)还要更宽松一点——中毒/灼烧这类持续伤害经常是玩家已经" +
             "转身走开、怪才真正断气，只要还没跑出这个范围就该算数；跟SleepRadius(AI" +
             "远距离休眠剔除，40)同一个量级，不是又拍一个新数字。")]
    [SerializeField] private float killCreditRadius = 40f;

    public event Action<QuestDefinition> OnQuestStarted;
    public event Action<QuestDefinition> OnQuestCompleted;

    private QuestDatabase _database;
    private float _scanTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _database = Resources.Load<QuestDatabase>(DatabaseResourcesPath);
        if (_database == null)
            Debug.LogWarning($"[QuestRuntime] 找不到 Resources/{DatabaseResourcesPath}，任务系统不会运行。");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        UnitDeathController.OnAnyEnemyDied += HandleAnyEnemyDied;
    }

    private void OnDisable()
    {
        UnitDeathController.OnAnyEnemyDied -= HandleAnyEnemyDied;
    }

    private void Start()
    {
        ScanAll();
    }

    // 击杀类任务目标要用的计数器——跟 SessionStatsTracker 那份"本局击杀数"是两回事
    // (那个回基地就清零，这个是存档永久累加，任务进度不能因为跑完一趟就没了)。
    // 每次死亡记三个粒度的key，任务作者按需要选用哪个粒度，不用另外为每个任务
    // 单独接一遍死亡事件。
    private void HandleAnyEnemyDied(UnitDeathController dc)
    {
        if (SaveManager.Player == null || dc == null) return;

        // 玩家必须在死亡点 killCreditRadius 范围内才计数——固定值，大怪小怪一视
        // 同仁，不跟任何单位自身的视野/听觉字段挂钩(那些是AI感知用的，跟"这次死
        // 算不算玩家打的"是两件不相关的事)。友方单位/陷阱/脚本强制处死这类玩家
        // 没有实际参与的死亡不会被计入。
        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        if (playerGo == null) return;
        if (Vector3.Distance(playerGo.transform.position, dc.transform.position) > killCreditRadius) return;

        SaveManager.Player.IncrementCounter("kill_any", 1);

        UnitDefinition ud = dc.GetComponent<UnitDefinitionRuntimeBinder>()?.UnitDefinitionAsset;
        if (ud == null) return;

        if (!string.IsNullOrWhiteSpace(ud.unitId))
            SaveManager.Player.IncrementCounter($"kill_unit_{ud.unitId}", 1);
        SaveManager.Player.IncrementCounter($"kill_identity_{ud.characterIdentity}", 1);
    }

    private void Update()
    {
        _scanTimer -= Time.deltaTime;
        if (_scanTimer > 0f) return;

        _scanTimer = Mathf.Max(0.1f, scanIntervalSeconds);
        ScanAll();
    }

    /// <summary>手动触发一次全量扫描——完成关键动作(拾取物品/设施升级等)后可以主动调用，
    /// 不用等下一次定时扫描才反应过来。</summary>
    public void ScanAll()
    {
        if (_database == null || SaveManager.Player == null) return;

        foreach (QuestDefinition quest in _database.quests)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.questId)) continue;
            if (IsCompleted(quest.questId)) continue;

            if (!IsActive(quest.questId))
            {
                // 有归属NPC的任务不参与自动扫描开始——giverNpc本身就是"需要仪式感
                // 接取"的唯一标志，不需要另外维护一个容易忘记同步的bool。解锁条件对
                // 它们来说只是"允许被接取的前提"，真正开始只能通过 TryAcceptQuest
                // (NPC对话选项调用)。没有归属NPC的任务(环境触发类)保持原来的自动开始。
                if (quest.giverNpc != null) continue;
                if (!quest.AreUnlockConditionsMet(debugLogs)) continue;
                StartQuest(quest);
            }

            // 完成也是同一个道理——有归属NPC的任务不参与后台静默自动完成，必须由玩家
            // 在对话里点开"显示任务列表"选项、选中这条已满足完成条件的任务才弹出结算
            // 界面(见 NPCDialogueWindowController.OnQuestSelected)，不然结算界面还没
            // 弹出来，任务已经在这里被静默完成、奖励静默到账了，玩家完全感知不到
            // "任务完成"这个瞬间。
            if (quest.giverNpc != null) continue;

            if (quest.AreRequiredObjectivesComplete(debugLogs))
                CompleteQuest(quest);
        }
    }

    /// <summary>结算界面确认领取时调用——跟被动扫描完成走的是同一个CompleteQuest，
    /// 只是多传一个"背包装不下时改送仓库"的开关(结算界面自己按SimulateAdd判断过
    /// 要不要传true)。</summary>
    public void CompleteQuestFromSettlement(QuestDefinition quest, bool sendItemsToStash)
    {
        if (quest == null || !IsActive(quest.questId)) return;
        CompleteQuest(quest, sendItemsToStash);
    }

    /// <summary>玩家在NPC对话里主动选择接受这个任务——有归属NPC(giverNpc!=null)的任务
    /// 只能靠这个方法开始，不会被 ScanAll() 自动扫描到。解锁条件依然要满足
    /// (退化成"允许接取的前提"，不是被跳过)，已经进行中/已完成的任务重复调用直接
    /// 忽略，不会重复触发 OnQuestStarted。</summary>
    public bool TryAcceptQuest(QuestDefinition quest)
    {
        if (quest == null || string.IsNullOrWhiteSpace(quest.questId)) return false;
        if (SaveManager.Player == null) return false;

        if (IsActive(quest.questId) || IsCompleted(quest.questId)) return false;
        if (!quest.AreUnlockConditionsMet(debugLogs)) return false;

        StartQuest(quest);
        return true;
    }

    public bool IsActive(string questId)
        => SaveManager.Player != null && SaveManager.Player.activeQuestIds.Contains(questId);

    public bool IsCompleted(string questId)
        => SaveManager.Player != null && SaveManager.Player.completedQuestIds.Contains(questId);

    public QuestDefinition GetQuestById(string questId)
        => _database != null ? _database.quests.Find(q => q != null && q.questId == questId) : null;

    /// <summary>把某条任务设为"追踪中任务"——同一时间只会有一条，供以后地图画面显示
    /// 追踪用。传null/空字符串=取消追踪中任务标记。</summary>
    public void SetTrackedQuest(QuestDefinition quest)
    {
        if (SaveManager.Player == null) return;
        SaveManager.Player.trackedQuestId = quest != null ? quest.questId : "";
    }

    public string GetTrackedQuestId()
        => SaveManager.Player != null ? SaveManager.Player.trackedQuestId : "";

    public QuestDefinition GetTrackedQuest()
    {
        string id = GetTrackedQuestId();
        return string.IsNullOrWhiteSpace(id) ? null : GetQuestById(id);
    }

    /// <summary>这条任务现在能不能被玩家接取——有归属NPC+还没接过/完成+解锁条件
    /// 成立。单纯判断任务本身此刻是否处于"可接取"状态，供对话选项显式指定
    /// questToAccept 时做可见性判断(一个NPC同时能给不止一条任务、每条任务各自一个
    /// 子选项的场景，需要逐条单独判断，不能只看"NPC名下随便一条")。</summary>
    public bool IsQuestAcceptable(QuestDefinition quest)
    {
        if (quest == null || string.IsNullOrWhiteSpace(quest.questId)) return false;
        if (quest.giverNpc == null) return false;
        if (IsActive(quest.questId) || IsCompleted(quest.questId)) return false;
        return quest.AreUnlockConditionsMet(debugLogs);
    }

    /// <summary>某个NPC名下当前全部"可以被接取"的任务(不止第一条)——NPC对话的
    /// "接受任务"入口选项点开后，用这个列出全部可选任务名字给玩家挑。</summary>
    public System.Collections.Generic.List<QuestDefinition> GetAcceptableQuestsForNpc(UnitDefinition npc)
    {
        var result = new System.Collections.Generic.List<QuestDefinition>();
        if (_database == null || npc == null) return result;

        foreach (QuestDefinition quest in _database.quests)
        {
            if (quest == null || quest.giverNpc != npc) continue;
            if (!IsQuestAcceptable(quest)) continue;
            result.Add(quest);
        }

        return result;
    }

    /// <summary>某个NPC名下当前"进行中"的任务——NPC对话"显示任务列表"选项用这个筛。</summary>
    public System.Collections.Generic.List<QuestDefinition> GetActiveQuestsForNpc(UnitDefinition npc)
    {
        var result = new System.Collections.Generic.List<QuestDefinition>();
        if (_database == null || npc == null) return result;

        foreach (var quest in _database.quests)
        {
            if (quest == null || quest.giverNpc != npc) continue;
            if (IsActive(quest.questId)) result.Add(quest);
        }

        return result;
    }

    /// <summary>某个NPC名下当前"进行中"、且当前目标正好是"交付物品给这个NPC"的
    /// 任务——交付对象不一定是giverNpc(比如A给的任务，要求交给B)，不能复用
    /// GetActiveQuestsForNpc(那个是按giverNpc筛的)。NPC对话"显示任务列表"选项
    /// 除了列giverNpc给的任务，也要把"轮到来找我交付"的任务列进来，玩家才有地方
    /// 点开触发交付窗口。</summary>
    public System.Collections.Generic.List<QuestDefinition> GetActiveQuestsWithCurrentDeliveryTarget(UnitDefinition npc)
    {
        var result = new System.Collections.Generic.List<QuestDefinition>();
        if (_database == null || npc == null) return result;

        foreach (var quest in _database.quests)
        {
            if (quest == null || !IsActive(quest.questId)) continue;

            QuestObjective current = quest.GetCurrentObjective();
            if (current == null || current.completionConditions.Count != 1) continue;

            LogicSentenceInstance c = current.completionConditions[0];
            if (c == null || c.templateId != "cond_has_delivered_item_to_npc") continue;

            UnitDefinition targetNpc = c.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
            if (targetNpc == npc) result.Add(quest);
        }

        return result;
    }

    /// <summary>全部"进行中"的任务，主线排前面——玩家任务日志面板用这个，不按NPC筛。</summary>
    public System.Collections.Generic.List<QuestDefinition> GetActiveQuests()
    {
        var result = new System.Collections.Generic.List<QuestDefinition>();
        if (_database == null) return result;

        foreach (var quest in _database.quests)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.questId)) continue;
            if (IsActive(quest.questId)) result.Add(quest);
        }

        result.Sort((a, b) => a.category.CompareTo(b.category)); // Mainline=0排在Side=1前面
        return result;
    }

    /// <summary>全部"已完成"的任务，主线排前面——玩家任务日志面板的"已完成"分页用。</summary>
    public System.Collections.Generic.List<QuestDefinition> GetCompletedQuests()
    {
        var result = new System.Collections.Generic.List<QuestDefinition>();
        if (_database == null) return result;

        foreach (var quest in _database.quests)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.questId)) continue;
            if (IsCompleted(quest.questId)) result.Add(quest);
        }

        result.Sort((a, b) => a.category.CompareTo(b.category)); // Mainline=0排在Side=1前面
        return result;
    }

    private void StartQuest(QuestDefinition quest)
    {
        SaveManager.Player.activeQuestIds.Add(quest.questId);
        SnapshotObjectiveCounterBaselines(quest);
        // 防御性清一次目标锁存——正常情况下放弃/完成时已经清过，理论上不会有残留，
        // 这里只是保证"每次重新开始，目标进度状态一定是干净的"，不依赖别处清理
        // 有没有漏掉。
        SaveManager.Player.ClearObjectiveLatches(quest.questId);
        OnQuestStarted?.Invoke(quest);

        if (debugLogs)
            Debug.Log($"[QuestRuntime] 任务开始：{quest.questId}");
    }

    /// <summary>任务开始那一刻，给它每条目标依赖的计数器(击杀数/对话次数)记一次"起点
    /// 快照"——TriggerConditionEvaluator判定这些计数器型条件时会用"现在值-起点快照"，
    /// 不看绝对值，这样任务开始之前(包括接任务这个动作本身)发生的事不会被误判成
    /// "已经推进了目标"。</summary>
    private static void SnapshotObjectiveCounterBaselines(QuestDefinition quest)
    {
        foreach (var objective in quest.objectives)
        {
            foreach (var condition in objective.completionConditions)
            {
                string counterKey = TriggerConditionEvaluator.GetCounterKeyForBaseline(condition);
                if (counterKey == null) continue;
                SaveManager.Player.SetCounterBaseline(quest.questId, counterKey, SaveManager.Player.GetCounter(counterKey));
            }
        }
    }

    /// <summary>玩家主动放弃一条进行中的任务——回退到"未接取"的初始状态，不是设成
    /// 完成/失败。清掉起点快照，解锁条件依然满足的话可以重新从头接取，不会永久
    /// 拉黑；不跑onCompleteActions/不设完成flag，这跟CompleteQuest是两条完全
    /// 不同的路径。</summary>
    public void AbandonQuest(QuestDefinition quest)
    {
        if (quest == null || string.IsNullOrWhiteSpace(quest.questId)) return;
        if (SaveManager.Player == null) return;
        if (!IsActive(quest.questId)) return;

        SaveManager.Player.activeQuestIds.Remove(quest.questId);
        SaveManager.Player.ClearCounterBaselines(quest.questId);
        SaveManager.Player.ClearObjectiveLatches(quest.questId);

        if (SaveManager.Player.trackedQuestId == quest.questId)
            SaveManager.Player.trackedQuestId = "";

        if (debugLogs)
            Debug.Log($"[QuestRuntime] 任务放弃：{quest.questId}");
    }

    /// <summary>物品奖励一律走下面GrantRewardItems手动处理，不能让onCompleteActions
    /// 里的act_add_item_to_bag按它自己默认的"直接AddItem、装不下的部分静默丢弃"跑
    /// 一遍——不管sendItemsToStash是勾是没勾，装得下的部分永远先进背包；只有背包
    /// 真正放不下的那部分数量，勾了sendItemsToStash才转送仓库。之前的写法是"勾了
    /// 就把整批奖励物品全部送仓库"(哪怕背包明明还有空位)，是错的——用户明确纠正过
    /// 这不是"二选一"，是"背包优先、仓库接手溢出"。货币奖励(act_add_currency)不
    /// 受影响，始终照常发放——货币没有"装不下"这个概念。</summary>
    private void CompleteQuest(QuestDefinition quest, bool sendItemsToStash = false)
    {
        SaveManager.Player.activeQuestIds.Remove(quest.questId);
        SaveManager.Player.completedQuestIds.Add(quest.questId);
        SaveManager.Player.SetQuestFlag(quest.questId);
        SaveManager.Player.ClearCounterBaselines(quest.questId);
        // 目标锁存不能在这里清——任务日志"已完成"分页复用的还是同一套BuildNodeChain/
        // IsObjectiveEffectivelyComplete实时判定，没有锁存记忆的话，"收集"这类看
        // 背包即时数量的目标，会因为东西已经交付出去了(背包里现在是0个)重新判定成
        // "未完成"，明明整条任务都结算过了，节点链却显示成卡在半路。锁存只应该在
        // "放弃任务"(真的要回退到最初状态)时才清，任务完成后要永久保留，"这条任务
        // 每一步都做过"这件事不会因为完成之后道具用掉了就失效。

        // 追踪中任务如果正好是这条，完成后自动摘掉标记——不然地图上会一直显示一条
        // 已经完成、不再需要追踪的任务。
        if (SaveManager.Player.trackedQuestId == quest.questId)
            SaveManager.Player.trackedQuestId = "";

        foreach (var action in quest.onCompleteActions)
        {
            if (action != null && action.templateId == "act_add_item_to_bag")
                continue; // 跳过默认的背包路径，下面GrantRewardItems统一手动处理
            TriggerActionExecutor.Execute(action, debugLogs);
        }

        GrantRewardItems(quest, sendItemsToStash);

        OnQuestCompleted?.Invoke(quest);

        if (debugLogs)
            Debug.Log($"[QuestRuntime] 任务完成：{quest.questId}");
    }

    /// <summary>背包优先、仓库接手溢出——每种奖励物品先尝试AddItem进背包，能塞多少
    /// 塞多少；剩下真正塞不进背包的数量，只有sendItemsToStash为true才转送仓库，
    /// 否则(理论上结算界面在这种情况下应该已经拦住了确认按钮，这里只是兜底)照旧
    /// 静默丢弃，跟act_add_item_to_bag自己原本的行为一致。</summary>
    private static void GrantRewardItems(QuestDefinition quest, bool sendItemsToStash)
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        foreach (var (item, amount) in quest.GetRewardItems())
        {
            int leftover = inv != null ? inv.AddItem(item, amount) : amount;
            if (leftover > 0 && sendItemsToStash)
                GrantItemToStash(item, leftover);
        }
    }

    /// <summary>仓库分4页，每页独立一份InventoryRuntime——按页序依次尝试塞入，
    /// 跟AddItem同一套"塞满一页的剩余空间"逻辑，直到全部塞完或者已解锁的仓库页
    /// 也放不下为止(理论上仓库比背包大得多，这种情况应该很少见，目前没有再往下
    /// 的兜底)。</summary>
    private static void GrantItemToStash(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || StashRuntime.Instance == null) return;

        int remaining = amount;
        for (int page = 0; page < StashRuntime.MaxPages && remaining > 0; page++)
        {
            if (!StashRuntime.Instance.IsPageUnlocked(page)) continue;
            InventoryRuntime inv = StashRuntime.Instance.GetPage(page);
            if (inv == null) continue;
            remaining = inv.AddItem(item, remaining);
        }
    }
}
