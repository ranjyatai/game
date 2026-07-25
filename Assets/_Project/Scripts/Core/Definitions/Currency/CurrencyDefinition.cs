using UnityEngine;

[CreateAssetMenu(menuName = "Sky Prison/Currency Definition", fileName = "CD_NewCurrency")]
public class CurrencyDefinition : ScriptableObject
{
    [Header("基础信息")]
    public string currencyId = "token";
    public string displayName = "代币";
    public string unitSymbol = "T";
    public string nameKey = "";
    public string descKey = "";
    public string note = "";
    public Sprite icon;

    [Header("规则")]
    public bool isStandard = false;

    /// <summary>
    /// 运行时显示名：优先通过 nameKey 查 UILocalizationTable，找不到退回 displayName。
    /// </summary>
    public string GetDisplayName(UILocalizationTable table = null)
    {
        if (table != null && !string.IsNullOrEmpty(nameKey))
        {
            string loc = table.Get(nameKey, "");
            if (!string.IsNullOrEmpty(loc)) return loc;
        }
        return displayName;
    }
}
