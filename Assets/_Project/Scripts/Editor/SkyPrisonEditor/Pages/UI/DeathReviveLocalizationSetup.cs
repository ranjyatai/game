#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 菜单：Sky Prison > 本地化 > 填充死亡弹窗默认译文
/// 在 UILocalizationTable 里写入中文/日文/英文默认值（不会覆盖已有译文）。
/// </summary>
public static class DeathReviveLocalizationSetup
{
    private const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";

    // key → (zh-CN, ja-JP, en-US)
    private static readonly (string key, string zh, string ja, string en)[] Defaults =
    {
        ("ui_death_title",       "生命维持警告",            "生命維持警告",                  "Life Support Warning"),
        ("ui_death_warning",     "死亡将会造成背包所持物品的损失", "死亡により所持品が失われます",       "Death will cause loss of carried items"),
        ("ui_death_use_item",    "使用该道具",              "このアイテムを使用",              "Use Item"),
        ("ui_death_on_cooldown", "冷却中",                 "クールダウン中",                 "On Cooldown"),
        ("ui_death_return_base", "返回据点",               "拠点に戻る",                    "Return to Base"),
    };

    private static readonly List<string> SupportedCodes = new List<string> { "zh-CN", "ja-JP", "en-US" };

    [MenuItem("Sky Prison/本地化/填充死亡弹窗默认译文")]
    public static void Run()
    {
        var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
        if (table == null)
        {
            Debug.LogError($"[DeathReviveLoc] 找不到 UILocalizationTable：{TablePath}");
            return;
        }

        int added = 0, updated = 0;
        foreach (var (key, zh, ja, en) in Defaults)
        {
            var entry = table.EnsureEntry(key, SupportedCodes);
            bool isNew = SetIfEmpty(entry, "zh-CN", zh, ref updated);
            SetIfEmpty(entry, "ja-JP", ja, ref updated);
            SetIfEmpty(entry, "en-US", en, ref updated);
            if (isNew) added++;
        }

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DeathReviveLoc] 完成：新增 {added} 条，补全译文 {updated} 处。");
        EditorUtility.DisplayDialog("死亡弹窗本地化", $"新增 {added} 条 key，补全 {updated} 处译文。", "OK");
    }

    /// <summary>仅在该语言译文为空时写入，不覆盖已有内容。</summary>
    private static bool SetIfEmpty(UILocalizationEntry entry, string code, string value, ref int counter)
    {
        bool isNew = false;
        foreach (var t in entry.texts)
        {
            if (t.languageCode != code) continue;
            if (string.IsNullOrEmpty(t.text))
            {
                t.text = value;
                counter++;
                isNew = true;
            }
            return isNew;
        }
        return isNew;
    }
}
#endif
