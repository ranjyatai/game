using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全部人物记录的清单——跟 QuestDatabase 同一套"Resources 单例数据库"惯例。
/// 资产本体放在 Assets/_Project/Data/Resources/CharacterArcDatabase.asset。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/任务/人物记录数据库", fileName = "CharacterArcDatabase")]
public class CharacterArcDatabase : ScriptableObject
{
    public List<CharacterArcDefinition> arcs = new List<CharacterArcDefinition>();
}
