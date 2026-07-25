using System.IO;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonCurrencySeeder
{
    private const string CurrencyFolder = "Assets/_Project/Data/Definitions/Standard/Currencies";
    private const string DefaultCurrencyPath = CurrencyFolder + "/CD_Token.asset";

    [MenuItem("Tools/Sky Prison/Currencies/确保默认标准货币")]
    public static void EnsureDefaultCurrency()
    {
        EnsureFolderExists(CurrencyFolder);

        CurrencyDefinition currency = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>(DefaultCurrencyPath);
        if (currency == null)
        {
            currency = ScriptableObject.CreateInstance<CurrencyDefinition>();
            currency.currencyId = "token";
            currency.displayName = "代币";
            currency.nameKey = "currency_token_name";
            currency.descKey = "currency_token_desc";
            currency.note = "系统默认标准货币。";
            currency.isStandard = true;

            AssetDatabase.CreateAsset(currency, DefaultCurrencyPath);
        }
        else
        {
            currency.currencyId = "token";
            currency.displayName = "代币";
            currency.nameKey = "currency_token_name";
            currency.descKey = "currency_token_desc";
            currency.isStandard = true;
            EditorUtility.SetDirty(currency);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完成", "已确保默认标准货币“代币”存在。", "确定");
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
