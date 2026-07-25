using System.IO;
using UnityEngine;

/// <summary>
/// 游戏所有持久化路径的唯一来源。
/// 任何需要读写磁盘的系统都从这里取路径，不要硬编码字符串。
/// </summary>
public static class GamePaths
{
    // ── 根目录 ────────────────────────────────────────────────────────────
    public static string Root        => Application.persistentDataPath;
    public static string Screenshots => Path.Combine(Root, "screenshots");
    public static string Logs        => Path.Combine(Root, "logs");
    public static string Cache       => Path.Combine(Root, "cache");

    // ── 存档与设置 ────────────────────────────────────────────────────────
    // 5 个槽位地位完全平等（怪物猎人式"一个角色一份存档"，不是 JRPG 那种单角色多存档）。
    // 之前 slot 0 被当成特殊的"自动存档"位，跟 1-4 号手动存档不对等——已经去掉这个
    // 特殊待遇，SaveSlotSelectorUI 现在把 5 个槽位一视同仁。GamePaths.AutoSave 只在
    // SaveManager.ActiveSlot 还没被设置（-1）时当兜底路径用，不代表槽位 0 有特殊含义。
    public static string SaveSlot(int slot) => Path.Combine(Root, $"save_{slot}.json");
    public static string AutoSave    => SaveSlot(0);
    public static string Settings    => Path.Combine(Root, "settings.json");

    /// <summary>存档同步保存的截图快照，跟对应 slot 一一对应。</summary>
    public static string SaveScreenshot(int slot) => Path.Combine(Screenshots, $"save_{slot}.png");

    public const int SaveSlotCount = 5;

    // ── 缓存子目录 ────────────────────────────────────────────────────────
    public static string MapCache    => Path.Combine(Cache, "maps");

    // ── 初始化（在游戏启动时调用一次）────────────────────────────────────
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Screenshots);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(MapCache);
    }

    /// <summary>清除所有临时缓存（不影响存档和设置）。</summary>
    public static void ClearCache()
    {
        if (Directory.Exists(Cache))
        {
            Directory.Delete(Cache, recursive: true);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(MapCache);
        }
    }

    /// <summary>返回人类可读的磁盘占用摘要（用于设置界面显示）。</summary>
    public static string GetStorageSummary()
    {
        long saveBytes  = FileSize(AutoSave);
        long cacheBytes = DirSize(Cache);
        return $"存档：{FormatBytes(saveBytes)}  缓存：{FormatBytes(cacheBytes)}";
    }

    /// <summary>当前缓存目录占用大小，格式化成人类可读字符串（用于清除缓存前的确认弹窗）。</summary>
    public static string GetCacheSizeFormatted() => FormatBytes(DirSize(Cache));

    private static long FileSize(string path)
        => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static long DirSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            total += new FileInfo(f).Length;
        return total;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)        return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
