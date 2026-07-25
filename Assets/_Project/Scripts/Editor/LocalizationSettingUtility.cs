using UnityEditor;
using UnityEngine;

public static class LocalizationSettingsUtility
{
    public const string DefaultSettingsFolder = "Assets/_Project/Data/ProjectSettings";
    public const string DefaultSettingsPath = "Assets/_Project/Data/ProjectSettings/LocalizationProjectSettings.asset";

    public static LocalizationProjectSettings GetOrCreateSettings()
    {
        LocalizationProjectSettings settings = AssetDatabase.LoadAssetAtPath<LocalizationProjectSettings>(DefaultSettingsPath);
        if (settings != null)
            return settings;

        EnsureFolderExists(DefaultSettingsFolder);

        settings = ScriptableObject.CreateInstance<LocalizationProjectSettings>();

        LocalizationProjectSettings.LanguageEntry defaultEntry = new LocalizationProjectSettings.LanguageEntry
        {
            enabled = true,
            isDefault = true,
            languageCode = "zh-CN",
            displayName = "中文（简体）"
        };

        settings.languages.Add(defaultEntry);
        settings.EnsureSingleDefault();

        AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return settings;
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
