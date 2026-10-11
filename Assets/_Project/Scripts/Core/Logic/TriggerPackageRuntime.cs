using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 驱动一组 TriggerPackage 在运行时执行。
///
/// 初始加载时自动生成：进入场景后会从 MapTriggerRegistry（Resources）按当前场景名
/// 查到要运行的触发器包，并自动创建本节点——无需手动在场景里挂任何组件。
///
/// 支持的动机（Motive）事件：
///   event_initialize    — Start 时触发一次
///   event_every_seconds — 每隔 N 秒触发（slots: intervalSeconds）
/// </summary>
[DisallowMultipleComponent]
public class TriggerPackageRuntime : MonoBehaviour
{
    [SerializeField] private List<TriggerPackage> packages = new List<TriggerPackage>();

    private const string RuntimeObjectName = "SkyPrisonTriggerRuntime";
    private const string IntervalSlotId = "intervalSeconds";

    private readonly Dictionary<TriggerPackage.TriggerRule, float> _cooldownUntil =
        new Dictionary<TriggerPackage.TriggerRule, float>();

    private static bool _sceneLoadedSubscribed;

    // ── 初始加载自动生成 ──────────────────────────────────────────────────────

    // 关闭域重载（Enter Play Mode Options）时静态字段不会自动归零，这里手动重置，
    // 避免上一次 Play 残留的订阅状态导致本次不再生成节点。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _sceneLoadedSubscribed = false;
        MapTriggerRegistry.ClearCache();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateAfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;

        EnsureForActiveScene();

