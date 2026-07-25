using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class StatusAttributeModifierPickerWindow : EditorWindow
{
    private static Action<CoreAttributeDefinition> onPicked;
    private static List<CoreAttributeDefinition> sourceDefinitions;
    private static HashSet<string> usedKeys;

    private Vector2 scroll;
    private string search = "";
    private int filterIndex = 0;

    private static readonly string[] FilterLabels = { "全部", "标准", "自定义" };

    public static void Open(List<CoreAttributeDefinition> definitions, Action<CoreAttributeDefinition> onSelect, HashSet<string> existingKeys)
    {
        sourceDefinitions = definitions ?? new List<CoreAttributeDefinition>();
        onPicked = onSelect;
        usedKeys = existingKeys ?? new HashSet<string>();

        StatusAttributeModifierPickerWindow window = CreateInstance<StatusAttributeModifierPickerWindow>();
        window.titleContent = new GUIContent("选择核心属性");
        window.minSize = new Vector2(520f, 620f);
        window.position = new Rect(Screen.currentResolution.width * 0.5f - 260f, Screen.currentResolution.height * 0.5f - 310f, 520f, 620f);
        window.ShowModalUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("核心属性选择", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            filterIndex = EditorGUILayout.Popup("筛选", filterIndex, FilterLabels);
            search = EditorGUILayout.TextField("搜索", search ?? "");
        }

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            List<CoreAttributeDefinition> items = GetFilteredDefinitions();
            if (items.Count == 0)
            {
                EditorGUILayout.LabelField("没有可选的核心属性。", EditorStyles.miniLabel);
            }
            else
            {
                foreach (CoreAttributeDefinition def in items)
                    DrawDefinitionRow(def);
            }

            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", GUILayout.Width(80f), GUILayout.Height(24f)))
                Close();
        }
    }

    private List<CoreAttributeDefinition> GetFilteredDefinitions()
    {
        IEnumerable<CoreAttributeDefinition> query = sourceDefinitions ?? Enumerable.Empty<CoreAttributeDefinition>();

        if (filterIndex == 1)
            query = query.Where(x => x != null && x.isStandard);
        else if (filterIndex == 2)
            query = query.Where(x => x != null && !x.isStandard);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x != null &&
                (
                    (!string.IsNullOrWhiteSpace(x.displayName) && x.displayName.ToLowerInvariant().Contains(q)) ||
                    (!string.IsNullOrWhiteSpace(x.key) && x.key.ToLowerInvariant().Contains(q))
                ));
        }

        return query.ToList();
    }

    private void DrawDefinitionRow(CoreAttributeDefinition def)
    {
        if (def == null)
            return;

        bool disabled = usedKeys != null && usedKeys.Contains(def.key);

        using (new EditorGUI.DisabledScope(disabled))
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(def.displayName) ? def.key : def.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(def.key, EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{def.valueType} / {(def.isStandard ? "标准" : "自定义")}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (disabled)
                {
                    GUILayout.Label("已添加", EditorStyles.miniLabel, GUILayout.Width(60f));
                }
                else if (GUILayout.Button("选择", GUILayout.Width(80f), GUILayout.Height(22f)))
                {
                    onPicked?.Invoke(def);
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
