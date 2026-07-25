#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性补：装备栏/商店/古物街/工房/世界地图这5个窗口的提示条之前全是写死中文，
    /// 改成查表(L(key,fallback))之后，还得把这几个新key的日/英翻译真正建到表里，
    /// 否则查不到还是落回中文fallback，等于机制修好了但数据没跟上。
    /// </summary>
    public static class SkyPrisonEnsureWindowHintLocKeys
    {
        private const string TablePath = "Assets/_Project/Data/Resources/UILocalizationTable.asset";

        [MenuItem("Tools/Sky Prison/UI/补齐5个窗口提示条本地化")]
        public static void Ensure()
        {
            var table = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(TablePath);
            if (table == null)
            {
                Debug.LogError($"[SkyPrisonEnsureWindowHintLocKeys] 找不到本地化表：{TablePath}");
                return;
            }

            Add(table, "ui_hint_unequip",          "卸下装备",       "装備を外す",     "Unequip");
            Add(table, "ui_hint_select_goods",     "选择商品",       "商品を選択",     "Select Item");
            Add(table, "ui_hint_add_cart_checkout","加入购物车/结账", "カートに追加/精算", "Add to Cart / Checkout");
            Add(table, "ui_hint_select_item",      "选择物品",       "アイテムを選択", "Select Item");
            Add(table, "ui_hint_select_slot_mod",  "选择槽位/组件",   "スロット/部品を選択", "Select Slot/Mod");
            Add(table, "ui_hint_select_chapter",   "选择章节",       "チャプターを選択", "Select Chapter");

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("[SkyPrisonEnsureWindowHintLocKeys] 6个提示条key已补齐zh-CN/ja/en翻译。");
        }

        private static void Add(UILocalizationTable table, string key, string zh, string ja, string en)
        {
            var entry = table.EnsureEntry(key, new List<string> { "zh-CN", "ja", "en" });
            SetLangText(entry, "zh-CN", zh);
            SetLangText(entry, "ja", ja);
            SetLangText(entry, "en", en);
        }

        private static void SetLangText(UILocalizationEntry entry, string languageCode, string text)
        {
            foreach (var t in entry.texts)
            {
                if (t.languageCode != languageCode) continue;
                if (string.IsNullOrEmpty(t.text)) t.text = text;
                return;
            }
        }
    }
}
#endif
