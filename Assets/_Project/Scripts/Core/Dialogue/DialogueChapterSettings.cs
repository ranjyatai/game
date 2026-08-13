using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 项目全局的章节列表——只有这一份，跟 LocalizationProjectSettings(语言列表)同一套
/// "项目级单例配置"模式。台本编辑器里给句子选章节、给台本包分组左栏分层，
/// 全部读这一份，不是每个句子库各自维护自己的章节列表。
/// 编辑入口在 Tools/Sky Prison/章节设置(ChapterSettingsToolWindow)。
///
/// 每个章节有一个改名不变的固定ID——句子/台本包存的是这个ID，不是显示名字本身，
/// 所以改章节名字不会弄丢已有的引用(之前用纯字符串当引用时，改名会让所有引用
/// 变成孤儿数据，这是改成ID的原因)。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/对话/章节设置", fileName = "DialogueChapterSettings")]
public class DialogueChapterSettings : ScriptableObject, ISerializationCallbackReceiver
{
    [Serializable]
    public class ChapterEntry
    {
        [Tooltip("固定不变的内部ID——新建时自动生成，改名不会变。")]
        public string id = "";
        public string displayName = "";
    }

    public List<ChapterEntry> entries = new List<ChapterEntry>();

    // 旧版本(改成固定ID方案之前)存的是纯字符串列表——留着这个字段只是为了让旧数据
    // 能被反序列化进来，OnAfterDeserialize 里一次性迁移成带ID的 entries 后清空，
    // 不会在 Inspector 里出现两份看起来重复的列表。
    [SerializeField] private List<string> chapters = new List<string>();

    public void OnAfterDeserialize()
    {
        if (entries != null && entries.Count > 0) return; // 已经迁移过(或本来就有数据)，不重复迁移
        if (chapters == null || chapters.Count == 0) return;

        foreach (string name in chapters)
            entries.Add(new ChapterEntry { id = NewId(), displayName = name });
        chapters.Clear();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssets();
            }
        };
#endif
    }

    public void OnBeforeSerialize() { }

    /// <summary>按ID找显示名字。找不到匹配(比如引用了已被删掉的章节，或者残留的
    /// 改ID方案之前的旧脏数据)明确提示"未知章节"，不把内部ID原样甩出来糊弄过去。</summary>
    public string GetDisplayName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        foreach (var e in entries)
            if (e != null && e.id == id) return e.displayName;
        return "(未知章节)";
    }

    public int IndexOfId(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i] != null && entries[i].id == id) return i;
        return -1;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");
}
