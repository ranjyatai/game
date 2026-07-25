using System;
using UnityEngine;

/// <summary>
/// 临时章节会话数据：玩家进本期间存在，出本或死亡后销毁。
/// 保存"此刻在哪、位置在哪"，不保存背包（背包在 PlayerSaveData 里实时更新）。
/// </summary>
[Serializable]
public class ChapterSessionData
{
    public string chapterId;
    public string mapId;

    // 玩家当前位置（用于本内地图切换时恢复）
    public float posX;
    public float posY;
    public float posZ;

    public Vector3 PlayerPosition
    {
        get => new Vector3(posX, posY, posZ);
        set { posX = value.x; posY = value.y; posZ = value.z; }
    }
}
