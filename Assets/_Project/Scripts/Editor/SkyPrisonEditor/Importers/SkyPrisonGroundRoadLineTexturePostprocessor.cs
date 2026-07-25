using UnityEditor;
using UnityEngine;

/// <summary>
/// RoadLine / Spline 地面画线贴图导入标准。
/// 标准规格：2048 x 1024，横向，X 为线条前进方向，Y 为线宽方向。
/// 放在 Assets 任意 Editor 目录下生效。
/// </summary>
public sealed class SkyPrisonGroundRoadLineTexturePostprocessor : AssetPostprocessor
{
    private const int RoadLineWidth = 2048;
    private const int RoadLineHeight = 1024;

    private static bool IsRoadLinePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string p = path.Replace('\\', '/').ToLowerInvariant();
        return p.Contains("/textures/ground/spline/")
            || p.Contains("/textures/ground/roadline/")
            || p.Contains("/ground/spline/roadline/")
            || p.Contains("roadline");
    }

    private void OnPreprocessTexture()
    {
        if (!IsRoadLinePath(assetPath))
            return;

        TextureImporter importer = assetImporter as TextureImporter;
        ApplyRoadLineImportSettings(importer);
    }

    private void OnPostprocessTexture(Texture2D texture)
    {
        if (!IsRoadLinePath(assetPath) || texture == null)
            return;

        if (texture.width != RoadLineWidth || texture.height != RoadLineHeight)
        {
            Debug.LogWarning(
                $"[SkyPrison RoadLine] {assetPath} 的尺寸是 {texture.width} x {texture.height}。" +
                $"地面画线 / Spline / RoadLine 标准规格是 {RoadLineWidth} x {RoadLineHeight}。", texture);
        }
    }

    [MenuItem("Tools/Sky Prison/Ground/RoadLine/应用 RoadLine 导入设置到选中贴图")]
    public static void ApplyToSelectedTextures()
    {
        int changed = 0;
        int checkedCount = 0;

        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;

            checkedCount++;
            ApplyRoadLineImportSettings(importer);
            importer.SaveAndReimport();
            changed++;
        }

        Debug.Log($"[SkyPrison RoadLine] 已处理 {changed} 张贴图。选中对象中可处理贴图数：{checkedCount}。");
    }

    [MenuItem("Tools/Sky Prison/Ground/RoadLine/检查选中 RoadLine 尺寸")]
    public static void CheckSelectedRoadLineTextureSize()
    {
        int checkedCount = 0;
        int okCount = 0;

        foreach (Object obj in Selection.objects)
        {
            Texture2D tex = obj as Texture2D;
            string path = AssetDatabase.GetAssetPath(obj);
            if (tex == null && !string.IsNullOrEmpty(path))
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
                continue;

            checkedCount++;
            bool ok = tex.width == RoadLineWidth && tex.height == RoadLineHeight;
            if (ok)
                okCount++;
            else
                Debug.LogWarning($"[SkyPrison RoadLine] {path} 尺寸为 {tex.width} x {tex.height}，标准为 {RoadLineWidth} x {RoadLineHeight}。", tex);
        }

        Debug.Log($"[SkyPrison RoadLine] 尺寸检查完成：{okCount}/{checkedCount} 符合 {RoadLineWidth} x {RoadLineHeight}。");
    }

    public static void ApplyRoadLineImportSettings(TextureImporter importer)
    {
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = RoadLineWidth;
        importer.npotScale = TextureImporterNPOTScale.None;

        TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
        standalone.overridden = true;
        standalone.maxTextureSize = RoadLineWidth;
        standalone.format = TextureImporterFormat.RGBA32;
        standalone.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(standalone);
    }
}
