#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性修复：背包 InventoryWindowController.tabFont 序列化值一直是
    /// "msyh SDF"（微软雅黑），不是项目其它地方都在用的主字体"ZhouFangRiMingTi-2 SDF"——
    /// 筛选标签("全部/消耗品/材料/装备/任务/重要物品")因此跟标题栏等其它文字用了
    /// 不同字体，跟"应该用设置好的中文字体"这个预期不符。这不是运行时字体解析出的
    /// 问题（SkyPrisonInventoryTextLocalizer 只有配了 primaryTMPFont 才会覆盖字体，
    /// 这份 LocalizationProjectSettings 里没配这个字段，压根不会碰 tabFont），是
    /// tabFont 这个字段本身在 prefab 里就没指对资产。跑一次这个菜单就行，不需要
    /// 反复执行。
    /// </summary>
    public static class FixInventoryFilterTabFont
    {
        private const string InventoryPrefabPath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";
        private const string TargetFontPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";

        [MenuItem("Tools/Sky Prison/UI/Fix Inventory Filter Tab Font")]
        public static void Fix()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TargetFontPath);
            if (font == null)
            {
                Debug.LogError($"[FixInventoryFilterTabFont] 找不到字体资产：{TargetFontPath}");
                return;
            }

            var inv = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPrefabPath);
            if (inv == null)
            {
                Debug.LogError($"[FixInventoryFilterTabFont] 找不到背包 prefab：{InventoryPrefabPath}");
                return;
            }

            var controller = inv.GetComponent<InventoryWindowController>();
            if (controller == null)
            {
                Debug.LogError("[FixInventoryFilterTabFont] 背包 prefab 根节点上找不到 InventoryWindowController。");
                return;
            }

            var so = new SerializedObject(controller);
            var prop = so.FindProperty("tabFont");
            if (prop == null)
            {
                Debug.LogError("[FixInventoryFilterTabFont] InventoryWindowController 上找不到 tabFont 字段，脚本可能改过名字。");
                return;
            }

            Object oldFont = prop.objectReferenceValue;
            prop.objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(inv);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FixInventoryFilterTabFont] 背包筛选标签字体已从 " +
                $"「{(oldFont != null ? oldFont.name : "空")}」改成「{font.name}」。");
        }
    }
}
#endif
