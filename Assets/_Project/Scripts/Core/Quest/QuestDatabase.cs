using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全部任务定义的清单——QuestRuntime 通过 Resources.Load 读这一份资产来知道
/// "游戏里一共有哪些任务"，跟 UILocalizationTable/ItemRegistry 同一套"Resources
/// 单例数据库"惯例。资产本体放在 Assets/_Project/Data/Resources/QuestDatabase.asset。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/任务/任务数据库", fileName = "QuestDatabase")]
public class QuestDatabase : ScriptableObject
{
    public List<QuestDefinition> quests = new List<QuestDefinition>();
}
