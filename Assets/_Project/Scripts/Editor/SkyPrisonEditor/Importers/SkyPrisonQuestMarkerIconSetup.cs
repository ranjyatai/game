using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 自动建立并填好 QuestMarkerIconSet——6 张图标 + 3 个配色。
///
/// 走自动化而不是留一份"请手动拖 6 个引用"的清单：拖引用这种事漏一个不会报错，
/// 只会安静地少一个图标，跟这个项目里反复出现的静默失败是同一类问题。
///
/// 图标同时会被强制成 Sprite 导入设置——它们是 UI 用图，Texture 类型进不了 Image。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonQuestMarkerIconSetup
{
    private const string LogPrefix = "[SkyPrison QuestMarker]";
    private const string SetupVersionKey = "SkyPrison.QuestMarkerIconSetup.Version";
    private const int SetupVersion = 1;

    private const string IconFolder = "Assets/_Project/Icon/Quest";
    private const string AssetPath = "Assets/_Project/Data/Resources/QuestMarkerIconSet.asset";

    static SkyPrisonQuestMarkerIconSetup()
    {
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        if (EditorPrefs.GetInt(SetupVersionKey, 0) >= SetupVersion)
            return;

        EditorPrefs.SetInt(SetupVersionKey, SetupVersion);
        Build();
    }

    [MenuItem("天空囚笼/重建注册表/重建任务标记图标集", false, 168)]
    public static void Rebuild()
    {
        Build();
    }

    private static void Build()
    {
        QuestMarkerIconSet set = AssetDatabase.LoadAssetAtPath<QuestMarkerIconSet>(AssetPath);
        bool created = false;

        if (set == null)
        {
            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
            set = ScriptableObject.CreateInstance<QuestMarkerIconSet>();
            AssetDatabase.CreateAsset(set, AssetPath);
            created = true;
        }

        int missing = 0;
        set.mainStoryAcceptable = LoadIcon("Quest_MainStory_Acceptable", ref missing);
        set.mainStoryReport     = LoadIcon("Quest_MainStory_Report", ref missing);
        set.personAcceptable    = LoadIcon("Quest_Person_Acceptable", ref missing);
        set.personReport        = LoadIcon("Quest_Person_Report", ref missing);
        set.subStoryAcceptable  = LoadIcon("Quest_SubStory_Acceptable", ref missing);
        set.subStoryReport      = LoadIcon("Quest_SubStory_Report", ref missing);

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        QuestMarkerIconSet.ClearCache();

        if (missing > 0)
        {
            Debug.LogWarning(
                $"{LogPrefix} {(created ? "新建" : "更新")}了 {AssetPath}，" +
                $"但有 {missing} 张图标没找到——对应的任务类型不会显示图标。" +
                $"检查 {IconFolder} 下的文件名是否和代码里一致。", set);
            return;
        }

        Debug.Log($"{LogPrefix} {(created ? "新建" : "更新")}了 {AssetPath}，6 张图标全部就位。", set);
    }

    /// <summary>
    /// 按名字取图标，顺手保证它是 Sprite 导入设置。
    /// Texture 类型的图放不进 UI Image，而这批图默认导入类型不一定对。
    /// </summary>
    private static Sprite LoadIcon(string fileName, ref int missing)
    {
        string path = $"{IconFolder}/{fileName}.png";

        if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
            importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            Debug.Log($"{LogPrefix} {path} 已改为 Sprite 导入。");
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            missing++;
            Debug.LogWarning($"{LogPrefix} 找不到图标：{path}");
        }
        return sprite;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
