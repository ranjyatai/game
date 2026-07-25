using UnityEditor;
using UnityEngine;

/// <summary>
/// 武器剪影图（战斗HUD武器切换条用）导入标准。
///
/// 必须用 Sprite Mode: Single + Mesh Type: Full Rect——美术统一按616:340画布画好剪影
/// 的位置/大小，Full Rect 才会把整张贴图（连同所有透明区域）当成Sprite的完整尺寸；
/// 如果被切成 Tight（或者不小心切成了 Multiple 模式手动裁出一小块不透明区域当Sprite
/// 范围），Unity会把透明部分当成"不存在"，UI这边显示出来的比例就会跟画布对不上，
/// 这个坑已经在 Default_hand.png 上踩过一次（Sprite Mode被设成Multiple，手动切出来的
/// 范围只有拳头那一小块，276x134，不是完整的616x340画布）。
///
/// 放在 Assets 任意 Editor 目录下生效，命中路径：
/// Icon/Equipment/WeaponSilhouette 文件夹、Resources/UI/HUD 文件夹（默认空手剪影所在）。
/// </summary>
public sealed class SkyPrisonWeaponSilhouetteTexturePostprocessor : AssetPostprocessor
{
    private static bool IsWeaponSilhouettePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string p = path.Replace('\\', '/').ToLowerInvariant();
        return p.Contains("/icon/equipment/weaponsilhouette/")
            || p.Contains("/resources/ui/hud/default_hand");
    }

    private void OnPreprocessTexture()
    {
        if (!IsWeaponSilhouettePath(assetPath))
            return;

        TextureImporter importer = assetImporter as TextureImporter;
        ApplyWeaponSilhouetteImportSettings(importer);
    }

    [MenuItem("Tools/Sky Prison/UI/应用武器剪影导入设置到选中贴图")]
    public static void ApplyToSelectedTextures()
    {
        int changed = 0;

        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;

            ApplyWeaponSilhouetteImportSettings(importer);
            importer.SaveAndReimport();
            changed++;
        }

        Debug.Log($"[SkyPrison WeaponSilhouette] 已处理 {changed} 张贴图，全部设为 Single + Full Rect。");
    }

    public static void ApplyWeaponSilhouetteImportSettings(TextureImporter importer)
    {
        if (importer == null)
            return;

        importer.textureType         = TextureImporterType.Sprite;
        importer.spriteImportMode    = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled       = false;
        importer.wrapMode            = TextureWrapMode.Clamp;
        importer.filterMode          = FilterMode.Bilinear;

        // TextureImporter 没有直接暴露 spriteMeshType 属性（这个 Unity 版本），
        // 走 TextureImporterSettings 这个官方支持的读写方式设置 Full Rect。
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }
}
