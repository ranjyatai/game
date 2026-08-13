using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 建立 CharacterArcDatabase 资产，并把项目里所有 CharacterArcDefinition 收进去。
///
/// 这个资产之前根本不存在，CharacterArcRuntime.GetAll() 一直拿到 null 返回空列表——
/// 不报错、不警告，人物记录在任务日志和 NPC 头顶都安静地不显示。又是一个静默失败。
///
/// 顺带做「收集全部定义」：数据库是个手填列表，新建了人物记录却忘了加进去的话，
/// 表现和数据库不存在时一模一样。每次脚本重载扫一遍，有遗漏就补上并说明。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonCharacterArcDatabaseSetup
{
    private const string LogPrefix = "[SkyPrison CharacterArc]";
    private const string AssetPath = "Assets/_Project/Data/Resources/CharacterArcDatabase.asset";

    static SkyPrisonCharacterArcDatabaseSetup()
    {
        EditorApplication.delayCall += Sync;
    }

    [MenuItem("Tools/Sky Prison/任务/重建人物记录数据库")]
    public static void Rebuild() => Sync();

    private static void Sync()
    {
        CharacterArcDatabase database = AssetDatabase.LoadAssetAtPath<CharacterArcDatabase>(AssetPath);
        bool created = false;

        if (database == null)
        {
            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
            database = ScriptableObject.CreateInstance<CharacterArcDatabase>();
            AssetDatabase.CreateAsset(database, AssetPath);
            created = true;
        }

        var all = AssetDatabase.FindAssets("t:CharacterArcDefinition")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<CharacterArcDefinition>)
            .Where(arc => arc != null)
            .ToList();

        database.arcs.RemoveAll(arc => arc == null);
        var added = all.Where(arc => !database.arcs.Contains(arc)).ToList();

        if (!created && added.Count == 0)
            return;

        database.arcs.AddRange(added);
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();

        if (created && all.Count == 0)
        {
            Debug.Log(
                $"{LogPrefix} 已新建 {AssetPath}（当前项目里还没有任何人物记录定义）。" +
                "以后新建的定义会自动收进来。", database);
            return;
        }

        Debug.Log(
            $"{LogPrefix} {(created ? "已新建" : "已更新")} {AssetPath}，" +
            $"收录 {database.arcs.Count} 条人物记录" +
            $"{(added.Count > 0 ? $"（本次新增 {added.Count} 条）" : "")}。", database);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
