#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按单位配置的友好名字(displayName)搜索选择 UnitDefinition 的弹出面板——
/// Unity 默认的 ObjectField 选择器是按资产文件名(比如"UD_Player_Axia_01")搜索，
/// 单位一多根本记不住文件名，这里改成按策划实际填的名字(比如"Axia")搜索/显示。
/// </summary>
public class UnitPickerPopupContent : PopupWindowContent
{
    private readonly Action<UnitDefinition> _onPicked;
    private readonly List<UnitDefinition> _all = new List<UnitDefinition>();
    private string _search = "";
    private Vector2 _scroll;

    public UnitPickerPopupContent(Action<UnitDefinition> onPicked)
    {
        _onPicked = onPicked;

        foreach (string guid in AssetDatabase.FindAssets("t:UnitDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ud = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);
            if (ud != null) _all.Add(ud);
        }

        _all.Sort((a, b) => string.Compare(GetLabel(a), GetLabel(b), StringComparison.OrdinalIgnoreCase));
    }

    public override Vector2 GetWindowSize() => new Vector2(320f, 360f);

    public override void OnOpen()
    {
        EditorGUI.FocusTextInControl("UnitPickerSearchField");
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        GUI.SetNextControlName("UnitPickerSearchField");
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        GUILayout.Space(2f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (GUILayout.Button("(不选择)", EditorStyles.label))
        {
            _onPicked?.Invoke(null);
            editorWindow.Close();
        }

        foreach (var ud in _all)
        {
            if (ud == null) continue;
            string label = GetLabel(ud);
            if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (GUILayout.Button(label, EditorStyles.label))
            {
                _onPicked?.Invoke(ud);
                editorWindow.Close();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    public static string GetLabel(UnitDefinition ud)
    {
        if (ud == null) return "";
        if (!string.IsNullOrWhiteSpace(ud.displayName)) return ud.displayName;
        return ud.name;
    }
}
#endif
