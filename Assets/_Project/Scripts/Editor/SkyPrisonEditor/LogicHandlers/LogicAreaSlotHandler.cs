using UnityEditor;
using UnityEngine;

/// <summary>
/// 区域（LogicSlotValueType.Area）槽位——只支持场景引用，值是放置工具建出来的
/// SkyPrisonSceneRegionMarker 的稳定GUID(sceneObjectId)。跟 LogicUnitSlotHandler
/// 比起来先不接 IAIScenePickCapableSlotHandler 那套"点场景选取"的交互，用一个
/// 下拉框列出当前打开场景里所有区域标记选，先把能用的做出来，后续要做成跟单位
/// 一样点选场景再加。
/// </summary>
public class LogicAreaSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.Area;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.sceneObjectId))
            return "<未指定区域>";

        return string.IsNullOrWhiteSpace(value.sceneObjectName)
            ? value.sceneObjectId
            : $"{value.sceneObjectName}（{value.sceneObjectId}）";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null && !string.IsNullOrWhiteSpace(value.sceneObjectId);
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null)
            return;

        value.sourceType = LogicValueSourceType.SceneReference;

        SkyPrisonSceneRegionMarker[] markers = Object.FindObjectsByType<SkyPrisonSceneRegionMarker>(FindObjectsSortMode.None);
        if (markers.Length == 0)
        {
            EditorGUILayout.HelpBox("当前打开的场景里还没有放置任何区域——先用地图放置工具的\"区域\"书签放一个。", MessageType.Warning);
            return;
        }

        string[] labels = new string[markers.Length + 1];
        labels[0] = "<未指定>";
        int selected = 0;
        for (int i = 0; i < markers.Length; i++)
        {
            labels[i + 1] = $"{markers[i].RegionName}（{markers[i].gameObject.name}）";
            if (markers[i].SceneRegionGuid == value.sceneObjectId)
                selected = i + 1;
        }

        int newIndex = EditorGUILayout.Popup("区域", selected, labels);
        if (newIndex == 0)
        {
            value.sceneObjectId = "";
            value.sceneObjectName = "";
        }
        else
        {
            SkyPrisonSceneRegionMarker picked = markers[newIndex - 1];
            value.sceneObjectId = picked.SceneRegionGuid;
            value.sceneObjectName = picked.RegionName;
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.Area,
            sourceType = LogicValueSourceType.SceneReference,
            sceneObjectId = "",
            sceneObjectName = ""
        };
    }
}
