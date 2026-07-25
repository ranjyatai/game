using UnityEditor;
using UnityEngine;

public class SkyPrisonBattleSimulationWorkspace
{
    private readonly SkyPrisonBattleParametersPage page;
    private Vector2 scroll;

    public SkyPrisonBattleSimulationWorkspace(SkyPrisonBattleParametersPage page)
    {
        this.page = page;
    }

    public void Draw()
    {
        Rect fullRect = GUILayoutUtility.GetRect(
            0f, 100000f, 0f, 100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        EditorGUI.DrawRect(fullRect, new Color(0.18f, 0.18f, 0.19f, 1f));
        page.DrawThinBorder(fullRect, new Color(1f, 1f, 1f, 0.06f));

        Rect inner = new Rect(
            fullRect.x + 12f,
            fullRect.y + 12f,
            Mathf.Max(0f, fullRect.width - 24f),
            Mathf.Max(0f, fullRect.height - 24f));

        const float verticalScrollbarWidth = 14f;
        const float horizontalScrollbarHeight = 14f;

        float contentWidth = Mathf.Max(0f, inner.width - verticalScrollbarWidth);
        float contentHeight = Mathf.Max(inner.height, CalculateContentHeight(contentWidth));
        Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);

        scroll = GUI.BeginScrollView(
            inner,
            scroll,
            viewRect,
            true,
            true);

        GUILayout.BeginArea(viewRect);

        EditorGUILayout.LabelField("Build 模拟工作台", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        EditorGUILayout.HelpBox(
            "这里已经和“定义填写”彻底分开。\n\n" +
            "后面这里可以继续接：\n" +
            "1. 配装选择\n" +
            "2. 属性汇总\n" +
            "3. 伤害模拟\n" +
            "4. 异常效率模拟\n" +
            "5. 生存 / 循环 / Build 对比\n\n" +
            "也就是说，这里不再是填数据，而是专门做解释、推演、验证。",
            MessageType.Info);

        GUILayout.Space(12f);
        EditorGUILayout.LabelField("预留模拟输入区", EditorStyles.boldLabel);
        page.DrawSimpleDivider();
        EditorGUILayout.LabelField("单位选择、武器选择、模组选择、属性读取……后续都在这里接。");

        GUILayout.Space(16f);
        EditorGUILayout.LabelField("预留模拟结果区", EditorStyles.boldLabel);
        page.DrawSimpleDivider();
        EditorGUILayout.LabelField("DPS、异常触发效率、负荷循环、Build 总评分……后续都在这里可视化。");

        GUILayout.Space(8f);

        GUILayout.EndArea();
        GUI.EndScrollView();
    }

    private float CalculateContentHeight(float contentWidth)
    {
        float height = 0f;

        height += EditorGUIUtility.singleLineHeight; // 标题
        height += 6f;

        GUIContent helpContent = new GUIContent(
            "这里已经和“定义填写”彻底分开。\n\n" +
            "后面这里可以继续接：\n" +
            "1. 配装选择\n" +
            "2. 属性汇总\n" +
            "3. 伤害模拟\n" +
            "4. 异常效率模拟\n" +
            "5. 生存 / 循环 / Build 对比\n\n" +
            "也就是说，这里不再是填数据，而是专门做解释、推演、验证。");
        height += EditorStyles.helpBox.CalcHeight(helpContent, Mathf.Max(100f, contentWidth - 8f));

        height += 12f;
        height += EditorGUIUtility.singleLineHeight;
        height += 2f;
        height += 1f;
        height += 2f;
        height += EditorGUIUtility.singleLineHeight;

        height += 16f;
        height += EditorGUIUtility.singleLineHeight;
        height += 2f;
        height += 1f;
        height += 2f;
        height += EditorGUIUtility.singleLineHeight;

        height += 8f;
        height += horizontalScrollbarHeightFallback;

        return height;
    }

    private const float horizontalScrollbarHeightFallback = 14f;
}
