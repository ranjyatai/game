#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 按窗口的友好显示名(SkyPrisonUIPrefabMetadata_V1.displayName)搜索选择窗口Prefab，
/// 不是Unity默认那种按资产文件名(比如"PF_Shop")选的选择器——交互包配"打开窗口"选项
/// 时，策划应该看得到"商店"而不是要去记"PF_xxx"对应哪个窗口。
/// </summary>
public class WindowPrefabPickerPopupContent : PopupWindowContent
{
    private readonly Action<GameObject> _onPicked;
    private readonly List<GameObject> _all = new List<GameObject>();
    private string _search = "";
    private Vector2 _scroll;

    public WindowPrefabPickerPopupContent(Action<GameObject> onPicked)
    {
        _onPicked = onPicked;

        // 每个窗口prefab在项目里实际存了两份(Assets/Resources/UI/Window 是运行时加载
        // 用的镜像，Assets/_Project/Prefabs/UI/Window 是编辑源头)——按uiId去重，
        // 不然选择器里每个窗口都会出现两次一模一样的名字。
        var seenUiIds = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            var meta = go.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
            if (meta == null) continue;

            string dedupeKey = !string.IsNullOrWhiteSpace(meta.uiId) ? meta.uiId : go.name;
            if (!seenUiIds.Add(dedupeKey)) continue;

            _all.Add(go);
        }

        _all.Sort((a, b) => string.Compare(GetLabel(a), GetLabel(b), StringComparison.OrdinalIgnoreCase));
    }

    public override Vector2 GetWindowSize() => new Vector2(320f, 360f);

    public override void OnOpen()
    {
        EditorGUI.FocusTextInControl("WindowPrefabPickerSearchField");
    }

    public override void OnGUI(Rect rect)
    {
        GUILayout.Space(4f);
        GUI.SetNextControlName("WindowPrefabPickerSearchField");
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
        GUILayout.Space(2f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (GUILayout.Button("(不选择)", EditorStyles.label))
        {
            _onPicked?.Invoke(null);
            editorWindow.Close();
        }

        foreach (var go in _all)
        {
            if (go == null) continue;
            string label = GetLabel(go);
            if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (GUILayout.Button(label, EditorStyles.label))
            {
                _onPicked?.Invoke(go);
                editorWindow.Close();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    public static string GetLabel(GameObject go)
    {
        if (go == null) return "";
        var meta = go.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
        if (meta != null && !string.IsNullOrWhiteSpace(meta.displayName)) return meta.displayName;
        return go.name;
    }
}
#endif
