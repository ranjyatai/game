using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 地形「格栅」地表的着色器参数绑定。挂在 Terrain 上。
///
/// 格栅镂空在着色器里做（SkyPrisonTerrainGrate.hlsl / 「SkyPrison/Terrain/Lit Grate」），
/// 但着色器不知道「哪一个地形层是格栅」——地形层的顺序随刷材质时按需追加，
/// 格栅层可能落在第几层、哪张 splat 的哪个通道都不固定。这里每次渲染前查一遍，
/// 写成全局参数：
///   _SkyPrisonGrateSplat / _SkyPrisonGrateChannel / _SkyPrisonGrateParams
///
/// 用全局参数而不是材质参数：地形 12 层分三次绘制，追加层的材质是 Terrain 内部
/// 复制出来的，写在模板材质上的参数不一定同步过去；全局参数所有 pass 都读得到。
/// 代价是同一时间只支持一张地形的格栅——项目每张地图只有一块 Terrain。
///
/// 放置工具刷格栅材质时会自动挂上并填好（编辑器 EnsureOn），一般不用手动配置。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain))]
public sealed class SkyPrisonTerrainGrateBinder : MonoBehaviour
{
    public const string GrateTerrainShaderName = "SkyPrison/Terrain/Lit Grate";

    [Tooltip("格栅地表材质（读格子间距 / 栅条宽度）。")]
    public GroundSurfaceMaterialDefinition grateSurface;

    [Tooltip("这个格栅材质刷到地形上时对应的地形层。")]
    public TerrainLayer grateLayer;

    [Tooltip("虚空地表材质（刷到哪里地面整块消失、不能走）。")]
    public GroundSurfaceMaterialDefinition voidSurface;

    [Tooltip("虚空材质刷到地形上时对应的地形层。")]
    public TerrainLayer voidLayer;

    private static readonly int VoidSplatId = Shader.PropertyToID("_SkyPrisonVoidSplat");
    private static readonly int VoidChannelId = Shader.PropertyToID("_SkyPrisonVoidChannel");

    private static readonly int SplatId = Shader.PropertyToID("_SkyPrisonGrateSplat");
    private static readonly int ChannelId = Shader.PropertyToID("_SkyPrisonGrateChannel");
    private static readonly int ParamsId = Shader.PropertyToID("_SkyPrisonGrateParams");
    private static readonly int MaskId = Shader.PropertyToID("_SkyPrisonGrateMask");
    private static readonly int MaskSTId = Shader.PropertyToID("_SkyPrisonGrateMaskST");
    private static readonly int MaskParamsId = Shader.PropertyToID("_SkyPrisonGrateMaskParams");

    private static readonly Vector4[] OneHot =
    {
        new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1),
    };

    private Terrain _terrain;

    private void OnEnable()
    {
        _terrain = GetComponent<Terrain>();
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        Bind();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        Shader.SetGlobalVector(ChannelId, Vector4.zero);
        Shader.SetGlobalVector(VoidChannelId, Vector4.zero);
    }

    /// <summary>layer 在这块地形上是第几层；不在返回 -1。</summary>
    public static int FindLayerIndex(TerrainData data, TerrainLayer layer)
    {
        if (data == null || layer == null)
            return -1;

        TerrainLayer[] layers = data.terrainLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == layer)
                return i;
        }
        return -1;
    }

    // 每帧渲染前绑定一次：刷新地形层顺序变化、编辑器里改参数都能立刻生效，开销只是
    // 扫一遍十几个地形层。
    private void OnBeginContextRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
    {
        Bind();
    }

    private void Bind()
    {
        TerrainData data = _terrain != null ? _terrain.terrainData : null;
        Texture2D[] splats = data != null ? data.alphamapTextures : null;

        int voidIndex = FindLayerIndex(data, voidLayer);
        if (voidIndex >= 0 && splats != null && voidIndex / 4 < splats.Length)
        {
            Shader.SetGlobalTexture(VoidSplatId, splats[voidIndex / 4]);
            Shader.SetGlobalVector(VoidChannelId, OneHot[voidIndex % 4]);
        }
        else
        {
            Shader.SetGlobalVector(VoidChannelId, Vector4.zero);
        }

        int index = FindLayerIndex(data, grateLayer);
        if (index < 0 || splats == null || index / 4 >= splats.Length)
        {
            Shader.SetGlobalVector(ChannelId, Vector4.zero);
            return;
        }

        float cell = grateSurface != null ? grateSurface.grateCellSize : 0.5f;
        float bar = grateSurface != null ? grateSurface.grateBarWidth : 0.08f;

        Shader.SetGlobalTexture(SplatId, splats[index / 4]);
        Shader.SetGlobalVector(ChannelId, OneHot[index % 4]);
        Shader.SetGlobalVector(ParamsId, new Vector4(data.size.x, data.size.z, cell, bar));

        // 纹理遮罩的平铺跟这个地形层本身完全一致（Terrain 给 _SplatN_ST 的换算：
        // 缩放 = 地形尺寸 / tileSize，偏移 = tileOffset / tileSize），遮罩才会和
        // 地形层贴图上画的栅条对齐。
        Texture2D mask = grateSurface != null ? grateSurface.grateMaskTexture : null;
        if (mask != null)
        {
            Vector2 tile = new Vector2(Mathf.Max(0.001f, grateLayer.tileSize.x), Mathf.Max(0.001f, grateLayer.tileSize.y));
            Shader.SetGlobalTexture(MaskId, mask);
            Shader.SetGlobalVector(MaskSTId, new Vector4(
                data.size.x / tile.x, data.size.z / tile.y,
                grateLayer.tileOffset.x / tile.x, grateLayer.tileOffset.y / tile.y));
            Shader.SetGlobalVector(MaskParamsId, new Vector4(1f, grateSurface.grateMaskUseAlpha ? 1f : 0f, grateSurface.grateMaskThreshold, 0f));
        }
        else
        {
            Shader.SetGlobalVector(MaskParamsId, Vector4.zero);
        }
    }

