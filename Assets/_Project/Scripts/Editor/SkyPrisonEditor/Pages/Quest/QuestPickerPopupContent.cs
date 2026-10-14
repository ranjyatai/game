#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按任务标题(玩家会看到的文字)搜索选择 QuestDefinition 的弹出面板——跟
/// UnitPickerPopupContent 同一套做法。任务一多，按内部questId(比如"quest_003")
/// 根本认不出是哪个任务，改成按标题搜索/显示，没填标题的才退回questId兜底。
/// </summary>
public class QuestPickerPopupContent : PopupWindowContent
{
    private readonly Action<QuestDefinition> _onPicked;
    private readonly List<QuestDefinition> _all = new List<QuestDefinition>();
    private readonly LocalizationProjectSettings _settings;
    private string _search = "";
    private Vector2 _scroll;

    public QuestPickerPopupContent(Action<QuestDefinition> onPicked)
    {
        _onPicked = onPicked;
        _settings = LocalizationSettingsUtility.GetOrCreateSettings();

        foreach (string guid in AssetDatabase.FindAssets("t:QuestDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
            if (quest != null) _all.Add(quest);
        }

        _all.Sort((a, b) => string.Compare(GetLabel(a, _settings), GetLabel(b, _settings), StringComparison.OrdinalIgnoreCase));
    }

    public override Vector2 GetWindowSize() => new Vector2(320f, 360f);

    public override void OnOpen()
    {
        EditorGUI.FocusTextInControl("QuestPickerSearchField");
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        GUI.SetNextControlName("QuestPickerSearchField");
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        GUILayout.Space(2f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (GUILayout.Button("(不选择)", EditorStyles.label))
        {
            _onPicked?.Invoke(null);
            editorWindow.Close();
        }

        foreach (QuestDefinition quest in _all)
        {
            if (quest == null) continue;
            string label = GetLabel(quest, _settings);
            if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (GUILayout.Button(label, EditorStyles.label))
            {
                _onPicked?.Invoke(quest);
                editorWindow.Close();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    public static string GetLabel(QuestDefinition quest, LocalizationProjectSettings settings)
    {
        if (quest == null) return "";

        string title = GetPreferredLocalizedText(quest.title, settings);
        string label = string.IsNullOrWhiteSpace(title) ? quest.name : title;
        return string.IsNullOrWhiteSpace(quest.questId) ? label : $"{label}  ({quest.questId})";
    }

    private static string GetPreferredLocalizedText(List<LocalizedTextEntry> texts, LocalizationProjectSettings settings)
    {
        if (texts == null) return "";

        if (settings != null)
        {
            foreach (LocalizationProjectSettings.LanguageEntry lang in settings.languages)
            {
                if (lang == null || !lang.isDefault) continue;
                foreach (LocalizedTextEntry t in texts)
                    if (t.languageCode == lang.languageCode && !string.IsNullOrWhiteSpace(t.text))
                        return t.text;
            }
        }

        foreach (LocalizedTextEntry t in texts)
            if (!string.IsNullOrWhiteSpace(t.text))
                return t.text;

        return "";
    }
}
#endif
