using UnityEditor;
using UnityEngine;

/// <summary>
/// 物品图标导入优化：Assets/_Project/Icon/Items 下的贴图自动启用 Mipmap + 三线性过滤 + 各向异性，
/// 大幅减少 1024px 大图缩到小格显示时的锯齿/走样。
///
/// - 新导入的图标自动应用（OnPreprocessTexture）。
/// - 已有图标用菜单「Sky Prison/物品/优化物品图标导入设置」一键重导入。
/// </summary>
public class ItemIconImportProcessor : AssetPostprocessor
{
    private static readonly string[] IconFolders =
    {
        "Assets/_Project/Icon/Items",
        "Assets/_Project/Icon/Currencies",
    };

    private static bool IsIconPath(string path)
    {
        foreach (string f in IconFolders)
            if (path.StartsWith(f)) return true;
        return false;
    }

    private void OnPreprocessTexture()
    {
        if (!IsIconPath(assetPath)) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType   = TextureImporterType.Sprite;
        ti.mipmapEnabled = true;              // 关键：缩小显示走 mip，消除锯齿
        ti.filterMode    = FilterMode.Trilinear;
        ti.anisoLevel    = 4;
        ti.wrapMode      = TextureWrapMode.Clamp;
        if (ti.maxTextureSize < 1024) ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.CompressedHQ; // 高质量压缩，进一步减少瑕疵
    }

    [MenuItem("Sky Prison/物品/优化物品图标导入设置")]
    public static void ReimportAllIcons()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", IconFolders);
        int n = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            n++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"[ItemIconImportProcessor] 已优化重导入 {n} 张物品图标（Mipmap+三线性+各向异性）。");
    }
}
