using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AILogicSentenceTemplatePickerWindow : EditorWindow
{
    private static Action<LogicSentenceInstance> onConfirm;
    private static AILogicSentenceTemplatePickerWindow activeWindow;

    private const float FixedWidth = 540f;
    private const float FixedHeight = 380f;

    private const string PosXKey = "AI.TemplatePicker.PosX";
    private const string PosYKey = "AI.TemplatePicker.PosY";
    private const string PosWKey = "AI.TemplatePicker.PosW";
    private const string PosHKey = "AI.TemplatePicker.PosH";

    private LogicSentenceCategory category;
    private LogicTemplateContext editorContext = LogicTemplateContext.AI;
    private string searchText = "";
    private int selectedIndex = -1;

    private int selectedTagIndex = 0;
    private readonly string[] tagOptions = { "全部", "单位", "AI", "数学", "时间", "点" };

    private readonly List<LogicSentenceTemplate> filteredTemplates = new List<LogicSentenceTemplate>();
    private LogicSentenceInstance workingInstance;

    private double attentionUntilTime = -1d;
    private string attentionMessage = "";

    public static bool HasOpenWindow => activeWindow != null;
    public static AILogicSentenceTemplatePickerWindow ActiveWindow => activeWindow;

    public static void Open(LogicSentenceCategory category, Action<LogicSentenceInstance> confirmCallback)
    {
        Open(category, LogicTemplateContext.AI, confirmCallback);
    }

    public static void Open(LogicSentenceCategory category, LogicTemplateContext context, Action<LogicSentenceInstance> confirmCallback)
    {
        var window = CreateOrRetargetWindow(category, context, confirmCallback);
        window.RefreshTemplates();
        window.ShowAsStackModal();
    }

    public static void OpenForEdit(
        LogicSentenceCategory category,
        LogicSentenceInstance existingInstance,
        Action<LogicSentenceInstance> confirmCallback)
    {
        OpenForEdit(category, LogicTemplateContext.AI, existingInstance, confirmCallback);
    }

    public static void OpenForEdit(
        LogicSentenceCategory category,
        LogicTemplateContext context,
        LogicSentenceInstance existingInstance,
        Action<LogicSentenceInstance> confirmCallback)
    {
        var window = CreateOrRetargetWindow(category, context, confirmCallback);
        window.RefreshTemplates();
        window.LoadExistingInstance(existingInstance);
        window.ShowAsStackModal();
    }

    public static void ReopenFromRestoreState(AIScenePickRestoreState state)
    {
        if (state == null)
            return;

        var window = CreateOrRetargetWindow(state.category, state.editorContext, onConfirm);
        window.selectedIndex = state.selectedTemplateIndex;
        window.selectedTagIndex = state.selectedTagIndex;
        window.searchText = state.searchText ?? "";
        window.workingInstance = CloneSentenceInstanceStatic(state.workingInstance);
        window.RefreshTemplatesPreserveWorkingState();
        window.ShowAsStackModal();

        if (state.windowRect.width > 0f && state.windowRect.height > 0f)
            window.position = ClampToScreen(state.windowRect);
    }

    public static void CloseActiveWindow()
    {
        if (activeWindow != null)
        {
            activeWindow.Close();
            activeWindow = null;
        }
    }

    public AIScenePickRestoreState BuildRestoreState(string slotId)
    {
        return new AIScenePickRestoreState
        {
            windowType = "AI.LogicSentenceTemplatePicker",
            windowRect = position,
            category = category,
            editorContext = editorContext,
            selectedTemplateIndex = selectedIndex,
            selectedTagIndex = selectedTagIndex,
            searchText = searchText,
            workingInstance = CloneSentenceInstanceStatic(workingInstance),
            slotId = slotId,
            payloadJson = ""
        };
    }

    public static void BeginScenePickForSlot(string slotId)
    {
        if (activeWindow == null || activeWindow.workingInstance == null || string.IsNullOrWhiteSpace(slotId))
        {
            EditorApplication.Beep();
            if (activeWindow != null)
                activeWindow.Notify("当前槽位无效，无法进入地图选择。");
            return;
        }

        LogicSentenceTemplate template = null;
        if (activeWindow.selectedIndex >= 0 && activeWindow.selectedIndex < activeWindow.filteredTemplates.Count)
            template = activeWindow.filteredTemplates[activeWindow.selectedIndex];

        LogicSentenceTemplate.SlotDefinition slotDef = null;
        if (template != null)
            slotDef = activeWindow.FindSlot(template, slotId);

        LogicSlotAssignment assignment = activeWindow.GetOrCreateAssignment(slotId, slotDef);
        if (slotDef == null || assignment == null)
        {
            EditorApplication.Beep();
            activeWindow.Notify("无法找到槽位定义或槽位数据。");
            return;
        }

        AIScenePickRestoreState restoreState = activeWindow.BuildRestoreState(slotId);

        bool started = AILogicScenePickLauncher.BeginFromLogicSlot(
            slotId,
            slotDef,
            () => activeWindow != null ? activeWindow.CloneValue(assignment.value) : new LogicSlotValue(),
            newValue =>
            {
                if (assignment != null && activeWindow != null)
                {
                    assignment.value = activeWindow.CloneValue(newValue);
                    activeWindow.Repaint();
                }
            },
            restoreState,
            AIScenePickKind.Unit
        );

        if (!started)
        {
            EditorApplication.Beep();
            activeWindow.Notify("进入地图选择失败。");
            return;
        }

        Debug.Log($"[AI Logic Picker] Scene pick launched for slotId={slotId}");

        SkyPrisonEditorWindow.HideForScenePick();
        CloseActiveWindow();
        AISlotValuePickerWindow.CloseActivePicker();
    }

    private static AILogicSentenceTemplatePickerWindow CreateOrRetargetWindow(
        LogicSentenceCategory category,
        LogicTemplateContext context,
        Action<LogicSentenceInstance> confirmCallback)
    {
        if (activeWindow == null)
        {
            activeWindow = CreateInstance<AILogicSentenceTemplatePickerWindow>();
            activeWindow.minSize = new Vector2(FixedWidth, FixedHeight);
            activeWindow.maxSize = new Vector2(FixedWidth, FixedHeight);
            activeWindow.position = LoadOrCreateCenteredRect(FixedWidth, FixedHeight);
        }

        activeWindow.category = category;
        activeWindow.editorContext = context;
        activeWindow.titleContent = new GUIContent($"{GetWindowTitle(category)} / {LogicSentenceContextUtility.GetContextDisplayName(context)}");
        if (confirmCallback != null)
            onConfirm = confirmCallback;

        return activeWindow;
    }

    private void ShowAsStackModal()
    {
        AIModalWindowStack.Register(this);
        position = ClampToScreen(LoadOrCreateCenteredRect(FixedWidth, FixedHeight));
        ShowModalUtility();
        Focus();
    }

    private void LoadExistingInstance(LogicSentenceInstance existingInstance)
    {
        if (existingInstance == null)
            return;

        workingInstance = CloneSentenceInstanceStatic(existingInstance);

        for (int i = 0; i < filteredTemplates.Count; i++)
        {
            if (filteredTemplates[i] != null && filteredTemplates[i].templateId == existingInstance.templateId)
            {
                selectedIndex = i;
                ApplyContextFixedSlotDefaults(filteredTemplates[i], workingInstance);
                break;
            }
        }

        Repaint();
    }

    private void Notify(string message)
    {
        EditorApplication.Beep();
        attentionMessage = message;
        attentionUntilTime = EditorApplication.timeSinceStartup + 1.0d;
        Focus();
        Repaint();
    }

    private void OnEnable()
    {
        AILogicSentenceTemplateLibrary.InvalidateCache();
        AIModalWindowStack.Register(this);
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

        if (activeWindow == this)
            activeWindow = null;
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
        DrawAttentionBanner();

        if (!isTop)
            EditorGUILayout.HelpBox("当前有更上层弹窗，下面内容暂不可操作。", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!isTop))
        {
            DrawTopBar();
            GUILayout.Space(6f);
            DrawTemplateDropdown();
            GUILayout.Space(8f);
            DrawPreviewArea();
            GUILayout.FlexibleSpace();
            DrawBottomButtons();
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
        Close();
        GUIUtility.ExitGUI();
    }

    private static string GetWindowTitle(LogicSentenceCategory category)
    {
        return category switch
        {
            LogicSentenceCategory.Condition => "条件句型",
            LogicSentenceCategory.Motive => "事件句型",
            LogicSentenceCategory.Action => "行动句型",
            _ => "句型"
        };
    }

    private void DrawAttentionBanner()
    {
        if (attentionUntilTime <= 0d || EditorApplication.timeSinceStartup >= attentionUntilTime)
            return;

        EditorGUILayout.HelpBox(attentionMessage, MessageType.Warning);
    }

    private void DrawTopBar()
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField(GetWindowTitle(category), EditorStyles.boldLabel);
        GUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(180f));
        EditorGUILayout.LabelField("筛选", EditorStyles.miniBoldLabel);
        int newTagIndex = EditorGUILayout.Popup(selectedTagIndex, tagOptions);
        if (newTagIndex != selectedTagIndex)
        {
            selectedTagIndex = newTagIndex;
            RefreshTemplates();
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10f);

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField("搜索", EditorStyles.miniBoldLabel);
        string newSearch = EditorGUILayout.TextField(searchText);
        if (newSearch != searchText)
        {
            searchText = newSearch;
            RefreshTemplates();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawTemplateDropdown()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("句型", EditorStyles.boldLabel);

        if (filteredTemplates.Count == 0)
        {
            EditorGUILayout.HelpBox("没有可用句型。", MessageType.Info);
        }
        else
        {
            string[] options = new string[filteredTemplates.Count];
            for (int i = 0; i < filteredTemplates.Count; i++)
                options[i] = filteredTemplates[i].displayName;

            int oldIndex = selectedIndex;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, filteredTemplates.Count - 1);
            selectedIndex = EditorGUILayout.Popup(selectedIndex, options);

            if (selectedIndex != oldIndex)
                RebuildWorkingInstance();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPreviewArea()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("句型结构预览", EditorStyles.boldLabel);

        Rect previewRect = GUILayoutUtility.GetRect(10f, 100f, GUILayout.ExpandWidth(true));
        GUI.Box(previewRect, GUIContent.none);

        if (selectedIndex < 0 || selectedIndex >= filteredTemplates.Count || workingInstance == null)
        {
            GUI.Label(
                new Rect(previewRect.x + 8f, previewRect.y + 8f, previewRect.width - 16f, 20f),
                "请选择一个句型。",
                GetPreviewStyle()
            );
        }
        else
        {
            LogicSentenceTemplate template = filteredTemplates[selectedIndex];
            DrawTemplatePreview(template, previewRect);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTemplatePreview(LogicSentenceTemplate template, Rect rect)
    {
        GUIStyle textStyle = GetPreviewStyle();

        float x = rect.x + 10f;
        float y = rect.y + 10f;

        string prefix = category switch
        {
            LogicSentenceCategory.Condition => "如果 ",
            LogicSentenceCategory.Motive => "当 ",
            LogicSentenceCategory.Action => "执行 ",
            _ => ""
        };

        Vector2 prefixSize = textStyle.CalcSize(new GUIContent(prefix));
        GUI.Label(new Rect(x, y, prefixSize.x, 22f), prefix, textStyle);
        x += prefixSize.x;

        for (int i = 0; i < template.tokens.Count; i++)
        {
            var token = template.tokens[i];
            if (!token.isSlot)
            {
                string text = token.text;
                Vector2 size = textStyle.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(x, y, size.x, 22f), text, textStyle);
                x += size.x;
            }
            else
            {
                var slot = FindSlot(template, token.slotId);
                var assignment = GetOrCreateAssignment(token.slotId, slot);

                bool fixedSlot = IsContextFixedUnitSlot(slot);
                string slotText = GetAssignmentDisplayText(assignment, slot);
                bool valid = fixedSlot || IsAssignmentValid(slot, assignment);

                Rect clickRect = DrawClickableUnderlinedText(
                    x,
                    y,
                    slotText,
                    fixedSlot
                        ? new Color(0.70f, 0.95f, 0.70f, 1f)
                        : valid ? new Color(0.30f, 0.75f, 1f, 1f) : new Color(1f, 0.25f, 0.25f, 1f)
                );

                if (!fixedSlot && GUI.Button(clickRect, GUIContent.none, GUIStyle.none))
                    OpenSlotPicker(slot, assignment);

                x = clickRect.xMax + 4f;
            }
        }

        string hint = "点击红色槽位可进入参数设置。";
        GUI.Label(
            new Rect(rect.x + 10f, rect.yMax - 24f, rect.width - 20f, 18f),
            hint,
            EditorStyles.miniLabel
        );
    }

    private Rect DrawClickableUnderlinedText(float x, float y, string text, Color color)
    {
        GUIStyle style = GetPreviewStyle();
        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect textRect = new Rect(x, y, size.x, 22f);

        Color old = GUI.color;
        GUI.color = color;
        GUI.Label(textRect, text, style);
        GUI.color = old;

        Rect underlineRect = new Rect(textRect.x, textRect.yMax - 3f, textRect.width, 1f);
        EditorGUI.DrawRect(underlineRect, color);

        return textRect;
    }

    private void DrawBottomButtons()
    {
        bool canConfirm = CanConfirm();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(!canConfirm))
        {
            if (GUILayout.Button("确定", GUILayout.Width(100f), GUILayout.Height(28f)))
            {
                onConfirm?.Invoke(CloneSentenceInstanceStatic(workingInstance));
                Close();
            }
        }

        if (GUILayout.Button("取消", GUILayout.Width(100f), GUILayout.Height(28f)))
            Close();

        EditorGUILayout.EndHorizontal();
    }

    private void RefreshTemplates()
    {
        filteredTemplates.Clear();

        List<LogicSentenceTemplate> templates = AILogicSentenceTemplateLibrary.GetTemplatesByCategory(category);
        string keyword = string.IsNullOrWhiteSpace(searchText) ? "" : searchText.Trim().ToLower();

        for (int i = 0; i < templates.Count; i++)
        {
            LogicSentenceTemplate t = templates[i];

            if (!LogicSentenceContextUtility.IsTemplateAllowed(editorContext, t))
                continue;

            if (!MatchTag(t))
                continue;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                filteredTemplates.Add(t);
            }
            else
            {
                bool match =
                    (!string.IsNullOrWhiteSpace(t.displayName) && t.displayName.ToLower().Contains(keyword)) ||
                    (!string.IsNullOrWhiteSpace(t.templateId) && t.templateId.ToLower().Contains(keyword));

                if (match)
                    filteredTemplates.Add(t);
            }
        }

        if (filteredTemplates.Count > 0)
            selectedIndex = Mathf.Clamp(selectedIndex, 0, filteredTemplates.Count - 1);
        else
            selectedIndex = -1;

        RebuildWorkingInstance();
        Repaint();
    }

    private void RefreshTemplatesPreserveWorkingState()
    {
        filteredTemplates.Clear();

        List<LogicSentenceTemplate> templates = AILogicSentenceTemplateLibrary.GetTemplatesByCategory(category);
        string keyword = string.IsNullOrWhiteSpace(searchText) ? "" : searchText.Trim().ToLower();

        for (int i = 0; i < templates.Count; i++)
        {
            LogicSentenceTemplate t = templates[i];

            if (!LogicSentenceContextUtility.IsTemplateAllowed(editorContext, t))
                continue;

            if (!MatchTag(t))
                continue;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                filteredTemplates.Add(t);
            }
            else
            {
                bool match =
                    (!string.IsNullOrWhiteSpace(t.displayName) && t.displayName.ToLower().Contains(keyword)) ||
                    (!string.IsNullOrWhiteSpace(t.templateId) && t.templateId.ToLower().Contains(keyword));

                if (match)
                    filteredTemplates.Add(t);
            }
        }

        if (filteredTemplates.Count == 0)
        {
            selectedIndex = -1;
            workingInstance = null;
        }
        else
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, filteredTemplates.Count - 1);
        }

        Repaint();
    }

    private void ApplyContextFixedSlotDefaults(LogicSentenceTemplate template, LogicSentenceInstance instance)
    {
        if (template == null || instance == null || template.slots == null)
            return;

        for (int i = 0; i < template.slots.Count; i++)
        {
            LogicSentenceTemplate.SlotDefinition slot = template.slots[i];
            if (!IsContextFixedUnitSlot(slot))
                continue;

            LogicSlotAssignment assignment = null;
            for (int j = 0; j < instance.slotAssignments.Count; j++)
            {
                if (instance.slotAssignments[j] != null && instance.slotAssignments[j].slotId == slot.slotId)
                {
                    assignment = instance.slotAssignments[j];
                    break;
                }
            }

            if (assignment == null)
            {
                assignment = new LogicSlotAssignment { slotId = slot.slotId };
                instance.slotAssignments.Add(assignment);
            }

            assignment.value = BuildContextDefaultValue(slot);
        }
    }

    private bool MatchTag(LogicSentenceTemplate template)
    {
        if (selectedTagIndex == 0)
            return true;

        LogicTemplateTag tag = selectedTagIndex switch
        {
            1 => LogicTemplateTag.Unit,
            2 => LogicTemplateTag.AI,
            3 => LogicTemplateTag.Math,
            4 => LogicTemplateTag.Time,
            5 => LogicTemplateTag.Point,
            _ => LogicTemplateTag.Unit
        };

        return template.tags != null && template.tags.Contains(tag);
    }

    private void RebuildWorkingInstance()
    {
        if (selectedIndex < 0 || selectedIndex >= filteredTemplates.Count)
        {
            workingInstance = null;
            return;
        }

        LogicSentenceTemplate template = filteredTemplates[selectedIndex];
        workingInstance = AILogicSentenceTemplateLibrary.CreateInstance(template.templateId);
        ApplyContextFixedSlotDefaults(template, workingInstance);
    }

    private void OpenSlotPicker(LogicSentenceTemplate.SlotDefinition slot, LogicSlotAssignment assignment)
    {
        if (slot == null || assignment == null)
            return;

        if (IsContextFixedUnitSlot(slot))
        {
            Notify("AI 编辑器中的单位主语由运行时上下文决定，不能手动修改。");
            return;
        }

        AISlotValuePickerWindow.OpenTemporary(
            assignment.slotId,
            slot,
            CloneValue(assignment.value),
            newValue =>
            {
                assignment.value = CloneValue(newValue);
                Repaint();
            },
            editorContext);
    }

    private LogicSentenceTemplate.SlotDefinition FindSlot(LogicSentenceTemplate template, string slotId)
    {
        for (int i = 0; i < template.slots.Count; i++)
        {
            if (template.slots[i].slotId == slotId)
                return template.slots[i];
        }

        return null;
    }

    private LogicSlotAssignment GetOrCreateAssignment(string slotId, LogicSentenceTemplate.SlotDefinition slot)
    {
        if (workingInstance == null || string.IsNullOrWhiteSpace(slotId))
            return null;

        for (int i = 0; i < workingInstance.slotAssignments.Count; i++)
        {
            if (workingInstance.slotAssignments[i] != null &&
                workingInstance.slotAssignments[i].slotId == slotId)
            {
                // 旧数据 floatValue==0 且模板有非零默认值时自动补齐
                LogicSlotAssignment found = workingInstance.slotAssignments[i];
                if (slot != null
                    && slot.valueType == LogicSlotValueType.Float
                    && slot.defaultFloatValue != 0f
                    && found.value != null
                    && Mathf.Approximately(found.value.floatValue, 0f))
                {
                    found.value.floatValue = slot.defaultFloatValue;
                }
                return found;
            }
        }

        LogicSlotValue initialValue = BuildContextDefaultValue(slot);

        LogicSlotAssignment created = new LogicSlotAssignment
        {
            slotId = slotId,
            value = initialValue
        };

        workingInstance.slotAssignments.Add(created);
        return created;
    }

    private bool CanConfirm()
    {
        if (workingInstance == null || selectedIndex < 0 || selectedIndex >= filteredTemplates.Count)
            return false;

        LogicSentenceTemplate template = filteredTemplates[selectedIndex];

        for (int i = 0; i < template.slots.Count; i++)
        {
            var slot = template.slots[i];
            if (!slot.required)
                continue;

            LogicSlotAssignment assignment = GetOrCreateAssignment(slot.slotId, slot);
            if (!IsAssignmentValid(slot, assignment))
                return false;
        }

        return true;
    }

    private bool IsAssignmentValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotAssignment assignment)
    {
        if (IsContextFixedUnitSlot(slot))
            return true;

        if (slot == null || assignment == null || assignment.value == null)
            return false;

        if (assignment.value.valueType != slot.valueType)
            return false;

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slot.valueType);
        if (handler == null)
            return false;

        return handler.IsValid(slot, assignment.value);
    }

    private string GetAssignmentDisplayText(LogicSlotAssignment assignment, LogicSentenceTemplate.SlotDefinition slot)
    {
        string fixedText = LogicSentenceContextUtility.GetFixedSlotDisplayText(editorContext, slot);
        if (!string.IsNullOrEmpty(fixedText))
            return fixedText;

        if (assignment == null || assignment.value == null)
            return "<未指定>";

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(assignment.value.valueType);
        if (handler == null)
            return "<未注册处理器>";

        return handler.GetDisplayText(assignment.value);
    }

    private bool IsContextFixedUnitSlot(LogicSentenceTemplate.SlotDefinition slot)
    {
        // AI 里只锁“执行者 / 主语”。
        // 目标槽（target / unitB / source）默认可以是“当前目标”，但必须允许用户改成
        // 最后看见单位、最后攻击来源等 AI 上下文对象。
        return LogicSentenceContextUtility.IsFixedSelfUnitSlot(editorContext, slot);
    }

    private LogicSlotValue BuildContextDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        if (LogicSentenceContextUtility.IsFixedSelfUnitSlot(editorContext, slot))
            return LogicSentenceContextUtility.BuildFixedSelfUnitValue();

        if (LogicSentenceContextUtility.IsCurrentTargetUnitSlot(editorContext, slot))
            return LogicSentenceContextUtility.BuildCurrentTargetUnitValue();

        // 触发器上下文：单位槽默认用场景引用（可选具体对象），不用 AI 的上下文引用
        LogicValueSourceType defaultSource;
        if (editorContext == LogicTemplateContext.Trigger
            && slot != null
            && slot.valueType == LogicSlotValueType.Unit)
        {
            defaultSource = LogicValueSourceType.SceneReference;
        }
        else
        {
            defaultSource = slot != null && slot.allowedSources != null && slot.allowedSources.Length > 0
                ? slot.allowedSources[0]
                : LogicValueSourceType.Constant;
        }

        return new LogicSlotValue
        {
            valueType = slot != null ? slot.valueType : LogicSlotValueType.Float,
            sourceType = defaultSource,
            floatValue = slot != null ? slot.defaultFloatValue : 0f
        };
    }

    public LogicSlotValue CloneValue(LogicSlotValue src)
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
            sceneObjectName = src.sceneObjectName
        };
    }

    private static LogicSentenceInstance CloneSentenceInstanceStatic(LogicSentenceInstance src)
    {
        if (src == null)
            return null;

        LogicSentenceInstance cloned = new LogicSentenceInstance
        {
            templateId = src.templateId,
            enabled = src.enabled
        };

        for (int i = 0; i < src.slotAssignments.Count; i++)
        {
            LogicSlotAssignment s = src.slotAssignments[i];
            if (s == null)
                continue;

            cloned.slotAssignments.Add(new LogicSlotAssignment
            {
                slotId = s.slotId,
                value = CloneValueStatic(s.value)
            });
        }

        return cloned;
    }

    private static LogicSlotValue CloneValueStatic(LogicSlotValue src)
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
            sceneObjectName = src.sceneObjectName
        };
    }

    private GUIStyle GetPreviewStyle()
    {
        return new GUIStyle(EditorStyles.label)
        {
            fontSize = 13,
            richText = false
        };
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
