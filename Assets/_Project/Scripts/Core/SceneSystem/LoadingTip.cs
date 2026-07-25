using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LocalizedTipText
{
    public string languageCode;
    [TextArea(2, 4)]
    public string richText;
}

[System.Serializable]
public class LoadingTip
{
    [Tooltip("仅编辑器显示名（不对玩家展示）")]
    public string tipName = "新 Tip";
    public List<LocalizedTipText> texts = new List<LocalizedTipText>();

    public string GetText(string langCode)
    {
        foreach (var t in texts)
            if (t.languageCode == langCode) return t.richText;
        // fallback: zh-CN → en → first
        foreach (var t in texts)
            if (t.languageCode == "zh-CN" || t.languageCode == "zh") return t.richText;
        foreach (var t in texts)
            if (t.languageCode.StartsWith("en")) return t.richText;
        return texts.Count > 0 ? texts[0].richText : string.Empty;
    }
}
