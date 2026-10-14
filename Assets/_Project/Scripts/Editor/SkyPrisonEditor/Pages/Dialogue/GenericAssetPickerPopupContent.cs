#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按资产自己配的"displayName"字段(反射读取)搜索选择——不认资产文件名(比如
/// "SD_DemoShop")，认策划实际填的名字(比如"供给站")。任意资产类型通用，
/// 不用为每个新的窗口数据类型(商店/设施升级/...)各写一个专属选择弹窗。
/// </summary>
public class GenericAssetPickerPopupContent : PopupWindowContent
{
    private readonly Action<UnityEngine.Object> _onPicked;
    private readonly List<UnityEngine.Object> _all = new List<UnityEngine.Object>();
    private string _search = "";
    private Vector2 _scroll;

    public GenericAssetPickerPopupContent(Type type, Action<UnityEngine.Object> onPicked)
    {
        _onPicked = onPicked;

        foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var obj = AssetDatabase.LoadAssetAtPath(path, type);
            if (obj != null) _all.Add(obj);
        }

        _all.Sort((a, b) => string.Compare(GetLabel(a), GetLabel(b), StringComparison.OrdinalIgnoreCase));
    }

    public override Vector2 GetWindowSize() => new Vector2(320f, 360f);

    public override void OnOpen()
    {
        EditorGUI.FocusTextInControl("GenericAssetPickerSearchField");
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        GUI.SetNextControlName("GenericAssetPickerSearchField");
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        GUILayout.Space(2f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (GUILayout.Button("(不选择)", EditorStyles.label))
        {
            _onPicked?.Invoke(null);
            editorWindow.Close();
        }

        foreach (var obj in _all)
        {
            if (obj == null) continue;
            string label = GetLabel(obj);
            if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (GUILayout.Button(label, EditorStyles.label))
            {
                _onPicked?.Invoke(obj);
                editorWindow.Close();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    public static string GetLabel(UnityEngine.Object obj)
    {
        if (obj == null) return "";

        FieldInfo field = obj.GetType().GetField("displayName", BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(string))
        {
            string value = field.GetValue(obj) as string;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return obj.name;
    }
}
#endif
