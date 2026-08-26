#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性修复：背包/仓库窗口筛选标签("全部/消耗品/材料/装备/任务/重要物品")字号偏小。
    /// 只跑一次即可——重复执行会在上次结果基础上继续叠加放大，不是幂等的。
    /// </summary>
    public static class EnlargeInventorySortAndTabText
    {
        private const float FontSizeDelta = 4f;

        private static readonly string[] PrefabPaths =
        {
            "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab",
            "Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab",
            "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonStash.prefab",
            "Assets/Resources/UI/Window/PF_SkyPrisonStash.prefab",
        };

        [MenuItem("Tools/Sky Prison/UI/Enlarge Inventory Sort And Tab Text")]
        public static void Fix()
        {
            int patchedPrefabs = 0;
            int patchedComponents = 0;

            foreach (string path in PrefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    Debug.LogWarning($"[EnlargeInventorySortAndTabText] 没找到 prefab：{path}");
                    continue;
                }

                bool changed = false;

                var invController = root.GetComponent<InventoryWindowController>();
                if (invController != null)
                    changed |= BumpFontSizeField(invController, "tabFontSize");

                var stashController = root.GetComponent<StashWindowController>();
                if (stashController != null)
                    changed |= BumpFontSizeField(stashController, "tabFontSize");

                Transform filterBar = FindDeep(root.transform, "FilterBar");
                if (filterBar != null)
                {
                    foreach (var tmp in filterBar.GetComponentsInChildren<TextMeshProUGUI>(true))
                    {
                        tmp.fontSize += FontSizeDelta;
                        changed = true;
                        patchedComponents++;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    patchedPrefabs++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[EnlargeInventorySortAndTabText] 完成，patch了 {patchedPrefabs} 份 prefab，" +
                $"{patchedComponents} 个文字组件字号 +{FontSizeDelta}。");
        }

        private static bool BumpFontSizeField(Object target, string fieldName)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) return false;

            prop.floatValue += FontSizeDelta;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
