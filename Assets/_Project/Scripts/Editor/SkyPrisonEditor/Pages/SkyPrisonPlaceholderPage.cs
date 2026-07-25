using UnityEngine;
using UnityEditor;

public class SkyPrisonPlaceholderPage : SkyPrisonEditorPageBase
{
    private readonly string tabName;
    private readonly string message;

    public SkyPrisonPlaceholderPage(SkyPrisonEditorContext context, string tabName, string message)
        : base(context)
    {
        this.tabName = tabName;
        this.message = message;
    }

    public override string TabName => tabName;

    public override void OnGUILeft()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(tabName, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("该分页当前为预留接口。", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    public override void OnGUIRight()
    {
        EditorGUILayout.HelpBox(message, MessageType.Info);
    }
}


