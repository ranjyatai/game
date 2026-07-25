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

    // ── 公开接口 ──────────────────────────────────────────────────────────────

    public void AddPackage(TriggerPackage pkg)
    {
        if (pkg != null && !packages.Contains(pkg))
            packages.Add(pkg);
    }

    public void FireMotiveEvent(string motiveTemplateId)
    {
        foreach (TriggerPackage pkg in packages)
        {
            if (pkg == null) continue;
            foreach (TriggerPackage.TriggerRule rule in pkg.rules)
                TryFireRule(pkg, rule, motiveTemplateId);
        }
    }

    // ── 规则评估与执行 ────────────────────────────────────────────────────────

    private void TryFireRule(TriggerPackage pkg, TriggerPackage.TriggerRule rule, string motiveTemplateId)
    {
        if (rule == null || !rule.enabled) return;
        if (!HasMotive(rule, motiveTemplateId)) return;
        if (IsOnCooldown(rule)) return;
        if (!EvaluateConditions(rule)) return;

        ExecuteActions(pkg, rule);
        ApplyCooldown(rule);
    }

    private static bool HasMotive(TriggerPackage.TriggerRule rule, string motiveTemplateId)
    {
        List<LogicSentenceInstance> motives = rule.sentenceRoot?.motiveGroup?.items;
        if (motives == null) return false;
        for (int i = 0; i < motives.Count; i++)
            if (motives[i] != null && motives[i].templateId == motiveTemplateId)
                return true;
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
                if (!HasMotive(rule, "event_every_seconds")) continue;

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
            if (a?.value != null) return Mathf.Max(0.1f, a.value.floatValue);
        }

        return 0f;
    }
}
