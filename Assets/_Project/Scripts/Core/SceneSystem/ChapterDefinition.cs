using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 章节定义 ScriptableObject。
/// 地图选择窗口读取这里的数据来显示节点信息；
/// 确认进入时 SceneLoader 加载 sceneName，场景里的 UnitSpawner/LootDrop 负责实际初始化。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/章节/章节定义", fileName = "ChapterDefinition")]
public class ChapterDefinition : ScriptableObject
{
    [Header("基础信息")]
    public string chapterId      = "chapter_01";
    public string displayName    = "第一章";
    public string sceneName      = "";          // 对应 Unity Build Settings 里的场景名

    [Header("地图节点位置（0~1，相对于地图图片）")]
    public Vector2 mapNodePosition = new Vector2(0.5f, 0.5f);

    [Header("难度与推荐等级")]
    public int     recommendedLevel = 1;
    [Range(1, 5)]
    public int     difficulty       = 1;        // 1=简单 … 5=极难

    [Header("任务目标（显示在进入确认面板）")]
    public List<string> objectives = new List<string>();

    [Header("解锁条件")]
    public ChapterDefinition unlockAfterChapter; // 完成该章节后解锁（null=默认开放）
    public int               requiredBaseLevel;  // 需要基地等级（0=无要求）

    [Header("描述与图片")]
    [TextArea(2, 4)]
    public string description = "";
    public Sprite previewImage;
}
