/// <summary>
/// 全项目统一的报错编号表。每个编号对应一类已知故障，玩家反馈问题时报个编号
/// （或者直接把 persistentDataPath/logs/error_log.txt 甩过来）就能立刻定位到是
/// 哪个系统出的问题，不用再靠"游戏卡住了"这种模糊描述来回猜。
///
/// 编号规则：类别码-SP（SP = Sky Prison，游戏 IP 自己的报错编号后缀）。
///   1xxx-SP 场景/读条系统
///   2xxx-SP 存档系统
///   3xxx-SP 输入系统
///   4xxx-SP 音频系统
///   5xxx-SP 地图/关卡数据
/// 新增故障点时，在这里加一条，不要在业务代码里随手编字符串。
/// </summary>
public static class SkyPrisonErrorCodes
{
    // ── 场景 / 读条系统 ──────────────────────────────────────────────────
    public const string SceneNotInBuildSettings = "1001-SP"; // 场景名没注册进 Build Settings，或者名字不存在
    public const string SceneLoadTimeout        = "1002-SP"; // BootCoordinator 各阶段超时没有真正就绪（见 SignalReady timeout 日志）

    // ── 存档系统 ─────────────────────────────────────────────────────────
    public const string SaveWriteFailed  = "2001-SP"; // 写入存档文件失败（磁盘满/权限/路径问题）
    public const string SaveReadCorrupt  = "2002-SP"; // 读取存档时反序列化失败，存档可能已损坏

    // ── 输入系统 ─────────────────────────────────────────────────────────
    public const string InputSettingsMissing = "3001-SP"; // SkyPrisonInputSettings 资产加载失败

    // ── 音频系统 ─────────────────────────────────────────────────────────
    public const string AudioCatalogMissing = "4001-SP"; // 运行时音频目录未能加载

    // ── 地图 / 关卡数据 ──────────────────────────────────────────────────
    public const string MapDefinitionMissing = "5001-SP"; // MapDefinition 资产缺失或场景引用失效
}
