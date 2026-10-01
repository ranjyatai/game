using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 查询某个世界坐标是不是落在 Terrain 的洞（Paint Hole）上。
///
/// 为什么需要：TerrainGroundMotor 找地面优先用 Terrain.SampleHeight，而 SampleHeight
/// 完全不管洞——洞口照样返回高度。结果是挖了洞的地方角色踩着一层看不见的地面悬空
/// 走过去，既不挡也不掉。移动系统用这里判断「这一步会不会走进洞」，把洞口当墙处理。
///
/// 洞数据按 TerrainData 缓存：运行时地形的洞不会变，每帧都 GetHoles 纯属浪费。
/// 编辑器里挖洞（SetHoles）会触发 TerrainCallbacks.textureChanged("holes")，据此让缓存失效。
/// </summary>
public static class SkyPrisonTerrainHoleQuery
{
    private static readonly Dictionary<TerrainData, bool[,]> Cache = new Dictionary<TerrainData, bool[,]>();

    // 虚空地表（SkyPrisonTerrainGrateBinder.voidLayer）：按 splat 权重 >= 0.5 算作虚空，和着色器
    // 的裁剪阈值一致。只缓存这一层的布尔网格；null 表示这块地形没有虚空层。
    private static readonly Dictionary<TerrainData, bool[,]> VoidCache = new Dictionary<TerrainData, bool[,]>();
    private const int AlphamapReadStripRows = 64;

    static SkyPrisonTerrainHoleQuery()
    {
        TerrainCallbacks.textureChanged += OnTerrainTextureChanged;
    }

    private static void OnTerrainTextureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synced)
    {
        if (terrain == null || terrain.terrainData == null)
            return;

        if (textureName == TerrainData.HolesTextureName)
            Cache.Remove(terrain.terrainData);
        else if (textureName == TerrainData.AlphamapTextureName)
            VoidCache.Remove(terrain.terrainData);
    }

    /// <summary>刷了虚空层、或者绑定组件换了虚空材质之后，让缓存重建。</summary>
    public static void InvalidateVoid(TerrainData data)
    {
        if (data != null)
            VoidCache.Remove(data);
    }

    /// <summary>落在洞上，或落在虚空地表上——两者对移动来说都是墙。</summary>
    public static bool IsBlocked(Vector3 worldPosition) => IsHole(worldPosition) || IsVoid(worldPosition);

    /// <summary>脚底中心或四周 margin 处任意一点被挡（洞或虚空）。</summary>
    public static bool IsBlockedAround(Vector3 footWorld, float margin)
    {
        if (IsBlocked(footWorld))
            return true;
        if (margin <= 0f)
            return false;

        return IsBlocked(footWorld + new Vector3(margin, 0f, 0f))
            || IsBlocked(footWorld + new Vector3(-margin, 0f, 0f))
            || IsBlocked(footWorld + new Vector3(0f, 0f, margin))
            || IsBlocked(footWorld + new Vector3(0f, 0f, -margin));
    }

    public static bool IsVoid(Vector3 worldPosition)
    {
        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null)
                continue;

            TerrainData data = t.terrainData;
            Vector3 origin = t.transform.position;
            float u = (worldPosition.x - origin.x) / data.size.x;
            float v = (worldPosition.z - origin.z) / data.size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                continue;

            bool[,] grid = GetVoidGrid(t);
            if (grid == null)
                return false;

            int res = grid.GetLength(0);
            int x = Mathf.Clamp((int)(u * res), 0, res - 1);
            int y = Mathf.Clamp((int)(v * res), 0, res - 1);
            return grid[y, x];
        }

        return false;
    }

    private static bool[,] GetVoidGrid(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        if (VoidCache.TryGetValue(data, out bool[,] cached))
            return cached;

        bool[,] grid = null;
        var binder = terrain.GetComponent<SkyPrisonTerrainGrateBinder>();
        int layerIndex = binder != null ? SkyPrisonTerrainGrateBinder.FindLayerIndex(data, binder.voidLayer) : -1;
        if (layerIndex >= 0)
        {
            int w = data.alphamapWidth, h = data.alphamapHeight;
            grid = new bool[h, w];
            // 分条读：GetAlphamaps 一次返回所有层，整张 2048² × 十几层一次读会分配几百 MB。
            for (int y0 = 0; y0 < h; y0 += AlphamapReadStripRows)
            {
                int rows = Mathf.Min(AlphamapReadStripRows, h - y0);
                float[,,] strip = data.GetAlphamaps(0, y0, w, rows);
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < w; x++)
                        grid[y0 + y, x] = strip[y, x, layerIndex] >= 0.5f;
            }
        }

        VoidCache[data] = grid;
        return grid;
    }

    /// <summary>worldPosition 在某块 Terrain 的范围内且落在洞上。不在任何 Terrain 上返回 false。</summary>
    public static bool IsHole(Vector3 worldPosition)
    {
        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null)
                continue;

            TerrainData data = t.terrainData;
            Vector3 origin = t.transform.position;
            Vector3 size = data.size;
            float u = (worldPosition.x - origin.x) / size.x;
            float v = (worldPosition.z - origin.z) / size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                continue;

            bool[,] holes = GetHoles(data);
            if (holes == null)
                return false;

            int res = holes.GetLength(0);
            int x = Mathf.Clamp((int)(u * res), 0, res - 1);
            int y = Mathf.Clamp((int)(v * res), 0, res - 1);
            // TerrainData 的约定：true = 有地面，false = 洞。
            return !holes[y, x];
        }

        return false;
    }

    /// <summary>脚底中心或四周 margin 处任意一点落在洞上。用来在身体还没悬空之前就挡住。</summary>
    public static bool IsHoleAround(Vector3 footWorld, float margin)
    {
        if (IsHole(footWorld))
            return true;
        if (margin <= 0f)
            return false;

        return IsHole(footWorld + new Vector3(margin, 0f, 0f))
            || IsHole(footWorld + new Vector3(-margin, 0f, 0f))
            || IsHole(footWorld + new Vector3(0f, 0f, margin))
            || IsHole(footWorld + new Vector3(0f, 0f, -margin));
    }

    private static bool[,] GetHoles(TerrainData data)
    {
        if (Cache.TryGetValue(data, out bool[,] holes) && holes != null)
            return holes;

        int res = data.holesResolution;
        if (res <= 0)
            return null;

        holes = data.GetHoles(0, 0, res, res);
        Cache[data] = holes;
        return holes;
    }
}
