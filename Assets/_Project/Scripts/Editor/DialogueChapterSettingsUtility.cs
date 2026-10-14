using UnityEditor;
using UnityEngine;

public static class DialogueChapterSettingsUtility
{
    public const string DefaultSettingsFolder = "Assets/_Project/Data/ProjectSettings";
    public const string DefaultSettingsPath = "Assets/_Project/Data/ProjectSettings/DialogueChapterSettings.asset";

    public static DialogueChapterSettings GetOrCreateSettings()
    {
        DialogueChapterSettings settings = AssetDatabase.LoadAssetAtPath<DialogueChapterSettings>(DefaultSettingsPath);
        if (settings != null)
            return settings;

        EnsureFolderExists(DefaultSettingsFolder);

        settings = ScriptableObject.CreateInstance<DialogueChapterSettings>();

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
