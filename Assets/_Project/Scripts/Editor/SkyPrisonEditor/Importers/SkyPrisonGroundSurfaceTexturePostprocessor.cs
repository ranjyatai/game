using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain 地表（TerrainLayer diffuse）贴图导入标准。
///
/// 地表贴图是平铺的：TerrainLayer 的 tileSize 通常是 4x4，400m 的地形上 UV 会跑到 100，
/// 一旦 wrap 是 Clamp，采样器在 [0,1] 之外只返回边缘像素，第一格之外整片都是从那一格
/// 边缘拉出来的条纹——表现为"刷上去的地表颜色很奇怪"，而不是缺纹理那么明显。
/// 素材导入默认多半是 Clamp，这个坑已经踩过两次（见 2bbe8b7b、以及漏掉的石砾）。
///
/// 同理 mipmap：地表在正交俯视下大面积缩小采样，没有 mip 就是闪烁摩尔纹。
/// 以及 alpha：地形着色器拿 diffuse 的 alpha 当 smoothness，带 alpha 会把该层锁成镜面。
///
/// 只强制这三项。textureType / sRGB / 压缩 都保持素材原样，别的地方（笔刷预览图标等）
/// 还依赖它们。
/// </summary>
public sealed class SkyPrisonGroundSurfaceTexturePostprocessor : AssetPostprocessor
{
    private const string LogPrefix = "[SkyPrison GroundSurface]";

    private static readonly HashSet<int> checkedTextureIds = new HashSet<int>();

    private static bool IsGroundSurfacePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string p = path.Replace('\\', '/').ToLowerInvariant();
        return p.Contains("/textures/ground/surface/");
    }

    private void OnPreprocessTexture()
    {
        if (!IsGroundSurfacePath(assetPath))
            return;

        ApplyGroundSurfaceImportSettings(assetImporter as TextureImporter);
    }

    /// <summary>
    /// 平铺地表的最低要求。返回是否真的改了东西——调用方据此决定要不要 SaveAndReimport。
    /// </summary>
    public static bool ApplyGroundSurfaceImportSettings(TextureImporter importer)
    {
        if (importer == null)
            return false;

        bool changed = false;

        if (importer.wrapMode != TextureWrapMode.Repeat)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            changed = true;
        }

        // wrapMode 只写总开关时，已经存在的逐轴 wrapU/V/W 覆盖不会被清掉，得逐个写。
        if (importer.wrapModeU != TextureWrapMode.Repeat)
        {
            importer.wrapModeU = TextureWrapMode.Repeat;
            changed = true;
        }

        if (importer.wrapModeV != TextureWrapMode.Repeat)
        {
            importer.wrapModeV = TextureWrapMode.Repeat;
            changed = true;
        }

        if (!importer.mipmapEnabled)
        {
            importer.mipmapEnabled = true;
            changed = true;
        }

        // 地表 albedo 绝对不能带 alpha 通道。
        //
        // URP 地形着色器把 diffuse 的 alpha 当作 smoothness 用，见
        // TerrainLitPasses.hlsl 里 SplatmapMix 上方那段注释：
        //   "Prior to coming in, _SmoothnessN is actually set to max(_DiffuseHasAlphaN, _SmoothnessN)
        //    This means that if we have an alpha channel, _SmoothnessN is locked to 1.0"
        // 也就是说贴图只要有 alpha，TerrainLayer 上那个 smoothness 滑条就直接作废、
        // 该层被锁成满光滑度。不透明贴图的 alpha 恒为 1，于是 smoothness = 1.0，
        // 地面变成镜面，高光会把 albedo 冲成一片亮灰——症状是"颜色发灰、像蒙了一层"，
        // 很容易误判成贴图或者笔刷的问题。
        if (importer.alphaSource != TextureImporterAlphaSource.None)
        {
            importer.alphaSource = TextureImporterAlphaSource.None;
            changed = true;
        }

        if (importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = false;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 给 GSM 引用了目录外贴图的情况兜底：贴图第一次被挂到 TerrainLayer 上时修一次。
    ///
    /// 不按 Texture 上的属性提前退出——wrap 和 mip 能从属性读到，但"有没有 alpha 通道"
    /// 得看导入格式，读起来并不比直接查 importer 便宜。改成一个会话每张贴图只查一次，
    /// 落在每笔刷一次的路径上开销可以忽略。
    /// </summary>
    public static void EnsureTerrainSurfaceTextureTiling(Texture texture)
    {
        if (texture == null)
            return;

        if (!checkedTextureIds.Add(texture.GetInstanceID()))
            return;

        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(path))
            return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        if (!ApplyGroundSurfaceImportSettings(importer))
            return;

        importer.SaveAndReimport();
        Debug.Log($"{LogPrefix} {path} 被用作 Terrain 地表，已套用地表导入标准（Repeat 平铺 / mipmap / 去 alpha）。", texture);
    }

    [MenuItem("Tools/Sky Prison/Ground/Surface/检查并修复所有地表贴图平铺设置")]
    public static void FixAllTerrainSurfaceTextures()
    {
        checkedTextureIds.Clear();
        var textures = new HashSet<Texture>();

        // 目录约定内的全部贴图。
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsGroundSurfacePath(path))
                continue;

            Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (tex != null)
                textures.Add(tex);
        }

        // 加上所有真正会变成 TerrainLayer 的 GSM 贴图，不管它放在哪个目录。
        foreach (string guid in AssetDatabase.FindAssets("t:GroundSurfaceMaterialDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
            if (def == null || !def.useAsTerrainSurface || def.baseTexture == null)
                continue;

            textures.Add(def.baseTexture);
        }

        int fixedCount = 0;
        foreach (Texture tex in textures)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                continue;

            if (!ApplyGroundSurfaceImportSettings(importer))
                continue;

            importer.SaveAndReimport();
            fixedCount++;
            Debug.Log($"{LogPrefix} 已修复 {path}。", tex);
        }

        Debug.Log($"{LogPrefix} 检查完成：{textures.Count} 张地表贴图，修复 {fixedCount} 张。");
    }
}