#if UNITY_EDITOR
    /// <summary>放置工具刷格栅 / 虚空材质时调用：挂组件、填对应的层、把地形材质换成格栅版着色器。</summary>
    public static void EnsureOn(Terrain terrain, GroundSurfaceMaterialDefinition surface, TerrainLayer layer)
    {
        if (terrain == null || surface == null || layer == null)
            return;

        var binder = terrain.GetComponent<SkyPrisonTerrainGrateBinder>();
        if (binder == null)
            binder = UnityEditor.Undo.AddComponent<SkyPrisonTerrainGrateBinder>(terrain.gameObject);

        if (surface.isVoid)
        {
            if (binder.voidSurface != surface || binder.voidLayer != layer)
            {
                if (binder.voidSurface != null && binder.voidSurface != surface)
                    Debug.LogWarning($"[TerrainGrate] 地形 {terrain.name} 原来的虚空材质是 {binder.voidSurface.displayName}，" +
                                     $"现在换成 {surface.displayName}——一张地形同一时间只支持一种虚空。", terrain);
                UnityEditor.Undo.RecordObject(binder, "Bind terrain void");
                binder.voidSurface = surface;
                binder.voidLayer = layer;
                UnityEditor.EditorUtility.SetDirty(binder);
                SkyPrisonTerrainHoleQuery.InvalidateVoid(terrain.terrainData);
            }
        }
        else if (binder.grateSurface != surface || binder.grateLayer != layer)
        {
            if (binder.grateSurface != null && binder.grateSurface != surface)
                Debug.LogWarning($"[TerrainGrate] 地形 {terrain.name} 原来的格栅材质是 {binder.grateSurface.displayName}，" +
                                 $"现在换成 {surface.displayName}——一张地形同一时间只支持一种格栅。", terrain);
            UnityEditor.Undo.RecordObject(binder, "Bind terrain grate");
            binder.grateSurface = surface;
            binder.grateLayer = layer;
            UnityEditor.EditorUtility.SetDirty(binder);
        }

        Shader grateShader = Shader.Find(GrateTerrainShaderName);
        Material template = terrain.materialTemplate;
        if (grateShader != null && template != null && template.shader != grateShader)
        {
            // 格栅版是 URP Terrain/Lit 的原样复制 + 镂空，属性完全一致，直接换 shader 不丢参数。
            UnityEditor.Undo.RecordObject(template, "Switch terrain shader to grate");
            template.shader = grateShader;
            UnityEditor.EditorUtility.SetDirty(template);
            Debug.Log($"[TerrainGrate] 地形材质 {template.name} 已换成 {GrateTerrainShaderName}。", template);
        }

        binder.Bind();
    }
#endif
}
