using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// V3 - 2026-05-28
/// Runtime resolver for footstep / hearing ground-surface audio.
///
/// Responsibility:
/// - Sample Unity Terrain alphamap at a world position.
/// - Convert TerrainLayer weights into GroundSurfaceMaterialDefinition weights.
/// - Return the material mix to UnitFootstepAudioEmitter without putting Terrain logic inside the emitter.
///
/// V3 changes:
/// - Uses GroundSurfaceMaterialDefinition.EffectiveAudioRuntimeLayerKey as the formal audio layer key.
/// - Keeps explicit TerrainLayer -> GroundSurfaceMaterialDefinition bindings.
/// - Keeps old definition.terrainLayer matching.
/// - Adds fallback matching by TerrainLayer name, surfaceId, displayName, formal audio key and texture name.
/// - Debug log prints raw TerrainLayer weights when no surface definition matches.
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonGroundAudioSurfaceResolver : MonoBehaviour
{
    [Serializable]
    public class TerrainLayerSurfaceBinding
    {
        public TerrainLayer terrainLayer;
        public GroundSurfaceMaterialDefinition surfaceDefinition;
    }

    [Serializable]
    public struct SurfaceWeight
    {
        public GroundSurfaceMaterialDefinition definition;
        public string surfaceId;
        public string displayName;
        public string runtimeLayerKey;
        public TerrainLayer terrainLayer;
        public int terrainLayerIndex;
        [Range(0f, 1f)] public float weight;
    }

    [Header("Terrain Source")]
    [SerializeField] private Terrain terrain;
    [SerializeField] private bool autoFindTerrain = true;

    [Header("Ground Surface Definitions")]
    [Tooltip("显式绑定：优先用这里把 TerrainData.terrainLayers 的每一层对应到地表定义。比自动猜测更稳定。")]
    [SerializeField] private List<TerrainLayerSurfaceBinding> terrainLayerBindings = new List<TerrainLayerSurfaceBinding>();

    [Tooltip("地表材质定义列表。Resolver 会用 definition.terrainLayer、名字、材质 ID、音声 Key、贴图名做兜底匹配。")]
    [SerializeField] private List<GroundSurfaceMaterialDefinition> surfaceDefinitions = new List<GroundSurfaceMaterialDefinition>();

    [Tooltip("找不到 TerrainLayer 对应定义时的兜底地表。可以为空；为空则忽略未知层。")]
    [SerializeField] private GroundSurfaceMaterialDefinition fallbackSurfaceDefinition;

    [Header("Sampling")]
    [Tooltip("忽略过小权重，避免 1% 的残留笔触也触发一层脚步声。")]
    [Range(0f, 1f)]
    [SerializeField] private float minWeightToReport = 0.01f;

    [Tooltip("返回前按权重从大到小排序。")]
    [SerializeField] private bool sortByWeightDescending = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private Vector3 lastSampleWorldPosition;
    [SerializeField] private string lastSampleResultRuntime = "";
    [SerializeField] private string lastRawTerrainLayerSampleRuntime = "";

    private readonly List<SurfaceWeight> resultBuffer = new List<SurfaceWeight>();
    private readonly Dictionary<TerrainLayer, GroundSurfaceMaterialDefinition> terrainLayerToDefinition = new Dictionary<TerrainLayer, GroundSurfaceMaterialDefinition>();
    private bool definitionCacheDirty = true;

    private void Awake()
    {
        EnsureTerrain();
        RebuildDefinitionCache();
    }

    private void OnValidate()
    {
        definitionCacheDirty = true;
        if (autoFindTerrain && terrain == null)
            EnsureTerrain();
    }

    [ContextMenu("Refresh Resolver Cache")]
    public void RefreshResolverCache()
    {
        EnsureTerrain();
        RebuildDefinitionCache();
    }

#if UNITY_EDITOR
    [ContextMenu("Editor Auto Collect Ground Surface Definitions")]
    public void EditorAutoCollectGroundSurfaceDefinitions()
    {
        surfaceDefinitions.Clear();

        string[] guids = AssetDatabase.FindAssets("t:GroundSurfaceMaterialDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GroundSurfaceMaterialDefinition definition = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
            if (definition != null && !surfaceDefinitions.Contains(definition))
                surfaceDefinitions.Add(definition);
        }

        AutoBindTerrainLayersByBestEffort();
        definitionCacheDirty = true;
        RebuildDefinitionCache();
        EditorUtility.SetDirty(this);

        Debug.Log($"[SkyPrisonGroundAudioSurfaceResolver] Collected {surfaceDefinitions.Count} ground surface definitions and {terrainLayerBindings.Count} terrain layer bindings.", this);
    }

    [ContextMenu("Editor Auto Bind Terrain Layers By Best Effort")]
    public void EditorAutoBindTerrainLayersByBestEffort()
    {
        AutoBindTerrainLayersByBestEffort();
        definitionCacheDirty = true;
        RebuildDefinitionCache();
        EditorUtility.SetDirty(this);
        Debug.Log($"[SkyPrisonGroundAudioSurfaceResolver] Auto bound {terrainLayerBindings.Count} terrain layers.", this);
    }
#endif

    public List<SurfaceWeight> GetSurfaceWeights(Vector3 worldPosition)
    {
        resultBuffer.Clear();
        lastSampleWorldPosition = worldPosition;

        EnsureTerrain();
        if (definitionCacheDirty)
            RebuildDefinitionCache();

        if (terrain == null || terrain.terrainData == null)
        {
            lastSampleResultRuntime = "No Terrain";
            lastRawTerrainLayerSampleRuntime = "No Terrain";
            return resultBuffer;
        }

        TerrainData data = terrain.terrainData;
        TerrainLayer[] terrainLayers = data.terrainLayers;
        if (terrainLayers == null || terrainLayers.Length == 0 || data.alphamapWidth <= 0 || data.alphamapHeight <= 0)
        {
            lastSampleResultRuntime = "No TerrainLayers";
            lastRawTerrainLayerSampleRuntime = "No TerrainLayers";
            return resultBuffer;
        }

        Vector3 local = worldPosition - terrain.transform.position;
        float normalizedX = Mathf.Clamp01(local.x / Mathf.Max(0.0001f, data.size.x));
        float normalizedZ = Mathf.Clamp01(local.z / Mathf.Max(0.0001f, data.size.z));

        int alphaX = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
        int alphaZ = Mathf.Clamp(Mathf.RoundToInt(normalizedZ * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);

        float[,,] alpha = data.GetAlphamaps(alphaX, alphaZ, 1, 1);
        int layerCount = Mathf.Min(alpha.GetLength(2), terrainLayers.Length);
        lastRawTerrainLayerSampleRuntime = BuildRawTerrainLayerDebugString(alpha, terrainLayers, layerCount);

        float reportedWeightSum = 0f;

        for (int i = 0; i < layerCount; i++)
        {
            float weight = Mathf.Clamp01(alpha[0, 0, i]);
            if (weight < minWeightToReport)
                continue;

            GroundSurfaceMaterialDefinition definition = ResolveDefinitionForTerrainLayer(terrainLayers[i]);
            if (definition == null)
                definition = fallbackSurfaceDefinition;

            if (definition == null)
                continue;

            SurfaceWeight entry = new SurfaceWeight
            {
                definition = definition,
                surfaceId = definition.surfaceId,
                displayName = definition.displayName,
                runtimeLayerKey = definition.EffectiveAudioRuntimeLayerKey,
                terrainLayer = terrainLayers[i],
                terrainLayerIndex = i,
                weight = weight
            };

            reportedWeightSum += weight;
            resultBuffer.Add(entry);
        }

        if (reportedWeightSum > 0.0001f)
        {
            for (int i = 0; i < resultBuffer.Count; i++)
            {
                SurfaceWeight entry = resultBuffer[i];
                entry.weight = Mathf.Clamp01(entry.weight / reportedWeightSum);
                resultBuffer[i] = entry;
            }
        }

        if (sortByWeightDescending)
            resultBuffer.Sort((a, b) => b.weight.CompareTo(a.weight));

        lastSampleResultRuntime = BuildDebugString(resultBuffer);

        if (debugLogs)
        {
            if (resultBuffer.Count > 0)
                Debug.Log($"[SkyPrisonGroundAudioSurfaceResolver] sample {worldPosition} => {lastSampleResultRuntime}", this);
            else
                Debug.Log($"[SkyPrisonGroundAudioSurfaceResolver] sample {worldPosition} => none. Terrain layers at point: {lastRawTerrainLayerSampleRuntime}", this);
        }

        return resultBuffer;
    }

    private void EnsureTerrain()
    {
        if (!autoFindTerrain || terrain != null)
            return;

        terrain = Terrain.activeTerrain;
        if (terrain != null)
            return;

#if UNITY_2023_1_OR_NEWER
        Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
#else
        Terrain[] terrains = FindObjectsOfType<Terrain>();
#endif
        if (terrains != null && terrains.Length > 0)
            terrain = terrains[0];
    }

    private void RebuildDefinitionCache()
    {
        terrainLayerToDefinition.Clear();

        if (terrainLayerBindings != null)
        {
            for (int i = 0; i < terrainLayerBindings.Count; i++)
            {
                TerrainLayerSurfaceBinding binding = terrainLayerBindings[i];
                if (binding == null || binding.terrainLayer == null || binding.surfaceDefinition == null)
                    continue;

                terrainLayerToDefinition[binding.terrainLayer] = binding.surfaceDefinition;
            }
        }

        if (surfaceDefinitions != null)
        {
            for (int i = 0; i < surfaceDefinitions.Count; i++)
            {
                GroundSurfaceMaterialDefinition definition = surfaceDefinitions[i];
                if (definition == null || definition.terrainLayer == null)
                    continue;

                if (!terrainLayerToDefinition.ContainsKey(definition.terrainLayer))
                    terrainLayerToDefinition.Add(definition.terrainLayer, definition);
            }
        }

        definitionCacheDirty = false;
    }

    private GroundSurfaceMaterialDefinition ResolveDefinitionForTerrainLayer(TerrainLayer layer)
    {
        if (layer == null)
            return null;

        if (terrainLayerToDefinition.TryGetValue(layer, out GroundSurfaceMaterialDefinition definition) && definition != null)
            return definition;

        return ResolveDefinitionForTerrainLayerWithoutExplicitBindings(layer);
    }

    private void AutoBindTerrainLayersByBestEffort()
    {
        EnsureTerrain();
        if (terrain == null || terrain.terrainData == null)
            return;

        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
        if (terrainLayers == null)
            return;

        if (terrainLayerBindings == null)
            terrainLayerBindings = new List<TerrainLayerSurfaceBinding>();

        for (int i = 0; i < terrainLayers.Length; i++)
        {
            TerrainLayer layer = terrainLayers[i];
            if (layer == null)
                continue;

            TerrainLayerSurfaceBinding existing = terrainLayerBindings.Find(x => x != null && x.terrainLayer == layer);
            if (existing != null && existing.surfaceDefinition != null)
                continue;

            GroundSurfaceMaterialDefinition matched = ResolveDefinitionForTerrainLayerWithoutExplicitBindings(layer);
            if (matched == null)
                continue;

            if (existing == null)
            {
                existing = new TerrainLayerSurfaceBinding();
                existing.terrainLayer = layer;
                terrainLayerBindings.Add(existing);
            }

            existing.surfaceDefinition = matched;
        }
    }

    private GroundSurfaceMaterialDefinition ResolveDefinitionForTerrainLayerWithoutExplicitBindings(TerrainLayer layer)
    {
        if (layer == null || surfaceDefinitions == null)
            return null;

        string layerName = NormalizeKey(layer.name);
        string diffuseName = NormalizeKey(layer.diffuseTexture != null ? layer.diffuseTexture.name : "");
        string normalName = NormalizeKey(layer.normalMapTexture != null ? layer.normalMapTexture.name : "");

        for (int i = 0; i < surfaceDefinitions.Count; i++)
        {
            GroundSurfaceMaterialDefinition candidate = surfaceDefinitions[i];
            if (candidate == null)
                continue;

            if (candidate.terrainLayer == layer)
                return candidate;

            if (candidate.terrainLayer != null && NormalizeKey(candidate.terrainLayer.name) == layerName)
                return candidate;

            if (!string.IsNullOrEmpty(layerName))
            {
                if (NormalizeKey(candidate.surfaceId) == layerName)
                    return candidate;
                if (NormalizeKey(candidate.displayName) == layerName)
                    return candidate;
                if (NormalizeKey(candidate.EffectiveAudioRuntimeLayerKey) == layerName)
                    return candidate;
            }

            if (!string.IsNullOrEmpty(diffuseName))
            {
                if (candidate.baseTexture != null && NormalizeKey(candidate.baseTexture.name) == diffuseName)
                    return candidate;
                if (candidate.terrainLayer != null && candidate.terrainLayer.diffuseTexture != null && NormalizeKey(candidate.terrainLayer.diffuseTexture.name) == diffuseName)
                    return candidate;
            }

            if (!string.IsNullOrEmpty(normalName))
            {
                if (candidate.terrainLayer != null && candidate.terrainLayer.normalMapTexture != null && NormalizeKey(candidate.terrainLayer.normalMapTexture.name) == normalName)
                    return candidate;
            }
        }

        return null;
    }

    private static string BuildDebugString(List<SurfaceWeight> weights)
    {
        if (weights == null || weights.Count == 0)
            return "none";

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < weights.Count; i++)
        {
            if (i > 0)
                builder.Append(" + ");

            SurfaceWeight weight = weights[i];
            builder.Append(string.IsNullOrWhiteSpace(weight.displayName) ? weight.surfaceId : weight.displayName);
            builder.Append(" ");
            builder.Append((weight.weight * 100f).ToString("0.#"));
            builder.Append("%");
            if (!string.IsNullOrWhiteSpace(weight.runtimeLayerKey))
            {
                builder.Append(" / ");
                builder.Append(weight.runtimeLayerKey);
            }
        }

        return builder.ToString();
    }

    private static string BuildRawTerrainLayerDebugString(float[,,] alpha, TerrainLayer[] terrainLayers, int layerCount)
    {
        if (alpha == null || terrainLayers == null || layerCount <= 0)
            return "empty";

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < layerCount; i++)
        {
            float weight = Mathf.Clamp01(alpha[0, 0, i]);
            if (weight <= 0.0001f)
                continue;

            if (builder.Length > 0)
                builder.Append(" + ");

            TerrainLayer layer = terrainLayers[i];
            builder.Append(layer != null ? layer.name : "<null>");
            builder.Append(":");
            builder.Append(Mathf.RoundToInt(weight * 100f));
            builder.Append("%");
        }

        return builder.Length > 0 ? builder.ToString() : "all weights below threshold";
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
    }
}