        if (!_sceneLoadedSubscribed)
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            _sceneLoadedSubscribed = true;
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.isPlaying)
            EnsureForActiveScene();
    }

    private static void EnsureForActiveScene()
    {
        if (FindObjectOfType<TriggerPackageRuntime>() != null)
            return;

        Scene scene = SceneManager.GetActiveScene();

        MapTriggerRegistry registry = MapTriggerRegistry.LoadOrNull();
        if (registry == null)
        {
            Debug.LogWarning($"[TriggerPackageRuntime] 找不到 Resources/{MapTriggerRegistry.ResourceName}，跳过触发器生成。请运行菜单「Sky Prison/触发器/重建触发器注册表」。");
            return;
        }

        List<TriggerPackage> pkgs = registry.GetForScene(scene.name, scene.path);
        if (pkgs == null || pkgs.Count == 0)
        {
            Debug.Log($"[TriggerPackageRuntime] 场景 \"{scene.name}\" 未配置触发器包，跳过。");
            return;
        }

        GameObject root = GameObject.Find("SkyPrisonRuntimeSystems");
        GameObject go = new GameObject(RuntimeObjectName);
        if (root != null)
            go.transform.SetParent(root.transform, false);

        TriggerPackageRuntime runtime = go.AddComponent<TriggerPackageRuntime>();
        for (int i = 0; i < pkgs.Count; i++)
            runtime.AddPackage(pkgs[i]);

        Debug.Log($"[TriggerPackageRuntime] 已为场景 \"{scene.name}\" 生成触发器节点，载入 {pkgs.Count} 个触发器包。");
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────────

    private void Start()
    {
        FireMotiveEvent("event_initialize");
        StartPeriodicRules();
    }

    // EquipmentRuntime 是 DontDestroyOnLoad 单例、事件是静态的，天然适合在这里
    // 桥接成 event_item_equipped/unequipped 这两个动机——跟 event_initialize 是
    // 同一个"把外部C#事件接进触发器"的职责，不需要另开一个专门的桥接类。
    private void OnEnable()
    {
        EquipmentRuntime.OnEquipped += HandleEquipped;
        EquipmentRuntime.OnUnequipped += HandleUnequipped;
    }

    private void OnDisable()
    {
        EquipmentRuntime.OnEquipped -= HandleEquipped;
        EquipmentRuntime.OnUnequipped -= HandleUnequipped;
    }

    private void HandleEquipped(EquipmentSlotType slot, InventoryItemEntry entry) => FireMotiveEvent("event_item_equipped");

    private void HandleUnequipped(EquipmentSlotType slot, InventoryItemEntry entry) => FireMotiveEvent("event_item_unequipped");

    // ── 公开接口 ──────────────────────────────────────────────────────────────

    public void AddPackage(TriggerPackage pkg)
    {
        if (pkg != null && !packages.Contains(pkg))
            packages.Add(pkg);
    }

    /// <summary>手动立刻执行一个触发器包——不经过场景注册表(MapTriggerRegistry)/事件
    /// 动机/条件门槛，直接把每条启用中的规则的动作列表按顺序跑一遍。用户明确要求
    /// "任务目标专属演出"用的触发包不该跟场景自动生成、按动机+条件驱动的那一套
    /// (AutoCreateAfterSceneLoad/FireMotiveEvent)混在一起——这是完全独立的第二条
    /// 执行路径，不需要场景里已经存在一个TriggerPackageRuntime实例。
    ///
    /// 用协程跑，不是普通同步方法——遇到act_show_dialogue_line会真的等这一句播完
    /// (自动播放计时结束、或玩家按键消失)才继续下一条动作，不是"发出去就不管"。
    /// 原来第一版是同步foreach一次跑完所有动作，连续好几条act_show_dialogue_line
    /// 会挤在同一帧里：第一句字幕刚设好文字，下一条动作立刻把文字覆盖掉，玩家
    /// 什么都看不清，表现成"演出被跳过"——这正是用户报的那个bug的根源。其它类型
    /// 的动作维持即时执行，跟场景自动触发的那一套(ExecuteActions)保持一致，不是
    /// 每种动作都要插入等待。调用方(NPCDialogueWindowController.ShowNpcResponseForObjective)
    /// 自己用StartCoroutine跑这个方法。</summary>
    public static IEnumerator ExecuteImmediatelyCoroutine(TriggerPackage pkg, System.Action onComplete = null)
    {
        if (pkg != null)
        {
            foreach (TriggerPackage.TriggerRule rule in pkg.rules)
            {
                if (rule == null || !rule.enabled) continue;

                List<LogicSentenceInstance> actions = rule.sentenceRoot?.actionGroup?.items;
                if (actions == null) continue;

                for (int i = 0; i < actions.Count; i++)
                {
                    LogicSentenceInstance action = actions[i];
                    if (action == null || !action.enabled) continue;

                    if (action.templateId == "act_show_dialogue_line")
                    {
                        LogicSlotValue lineValue = action.GetAssignment("line")?.value;
                        DialogueSentenceLibrary library = lineValue?.assetReference as DialogueSentenceLibrary;
                        string sentenceId = lineValue?.EvaluateString();
                        if (library != null && !string.IsNullOrWhiteSpace(sentenceId) && DialogueSubtitleHUD.Instance != null)
                        {
                            // 借用"队列播放"机制播这一句——列表只有一个元素时，效果就是
                            // "播完这一句、真正淡出之后再回调"，不需要另外给DialogueSubtitleHUD
                            // 加一套单句等待的重复逻辑。
                            bool lineDone = false;
                            DialogueSubtitleHUD.Instance.ShowLineSequence(
                                library, new List<string> { sentenceId }, () => lineDone = true);
                            yield return new WaitUntil(() => lineDone);
                            continue;
                        }
                    }

                    TriggerActionExecutor.Execute(action, pkg.debugLogs);
                }
            }
        }

        onComplete?.Invoke();
    }

    /// <summary>regionGuid：区域出入类动机(event_player_enter_region/leave_region)
    /// 用来筛"具体是哪个区域"——规则里那条动机自己没选区域(留空)就当"任意区域都算"，
    /// 选了具体区域就必须跟这次真正触发的区域GUID对上，不然场上随便进哪个区域
    /// 都会把所有"玩家进入区域"规则一起点着，选区域这个字段就形同虚设了。
    /// 其余动机(event_initialize等)不传这个参数，走null等于不做区域过滤。</summary>
    public void FireMotiveEvent(string motiveTemplateId, string regionGuid = null)
    {
        foreach (TriggerPackage pkg in packages)
        {
            if (pkg == null) continue;
            foreach (TriggerPackage.TriggerRule rule in pkg.rules)
                TryFireRule(pkg, rule, motiveTemplateId, regionGuid);
        }
    }

    // ── 规则评估与执行 ────────────────────────────────────────────────────────

    private void TryFireRule(TriggerPackage pkg, TriggerPackage.TriggerRule rule, string motiveTemplateId, string regionGuid = null)
    {
        if (rule == null || !rule.enabled) return;
        if (!HasMotive(rule, motiveTemplateId, regionGuid)) return;
        if (IsOnCooldown(rule)) return;
        if (!EvaluateConditions(rule)) return;

        ExecuteActions(pkg, rule);
        ApplyCooldown(rule);
    }

    private static bool HasMotive(TriggerPackage.TriggerRule rule, string motiveTemplateId, string regionGuid)
    {
        List<LogicSentenceInstance> motives = rule.sentenceRoot?.motiveGroup?.items;
        if (motives == null) return false;

        for (int i = 0; i < motives.Count; i++)
        {
            LogicSentenceInstance m = motives[i];
            if (m == null || m.templateId != motiveTemplateId) continue;

            if (regionGuid != null)
            {
                string wantedRegion = m.GetAssignment("region")?.value?.sceneObjectId;
                if (!string.IsNullOrWhiteSpace(wantedRegion) && wantedRegion != regionGuid)
                    continue; // 这条规则指定的是另一个区域，不是这次真正触发的那个
            }

            return true;
        }
        return false;
    }

    private bool EvaluateConditions(TriggerPackage.TriggerRule rule)
    {
        List<LogicSentenceInstance> conds = rule.sentenceRoot?.conditionGroup?.items;
        if (conds == null || conds.Count == 0) return true;

        bool anyPassed = false;
        for (int i = 0; i < conds.Count; i++)
        {
            bool passed = EvaluateCondition(conds[i]);
            if (rule.requireAllConditions && !passed) return false;
            if (!rule.requireAllConditions && passed) anyPassed = true;
        }

        return rule.requireAllConditions || anyPassed;
    }

    private bool EvaluateCondition(LogicSentenceInstance cond)
    {
        return TriggerConditionEvaluator.Evaluate(cond, debugLogsForConditions);
    }

    // 2026-07-17：条件求值原来是"永远返回true"的占位实现，现在真正接到
    // TriggerConditionEvaluator。这里额外读一下所在包的debugLogs开关，方便排查
    // 条件到底判成了什么。
    private bool debugLogsForConditions =>
        packages != null && packages.Exists(p => p != null && p.debugLogs);

    private void ExecuteActions(TriggerPackage pkg, TriggerPackage.TriggerRule rule)
    {
        List<LogicSentenceInstance> actions = rule.sentenceRoot?.actionGroup?.items;
        if (actions == null) return;

        for (int i = 0; i < actions.Count; i++)
        {
            LogicSentenceInstance action = actions[i];
            if (action == null || !action.enabled) continue;
            TriggerActionExecutor.Execute(action, pkg.debugLogs);
        }

        if (pkg.debugLogs)
            Debug.Log($"[TriggerPackageRuntime] 规则 \"{rule.ruleName}\" 执行完毕（{actions.Count} 条行动）", this);
    }

    // ── 冷却管理 ──────────────────────────────────────────────────────────────

    private bool IsOnCooldown(TriggerPackage.TriggerRule rule)
    {
        if (rule.cooldownSeconds <= 0f) return false;
        return _cooldownUntil.TryGetValue(rule, out float until) && Time.time < until;
    }

    private void ApplyCooldown(TriggerPackage.TriggerRule rule)
    {
        if (rule.cooldownSeconds > 0f)
            _cooldownUntil[rule] = Time.time + rule.cooldownSeconds;
    }

    // ── event_every_seconds 定时触发 ─────────────────────────────────────────

    private void StartPeriodicRules()
    {
        foreach (TriggerPackage pkg in packages)
        {
            if (pkg == null) continue;
            foreach (TriggerPackage.TriggerRule rule in pkg.rules)
            {
                if (!rule.enabled) continue;
                if (!HasMotive(rule, "event_every_seconds", null)) continue;

                float interval = GetIntervalFromRule(rule);
                if (interval > 0f)
                    StartCoroutine(PeriodicRoutine(pkg, rule, interval));
            }
        }
    }

    private IEnumerator PeriodicRoutine(TriggerPackage pkg, TriggerPackage.TriggerRule rule, float interval)
    {
        while (true)
        {
            yield return new WaitForSeconds(interval);
            if (pkg == null || rule == null) yield break;
            TryFireRule(pkg, rule, "event_every_seconds");
        }
    }

    private static float GetIntervalFromRule(TriggerPackage.TriggerRule rule)
    {
        List<LogicSentenceInstance> motives = rule.sentenceRoot?.motiveGroup?.items;
        if (motives == null) return 0f;

        for (int i = 0; i < motives.Count; i++)
        {
            LogicSentenceInstance m = motives[i];
            if (m == null || m.templateId != "event_every_seconds") continue;
            LogicSlotAssignment a = m.GetAssignment(IntervalSlotId);
            if (a?.value != null) return Mathf.Max(0.1f, a.value.EvaluateFloat());
        }

        return 0f;
    }
}
