/// <summary>
/// 存档槽位轻量元数据，仅用于主菜单存档选择 UI 显示，不包含完整游戏数据。
/// </summary>
[System.Serializable]
public class SaveSlotMeta
{
    public int    slot;
    public bool   isEmpty;
    public string lastSaved;     // "yyyy-MM-dd HH:mm:ss"
    public string playTimeText;  // "12h 34m"
    public string chapterId;     // 上次在哪个章节（空 = 在基地）
    public string mapId;         // 上次所在的具体地图/场景（对应 MapDefinition.scenePath 的场景名）

    // 5 个存档位地位完全平等（怪物猎人式"一个角色一份存档"，不是 JRPG 那种单角色
    // 多存档），不再有"0 号是自动存档、跟 1-4 号手动存档不对等"这种特殊待遇。
    public string SlotLabel => $"存档 {slot}";

    public string StatusLine
    {
        get
        {
            if (isEmpty) return "（空）";
            string loc = string.IsNullOrEmpty(chapterId) ? "基地" : chapterId;
            return $"{lastSaved}  |  {loc}  |  {playTimeText}";
        }
    }
}
