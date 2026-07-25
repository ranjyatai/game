using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 余穹科技树运行时。单例，DontDestroyOnLoad。
/// 直接复用编辑器的 TechTreeGraphAsset 数据结构。
///
/// 节点升级消耗科技点（由余穹中枢升级发放）。
/// 每个节点支持多等级，每级有独立的 costs（科技点）和 rewards（属性加成）。
/// </summary>
public class TechTreeRuntime : MonoBehaviour
{
    public static TechTreeRuntime Instance { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────
    /// <summary>节点升级后触发（nodeId, newLevel）。供属性系统监听应用加成。</summary>
    public static event Action<string, int> OnNodeLevelUp;
    /// <summary>科技点变化时触发，供 UI 刷新。</summary>
    public static event Action<int> OnPointsChanged;

    [Tooltip("科技树 Graph asset。留空则从 Resources/TechTree 自动加载")]
    [SerializeField] private TechTreeGraphAsset graphAsset;

    [Tooltip("余穹中枢每升一级给予的科技点数")]
    [SerializeField] private int pointsPerCommandCenterLevel = 3;

    // nodeId → 当前等级缓存（从存档读）
    private readonly Dictionary<string, int> _levelCache = new Dictionary<string, int>();
    private bool _cacheDirty = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (graphAsset == null)
            graphAsset = GameAssetLoader.TechTree;
        if (graphAsset == null)
            Debug.LogError("[TechTreeRuntime] 找不到 TechTreeGraphAsset。请执行菜单 Sky Prison → 重建 Asset Manifest。");

        BaseManager.OnFacilityUpgraded += HandleFacilityUpgraded;
        SaveManager.OnSaveLoaded       += () => _cacheDirty = true;
        SaveManager.OnNewGame          += () => _cacheDirty = true;
    }

    private void OnDestroy()
    {
        BaseManager.OnFacilityUpgraded -= HandleFacilityUpgraded;
    }

    // ── 科技点 ────────────────────────────────────────────────────────────

    public int AvailablePoints => SaveManager.Player?.techTree?.availablePoints ?? 0;

    private void HandleFacilityUpgraded(FacilityType type, int newLevel)
    {
        if (type != FacilityType.CommandCenter) return;
        AddPoints(pointsPerCommandCenterLevel);
    }

    public void AddPoints(int amount)
    {
        var data = SaveManager.Player?.techTree;
        if (data == null || amount <= 0) return;
        data.availablePoints   += amount;
        data.totalPointsEarned += amount;
        SaveManager.Save();
        OnPointsChanged?.Invoke(data.availablePoints);
    }

    // ── 节点等级查询 ──────────────────────────────────────────────────────

    public int GetNodeLevel(string nodeId)
    {
        RebuildCacheIfNeeded();
        _levelCache.TryGetValue(nodeId, out int lv);
        return lv;
    }

    public TechTreeNodeData FindNode(string nodeId)
    {
        if (graphAsset == null) return null;
        foreach (var n in graphAsset.nodes)
            if (n.nodeId == nodeId) return n;
        return null;
    }

    // ── 升级节点 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 尝试将节点升一级。
    /// 返回 true = 成功；false 时 reason 说明失败原因。
    /// </summary>
    public bool TryLevelUp(string nodeId, out string reason)
    {
        reason = "";
        var data = SaveManager.Player?.techTree;
        if (data == null) { reason = "存档未就绪"; return false; }
        if (graphAsset == null) { reason = "科技树未加载"; return false; }

        var node = FindNode(nodeId);
        if (node == null)          { reason = $"找不到节点：{nodeId}"; return false; }
        if (!node.enabled)         { reason = "节点未启用"; return false; }

        int currentLevel = GetNodeLevel(nodeId);
        if (currentLevel >= node.maxLevel) { reason = "已达最高等级"; return false; }

        int targetLevel = currentLevel + 1;
        var levelData   = GetLevelData(node, targetLevel);
        if (levelData == null) { reason = "等级数据缺失"; return false; }

        // 主前置节点检查（父节点必须至少 LV1）
        if (node.primaryParentIndex >= 0 && node.primaryParentIndex < graphAsset.nodes.Count)
        {
            var parent = graphAsset.nodes[node.primaryParentIndex];
            if (GetNodeLevel(parent.nodeId) < 1)
            {
                reason = $"需先解锁「{parent.nodeName}」";
                return false;
            }
        }

        // 次级前置检查
        foreach (var req in node.secondaryRequirements)
        {
            int reqLevel = GetNodeLevel(req.targetNodeId);
            if (reqLevel < req.requiredLevel)
            {
                var reqNode = FindNode(req.targetNodeId);
                reason = $"需「{reqNode?.nodeName ?? req.targetNodeId}」达到 LV{req.requiredLevel}（当前 LV{reqLevel}）";
                return false;
            }
        }

        // 科技点检查（costs 里 item==null 的条目视为科技点消耗）
        int pointCost = GetPointCost(levelData);
        if (data.availablePoints < pointCost)
        {
            reason = $"科技点不足（需要 {pointCost}，剩余 {data.availablePoints}）";
            return false;
        }

        // 执行升级
        data.availablePoints -= pointCost;
        SetNodeLevel(data, nodeId, targetLevel);
        _cacheDirty = true;
        SaveManager.Save();

        OnPointsChanged?.Invoke(data.availablePoints);
        OnNodeLevelUp?.Invoke(nodeId, targetLevel);
        return true;
    }

    // ── 属性加成汇总 ──────────────────────────────────────────────────────

    /// <summary>
    /// 汇总所有已解锁节点各等级的 rewards，按 key 累加。
    /// key 对应 TechStatType 名称或自定义字符串，由调用方解析。
    /// </summary>
    public Dictionary<string, float> GetAllRewards()
    {
        var result = new Dictionary<string, float>();
        var data   = SaveManager.Player?.techTree;
        if (data == null || graphAsset == null) return result;

        foreach (var node in graphAsset.nodes)
        {
            int lv = GetNodeLevel(node.nodeId);
            if (lv <= 0) continue;

            for (int i = 1; i <= lv; i++)
            {
                var ld = GetLevelData(node, i);
                if (ld == null) continue;
                foreach (var reward in ld.rewards)
                {
                    if (string.IsNullOrEmpty(reward.key)) continue;
                    result.TryGetValue(reward.key, out float cur);
                    result[reward.key] = cur + reward.value;
                }
            }
        }
        return result;
    }

    // ── 内部工具 ──────────────────────────────────────────────────────────

    private static TechTreeLevelData GetLevelData(TechTreeNodeData node, int level)
    {
        foreach (var ld in node.levels)
            if (ld.level == level) return ld;
        return null;
    }

    private static int GetPointCost(TechTreeLevelData levelData)
    {
        // costs 中 item == null 的条目视为科技点
        int total = 0;
        foreach (var c in levelData.costs)
            if (c.item == null) total += c.amount;
        return total;
    }

    private void RebuildCacheIfNeeded()
    {
        if (!_cacheDirty) return;
        _levelCache.Clear();
        var data = SaveManager.Player?.techTree;
        if (data != null)
            foreach (var kv in data.nodeLevels)
                _levelCache[kv.key] = kv.value;
        _cacheDirty = false;
    }

    private static void SetNodeLevel(TechTreeSaveData data, string nodeId, int level)
    {
        foreach (var kv in data.nodeLevels)
        {
            if (kv.key == nodeId) { kv.value = level; return; }
        }
        data.nodeLevels.Add(new TechTreeNodeLevel { key = nodeId, value = level });
    }
}
