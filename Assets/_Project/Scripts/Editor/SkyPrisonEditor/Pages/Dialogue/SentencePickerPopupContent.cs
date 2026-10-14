#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按"编号 | 说话人：文本"搜索选择句子库里的一句台词——句子一多，纯下拉菜单要
/// 一个个翻着找会很累，这个带搜索框，输编号/说话人/文本任何一部分关键字都能过滤。
/// 开场白、跳转句子这些"挑一句台词"的地方都用这一个弹窗。
/// </summary>
public class SentencePickerPopupContent : PopupWindowContent
{
    private readonly Action<string> _onPicked;
    private readonly List<DialogueSentenceLibrary.SentenceEntry> _entries;
    private readonly Func<DialogueSentenceLibrary.SentenceEntry, string> _labelBuilder;
    private string _search = "";
    private Vector2 _scroll;

    public SentencePickerPopupContent(
        List<DialogueSentenceLibrary.SentenceEntry> entries,
        Func<DialogueSentenceLibrary.SentenceEntry, string> labelBuilder,
        Action<string> onPicked)
    {
        _entries = entries;
        _labelBuilder = labelBuilder;
        _onPicked = onPicked;
    }

    public override Vector2 GetWindowSize() => new Vector2(420f, 380f);

    public override void OnOpen()
    {
        EditorGUI.FocusTextInControl("SentencePickerSearchField");
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        GUI.SetNextControlName("SentencePickerSearchField");
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        GUILayout.Space(2f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (var entry in _entries)
        {
            if (entry == null) continue;
            string label = _labelBuilder(entry);
            if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            GUIStyle style = new GUIStyle(EditorStyles.label) { wordWrap = true };
            if (GUILayout.Button(label, style))
            {
                _onPicked?.Invoke(entry.sentenceId);
                editorWindow.Close();
            }
        }

        EditorGUILayout.EndScrollView();
    }
}
#endif
