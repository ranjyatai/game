using UnityEditor;
using UnityEngine;

public static class SkyPrisonTerrainLayerCleaner
{
    private const string MenuRoot = "Tools/Sky Prison/Ground Overlay/";

    [MenuItem(MenuRoot + "清除选中 TerrainLayer 的地表残留")]
    public static void ClearSelectedTerrainLayerResidue()
    {
        Terrain terrain = GetTargetTerrain();
        TerrainLayer layer = Selection.activeObject as TerrainLayer;

        if (terrain == null)
        {
            EditorUtility.DisplayDialog("清除 TerrainLayer 残留", "请先选中场景里的 Terrain，或确保场景里有 Active Terrain。", "知道了");
            return;
        }

        if (layer == null)
        {
            EditorUtility.DisplayDialog("清除 TerrainLayer 残留", "请在 Project 面板选中要清除的 TerrainLayer 资源，例如误刷进去的马路线 TerrainLayer。", "知道了");
            return;
        }

        int layerIndex = FindLayerIndex(terrain.terrainData, layer);
        if (layerIndex < 0)
        {
            EditorUtility.DisplayDialog("清除 TerrainLayer 残留", $"当前 Terrain 没有使用这个 TerrainLayer：\n{AssetDatabase.GetAssetPath(layer)}", "知道了");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "清除 TerrainLayer 残留",
            $"将从当前 Terrain 的 alphamap 中清除该图层权重：\n\n{layer.name}\n\n这会删除之前误刷进去的低清马路线/贴花痕迹，但不会删除 TerrainLayer 资源本身。",
            "清除",
            "取消");

        if (!ok)
            return;

        ClearLayerWeights(terrain, layerIndex);
        Debug.Log($"[SkyPrisonTerrainLayerCleaner] 已清除 TerrainLayer 残留: {layer.name} on {terrain.name}", terrain);
    }

    [MenuItem(MenuRoot + "清除选中 TerrainLayer 的地表残留", true)]
    private static bool ValidateClearSelectedTerrainLayerResidue()
    {
        return Selection.activeObject is TerrainLayer || Selection.activeGameObject != null || Terrain.activeTerrain != null;
    }

    [MenuItem(MenuRoot + "清除 RoadLine 类 TerrainLayer 残留")]
    public static void ClearRoadLineLikeTerrainLayers()
    {
        Terrain terrain = GetTargetTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("清除 RoadLine 残留", "请先选中场景里的 Terrain，或确保场景里有 Active Terrain。", "知道了");
            return;
        }

        TerrainData data = terrain.terrainData;
        TerrainLayer[] layers = data.terrainLayers;
        if (layers == null || layers.Length == 0)
        {
            EditorUtility.DisplayDialog("清除 RoadLine 残留", "当前 Terrain 没有 TerrainLayer。", "知道了");
            return;
        }

        bool[] clear = new bool[layers.Length];
        int count = 0;
        string names = "";
        for (int i = 0; i < layers.Length; i++)
        {
            TerrainLayer layer = layers[i];
            if (layer == null)
                continue;

            string path = AssetDatabase.GetAssetPath(layer);
            string key = (layer.name + " " + path).ToLowerInvariant();
            bool looksLikeRoadLine =
                key.Contains("roadline") ||
                key.Contains("road_line") ||
                key.Contains("road line") ||
                key.Contains("spline/roadline") ||
                key.Contains("马路线") ||
                key.Contains("画线") ||
                key.Contains("样条图案");

            if (!looksLikeRoadLine)
                continue;

            clear[i] = true;
            count++;
            names += $"- {layer.name}\n";
        }

        if (count <= 0)
        {
            EditorUtility.DisplayDialog("清除 RoadLine 残留", "没有在当前 Terrain 的 TerrainLayers 里找到 RoadLine / 马路线 / 画线类图层。", "知道了");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "清除 RoadLine 残留",
            $"将清除当前 Terrain alphamap 中这些疑似 RoadLine 图层的权重：\n\n{names}\n这不会删除资源，只会把它们从地形权重图里擦掉。",
            "清除",
            "取消");

        if (!ok)
            return;

        ClearLayerWeights(terrain, clear);
        Debug.Log($"[SkyPrisonTerrainLayerCleaner] 已清除 {count} 个 RoadLine 类 TerrainLayer 残留 on {terrain.name}", terrain);
    }

    private static Terrain GetTargetTerrain()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected != null)
        {
            Terrain terrain = selected.GetComponent<Terrain>();
            if (terrain != null)
                return terrain;

            terrain = selected.GetComponentInParent<Terrain>();
            if (terrain != null)
                return terrain;
        }

        return Terrain.activeTerrain;
    }

    private static int FindLayerIndex(TerrainData data, TerrainLayer target)
    {
        TerrainLayer[] layers = data.terrainLayers;
        if (layers == null)
            return -1;

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == target)
                return i;
        }

        string targetPath = AssetDatabase.GetAssetPath(target);
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null && AssetDatabase.GetAssetPath(layers[i]) == targetPath)
                return i;
        }

        return -1;
    }

    private static void ClearLayerWeights(Terrain terrain, int layerIndex)
    {
        TerrainData data = terrain.terrainData;
        bool[] clear = new bool[data.alphamapLayers];
        if (layerIndex >= 0 && layerIndex < clear.Length)
            clear[layerIndex] = true;
        ClearLayerWeights(terrain, clear);
    }

    private static void ClearLayerWeights(Terrain terrain, bool[] clear)
    {
        TerrainData data = terrain.terrainData;
        int width = data.alphamapWidth;
        int height = data.alphamapHeight;
        int layers = data.alphamapLayers;

        if (clear == null || clear.Length < layers)
            return;

        Undo.RegisterCompleteObjectUndo(data, "Clear TerrainLayer Residue");

        float[,,] maps = data.GetAlphamaps(0, 0, width, height);

        int fallbackLayer = FindFallbackLayer(clear, layers);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int l = 0; l < layers; l++)
                {
                    if (clear[l])
                        maps[y, x, l] = 0f;
                }

                float sum = 0f;
                for (int l = 0; l < layers; l++)
                    sum += maps[y, x, l];

                if (sum <= 0.00001f)
                {
                    for (int l = 0; l < layers; l++)
                        maps[y, x, l] = 0f;

                    if (fallbackLayer >= 0)
                        maps[y, x, fallbackLayer] = 1f;
                }
                else
                {
                    float inv = 1f / sum;
                    for (int l = 0; l < layers; l++)
                        maps[y, x, l] *= inv;
                }
            }
        }

        data.SetAlphamaps(0, 0, maps);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    private static int FindFallbackLayer(bool[] clear, int layers)
    {
        for (int i = 0; i < layers; i++)
        {
            if (!clear[i])
                return i;
        }

        return layers > 0 ? 0 : -1;
    }
}
