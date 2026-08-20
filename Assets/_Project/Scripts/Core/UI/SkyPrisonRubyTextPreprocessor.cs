using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 挂进 TMP_Text.textPreprocessor——TMP 每次要重新排版这段文字时都会先过一遍这个
/// 接口。这里只做"拆标记、留纯净文本"，真正的注音坐标要等 TMP 用这份纯净文本
/// 生成完网格之后才量得出来，那部分在 SkyPrisonRubyOverlayController.LateUpdate
/// 里做（读 ForceMeshUpdate 之后的 characterInfo）。
///
/// 链式包一层——很多文本框（对话框、交互提示这类）可能已经挂了别的预处理器做变量
/// 替换（比如 {playerName} 这种），直接覆盖会让那套逻辑失效。这里先跑原来那个
/// （如果有），拿到结果再拆注音标记，两边都不耽误。注音语法要求花括号里必须有一个
/// "|"，跟纯变量占位符 {xxx}（没有 |）不会互相误判。
/// </summary>
public sealed class SkyPrisonRubyTextPreprocessor : ITextPreprocessor
{
    private readonly ITextPreprocessor _inner;

    /// <summary>上一次处理时拆出来的注音分组，OverlayController 在网格生成完之后
    /// 读这份数据去定位。</summary>
    public List<SkyPrisonRubyTextProcessor.RubyGroup> LastGroups { get; private set; } =
        new List<SkyPrisonRubyTextProcessor.RubyGroup>();

    private readonly TMP_Text _owner;

    public SkyPrisonRubyTextPreprocessor(TMP_Text owner, ITextPreprocessor inner)
    {
        _owner = owner;
        _inner = inner;
    }

    public string PreprocessText(string text)
    {
        string resolved = _inner != null ? _inner.PreprocessText(text) : text;
        string baseOnly = SkyPrisonRubyTextProcessor.ExtractGroups(resolved, out var groups);
        LastGroups = groups;

        // 2026-08-19：一次性诊断——确认这几个显示不对的文本框到底有没有真的走到
        // 这个方法。确认完可以删。
        if (SkyPrisonRubyTextProcessor.ContainsRubyMarkup(resolved))
        {
            string path = _owner != null ? GetHierarchyPath(_owner.transform) : "null";
            Debug.Log($"[RubyDiag] PreprocessText 跑了 -> path={path}, " +
                      $"in=\"{resolved}\", out=\"{baseOnly}\", groups={groups.Count}", _owner);
        }

        return baseOnly;
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 10)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }
}
