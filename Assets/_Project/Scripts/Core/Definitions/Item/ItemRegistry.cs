using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局物品注册表。ScriptableObject，存一份 asset。
/// 把所有 ItemDefinition 收录进来，供存档系统通过 itemKey 还原 asset 引用。
/// 创建路径：Sky Prison / Item Registry
/// Editor 工具可一键扫描并自动填充。
/// </summary>
[CreateAssetMenu(
    fileName = "ItemRegistry",
    menuName  = "Sky Prison/Item Registry",
    order     = 10)]
public class ItemRegistry : ScriptableObject
{
    [Tooltip("所有游戏内物品定义。itemKey 必须唯一。")]
    public List<ItemDefinition> items = new List<ItemDefinition>();

    private Dictionary<string, ItemDefinition> _lookup;

    // ── 运行时查询 ────────────────────────────────────────────────────────

    public ItemDefinition Find(string itemKey)
    {
        BuildLookup();
        _lookup.TryGetValue(itemKey, out var def);
        return def;
    }

    public bool TryFind(string itemKey, out ItemDefinition def)
    {
        BuildLookup();
        return _lookup.TryGetValue(itemKey, out def);
    }

    private void BuildLookup()
    {
        if (_lookup != null) return;
        _lookup = new Dictionary<string, ItemDefinition>(items.Count);
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.itemKey)) continue;
            if (!_lookup.TryAdd(item.itemKey, item))
                Debug.LogWarning($"[ItemRegistry] 重复 itemKey：{item.itemKey}，后者被忽略。", item);
        }
    }

    // 热重载时重建
    private void OnValidate() => _lookup = null;

#if UNITY_EDITOR
    /// <summary>编辑器一键扫描项目内所有 ItemDefinition 并填入列表。</summary>
    [ContextMenu("扫描并自动填充所有 ItemDefinition")]
    public void ScanAndFill()
    {
        items.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition");
        foreach (var guid in guids)
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var def  = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def != null) items.Add(def);
        }
        _lookup = null;
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"[ItemRegistry] 已扫描到 {items.Count} 个物品定义。");
    }
#endif
}
