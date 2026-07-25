using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class AISlotValuePickerWindow : EditorWindow
{
    private const float FixedWidth = 520f;
    private const float FixedHeight = 300f;

    private const string PosXKey = "AI.SlotPicker.PosX";
    private const string PosYKey = "AI.SlotPicker.PosY";
    private const string PosWKey = "AI.SlotPicker.PosW";
    private const string PosHKey = "AI.SlotPicker.PosH";

    // 2026-07-17：改掉单例复用——之前 Open/OpenTemporary 永远只 CreateInstance 一次，
    // 后续调用全部复用同一个 activeInstance，导致嵌套表达式里"运算数A"这类从已经
    // 打开的槽位窗口内部再弹一个槽位窗口时，新窗口直接把旧窗口的内容整个覆盖掉，
    // 表现成"弹窗盖住了上一个弹窗"而不是叠出一个新的——本质是把两次编辑会话错误地
    // 塞进了同一个对象里。现在每次 Open/OpenTemporary 都真正 CreateInstance 一个新
    // 实例，靠 AIModalWindowStack（本身就是通用的、不限类型的窗口栈）管理层级/焦点/
    // ESC，_openInstances 只用来回答"当前最上层的取值窗口是哪一个"（给
    // LogicUnitSlotHandler 那种需要知道"往哪个slotId写回场景拾取结果"的调用方用）。
    private static readonly List<AISlotValuePickerWindow> _openInstances = new List<AISlotValuePickerWindow>();

    private UnityEngine.Object targetObject;
    private string assignmentPropertyPath;
    private string editingSlotId;

    private LogicSentenceTemplate.SlotDefinition slotDef;
    private LogicSlotValue workingValue;
    private LogicTemplateContext editorContext = LogicTemplateContext.AI;

    private bool useTemporaryMode = false;
    private Action<LogicSlotValue> onTemporaryConfirm;

    private double attentionUntilTime = -1d;
    private string attentionMessage = "";
    private bool _intentionalClose = false;

    // 最上层（最后打开）的实例——嵌套时子窗口是从父窗口内部触发打开的，天然排在
    // 列表末尾，跟"最后打开=当前最上层"这个假设是一致的。
    public static AISlotValuePickerWindow ActiveInstance => _openInstances.Count > 0 ? _openInstances[_openInstances.Count - 1] : null;
    public static bool HasOpenWindow => _openInstances.Count > 0;

    public string EditingSlotId => editingSlotId;
    public LogicSlotValue WorkingValue => workingValue;
    public bool IsTemporaryMode => useTemporaryMode;

    public static void Open(
        UnityEngine.Object targetObject,
        string assignmentPropertyPath,
        string slotId,
        LogicSentenceTemplate.SlotDefinition slotDef,
        LogicTemplateContext context = LogicTemplateContext.AI)
    {
        AISlotValuePickerWindow instance = CreateNewInstance(slotDef);
        instance.targetObject = targetObject;
        instance.assignmentPropertyPath = assignmentPropertyPath;
        instance.editingSlotId = slotId;
        instance.slotDef = slotDef;
        instance.editorContext = context;
        instance.workingValue = ReadCurrentValue(targetObject, assignmentPropertyPath, slotDef);
        instance.useTemporaryMode = false;
        instance.onTemporaryConfirm = null;

        instance.ShowAsStackModal();
    }

    public static void OpenTemporary(
        string slotId,
        LogicSentenceTemplate.SlotDefinition slotDef,
        LogicSlotValue currentValue,
        Action<LogicSlotValue> confirmCallback,
        LogicTemplateContext context = LogicTemplateContext.AI)
    {
        AISlotValuePickerWindow instance = CreateNewInstance(slotDef);
        instance.targetObject = null;
        instance.assignmentPropertyPath = "";
        instance.editingSlotId = slotId;
        instance.slotDef = slotDef;
        instance.editorContext = context;
        instance.workingValue = CloneValue(currentValue);
        instance.useTemporaryMode = true;
        instance.onTemporaryConfirm = confirmCallback;

        instance.ShowAsStackModal();
    }

    private static AISlotValuePickerWindow CreateNewInstance(LogicSentenceTemplate.SlotDefinition slotDef)
    {
        AISlotValuePickerWindow instance = CreateInstance<AISlotValuePickerWindow>();
        instance.minSize = new Vector2(FixedWidth, FixedHeight);
        instance.maxSize = new Vector2(FixedWidth, FixedHeight);
        instance.titleContent = new GUIContent(slotDef != null ? slotDef.displayName : "参数");

        // 嵌套弹出时（已经有窗口开着）按对角线错开摆放，视觉上一眼能看出"这是叠出来
        // 的新窗口"，不会跟父窗口完全重叠在一起看起来像"顶替"了它。
        int cascadeIndex = _openInstances.Count;
        Rect baseRect = LoadOrCreateCenteredRect(FixedWidth, FixedHeight);
        baseRect.x += cascadeIndex * 32f;
        baseRect.y += cascadeIndex * 32f;
        instance.position = ClampToScreen(baseRect);

        return instance;
    }

    /// <summary>关掉当前最上层（最后打开）的那一个窗口，不影响更底下还开着的父窗口。
    /// 保持原行为：不标记 _intentionalClose，OnDestroy 该自动写回的还是会写回。</summary>
    public static void CloseActivePicker()
    {
        ActiveInstance?.Close();
    }

    private void ShowAsStackModal()
    {
        AIModalWindowStack.Register(this);
        ShowModalUtility();
        Focus();
    }

    private void NotifyRetargeted(string message)
    {
        EditorApplication.Beep();
        attentionMessage = message;
        attentionUntilTime = EditorApplication.timeSinceStartup + 0.8d;
        Focus();
        Repaint();
    }

    private void OnEnable()
    {
        AIModalWindowStack.Register(this);
        if (!_openInstances.Contains(this))
            _openInstances.Add(this);
    }

    private void OnDisable()
    {
        SaveWindowPosition();
        AIModalWindowStack.Unregister(this);
    }

    private void OnDestroy()
    {
        SaveWindowPosition();
        AIModalWindowStack.Unregister(this);
        _openInstances.Remove(this);

        // 按 X 关闭时 _intentionalClose 为 false——如果数据合法就自动写回，避免丢失填写内容。
        if (!_intentionalClose && workingValue != null && slotDef != null && IsWorkingValueValid())
        {
            if (useTemporaryMode)
                onTemporaryConfirm?.Invoke(CloneValue(workingValue));
            else
                WriteBack();
        }
    }

    private void OnLostFocus()
    {
        if (AIScenePickFocusGuard.ShouldSuppressFocusSteal())
            return;

        EditorApplication.delayCall += () =>
        {
            if (this == null)
                return;

            if (AIScenePickFocusGuard.ShouldSuppressFocusSteal())
                return;

            if (AIModalWindowStack.IsTop(this))
                Focus();
            else
                AIModalWindowStack.FocusTop();
        };
    }

    private void OnGUI()
    {
        bool isTop = AIModalWindowStack.IsTop(this);

        HandleEscapeKey(isTop);

        if (slotDef == null)
        {
            EditorGUILayout.HelpBox("槽位定义不存在。", MessageType.Error);
            if (GUILayout.Button("关闭"))
                Close();
            return;
        }

        DrawAttentionBanner();

        if (!isTop)
            EditorGUILayout.HelpBox("当前有更上层弹窗，下面内容暂不可操作。", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!isTop))
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(slotDef.displayName, EditorStyles.boldLabel);
            GUILayout.Space(4f);

            DrawSourceSelector();
            GUILayout.Space(6f);
            DrawValueEditor();
            GUILayout.Space(8f);
            DrawPreview();
            GUILayout.FlexibleSpace();
            DrawBottomButtons();
            EditorGUILayout.EndVertical();
        }

        if (attentionUntilTime > 0d && EditorApplication.timeSinceStartup < attentionUntilTime)
            Repaint();
    }

    private void HandleEscapeKey(bool isTop)
    {
        if (!isTop)
            return;

        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (e.keyCode != KeyCode.Escape)
            return;

        e.Use();
        _intentionalClose = true; // ESC = 主动取消，不自动写回
        Close();
        GUIUtility.ExitGUI();
    }

    private void DrawAttentionBanner()
    {
        if (attentionUntilTime <= 0d || EditorApplication.timeSinceStartup >= attentionUntilTime)
            return;

        EditorGUILayout.HelpBox(attentionMessage, MessageType.Warning);
    }

    private void DrawSourceSelector()
    {
        EditorGUILayout.LabelField("来源", EditorStyles.miniBoldLabel);

        LogicValueSourceType[] rawAllowed = slotDef.allowedSources != null && slotDef.allowedSources.Length > 0
            ? slotDef.allowedSources
            : new[] { LogicValueSourceType.Constant };

        // 触发器上下文：单位槽不显示 AI 专属的 ContextReference 来源
        LogicValueSourceType[] allowed;
        if (editorContext == LogicTemplateContext.Trigger && slotDef.valueType == LogicSlotValueType.Unit)
        {
            var filtered = new System.Collections.Generic.List<LogicValueSourceType>();
            foreach (var s in rawAllowed)
                if (s != LogicValueSourceType.ContextReference)
                    filtered.Add(s);
            allowed = filtered.Count > 0 ? filtered.ToArray() : new[] { LogicValueSourceType.SceneReference };
        }
        else
        {
            allowed = rawAllowed;
        }

        // 整数/浮点数槽自动注入随机范围 + 运算表达式选项——不用挨个改各个句型模板
        // 的allowedSources，全局统一生效。
        if (slotDef.valueType == LogicSlotValueType.Int || slotDef.valueType == LogicSlotValueType.Float)
        {
            allowed = AppendSourceIfMissing(allowed, LogicValueSourceType.RandomRange);
            allowed = AppendSourceIfMissing(allowed, LogicValueSourceType.Expression);
        }

        bool currentAllowed = false;
        for (int i = 0; i < allowed.Length; i++)
        {
            if (allowed[i] == workingValue.sourceType)
            {
                currentAllowed = true;
                break;
            }
        }

        if (!currentAllowed)
            workingValue.sourceType = allowed[0];

        if (allowed.Length == 1)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(GetSourceTypeDisplayName(allowed[0]));
            }
        }
        else
        {
            string[] names = new string[allowed.Length];
            int selected = 0;

            for (int i = 0; i < allowed.Length; i++)
            {
                names[i] = GetSourceTypeDisplayName(allowed[i]);
                if (allowed[i] == workingValue.sourceType)
                    selected = i;
            }

            int newIndex = EditorGUILayout.Popup(selected, names);
            var newSource = allowed[newIndex];
            // 切换到随机范围时，若当前范围无效则重置为合法默认值
            if (newSource == LogicValueSourceType.RandomRange && workingValue.sourceType != LogicValueSourceType.RandomRange)
            {
                if (slotDef.valueType == LogicSlotValueType.Float && workingValue.floatValue >= workingValue.floatValueMax)
                {
                    workingValue.floatValue    = 1f;
                    workingValue.floatValueMax = 3f;
                }
                if (slotDef.valueType == LogicSlotValueType.Int && workingValue.intValue >= workingValue.intValueMax)
                {
                    workingValue.intValue    = 1;
                    workingValue.intValueMax = 3;
                }
            }
            // 切换到表达式时不预设运算数——LogicExpressionSlotEditorGUI的按钮点击时
            // 才会懒创建默认值，留空显示"<未设置>"更诚实，不会看起来像已经配好了。
            workingValue.sourceType = newSource;
        }

        workingValue.valueType = slotDef.valueType;
    }

    private static string GetSourceTypeDisplayName(LogicValueSourceType source)
    {
        return source switch
        {
            LogicValueSourceType.Constant => "常量",
            LogicValueSourceType.Variable => "变量",
            LogicValueSourceType.ContextReference => "上下文引用",
            LogicValueSourceType.AssetReference => "资源引用",
            LogicValueSourceType.SceneReference => "场景引用",
            LogicValueSourceType.RandomRange => "随机范围",
            LogicValueSourceType.Expression => "表达式",
            _ => source.ToString()
        };
    }

    private static LogicValueSourceType[] AppendSourceIfMissing(LogicValueSourceType[] allowed, LogicValueSourceType source)
    {
        foreach (var s in allowed)
            if (s == source) return allowed;

        var extended = new LogicValueSourceType[allowed.Length + 1];
        allowed.CopyTo(extended, 0);
        extended[allowed.Length] = source;
        return extended;
    }

    private void DrawValueEditor()
    {
        EditorGUILayout.LabelField("值", EditorStyles.miniBoldLabel);

        if (DrawEnumOrPresetPopupIfNeeded())
            return;

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slotDef.valueType);
        if (handler == null)
        {
            EditorGUILayout.HelpBox($"未注册槽位处理器：{slotDef.valueType}", MessageType.Warning);
            return;
        }

        handler.DrawEditor(slotDef, workingValue);
    }

    private void DrawPreview()
    {
        bool valid = IsWorkingValueValid();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);

        string sourceText = GetSourceTypeDisplayName(workingValue.sourceType);
        string valueText = GetWorkingValueDisplayText();

        EditorGUILayout.LabelField($"来源：{sourceText}");
        EditorGUILayout.LabelField($"当前值：{valueText}");

        EditorGUILayout.HelpBox(
            valid ? "当前参数合法。" : "当前参数未填写或不合法。",
            valid ? MessageType.Info : MessageType.Error
        );
        EditorGUILayout.EndVertical();
    }

    private void DrawBottomButtons()
    {
        bool valid = IsWorkingValueValid();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(!valid))
        {
            if (GUILayout.Button("确定", GUILayout.Width(100f), GUILayout.Height(28f)))
            {
                _intentionalClose = true;
                if (useTemporaryMode)
                    onTemporaryConfirm?.Invoke(CloneValue(workingValue));
                else
                    WriteBack();

                Close();
            }
        }

        if (GUILayout.Button("取消", GUILayout.Width(100f), GUILayout.Height(28f)))
        {
            _intentionalClose = true; // 取消 = 主动丢弃，不自动写回
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }

    private bool IsWorkingValueValid()
    {
        if (IsEnumSlot())
            return !string.IsNullOrWhiteSpace(workingValue.enumValue) || !string.IsNullOrWhiteSpace(workingValue.stringValue);

        if (TryGetPresetOptionsForCurrentSlot(out List<LocalizedOption> _))
            return !string.IsNullOrWhiteSpace(workingValue.stringValue) || !string.IsNullOrWhiteSpace(workingValue.enumValue);

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slotDef.valueType);
        if (handler == null)
            return false;

        return handler.IsValid(slotDef, workingValue);
    }

    private string GetWorkingValueDisplayText()
    {
        if (IsEnumSlot())
        {
            List<LocalizedOption> options = BuildEnumOptionsFromSlot();
            string value = !string.IsNullOrWhiteSpace(workingValue.enumValue) ? workingValue.enumValue : workingValue.stringValue;
            return GetOptionLabel(options, value);
        }

        if (TryGetPresetOptionsForCurrentSlot(out List<LocalizedOption> presetOptions))
        {
            string value = !string.IsNullOrWhiteSpace(workingValue.stringValue) ? workingValue.stringValue : workingValue.enumValue;
            return GetOptionLabel(presetOptions, value);
        }

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slotDef.valueType);
        if (handler == null)
            return "<未注册处理器>";

        return handler.GetDisplayText(workingValue);
    }

    private void WriteBack()
    {
        if (targetObject == null || string.IsNullOrWhiteSpace(assignmentPropertyPath))
            return;

        SerializedObject so = new SerializedObject(targetObject);
        SerializedProperty assignmentProp = so.FindProperty(assignmentPropertyPath);
        if (assignmentProp == null)
            return;

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null)
            return;

        WriteValueToProperty(valueProp, workingValue);

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetObject);
    }

    // 递归写——表达式的运算数A/B自己也是完整的LogicSlotValue，嵌套的SerializedProperty
    // 要用同一套字段拷贝逻辑再走一遍，不然只有顶层字段能存住，嵌套的运算数关掉窗口就丢了。
    private static void WriteValueToProperty(SerializedProperty valueProp, LogicSlotValue value)
    {
        valueProp.FindPropertyRelative("valueType").enumValueIndex = (int)value.valueType;
        valueProp.FindPropertyRelative("sourceType").enumValueIndex = (int)value.sourceType;
        valueProp.FindPropertyRelative("boolValue").boolValue = value.boolValue;
        valueProp.FindPropertyRelative("intValue").intValue = value.intValue;
        SerializedProperty intMaxProp = valueProp.FindPropertyRelative("intValueMax");
        if (intMaxProp != null) intMaxProp.intValue = value.intValueMax;
        valueProp.FindPropertyRelative("floatValue").floatValue = value.floatValue;
        SerializedProperty floatMaxProp = valueProp.FindPropertyRelative("floatValueMax");
        if (floatMaxProp != null) floatMaxProp.floatValue = value.floatValueMax;
        valueProp.FindPropertyRelative("stringValue").stringValue = value.stringValue ?? "";
        valueProp.FindPropertyRelative("enumValue").stringValue = value.enumValue ?? "";
        var enumDisplayNameProp = valueProp.FindPropertyRelative("enumDisplayName");
        if (enumDisplayNameProp != null) enumDisplayNameProp.stringValue = value.enumDisplayName ?? "";
        valueProp.FindPropertyRelative("variableKey").stringValue = value.variableKey ?? "";
        valueProp.FindPropertyRelative("contextKey").stringValue = value.contextKey ?? "";
        valueProp.FindPropertyRelative("assetReference").objectReferenceValue = value.assetReference;
        valueProp.FindPropertyRelative("sceneObjectId").stringValue = value.sceneObjectId ?? "";
        valueProp.FindPropertyRelative("sceneObjectName").stringValue = value.sceneObjectName ?? "";

        SerializedProperty operatorProp = valueProp.FindPropertyRelative("expressionOperator");
        if (operatorProp != null) operatorProp.enumValueIndex = (int)value.expressionOperator;

        SerializedProperty operandAProp = valueProp.FindPropertyRelative("expressionOperandA");
        if (operandAProp != null && value.expressionOperandA != null)
            WriteValueToProperty(operandAProp, value.expressionOperandA);

        SerializedProperty operandBProp = valueProp.FindPropertyRelative("expressionOperandB");
        if (operandBProp != null && value.expressionOperandB != null)
            WriteValueToProperty(operandBProp, value.expressionOperandB);
    }

    private static LogicSlotValue ReadCurrentValue(UnityEngine.Object targetObject, string assignmentPropertyPath, LogicSentenceTemplate.SlotDefinition slotDef)
    {
        LogicSlotValue result = new LogicSlotValue
        {
            valueType = slotDef != null ? slotDef.valueType : LogicSlotValueType.Float,
            sourceType = slotDef != null && slotDef.allowedSources != null && slotDef.allowedSources.Length > 0
                ? slotDef.allowedSources[0]
                : LogicValueSourceType.Constant
        };

        if (targetObject == null || string.IsNullOrWhiteSpace(assignmentPropertyPath))
            return result;

        SerializedObject so = new SerializedObject(targetObject);
        SerializedProperty assignmentProp = so.FindProperty(assignmentPropertyPath);
        if (assignmentProp == null)
            return result;

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null)
            return result;

        return ReadValueFromProperty(valueProp);
    }

    // 递归读，跟WriteValueToProperty对称。
    private static LogicSlotValue ReadValueFromProperty(SerializedProperty valueProp)
    {
        LogicSlotValue result = new LogicSlotValue
        {
            valueType = (LogicSlotValueType)valueProp.FindPropertyRelative("valueType").enumValueIndex,
            sourceType = (LogicValueSourceType)valueProp.FindPropertyRelative("sourceType").enumValueIndex,
            boolValue = valueProp.FindPropertyRelative("boolValue").boolValue,
            intValue = valueProp.FindPropertyRelative("intValue").intValue,
            intValueMax = valueProp.FindPropertyRelative("intValueMax")?.intValue ?? 0,
            floatValue = valueProp.FindPropertyRelative("floatValue").floatValue,
            floatValueMax = valueProp.FindPropertyRelative("floatValueMax")?.floatValue ?? 0f,
            stringValue = valueProp.FindPropertyRelative("stringValue").stringValue,
            enumValue = valueProp.FindPropertyRelative("enumValue").stringValue,
            enumDisplayName = valueProp.FindPropertyRelative("enumDisplayName")?.stringValue ?? "",
            variableKey = valueProp.FindPropertyRelative("variableKey").stringValue,
            contextKey = valueProp.FindPropertyRelative("contextKey").stringValue,
            assetReference = valueProp.FindPropertyRelative("assetReference").objectReferenceValue,
            sceneObjectId = valueProp.FindPropertyRelative("sceneObjectId").stringValue,
            sceneObjectName = valueProp.FindPropertyRelative("sceneObjectName").stringValue
        };

        SerializedProperty operatorProp = valueProp.FindPropertyRelative("expressionOperator");
        if (operatorProp != null) result.expressionOperator = (LogicMathOperator)operatorProp.enumValueIndex;

        SerializedProperty operandAProp = valueProp.FindPropertyRelative("expressionOperandA");
        if (operandAProp != null && result.sourceType == LogicValueSourceType.Expression)
            result.expressionOperandA = ReadValueFromProperty(operandAProp);

        SerializedProperty operandBProp = valueProp.FindPropertyRelative("expressionOperandB");
        if (operandBProp != null && result.sourceType == LogicValueSourceType.Expression)
            result.expressionOperandB = ReadValueFromProperty(operandBProp);

        return result;
    }

    private static LogicSlotValue CloneValue(LogicSlotValue src)
    {
        if (src == null)
            return new LogicSlotValue();

        return new LogicSlotValue
        {
            valueType = src.valueType,
            sourceType = src.sourceType,
            boolValue = src.boolValue,
            intValue = src.intValue,
            intValueMax = src.intValueMax,
            floatValue = src.floatValue,
            floatValueMax = src.floatValueMax,
            stringValue = src.stringValue,
            enumValue = src.enumValue,
            enumDisplayName = src.enumDisplayName,
            variableKey = src.variableKey,
            contextKey = src.contextKey,
            assetReference = src.assetReference,
            sceneObjectId = src.sceneObjectId,
            sceneObjectName = src.sceneObjectName,
            expressionOperator = src.expressionOperator,
            expressionOperandA = src.expressionOperandA != null ? CloneValue(src.expressionOperandA) : null,
            expressionOperandB = src.expressionOperandB != null ? CloneValue(src.expressionOperandB) : null
        };
    }


    private struct LocalizedOption
    {
        public string value;
        public string label;

        public LocalizedOption(string value, string label)
        {
            this.value = value ?? "";
            this.label = label ?? value ?? "";
        }
    }

    private bool DrawEnumOrPresetPopupIfNeeded()
    {
        if (IsEnumSlot())
        {
            workingValue.valueType = LogicSlotValueType.Enum;
            workingValue.sourceType = LogicValueSourceType.Constant;

            List<LocalizedOption> options = BuildEnumOptionsFromSlot();
            if (options.Count <= 0)
            {
                EditorGUILayout.HelpBox("该枚举槽位没有可选项。", MessageType.Warning);
                return true;
            }

            string currentValue = !string.IsNullOrWhiteSpace(workingValue.enumValue)
                ? workingValue.enumValue
                : workingValue.stringValue;

            int selected = FindOptionIndex(options, currentValue);
            string[] labels = BuildLabels(options);
            int next = EditorGUILayout.Popup(Mathf.Max(0, selected), labels);
            next = Mathf.Clamp(next, 0, options.Count - 1);

            workingValue.enumValue = options[next].value;
            workingValue.enumDisplayName = !string.IsNullOrWhiteSpace(options[next].label) ? options[next].label : options[next].value;
            workingValue.stringValue = "";
            return true;
        }

        if (TryGetPresetOptionsForCurrentSlot(out List<LocalizedOption> presetOptions))
        {
            // 兼容旧 AI 包或旧模板：有些“行为类型 / 移动方式 / 感知对象”槽位历史上是 String。
            // 这里不强改 slotDef.valueType，避免旧数据校验炸掉，只把输入框替换成下拉框。
            workingValue.valueType = slotDef.valueType;
            workingValue.sourceType = LogicValueSourceType.Constant;

            string currentValue = !string.IsNullOrWhiteSpace(workingValue.stringValue)
                ? workingValue.stringValue
                : workingValue.enumValue;

            int selected = FindOptionIndex(presetOptions, currentValue);
            string[] labels = BuildLabels(presetOptions);
            int next = EditorGUILayout.Popup(Mathf.Max(0, selected), labels);
            next = Mathf.Clamp(next, 0, presetOptions.Count - 1);

            if (slotDef.valueType == LogicSlotValueType.Enum)
            {
                workingValue.enumValue = presetOptions[next].value;
                workingValue.enumDisplayName = !string.IsNullOrWhiteSpace(presetOptions[next].label) ? presetOptions[next].label : presetOptions[next].value;
                workingValue.stringValue = "";
            }
            else
            {
                workingValue.stringValue = presetOptions[next].value;
                workingValue.enumValue = "";
                workingValue.enumDisplayName = "";
            }

            return true;
        }

        return false;
    }

    private bool IsEnumSlot()
    {
        return slotDef != null && slotDef.valueType == LogicSlotValueType.Enum;
    }

    private bool TryGetPresetOptionsForCurrentSlot(out List<LocalizedOption> options)
    {
        string slotId = !string.IsNullOrWhiteSpace(editingSlotId) ? editingSlotId : slotDef != null ? slotDef.slotId : "";
        string displayName = slotDef != null ? slotDef.displayName : "";
        string currentValue = GetCurrentRawValueForPresetDetection();

        options = null;

        if (IsBehaviorTypeSlot(slotId, displayName, currentValue))
        {
            options = BuildBehaviorTypeOptions();
            return true;
        }

        if (IsMoveModeSlot(slotId, displayName, currentValue))
        {
            options = BuildMoveModeOptions();
            return true;
        }

        if (IsPerceptionTargetKindSlot(slotId, displayName, currentValue))
        {
            options = BuildPerceptionTargetKindOptions();
            return true;
        }

        return false;
    }

    private string GetCurrentRawValueForPresetDetection()
    {
        if (workingValue == null)
            return "";

        if (!string.IsNullOrWhiteSpace(workingValue.enumValue))
            return workingValue.enumValue;

        if (!string.IsNullOrWhiteSpace(workingValue.stringValue))
            return workingValue.stringValue;

        return "";
    }

    private static bool IsBehaviorTypeSlot(string slotId, string displayName, string currentValue)
    {
        string id = (slotId ?? "").Trim();
        string name = (displayName ?? "").Trim();
        string value = (currentValue ?? "").Trim();

        if (EqualsAny(id, "behaviorType", "behaviourType", "actionType", "aiBehaviorType", "aiState", "stateType"))
            return true;

        if (name.Contains("行动类型") || name.Contains("行为类型") || name.Contains("AI状态") || name.Contains("状态类型"))
            return true;

        return IsKnownBehaviorTypeValue(value);
    }

    private static bool IsMoveModeSlot(string slotId, string displayName, string currentValue)
    {
        string id = (slotId ?? "").Trim();
        string name = (displayName ?? "").Trim();
        string value = (currentValue ?? "").Trim();

        if (EqualsAny(id, "moveMode", "movementMode", "moveType", "movementType", "moveSpeedMode"))
            return true;

        if (name.Contains("移动方式") || name.Contains("移动类型") || name.Contains("移动模式"))
            return true;

        return IsKnownMoveModeValue(value);
    }

    private static bool IsPerceptionTargetKindSlot(string slotId, string displayName, string currentValue)
    {
        string id = (slotId ?? "").Trim();
        string name = (displayName ?? "").Trim();
        string value = (currentValue ?? "").Trim();

        if (EqualsAny(id, "targetKind", "perceptionTargetKind", "perceptionTarget", "targetCategory", "targetRelation"))
            return true;

        if (name == "对象" || name.Contains("感知对象") || name.Contains("目标类别") || name.Contains("目标关系"))
            return true;

        return IsKnownPerceptionTargetKindValue(value);
    }

    private static List<LocalizedOption> BuildBehaviorTypeOptions()
    {
        return new List<LocalizedOption>
        {
            new LocalizedOption("待机", "待机"),
            new LocalizedOption("怀疑", "怀疑"),
            new LocalizedOption("警戒", "警戒"),
            new LocalizedOption("发现", "发现"),
            new LocalizedOption("徘徊", "徘徊"),
            new LocalizedOption("逃跑", "逃跑"),
            new LocalizedOption("停止", "停止"),
            new LocalizedOption("追击", "追击"),
            new LocalizedOption("攻击", "攻击"),
            new LocalizedOption("返回", "返回")
        };
    }

    private static List<LocalizedOption> BuildMoveModeOptions()
    {
        return new List<LocalizedOption>
        {
            new LocalizedOption("行走", "行走"),
            new LocalizedOption("奔跑", "奔跑"),
            new LocalizedOption("潜行", "潜行")
        };
    }

    private static List<LocalizedOption> BuildPerceptionTargetKindOptions()
    {
        return new List<LocalizedOption>
        {
            new LocalizedOption("玩家", "玩家"),
            new LocalizedOption("当前目标", "当前目标"),
            new LocalizedOption("敌对单位", "敌对单位"),
            new LocalizedOption("友军", "友军"),
            new LocalizedOption("中立单位", "中立单位"),
            new LocalizedOption("生物", "生物")
        };
    }

    private static bool IsKnownBehaviorTypeValue(string value)
    {
        return EqualsAny(value,
            "Idle", "待机",
            "Suspicious", "怀疑",
            "Alert", "警戒",
            "Detected", "发现",
            "Wander", "徘徊",
            "Flee", "逃跑",
            "Stop", "停止",
            "Chase", "追击",
            "Attack", "攻击",
            "Return", "返回");
    }

    private static bool IsKnownMoveModeValue(string value)
    {
        return EqualsAny(value, "Walk", "行走", "Run", "奔跑", "Sneak", "潜行");
    }

    private static bool IsKnownPerceptionTargetKindValue(string value)
    {
        return EqualsAny(value,
            "Player", "玩家",
            "CurrentTarget", "当前目标",
            "HostileUnit", "敌对单位",
            "FriendlyUnit", "友军",
            "NeutralUnit", "中立单位",
            "CreatureUnit", "生物");
    }

    private static bool EqualsAny(string value, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value) || candidates == null)
            return false;

        for (int i = 0; i < candidates.Length; i++)
        {
            if (string.Equals(value, candidates[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private List<LocalizedOption> BuildEnumOptionsFromSlot()
    {
        List<LocalizedOption> result = new List<LocalizedOption>();

        if (slotDef == null || slotDef.enumOptions == null)
            return result;

        foreach (object option in slotDef.enumOptions)
        {
            if (option == null)
                continue;

            string value = ReadStringMember(option, "value", "id", "key", "enumValue", "name");
            string label = ReadStringMember(option, "label", "displayName", "display", "text", "name");

            if (string.IsNullOrWhiteSpace(value))
                value = option.ToString();

            if (string.IsNullOrWhiteSpace(label))
                label = value;

            result.Add(new LocalizedOption(value, label));
        }

        return result;
    }

    private static string ReadStringMember(object target, params string[] names)
    {
        if (target == null || names == null)
            return "";

        Type type = target.GetType();
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(string))
            {
                string value = field.GetValue(target) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(string) && property.CanRead)
            {
                string value = property.GetValue(target, null) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return "";
    }

    private static int FindOptionIndex(List<LocalizedOption> options, string currentValue)
    {
        if (options == null || options.Count <= 0)
            return 0;

        if (string.IsNullOrWhiteSpace(currentValue))
            return 0;

        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].value, currentValue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options[i].label, currentValue, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static string[] BuildLabels(List<LocalizedOption> options)
    {
        if (options == null || options.Count <= 0)
            return new[] { "<无选项>" };

        string[] labels = new string[options.Count];
        for (int i = 0; i < options.Count; i++)
            labels[i] = string.IsNullOrWhiteSpace(options[i].label) ? options[i].value : options[i].label;

        return labels;
    }

    private static string GetOptionLabel(List<LocalizedOption> options, string value)
    {
        if (options != null && !string.IsNullOrWhiteSpace(value))
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].value, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options[i].label, value, StringComparison.OrdinalIgnoreCase))
                    return options[i].label;
            }
        }

        return string.IsNullOrWhiteSpace(value) ? "<未指定>" : value;
    }

    private void SaveWindowPosition()
    {
        Rect r = position;
        SessionState.SetFloat(PosXKey, r.x);
        SessionState.SetFloat(PosYKey, r.y);
        SessionState.SetFloat(PosWKey, r.width);
        SessionState.SetFloat(PosHKey, r.height);
    }

    private static Rect LoadOrCreateCenteredRect(float width, float height)
    {
        float w = SessionState.GetFloat(PosWKey, width);
        float h = SessionState.GetFloat(PosHKey, height);
        float x = SessionState.GetFloat(PosXKey, float.NaN);
        float y = SessionState.GetFloat(PosYKey, float.NaN);

        if (float.IsNaN(x) || float.IsNaN(y))
        {
            x = (Screen.currentResolution.width - w) * 0.5f;
            y = (Screen.currentResolution.height - h) * 0.5f;
        }

        return new Rect(x, y, w, h);
    }

    private static Rect ClampToScreen(Rect r)
    {
        float screenW = Mathf.Max(800f, Screen.currentResolution.width);
        float screenH = Mathf.Max(600f, Screen.currentResolution.height);

        r.width = FixedWidth;
        r.height = FixedHeight;
        r.x = Mathf.Clamp(r.x, 0f, screenW - r.width);
        r.y = Mathf.Clamp(r.y, 0f, screenH - r.height);
        return r;
    }
}
