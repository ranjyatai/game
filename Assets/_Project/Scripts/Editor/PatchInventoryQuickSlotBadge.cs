using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SkyPrison.Runtime.UI;

// 背包格子（InventorySlotView）本来是"Workbench builder在构建PF时创建"的（见该类
// 头注释）——Workbench工具已经删掉了，这类"给现有格子批量加一个新UI元素"的活现在
// 只能靠这种一次性、安全的 PrefabUtility 补丁脚本来做（跟 PatchQuickSlotsHUDCorners
// 同一个套路）。给每个格子右上角加一个"绑在快捷栏几号"的角标，跟左上角的 NewBadge
// 错开（NewBadge 是 anchorMin/Max=(0,1) 左上角，这个用 (1,1) 右上角）。
public static class PatchInventoryQuickSlotBadge
{
    // 两份都要打——跟HUD prefab同一个"双份资产"结构（Prefabs/ 编辑器用，Resources/
    // 运行时Resources.Load加载），内容不保证完全一致，得各自打一遍。
    private static readonly string[] InventoryPrefabPaths =
    {
        "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab",
        "Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab",
    };

    private static readonly Color BadgeBgColor = new Color(0.42f, 0.92f, 0.68f, 0.85f); // 冷绿，跟"选中/绑定"同一个强调色
    private static readonly Color BadgeTextColor = Color.white;

    [MenuItem("SkyPrison/Inventory/Patch QuickSlot Badge")]
    public static void Patch()
    {
        int totalPatched = 0;
        foreach (string path in InventoryPrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogWarning($"[PatchInventoryQuickSlotBadge] 没找到 prefab：{path}");
                continue;
            }

            int patched = PatchOne(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            totalPatched += patched;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PatchInventoryQuickSlotBadge] 完成，两份prefab一共给 {totalPatched} 个背包格子加了快捷栏角标。");
    }

    private static int PatchOne(GameObject root)
    {
        var cells = root.GetComponentsInChildren<InventorySlotView>(true);
        int patched = 0;

        foreach (var cell in cells)
        {
            Transform cellT = cell.transform;

            Transform existing = cellT.Find("QuickSlotBadge");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var badgeGo = new GameObject("QuickSlotBadge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(cellT, false);
            var badgeRt = (RectTransform)badgeGo.transform;
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot     = new Vector2(1f, 1f);
            badgeRt.anchoredPosition = new Vector2(-4f, -4f);
            // 同一种物品可能同时绑给好几个快捷槽（比如"1,2"），宽度留够两位数字+逗号，
            // 单个数字时居中显示也不会看着奇怪。
            badgeRt.sizeDelta = new Vector2(36f, 18f);

            var bg = badgeGo.GetComponent<Image>();
            bg.color = BadgeBgColor;
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(badgeRt, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = "";
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = BadgeTextColor;
            text.raycastTarget = false;

            badgeGo.SetActive(false); // 默认没绑快捷栏时不显示，InventorySlotView.Bind 有绑定时才打开

            var so = new SerializedObject(cell);
            var badgeProp = so.FindProperty("quickSlotBadge");
            var textProp  = so.FindProperty("quickSlotBadgeText");
            if (badgeProp != null) badgeProp.objectReferenceValue = badgeGo;
            if (textProp != null) textProp.objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            patched++;
        }

        return patched;
    }
}
