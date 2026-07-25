using System;
using UnityEngine;

/// <summary>
/// 系统 UI SE 音效表。挂在 Resources/Data/Audio/SkyPrisonSystemSETable.asset。
/// 每种 SkyPrisonSystemSEType 对应一条记录；可配置多个候选 clip（随机选一个）
/// 以及独立的音量 / 音高微调。
/// </summary>
[CreateAssetMenu(
    fileName = "SkyPrisonSystemSETable",
    menuName  = "Sky Prison/Audio/系统SE音效表",
    order     = 1310)]
public class SkyPrisonSystemSETable : ScriptableObject
{
    public const string ResourcesPath = "Data/Audio/SkyPrisonSystemSETable";

    [Serializable]
    public class Entry
    {
        public SkyPrisonSystemSEType type;

        [Tooltip("同一类型可放多个 clip，播放时随机取一个（增加自然感）")]
        public AudioClip[] clips;

        [Range(0f, 2f)]
        public float volume = 1f;

        [Range(0.8f, 1.2f), Tooltip("基础音高；Switch 类型建议配 0.95~1.05 随机范围")]
        public float pitch = 1f;

        [Range(0f, 0.15f), Tooltip("随机音高偏移幅度（±此值），0 = 固定音高")]
        public float pitchVariance = 0f;
    }

    public Entry[] entries = Array.Empty<Entry>();

    private Entry[] _indexed;

    /// <summary>按 type 快速查找（运行时首次访问时建索引）。</summary>
    public bool TryGet(SkyPrisonSystemSEType type, out Entry entry)
    {
        if (_indexed == null) BuildIndex();
        int i = (int)type;
        if (i >= 0 && i < _indexed.Length && _indexed[i] != null)
        {
            entry = _indexed[i];
            return entry.clips != null && entry.clips.Length > 0;
        }
        entry = null;
        return false;
    }

    private void BuildIndex()
    {
        int max = 0;
        foreach (var e in entries) max = Mathf.Max(max, (int)e.type);
        _indexed = new Entry[max + 1];
        foreach (var e in entries) _indexed[(int)e.type] = e;
    }

    private void OnValidate() => _indexed = null; // 编辑器修改后重建
}
