using UnityEditor;
using UnityEngine;

/// <summary>
/// 编译后自动把当前场景所有 Terrain 的分辨率参数打进日志。
///
/// TerrainData 是二进制资产（Force Text 只对 YAML 资产生效），alphamapResolution
/// 这些值没法从文件里读，只能问运行时 API。做成 InitializeOnLoad 是为了不用点按钮——
/// 编译一次就有结果，从 Editor.log 里读。
///
/// 看完删。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonTerrainResolutionReport
{
    static SkyPrisonTerrainResolutionReport()
    {
        EditorApplication.delayCall += Report;
    }

    private static void Report()
    {
        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            Debug.Log("[地形分辨率] 当前场景没有 active Terrain。");
            return;
        }

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null) continue;

            TerrainData d = t.terrainData;
            Vector3 size = d.size;

            float alphaMetersPerTexel = d.alphamapResolution > 0 ? size.x / d.alphamapResolution : 0f;
            float heightMetersPerTexel = d.heightmapResolution > 1 ? size.x / (d.heightmapResolution - 1) : 0f;

            // alphamap 是按 RGBA 打包的，4 层共用一张贴图
            int alphaTextureCount = Mathf.CeilToInt(d.terrainLayers.Length / 4f);
            long alphaBytes = (long)d.alphamapResolution * d.alphamapResolution * 4 * alphaTextureCount;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[地形分辨率] {t.name}");
            sb.AppendLine($"  地形尺寸            = {size.x} × {size.z} m（高 {size.y}）");
            sb.AppendLine($"  Control Texture     = {d.alphamapResolution}" +
                          $"   →  每控制像素 {alphaMetersPerTexel:0.###} m   ← 决定刷子边界能有多清晰");
            sb.AppendLine($"  Heightmap           = {d.heightmapResolution}" +
                          $"   →  每高度像素 {heightMetersPerTexel:0.###} m");
            sb.AppendLine($"  Base Texture        = {d.baseMapResolution}");
            sb.AppendLine($"  地表层数            = {d.terrainLayers.Length}（占 {alphaTextureCount} 张 alphamap 贴图）");
            sb.AppendLine($"  alphamap 显存占用   ≈ {alphaBytes / 1024f / 1024f:0.##} MB");
            sb.AppendLine($"  Basemap Distance    = {t.basemapDistance} m");
            sb.AppendLine($"  Pixel Error         = {t.heightmapPixelError}");

            for (int l = 0; l < d.terrainLayers.Length; l++)
            {
                TerrainLayer layer = d.terrainLayers[l];
                if (layer == null) continue;
                sb.AppendLine($"    层{l}: {layer.name}  平铺={layer.tileSize.x}×{layer.tileSize.y} m" +
                              $"  贴图={(layer.diffuseTexture != null ? layer.diffuseTexture.name + " " + layer.diffuseTexture.width + "px" : "无")}");
            }

            Debug.Log(sb.ToString(), t);
        }
    }
}
