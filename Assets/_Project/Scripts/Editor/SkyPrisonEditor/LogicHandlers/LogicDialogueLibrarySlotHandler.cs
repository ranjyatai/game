using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "台词"槽位——一个槽同时装句子库(assetReference)+句子编号(stringValue)，不是两个
/// 独立槽位。这样句子编号能直接做成依赖句子库的下拉菜单(选完库才知道有哪些句子可
/// 选)，不用手打编号，也不需要给槽位系统加"读取兄弟槽位"这种跨槽能力。
/// </summary>
public class LogicDialogueLibrarySlotHandler : ILogicSlotHandler
{
    public LogicSlotValueType ValueType => LogicSlotValueType.DialogueLibrary;

    public string GetDisplayText(LogicSlotValue value)
    {
        if (value == null) return "<未指定台词>";

        DialogueSentenceLibrary lib = value.sourceType == LogicValueSourceType.AssetReference
            ? value.assetReference as DialogueSentenceLibrary
            : null;
        if (lib == null) return "<未指定台词>";

        return string.IsNullOrEmpty(value.stringValue)
            ? lib.name
            : $"{lib.name} · {value.stringValue}";
    }

    public bool IsValid(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        return value != null
            && value.sourceType == LogicValueSourceType.AssetReference
            && value.assetReference is DialogueSentenceLibrary
            && !string.IsNullOrEmpty(value.stringValue);
    }

    // 弹窗(AISlotValuePickerWindow)是固定 520x300，装不下太多行——这里刻意压得很紧凑
    // (库名+选择/清空挤一行、预览用单行小字而不是自动换行的HelpBox)，不然内容会把
    // 弹窗自带的"预览"区块挤出可视范围外。
    public void DrawEditor(LogicSentenceTemplate.SlotDefinition slot, LogicSlotValue value)
    {
        if (value == null) return;

        DialogueSentenceLibrary current = value.assetReference as DialogueSentenceLibrary;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(current != null ? current.name : "（未选择句子库）", EditorStyles.boldLabel);

        if (GUILayout.Button("选择…", GUILayout.Width(60f)))
        {
            SkyPrisonItemPickerPopup.Open(
                current,
                picked =>
                {
                    if (picked is DialogueSentenceLibrary lib)
                    {
                        value.assetReference = lib;
                        value.sourceType = LogicValueSourceType.AssetReference;
                        value.stringValue = ""; // 换库清空句子选择，避免张冠李戴引用错句子
                    }
                },
                nameof(DialogueSentenceLibrary));
        }

        if (current != null && GUILayout.Button("×", GUILayout.Width(24f)))
        {
            value.assetReference = null;
            value.stringValue = "";
        }
        EditorGUILayout.EndHorizontal();

        if (current == null) return;

        List<DialogueSentenceLibrary.SentenceEntry> entries = current.entries;
        string[] ids = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            ids[i] = string.IsNullOrEmpty(entries[i].sentenceId) ? "(未命名)" : entries[i].sentenceId;

        int currentIdx = entries.FindIndex(e => e != null && e.sentenceId == value.stringValue);
        int newIdx = EditorGUILayout.Popup("句子", currentIdx, ids);
        if (newIdx >= 0 && newIdx != currentIdx)
            value.stringValue = entries[newIdx].sentenceId;

        DialogueSentenceLibrary.SentenceEntry selected =
            entries.Find(e => e != null && e.sentenceId == value.stringValue);

        if (selected != null)
        {
            string speaker = ResolveSpeakerPreview(selected);
            string text = ResolveDefaultText(selected);
            string preview = string.IsNullOrEmpty(speaker) ? text : $"{speaker}：{text}";
            if (string.IsNullOrEmpty(preview)) preview = "（这句还没填内容）";

            GUIStyle miniStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };
            EditorGUILayout.LabelField(preview, miniStyle);
        }
    }

    public LogicSlotValue CreateDefaultValue(LogicSentenceTemplate.SlotDefinition slot)
    {
        return new LogicSlotValue
        {
            valueType = LogicSlotValueType.DialogueLibrary,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = null,
            stringValue = ""
        };
    }

    // ── 预览（跟台本编辑器折叠行的预览同一个意图，方便核对选没选对）──────────────

    private static string GetDefaultLanguageCode()
    {
        var settings = LocalizationSettingsUtility.GetOrCreateSettings();
        var def = settings != null ? settings.GetDefaultLanguage() : null;
        return def != null ? def.languageCode : null;
    }

    private static string ResolveDefaultText(DialogueSentenceLibrary.SentenceEntry entry)
    {
        return FindText(entry.texts, GetDefaultLanguageCode());
    }

    private static string ResolveSpeakerPreview(DialogueSentenceLibrary.SentenceEntry entry)
    {
        if (entry.speakerAnonymous) return "???";

        string code = GetDefaultLanguageCode();

        string alias = FindText(entry.speakerAliasOverride, code);
        if (!string.IsNullOrEmpty(alias)) return alias;

        if (entry.speakerUnit == null) return null;

        string localized = FindText(entry.speakerUnit.localizedNames, code);
        return !string.IsNullOrEmpty(localized) ? localized : entry.speakerUnit.displayName;
    }

    private static string FindText(List<LocalizedTextEntry> texts, string languageCode)
    {
        if (texts == null || string.IsNullOrEmpty(languageCode)) return null;
        foreach (var t in texts)
            if (t != null && t.languageCode == languageCode) return t.text;
        return null;
    }
}
