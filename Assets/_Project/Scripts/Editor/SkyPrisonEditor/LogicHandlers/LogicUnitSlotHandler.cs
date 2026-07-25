using UnityEditor;
using UnityEngine;

public class LogicUnitSlotHandler : ILogicSlotHandler, IAIScenePickCapableSlotHandler
{
    private static readonly string[] ContextUnitOptions =
    {
        "当前目标",
        "自身"
    };

    public LogicSlotValueType ValueType => LogicSlotValueType.Unit;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null)
            return "<未指定单位>";

        return value.sourceType switch
        {
            LogicValueSourceType.ContextReference =>
                string.IsNullOrWhiteSpace(value.contextKey) ? "<未指定单位>" : value.contextKey,

            LogicValueSourceType.SceneReference =>
                FormatSceneUnitDisplay(value),

            LogicValueSourceType.Variable =>
                string.IsNullOrWhiteSpace(value.variableKey) ? "<未指定变量单位>" : value.variableKey,

            LogicValueSourceType.AssetReference =>
                value.assetReference != null ? value.assetReference.name : "<未指定资源单位>",

            _ => "<未指定单位>"
        };
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null)
            return false;

        return value.sourceType switch
        {
            LogicValueSourceType.ContextReference => !string.IsNullOrWhiteSpace(value.contextKey),
            LogicValueSourceType.SceneReference => !string.IsNullOrWhiteSpace(value.sceneObjectId),
            LogicValueSourceType.Variable => !string.IsNullOrWhiteSpace(value.variableKey),
            LogicValueSourceType.AssetReference => value.assetReference != null,
            _ => false
        };
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null)
            return;

        switch (value.sourceType)
        {
            case LogicValueSourceType.ContextReference:
                DrawContextUnitEditor(value);
                break;

            case LogicValueSourceType.SceneReference:
                DrawSceneUnitEditor(value);
                break;

            case LogicValueSourceType.Variable:
                value.variableKey = EditorGUILayout.TextField("变量Key", value.variableKey);
                break;

            case LogicValueSourceType.AssetReference:
                value.assetReference = EditorGUILayout.ObjectField("资源", value.assetReference, typeof(UnityEngine.Object), false);
                break;

            default:
                EditorGUILayout.HelpBox("单位槽位建议使用上下文引用或场景选择。", MessageType.Info);
                break;
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        LogicValueSourceType defaultSource =
            slot != null && slot.allowedSources != null && slot.allowedSources.Length > 0
                ? slot.allowedSources[0]
                : LogicValueSourceType.ContextReference;

        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Unit,
            sourceType = defaultSource,
            contextKey = defaultSource == LogicValueSourceType.ContextReference ? "当前目标" : "",
            sceneObjectId = "",
            sceneObjectName = ""
        };
    }

    public bool SupportsScenePick(AIScenePickKind kind)
    {
        return kind == AIScenePickKind.Unit;
    }

    public AIScenePickRequest BuildScenePickRequest(
        string slotId,
        LogicSentenceTemplate.SlotDefinition slotDef,
        LogicSlotValue currentValue,
        AIScenePickRestoreState restoreState)
    {
        if (slotDef == null)
            return null;

        return new AIScenePickRequest
        {
            requestId = System.Guid.NewGuid().ToString("N"),
            pickKind = AIScenePickKind.Unit,

            sourceWindowId = "AI.LogicSentenceTemplatePicker",
            slotId = slotId,
            slotDisplayName = slotDef.displayName,

            title = $"正在选择单位：{slotDef.displayName}",
            hint = "请在 Scene 中点击一个单位。按 Esc 取消。",

            restoreState = restoreState,
            constraintsJson = ""
        };
    }

    public void ApplyScenePickResult(LogicSlotValue targetValue, AIScenePickResult result)
    {
        if (targetValue == null || result == null)
            return;

        targetValue.valueType = LogicSlotValueType.Unit;
        targetValue.sourceType = LogicValueSourceType.SceneReference;
        targetValue.sceneObjectId = result.objectId ?? "";
        targetValue.sceneObjectName = result.objectName ?? "";
        targetValue.contextKey = "";
    }

    private void DrawContextUnitEditor(LogicSlotValue value)
    {
        int selected = 0;
        for (int i = 0; i < ContextUnitOptions.Length; i++)
        {
            if (ContextUnitOptions[i] == value.contextKey)
            {
                selected = i;
                break;
            }
        }

        int newIndex = EditorGUILayout.Popup("目标单位", selected, ContextUnitOptions);
        value.contextKey = ContextUnitOptions[newIndex];
    }

    private void DrawSceneUnitEditor(LogicSlotValue value)
    {
        EditorGUILayout.HelpBox("点击下面按钮后，会进入 Scene 选择模式。", MessageType.Info);

        string displayText = FormatSceneUnitDisplay(value);

        EditorGUILayout.LabelField(
            "单位显示",
            string.IsNullOrWhiteSpace(value.sceneObjectId) ? "<未选择>" : displayText
        );

        EditorGUILayout.LabelField(
            "场景ID",
            string.IsNullOrWhiteSpace(value.sceneObjectId) ? "<未选择>" : value.sceneObjectId
        );

        if (GUILayout.Button("从当前地图选择…", GUILayout.Height(24f)))
        {
            string slotId = AISlotValuePickerWindow.ActiveInstance != null
                ? AISlotValuePickerWindow.ActiveInstance.EditingSlotId
                : "";

            if (string.IsNullOrWhiteSpace(slotId))
            {
                EditorUtility.DisplayDialog("无法开始场景选择", "当前没有可回填的槽位 ID。", "确定");
                return;
            }

            AILogicSentenceTemplatePickerWindow.BeginScenePickForSlot(slotId);
        }

        if (GUILayout.Button("清空", GUILayout.Width(60f), GUILayout.Height(24f)))
        {
            value.sceneObjectId = "";
            value.sceneObjectName = "";
            value.contextKey = "";
        }
    }

    private string FormatSceneUnitDisplay(LogicSlotValue value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.sceneObjectId))
            return "<未指定场景单位>";

        string id = value.sceneObjectId;
        string name = value.sceneObjectName;

        if (string.IsNullOrWhiteSpace(name))
            return id;

        return $"{name}（{id}）";
    }
}
