#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UILocalizationTable))]
public class UILocalizationTableEditor : Editor
{
    private UILocalizationTable _table;
    private LocalizationProjectSettings _settings;
    private string _searchKey = "";
    private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

    private void OnEnable()
    {
        _table = (UILocalizationTable)target;
        _settings = LocalizationSettingsUtility.GetOrCreateSettings();
    }

    public override void OnInspectorGUI()
    {
        if (_settings == null)
        {
            EditorGUILayout.HelpBox("找不到 LocalizationProjectSettings。", MessageType.Warning);
            return;
        }

        List<LocalizationProjectSettings.LanguageEntry> langs = GetEnabledLanguages();

        // ── 工具栏 ────────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("同步标准 UI Key", GUILayout.Height(24f)))
            SyncStandardKeys(langs);
        if (GUILayout.Button("同步语言列表", GUILayout.Height(24f)))
            SyncLanguagesToAllEntries(langs);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // ── 搜索 ──────────────────────────────────────────────────────
        _searchKey = EditorGUILayout.TextField("搜索 Key", _searchKey);
        GUILayout.Space(4f);

        // ── 条目列表 ──────────────────────────────────────────────────
        serializedObject.Update();

        foreach (var entry in _table.entries)
        {
            if (entry == null) continue;
            if (!string.IsNullOrEmpty(_searchKey) &&
                !entry.key.Contains(_searchKey, System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_foldouts.ContainsKey(entry.key))
                _foldouts[entry.key] = false;

            _foldouts[entry.key] = EditorGUILayout.Foldout(_foldouts[entry.key], entry.key, true);

            if (_foldouts[entry.key])
            {
                EditorGUI.indentLevel++;
                DrawEntryLanguageFields(entry, langs);
                EditorGUI.indentLevel--;
            }
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(_table);
            serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }

    private void DrawEntryLanguageFields(UILocalizationEntry entry,
        List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        // 确保所有当前语言都有对应条目
        foreach (var lang in langs)
        {
            bool found = false;
            foreach (var t in entry.texts)
                if (t.languageCode == lang.languageCode) { found = true; break; }
            if (!found)
                entry.texts.Add(new LocalizedTextEntry { languageCode = lang.languageCode, text = "" });
        }

        // 主语言优先显示
        foreach (var lang in langs)
        {
            string label = lang.isDefault
                ? $"{lang.displayName}（默认）"
                : lang.displayName;

            foreach (var t in entry.texts)
            {
                if (t.languageCode != lang.languageCode) continue;
                t.text = EditorGUILayout.TextField(label, t.text);
                break;
            }
        }
    }

    private void SyncStandardKeys(List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        var codes = new List<string>();
        foreach (var l in langs) codes.Add(l.languageCode);

        foreach (string key in UILocalizationTable.StandardUIKeys)
            _table.EnsureEntry(key, codes);

        EditorUtility.SetDirty(_table);
        AssetDatabase.SaveAssets();
    }

    private void SyncLanguagesToAllEntries(List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        var codes = new List<string>();
        foreach (var l in langs) codes.Add(l.languageCode);

        foreach (var entry in _table.entries)
            if (entry != null) _table.EnsureEntry(entry.key, codes);

        EditorUtility.SetDirty(_table);
        AssetDatabase.SaveAssets();
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetEnabledLanguages()
    {
        var result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (_settings == null) return result;

        // 默认语言排首位
        foreach (var l in _settings.languages)
            if (l != null && l.enabled && l.isDefault) result.Add(l);
        foreach (var l in _settings.languages)
            if (l != null && l.enabled && !l.isDefault) result.Add(l);

        return result;
    }
}
#endif
