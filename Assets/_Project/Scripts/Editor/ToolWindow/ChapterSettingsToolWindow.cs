using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目全局章节列表的独立设置窗口——章节"有几个、叫什么名字"只在这里定，
/// 台本编辑器(SkyPrisonDialogueScriptPage)只负责从这份列表里选，不重复维护。
/// 每个章节的显示名字可以随时改，内部ID固定不变，改名不会弄丢已有引用。
/// </summary>
public class ChapterSettingsToolWindow : EditorWindow
{
    private DialogueChapterSettings _settings;
    private Vector2 _scroll;
    private int _quickGenerateCount = 6;

    [MenuItem("天空囚笼/章节设置", false, 3)]
    public static void Open()
    {
        var window = GetWindow<ChapterSettingsToolWindow>(true, "章节设置", true);
        window.minSize = new Vector2(360f, 300f);
        window.Show();
    }

    private void OnEnable()
    {
        _settings = DialogueChapterSettingsUtility.GetOrCreateSettings();
    }

    private void OnGUI()
    {
        if (_settings == null)
            _settings = DialogueChapterSettingsUtility.GetOrCreateSettings();

        EditorGUILayout.HelpBox("这里定的章节列表是全项目共用的——台本编辑器给句子选章节、给台本包分组，都读这一份。" +
            "改名字很安全，内部有固定ID兜底，不会弄丢已有引用。", MessageType.None);

        GUILayout.Space(6f);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        int removeIndex = -1;
        bool dirty = false;

        for (int i = 0; i < _settings.entries.Count; i++)
        {
            var entry = _settings.entries[i];
            if (entry == null) continue;

            EditorGUILayout.BeginHorizontal();
            string newValue = EditorGUILayout.TextField($"第{i + 1}项", entry.displayName);
            if (newValue != entry.displayName)
            {
                entry.displayName = newValue;
                dirty = true;
            }
            if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(20f)))
                removeIndex = i;
            EditorGUILayout.EndHorizontal();
        }

        if (removeIndex >= 0)
        {
            _settings.entries.RemoveAt(removeIndex);
            dirty = true;
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(4f);

        if (GUILayout.Button("＋ 添加章节"))
        {
            _settings.entries.Add(new DialogueChapterSettings.ChapterEntry
            {
                id = DialogueChapterSettings.NewId(),
                displayName = ""
            });
            dirty = true;
        }

        GUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        _quickGenerateCount = EditorGUILayout.IntField("阶段数量", _quickGenerateCount);
        _quickGenerateCount = Mathf.Max(1, _quickGenerateCount);
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button($"生成 1~{_quickGenerateCount} 阶段编号（覆盖当前列表）"))
        {
            _settings.entries.Clear();
            for (int i = 1; i <= _quickGenerateCount; i++)
            {
                _settings.entries.Add(new DialogueChapterSettings.ChapterEntry
                {
                    id = DialogueChapterSettings.NewId(),
                    displayName = i.ToString()
                });
            }
            dirty = true;
        }

        if (dirty)
        {
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
        }
    }
}
