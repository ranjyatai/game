using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>DoorLocalizedLabel 的属性面板：按项目里启用的语言逐行填写，全部留空 = 走字典表默认文字。</summary>
[CustomPropertyDrawer(typeof(DoorLocalizedLabel))]
public class DoorLocalizedLabelDrawer : PropertyDrawer
{
    private static List<(string code, string name)> Languages()
    {
        var list = new List<(string, string)>();
        var settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings != null)
            foreach (var l in settings.languages)
                if (l != null && l.enabled && !string.IsNullOrEmpty(l.languageCode))
                    list.Add((l.languageCode, string.IsNullOrEmpty(l.displayName) ? l.languageCode : l.displayName));
        if (list.Count == 0) list.Add(("zh-CN", "中文（简体）"));
        return list;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
        return (Languages().Count + 1) * (EditorGUIUtility.singleLineHeight + 2f) + 2f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var texts = property.FindPropertyRelative("texts");
        float h = EditorGUIUtility.singleLineHeight;
        var langs = Languages();

        bool any = false;
        for (int i = 0; i < texts.arraySize; i++)
            if (!string.IsNullOrEmpty(texts.GetArrayElementAtIndex(i).FindPropertyRelative("text").stringValue)) { any = true; break; }

        Rect head = new Rect(position.x, position.y, position.width, h);
        property.isExpanded = EditorGUI.Foldout(head, property.isExpanded,
            label.text + (any ? "" : "（默认文字）"), true);
        if (!property.isExpanded) return;

        float y = position.y + h + 4f;
        EditorGUI.indentLevel++;
        foreach (var (code, name) in langs)
        {
            int idx = -1;
            for (int i = 0; i < texts.arraySize; i++)
                if (texts.GetArrayElementAtIndex(i).FindPropertyRelative("languageCode").stringValue == code) { idx = i; break; }
            string cur = idx >= 0 ? texts.GetArrayElementAtIndex(idx).FindPropertyRelative("text").stringValue : "";
            EditorGUI.BeginChangeCheck();
            string nv = EditorGUI.TextField(new Rect(position.x, y, position.width, h), name, cur);
            if (EditorGUI.EndChangeCheck())
            {
                if (idx < 0)
                {
                    texts.arraySize++;
                    idx = texts.arraySize - 1;
                    texts.GetArrayElementAtIndex(idx).FindPropertyRelative("languageCode").stringValue = code;
                }
                texts.GetArrayElementAtIndex(idx).FindPropertyRelative("text").stringValue = nv;
            }
            y += h + 2f;
        }
        EditorGUI.indentLevel--;
        EditorGUI.LabelField(new Rect(position.x, y, position.width, h), "全部留空 = 用多语言字典表里的默认文字", EditorStyles.miniLabel);
    }
}
