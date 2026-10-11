using System.Collections.Generic;
using UnityEngine;

/// <summary>一个可选的 RAID 本。全屏选本地图读这份数据：位置用 0~1 归一化坐标，以后换整座城市底图只换背景。</summary>
[CreateAssetMenu(menuName = "天空囚笼/RAID 本", fileName = "RAID_New")]
public class RaidDefinition : ScriptableObject
{
    [Header("标识")]
    public string raidId = "old_factory";
    [Tooltip("按顺序排列用，数字小的在前。")]
    public int order;

    [Header("名称与简介（中/日/英）")]
    public DoorLocalizedLabel displayName = new DoorLocalizedLabel();
    public DoorLocalizedLabel description = new DoorLocalizedLabel();
    [Tooltip("旧的 1~5 危险度，选关界面已不再读取（保留只为不破坏旧资产）。")]
    [Range(1, 5)] public int dangerLevel = 1;

    [Header("夜间版本（同一张图；夜更难，一个个开启）")]
    [Tooltip("这个本有没有夜间版本。没有就不显示昼夜切换。")]
    public bool hasNight;
    [Tooltip("夜间版本已开放。关着就是「尚未开放」；和下面的解锁条件要同时满足。")]
    public bool nightOpen;
    [Tooltip("夜间版本补充的一句话（如暴雨），可空。")]
    public DoorLocalizedLabel nightDescription = new DoorLocalizedLabel();
    public List<DoorCondition> nightUnlockConditions = new List<DoorCondition>();
    public DoorConditionMatch nightUnlockMatch = DoorConditionMatch.All;

    [Header("进入")]
    [Tooltip("目标场景名（需在 Build Settings 中）。")]
    public string targetScene;
    [Tooltip("到达后的玩家落点。")]
    public Vector3 arrivalPosition;
    [Tooltip("勾选 = 要先乘地铁（播放转场演出），只是表现，不换界面。")]
    public bool requiresSubway;

    [Header("地图上的位置（0~1，左下角为原点）")]
    public Vector2 mapPosition = new Vector2(0.5f, 0.5f);
    [Tooltip("选关右侧窗口顶部的影像图（印象图）。比例按 2:1 居中裁切，建议 1000×500 以上；界面里会做成黑白。")]
    public Sprite thumbnail;
    [Tooltip("夜间版本自己的影像图，可空（空则沿用上面的）。")]
    public Sprite nightThumbnail;

    [Header("可见/解锁（未满足 = 地图上完全不显示）")]
    [Tooltip("白天版本默认是否开放。关着就完全不显示，直到触发器（设置RAID入口状态）把它设为可进入。夜版另看上面的「夜间版本已开放」。")]
    public bool dayOpenByDefault = true;
    public List<DoorCondition> unlockConditions = new List<DoorCondition>();
    public DoorConditionMatch unlockMatch = DoorConditionMatch.All;

    /// <summary>测试阶段总开关：编辑器 / Development Build 里所有 RAID 和夜版都视为已解锁。正式发布前关掉（或去掉 DEVELOPMENT_BUILD）。</summary>
    public static bool UnlockAllForTesting = false;   // 默认关：只有 dayOpenByDefault 的本（目前仅旧工厂昼）开放，其余用触发器开启。想临时全开测试，把它改成 true
    static bool TestUnlock => UnlockAllForTesting && (Application.isEditor || Debug.isDebugBuild);

    // 入口状态：触发器（act_set_raid_entry）设置过的以它为准，没设置过的才看下面各自的解锁条件
    public bool IsDayEnterable => TestUnlock || (SkyPrisonRaidEntry.Get(SkyPrisonRaidEntry.Key(raidId, false)) ?? (dayOpenByDefault && DoorCondition.AllMet(unlockConditions, unlockMatch)));
    public bool IsNightUnlocked => hasNight && (TestUnlock || (SkyPrisonRaidEntry.Get(SkyPrisonRaidEntry.Key(raidId, true)) ?? (nightOpen && DoorCondition.AllMet(nightUnlockConditions, nightUnlockMatch))));
    /// <summary>地图上显不显示：昼夜任意一个可进入就显示；两个都不可进入 = 完全不显示。</summary>
    public bool IsUnlocked => IsDayEnterable || IsNightUnlocked;
    public bool CanEnter(bool night) { return night ? IsNightUnlocked : IsDayEnterable; }
    public string NightDesc => nightDescription.Resolve("", "");
    /// <summary>战术标注用的经纬度（由 mapPosition 推出，Tokyo 神田一带）。选关标记和进图地名牌共用同一个公式。</summary>
    public string CoordinateText()
    {
        double lat = 35.6950 + (mapPosition.y - 0.5) * 0.07, lon = 139.7700 + (mapPosition.x - 0.5) * 0.14;
        return Dms(lat, 'N', 'S') + "  " + Dms(lon, 'E', 'W');
    }

    static string Dms(double v, char pos, char neg)
    {
        char c = v >= 0 ? pos : neg; v = System.Math.Abs(v);
        int d = (int)v; double m = (v - d) * 60; int mi = (int)m; int sec = (int)((m - mi) * 60);
        return d + "°" + mi.ToString("00") + "′" + sec.ToString("00") + "″" + c;
    }

    public string Name => displayName.Resolve("", raidId);
    public string Desc => description.Resolve("", "");
}
