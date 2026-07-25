using UnityEditor;
using UnityEngine;

/// <summary>
/// WE式嵌套四则运算表达式的槽位编辑UI——渲染成"A 运算符 B"一行内联文字，运算数A/B
/// 用项目里句型预览同一套配色（未设置=红色ErrorColor，已设置=蓝色ValidColor，带下
/// 划线），点击直接弹出跟普通槽位编辑同一个窗口（AISlotValuePickerWindow.OpenTemporary，
/// 本身已经支持弹窗叠弹窗的模态栈AIModalWindowStack），运算数本身还能再选"表达式"，
/// 天然递归。运算符本身也是可点的token，点了弹出一个小菜单选择四则运算。
/// LogicIntSlotHandler / LogicFloatSlotHandler 共用这一份，避免两边各写一遍。
/// </summary>
public static class LogicExpressionSlotEditorGUI
{
    public static void DrawExpressionEditor(LogicSlotValueType valueType, LogicSlotValue value)
    {
        EditorGUILayout.BeginHorizontal();

        if (DrawOperandToken(value.expressionOperandA))
            OpenOperandEditor(valueType, value, isOperandA: true);

        GUILayout.Space(6f);

        if (DrawClickableToken(LogicSlotValue.GetOperatorSymbol(value.expressionOperator), LogicSentenceEditorGUI.KeywordColor))
            ShowOperatorMenu(value);

        GUILayout.Space(6f);

        if (DrawOperandToken(value.expressionOperandB))
            OpenOperandEditor(valueType, value, isOperandA: false);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private static bool DrawOperandToken(LogicSlotValue operand)
    {
        bool empty = operand == null || operand.IsEmpty();
        string text = empty ? "<未设置>" : operand.GetDisplayText();
        Color color = empty ? LogicSentenceEditorGUI.ErrorColor : LogicSentenceEditorGUI.ValidColor;
        return DrawClickableToken(text, color);
    }

    // 跟 SkyPrisonAILogicPanel.DrawCachedSentenceSegments 同一套视觉语言：彩色文字+
    // 下划线。用 GUILayout.Button 套 EditorStyles.label 拿点击检测（没有按钮边框/背景，
    // 看起来就是一段普通文字），再手动画一条下划线。
    private static bool DrawClickableToken(string text, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        bool clicked = GUILayout.Button(text, EditorStyles.label, GUILayout.ExpandWidth(false));
        Rect r = GUILayoutUtility.GetLastRect();
        GUI.color = old;

        Rect underline = new Rect(r.x, r.yMax - 2f, r.width, 1f);
        EditorGUI.DrawRect(underline, color);

        return clicked;
    }

    private static void ShowOperatorMenu(LogicSlotValue value)
    {
        GenericMenu menu = new GenericMenu();
        AddOperatorMenuItem(menu, value, LogicMathOperator.Add);
        AddOperatorMenuItem(menu, value, LogicMathOperator.Subtract);
        AddOperatorMenuItem(menu, value, LogicMathOperator.Multiply);
        AddOperatorMenuItem(menu, value, LogicMathOperator.Divide);
        menu.ShowAsContext();
    }

    private static void AddOperatorMenuItem(GenericMenu menu, LogicSlotValue value, LogicMathOperator op)
    {
        string label = $"{LogicSlotValue.GetOperatorSymbol(op)}  ({op})";
        menu.AddItem(new GUIContent(label), value.expressionOperator == op, () => value.expressionOperator = op);
    }

    private static void OpenOperandEditor(LogicSlotValueType valueType, LogicSlotValue parent, bool isOperandA)
    {
        LogicSlotValue operand = isOperandA ? parent.expressionOperandA : parent.expressionOperandB;
        LogicSlotValue editing = operand ?? CreateDefaultOperand(valueType);
        LogicSentenceTemplate.SlotDefinition operandSlotDef = BuildOperandSlotDefinition(valueType, isOperandA ? "运算数 A" : "运算数 B");

        AISlotValuePickerWindow.OpenTemporary(
            isOperandA ? "expressionOperandA" : "expressionOperandB",
            operandSlotDef,
            editing,
            result =>
            {
                if (isOperandA) parent.expressionOperandA = result;
                else parent.expressionOperandB = result;
            });
    }

    private static LogicSlotValue CreateDefaultOperand(LogicSlotValueType valueType)
    {
        return new LogicSlotValue
        {
            valueType = valueType,
            sourceType = LogicValueSourceType.Constant
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildOperandSlotDefinition(LogicSlotValueType valueType, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = "operand",
            displayName = displayName,
            valueType = valueType,
            required = true,
            // 运算数自己也能是表达式（嵌套），也允许常量/随机范围。不开放Variable/
            // ContextReference/AssetReference——那几个来源要靠运行时上下文/资源解析，
            // 塞进算式里语义上说不通。
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.RandomRange,
                LogicValueSourceType.Expression
            }
        };
    }
}
