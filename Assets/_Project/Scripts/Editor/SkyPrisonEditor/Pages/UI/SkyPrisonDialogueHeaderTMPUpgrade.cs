using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把 NPC 对话窗口标题(HeaderText)从旧版 UnityEngine.UI.Text 换成 TextMeshProUGUI——
/// 换成 TMP 之后才能接上注音预处理器（旧版 Text 没有 ITextPreprocessor 这个机制，
/// NPC 名字里配的 {A|B} 注音标记只能原样显示出来）。
///
/// 用编辑器脚本操作实际 Prefab 资产，不手改 YAML——组件类型替换、字段重新绑定这类
/// 涉及 Unity 内部序列化一致性的改动，交给 Unity 自己的 API 做才安全，手写 YAML
/// 稍有偏差就是资产损坏。
/// </summary>
public static class SkyPrisonDialogueHeaderTMPUpgrade
{
    private static readonly string[] PrefabPaths =
    {
        "Assets/Resources/UI/Window/PF_NPCDialogue.prefab",
        "Assets/_Project/Prefabs/UI/Window/PF_NPCDialogue.prefab",
    };

    private const string FontAssetPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";

    [MenuItem("Tools/Sky Prison/UI/把对话窗口标题换成 TMP")]
    public static void Upgrade()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
            Debug.LogWarning($"[对话标题升级] 找不到字体资产 {FontAssetPath}，会用 TMP 默认字体，记得手动配一下。");

        foreach (string path in PrefabPaths)
            UpgradeOne(path, font);
    }

    private static void UpgradeOne(string prefabPath, TMP_FontAsset font)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[对话标题升级] 找不到 Prefab：{prefabPath}");
            return;
        }

        try
        {
            NPCDialogueWindowController controller = root.GetComponentInChildren<NPCDialogueWindowController>(true);
            if (controller == null)
            {
                Debug.LogWarning($"[对话标题升级] {prefabPath} 上找不到 NPCDialogueWindowController。");
                return;
            }

            SerializedObject so = new SerializedObject(controller);
            SerializedProperty headerProp = so.FindProperty("headerText");
            if (headerProp == null)
            {
                Debug.LogWarning($"[对话标题升级] {prefabPath} 的 NPCDialogueWindowController 找不到 headerText 字段。");
                return;
            }

            // 不能用 headerProp.objectReferenceValue 去找目标物体——脚本那边字段类型
            // 已经从 Text 改成了 TextMeshProUGUI，Prefab 里原来存的是 Text 类型的
            // 引用，类型对不上，Unity 反序列化这个字段时直接读成 null（不会报错，
            // 静默清空），实测踩过：这里读到的永远是空，等于整个替换逻辑没跑起来，
            // Prefab 原封不动。改成直接按名字找，不依赖这个已经失效的字段引用。
            Transform headerTr = FindDeepChild(root.transform, "HeaderText");
            GameObject headerGo = headerTr != null ? headerTr.gameObject : null;
            if (headerGo == null)
            {
                Debug.LogWarning($"[对话标题升级] {prefabPath} 里找不到名叫 HeaderText 的子物体，跳过。");
                return;
            }

            Text oldText = headerGo.GetComponent<Text>();
            if (oldText == null)
            {
                Debug.Log($"[对话标题升级] {prefabPath} 的 HeaderText 已经不是 UnityEngine.UI.Text，跳过。");
                return;
            }

            Color color = oldText.color;
            float fontSize = oldText.fontSize;
            TextAnchor oldAlignment = oldText.alignment;

            Object.DestroyImmediate(oldText, true);

            TextMeshProUGUI tmp = headerGo.AddComponent<TextMeshProUGUI>();
            tmp.color = color;
            tmp.fontSize = fontSize;
            tmp.alignment = ConvertAlignment(oldAlignment);
            tmp.raycastTarget = false;
            if (font != null)
                tmp.font = font;
            tmp.text = "NPC";

            headerProp.objectReferenceValue = tmp;
            so.ApplyModifiedPropertiesWithoutUndo();

            bool saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            Debug.Log($"[对话标题升级] {prefabPath} 处理完成，保存{(success ? "成功" : "失败")}。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null)
            return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    private static TextAlignmentOptions ConvertAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Midline;
        }
    }
}
