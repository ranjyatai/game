using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目里的图标默认都不需要切片（Multiple）——之前发生过图标被意外切碎、又被我擅自
/// 改回整图两次反复的情况，用户明确的诉求是"这个项目根本不需要切片功能，除非特殊需求"。
/// 这里在导入阶段就把 Assets/_Project/Icon 下所有贴图强制设成 Sprite Mode = Single，
/// 新图标不用每次手动改、也不用来回问。
///
/// 真有特殊需求要切片的极少数情况，把那张图放进名字/路径包含 "_Sliced" 的子文件夹或
/// 文件名里即可跳过这条强制规则（导入后可以自己设成 Multiple，不会被这个处理器改回去）。
/// </summary>
public class IconSpriteModeGuardProcessor : AssetPostprocessor
{
    private const string IconRoot = "Assets/_Project/Icon";
    private const string ExceptionMarker = "_Sliced";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(IconRoot)) return;
        if (assetPath.Contains(ExceptionMarker)) return;

        var ti = (TextureImporter)assetImporter;
        if (ti.textureType != TextureImporterType.Sprite)
            ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
    }

    [MenuItem("Sky Prison/图标/把所有图标改回整图（Single）")]
    public static void ForceAllIconsToSingle()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { IconRoot });
        int n = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains(ExceptionMarker)) continue;

            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;

            if (ti.spriteImportMode == SpriteImportMode.Multiple)
            {
                ti.spriteImportMode = SpriteImportMode.Single;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                n++;
            }
        }
        AssetDatabase.Refresh();
        Debug.Log($"[IconSpriteModeGuardProcessor] 已把 {n} 张已经被切片的图标改回整图。");
    }
}
