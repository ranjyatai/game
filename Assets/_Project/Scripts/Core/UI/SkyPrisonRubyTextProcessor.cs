using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 富文本注音语法解析——{文字A|文字B}，A 是本体，B 是要标注在 A 正上方的小字
/// （日文ルビ那种效果）。这个类只负责"从原始字符串里把标记拆出来"，不负责渲染：
/// 拆完得到一份不含任何标记的纯净文本（交给 TMP 正常渲染/换行，不用猜测的富文本
/// 间距标签去拼位置），以及每一段注音在纯净文本里对应的字符范围——真正的坐标要等
/// TMP 把纯净文本渲染完，从 TMP_TextInfo.characterInfo[] 里读实际生成出来的字形
/// 坐标才准，见 SkyPrisonRubyOverlayController。
/// </summary>
public static class SkyPrisonRubyTextProcessor
{
    private static readonly Regex RubyPattern = new Regex(@"\{([^{}|]+)\|([^{}|]+)\}", RegexOptions.Compiled);

    public readonly struct RubyGroup
    {
        /// <summary>本体文字在纯净文本（去掉标记之后）里的起始字符下标。</summary>
        public readonly int BaseStartIndex;
        /// <summary>本体文字长度。</summary>
        public readonly int BaseLength;
        /// <summary>要标注的注音文字。</summary>
        public readonly string RubyText;

        public RubyGroup(int baseStartIndex, int baseLength, string rubyText)
        {
            BaseStartIndex = baseStartIndex;
            BaseLength = baseLength;
            RubyText = rubyText;
        }
    }

    public static bool ContainsRubyMarkup(string raw) => !string.IsNullOrEmpty(raw) && RubyPattern.IsMatch(raw);

    /// <summary>只要干净文本、不需要注音标注时用这个——比如旧版 UnityEngine.UI.Text
    /// 这类接不上 TMP ITextPreprocessor 的显示路径。花括号和竖线是给注音标记用的
    /// 内部语法，不该让玩家看到原始写法，哪怕这个地方不打算真的把注音标出来。
    /// 纯字符串处理，不依赖任何 TMP_Text 实例，字符串在哪都能调。</summary>
    public static string StripToPlainText(string raw) => ExtractGroups(raw, out _);

    /// <summary>把 {A|B} 语法拆开。返回纯净文本（只剩 A，没有任何标记，TMP 可以直接
    /// 拿去正常渲染），groups 记录每一段注音在这份纯净文本里对应的字符范围。</summary>
    public static string ExtractGroups(string raw, out List<RubyGroup> groups)
    {
        groups = new List<RubyGroup>();
        if (string.IsNullOrEmpty(raw))
            return raw;

        if (!RubyPattern.IsMatch(raw))
            return raw;

        var sb = new StringBuilder(raw.Length);
        int lastIndex = 0;
        foreach (Match m in RubyPattern.Matches(raw))
        {
            sb.Append(raw, lastIndex, m.Index - lastIndex);

            string baseText = m.Groups[1].Value;
            string rubyText = m.Groups[2].Value;
            groups.Add(new RubyGroup(sb.Length, baseText.Length, rubyText));
            sb.Append(baseText);

            lastIndex = m.Index + m.Length;
        }
        sb.Append(raw, lastIndex, raw.Length - lastIndex);
        return sb.ToString();
    }
}
