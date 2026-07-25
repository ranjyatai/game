using System.Collections.Generic;
using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "SkyPrison/Localization/Project Settings", fileName = "LocalizationProjectSettings")]
public class LocalizationProjectSettings : ScriptableObject
{
    [System.Serializable]
    public class LanguageEntry
    {
        [Header("基础信息")]
        public bool enabled = true;
        public bool isDefault = false;

        [Tooltip("建议使用稳定语言代码，例如 zh-CN / ja-JP / en-US")]
        public string languageCode = "zh-CN";

        [Tooltip("给编辑器里看的显示名称")]
        public string displayName = "中文（简体）";

        [TextArea(1, 3)]
        public string note;

        [Header("字体设置")]
        public Font primaryFont;
        public List<Font> fallbackFonts = new List<Font>();
        [Tooltip("TMP 字体（用于代码动态创建的 TextMeshProUGUI，必须填否则 Build 显示方块）")]
        public TMP_FontAsset primaryTMPFont;
    }

    [Header("项目级语言配置")]
    public List<LanguageEntry> languages = new List<LanguageEntry>();

    public LanguageEntry GetDefaultLanguage()
    {
        for (int i = 0; i < languages.Count; i++)
        {
            if (languages[i] != null && languages[i].isDefault)
                return languages[i];
        }

        return languages.Count > 0 ? languages[0] : null;
    }

    public void EnsureSingleDefault()
    {
        int foundDefaultIndex = -1;

        for (int i = 0; i < languages.Count; i++)
        {
            if (languages[i] == null)
                continue;

            if (languages[i].isDefault)
            {
                foundDefaultIndex = i;
                break;
            }
        }

        if (foundDefaultIndex < 0 && languages.Count > 0 && languages[0] != null)
        {
            languages[0].isDefault = true;
            foundDefaultIndex = 0;
        }

        for (int i = 0; i < languages.Count; i++)
        {
            if (languages[i] == null)
                continue;

            if (i != foundDefaultIndex)
                languages[i].isDefault = false;
        }
    }

    public bool HasLanguageCode(string code, int ignoreIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        for (int i = 0; i < languages.Count; i++)
        {
            if (i == ignoreIndex)
                continue;

            LanguageEntry entry = languages[i];
            if (entry == null)
                continue;

            if (entry.languageCode == code)
                return true;
        }

        return false;
    }

    public string GenerateUniqueLanguageCode(string baseCode)
    {
        string safeBase = string.IsNullOrWhiteSpace(baseCode) ? "new-language" : baseCode.Trim();

        if (!HasLanguageCode(safeBase))
            return safeBase;

        int index = 1;
        while (true)
        {
            string candidate = $"{safeBase}-{index:00}";
            if (!HasLanguageCode(candidate))
                return candidate;
            index++;
        }
    }
}
