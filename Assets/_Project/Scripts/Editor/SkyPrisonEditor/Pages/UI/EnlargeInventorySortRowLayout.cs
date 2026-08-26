#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性修复第二批（配合 EnlargeInventorySortAndTabText 那一批用）：
    ///  - "排序"标签放大之后被自己的容器裁掉第二个字("排"字后面的"序"不见了)——
    ///    容器(SortLabel)加宽，右边的 SortDropdown/SortOrderButton 一起挪开腾出空间。
    ///  - "负重"标签、"xx/xx 石英"数值、"整理"按钮的字号之前那一批没覆盖到，这里补上；
    ///    "负重"同样是两字窄标签，容器一起加宽；整行(WeightBar)加高，给变大的字留竖直
    ///    方向余量。
    ///
    /// 只跑一次即可——重复执行会在上次结果基础上继续叠加放大/挪位置，不是幂等的。
    /// 必须先跑过 EnlargeInventorySortAndTabText（把"排序"/"↑""↓"字号放大过）再跑这个，
    /// 顺序反了的话"排序"标签的宽度会跟字号对不上。
    /// </summary>
    public static class EnlargeInventorySortRowLayout
    {
        private const float FontSizeDelta = 4f;
        // "排序"/"负重"都是两个汉字的窄标签，容器只有40~46px宽，字号变大后肉眼可见会
        // 被自己的容器裁掉第二个字——不是字太大，是容器从一开始就没留够宽度。
        private const float NarrowLabelWidthDelta = 24f;
        private const float RowHeightDelta = 8f;

        private static readonly string[] PrefabPaths =
        {
            "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab",
            "Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab",
            "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonStash.prefab",
            "Assets/Resources/UI/Window/PF_SkyPrisonStash.prefab",
        };

        [MenuItem("Tools/Sky Prison/UI/Enlarge Inventory Sort Row Layout")]
        public static void Fix()
        {
            int patchedPrefabs = 0;

            foreach (string path in PrefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    Debug.LogWarning($"[EnlargeInventorySortRowLayout] 没找到 prefab：{path}");
                    continue;
                }

                bool changed = false;

                // "排序"标签加宽；SortDropdown/SortOrderButton 右移同样的量腾出空间。
                // 这三个字号已经在上一批脚本里改过了，这里只动位置/宽度，不再碰字号。
                Transform sortLabel = FindDeep(root.transform, "SortLabel");
                if (sortLabel != null)
                {
                    var rt = (RectTransform)sortLabel;
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x + NarrowLabelWidthDelta, rt.sizeDelta.y);
                    changed = true;
                }

                foreach (string name in new[] { "SortDropdown", "SortOrderButton" })
                {
                    Transform t = FindDeep(root.transform, name);
                    if (t == null) continue;
                    var rt = (RectTransform)t;
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + NarrowLabelWidthDelta, rt.anchoredPosition.y);
                    changed = true;
                }

                // "负重"标签加宽+放大字号(之前那批没覆盖到)。
                Transform weightLabel = FindDeep(root.transform, "WeightLabel");
                if (weightLabel != null)
                {
                    var rt = (RectTransform)weightLabel;
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x + NarrowLabelWidthDelta, rt.sizeDelta.y);
                    Text text = weightLabel.GetComponent<Text>();
                    if (text != null) text.fontSize += Mathf.RoundToInt(FontSizeDelta);
                    changed = true;
                }

                // "xx/xx 石英"数值——右边缘锚定，不需要挪位置，只放大字号。
                Transform weightValueText = FindDeep(root.transform, "WeightValueText");
                if (weightValueText != null)
                {
                    Text text = weightValueText.GetComponent<Text>();
                    if (text != null) { text.fontSize += Mathf.RoundToInt(FontSizeDelta); changed = true; }
                }

                // "整理"按钮——右边缘锚定，不需要挪位置，只放大按钮里的文字。
                Transform tidyButton = FindDeep(root.transform, "TidyButton");
                if (tidyButton != null)
                {
                    foreach (var text in tidyButton.GetComponentsInChildren<Text>(true))
                    {
                        text.fontSize += Mathf.RoundToInt(FontSizeDelta);
                        changed = true;
                    }
                }

                // 负重条整行加高，给变大的字留竖直方向余量。
                Transform weightBar = FindDeep(root.transform, "WeightBar");
                if (weightBar != null)
                {
                    var rt = (RectTransform)weightBar;
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + RowHeightDelta);
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    patchedPrefabs++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[EnlargeInventorySortRowLayout] 完成，patch了 {patchedPrefabs} 份 prefab。");
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
