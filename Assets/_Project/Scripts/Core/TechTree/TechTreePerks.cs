using System.Collections.Generic;

/// <summary>
/// 科技树(战术研修)效果的统一读取口：各个游戏系统要吃科技树加成时都从这里取，不用自己去翻科技树数据。
///   · Get(key)：所有已点节点里这个奖励键的累加值(掉宝率 +5 就是 5)，没有就是 0。
///   · Level(nodeId)：某个节点当前等级。像「夜视回充」「重装兵器」这种不能累加、要按节点等级查表的，用这个。
/// 汇总结果缓存着，节点升级 / 读档时自动失效重算。
/// </summary>
public static class TechTreePerks
{
    static Dictionary<string, float> _cache;
    static bool _hooked;

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        TechTreeRuntime.OnNodeLevelUp += (_, __) => _cache = null;
        SaveManager.OnSaveLoaded += () => _cache = null;
    }

    public static float Get(string key)
    {
        Hook();
        if (_cache == null)
        {
            var t = TechTreeRuntime.Instance;
            if (t == null) return 0f;
            _cache = t.GetAllRewards();
        }
        return _cache != null && _cache.TryGetValue(key, out float v) ? v : 0f;
    }

    /// <summary>百分比奖励换成乘数：+10 → 1.10。</summary>
    public static float Mult(string key) => 1f + Get(key) / 100f;

    /// <summary>玩家用掉一个消耗品时调用：先按「节省」概率判断这次要不要真的消耗，再按「回收」回复 AFP。返回 true = 照常消耗。</summary>
    public static bool OnConsumableUsed()
    {
        int afp = UnityEngine.Mathf.RoundToInt(Get("afpOnConsume"));
        if (afp > 0 && AFPRuntime.Instance != null) AFPRuntime.Instance.AddAFP(afp);
        float save = UnityEngine.Mathf.Clamp(Get("consumableSaveChance"), 0f, 25f);      // 封顶 25%，不会变成必然不消耗
        return save <= 0f || UnityEngine.Random.value * 100f >= save;
    }

    public static int Level(string nodeId)
    {
        var t = TechTreeRuntime.Instance;
        return t != null ? t.GetNodeLevel(nodeId) : 0;
    }
}
