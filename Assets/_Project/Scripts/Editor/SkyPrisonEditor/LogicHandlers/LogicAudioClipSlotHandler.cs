using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class LogicAudioClipSlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.AudioClip;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value?.assetReference is AudioClip clip)
            return clip.name;
        return "<未指定音频>";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is AudioClip;
    }

    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        AudioClip current = value.assetReference as AudioClip;

        // ObjectField 支持拖拽赋值，但 Modal 窗口里圆点拾取器是空的，用"选择…"按钮代替
        EditorGUILayout.BeginHorizontal();
        AudioClip dragged = (AudioClip)EditorGUILayout.ObjectField("音频片段", current, typeof(AudioClip), false);
        if (dragged != current)
        {
            value.assetReference = dragged;
            value.sourceType = LogicValueSourceType.AssetReference;
        }
        if (GUILayout.Button("选择…", GUILayout.Width(52f)))
            AudioClipPickerPopup.Open(current, picked =>
            {
                value.assetReference = picked;
                value.sourceType = LogicValueSourceType.AssetReference;
            });
        EditorGUILayout.EndHorizontal();
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.AudioClip,
            sourceType = LogicValueSourceType.AssetReference
        };
    }
}

/// <summary>
/// 弹出式 AudioClip 资产选择器，列出项目内所有 AudioClip。
/// </summary>
public class AudioClipPickerPopup : EditorWindow
{
    private System.Action<AudioClip> onPicked;
    private List<AudioClip> clips = new();
    private string search = "";
    private Vector2 scroll;

    public static void Open(AudioClip current, System.Action<AudioClip> callback)
    {
        var win = CreateInstance<AudioClipPickerPopup>();
        win.onPicked = callback;
        win.titleContent = new GUIContent("选择音频");
        win.LoadClips();
        win.ShowUtility();
        win.minSize = new Vector2(320f, 420f);
    }

    private void LoadClips()
    {
        clips.Clear();
        string[] guids = AssetDatabase.FindAssets("t:AudioClip");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (c != null) clips.Add(c);
        }
        clips.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4f);
        string newSearch = EditorGUILayout.TextField("搜索", search);
        if (newSearch != search) { search = newSearch; }

        EditorGUILayout.Space(4f);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (AudioClip clip in clips)
        {
            if (!string.IsNullOrEmpty(search) &&
                clip.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (GUILayout.Button(clip.name, EditorStyles.label, GUILayout.Height(22f)))
            {
                onPicked?.Invoke(clip);
                Close();
            }
            Rect r = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1f), new Color(1, 1, 1, 0.05f));
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("取消")) Close();
    }
}
