using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 人物记录的只读查询入口——跟 QuestRuntime 不同，人物记录没有"进行中/已完成"要
/// 持久化的状态机(不需要 unlockConditions/scanIntervalSeconds 那一套)，完成度是
/// 每次调用时按当前存档实时算出来的，所以不需要一个常驻 MonoBehaviour 单例，
/// 一个静态工具类就够。
/// </summary>
public static class CharacterArcRuntime
{
    private const string DatabaseResourcesPath = "CharacterArcDatabase";

    private static CharacterArcDatabase _database;
    private static bool _loadAttempted;

    private static CharacterArcDatabase Database
    {
        get
        {
            if (!_loadAttempted)
            {
                _loadAttempted = true;
                _database = Resources.Load<CharacterArcDatabase>(DatabaseResourcesPath);
            }
            return _database;
        }
    }

    /// <summary>全部人物记录——任务日志面板用这个，不按NPC筛。</summary>
    public static List<CharacterArcDefinition> GetAll()
    {
        var result = new List<CharacterArcDefinition>();
        if (Database == null) return result;

        foreach (var arc in Database.arcs)
            if (arc != null)
                result.Add(arc);

        return result;
    }
}
