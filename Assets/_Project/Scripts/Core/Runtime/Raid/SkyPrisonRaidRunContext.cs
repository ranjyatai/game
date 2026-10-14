/// <summary>本次出发选的是哪个 RAID 的昼版还是夜版。进本的场景（光照、天气、怪物）读这里决定用昼 / 夜。</summary>
public static class SkyPrisonRaidRunContext
{
    public static string RaidId = "";
    public static bool Night;
    /// <summary>这次出发要进的场景名；进场景的地名牌用它判断「当前这张图是不是这个 RAID」，从而显示 RAID 编号和昼夜。</summary>
    public static string Scene = "";
    /// <summary>RAID 在选关里的编号（RaidDefinition.order），地名牌副标显示成 RAID-01。</summary>
    public static int Order;
    /// <summary>本次 RAID 的经纬度文字（RaidDefinition.CoordinateText），地名牌在地图编辑器没填坐标时用它。</summary>
    public static string Coordinates = "";

    public static void Set(string raidId, bool night) { RaidId = raidId ?? ""; Night = night; Scene = ""; Order = 0; Coordinates = ""; }

    public static void Set(string raidId, bool night, string scene, int order, string coordinates = "")
    {
        RaidId = raidId ?? ""; Night = night; Scene = scene ?? ""; Order = order; Coordinates = coordinates ?? "";
    }
}
