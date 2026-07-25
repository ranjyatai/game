using System.IO;
using UnityEditor;
using UnityEngine;
using Effekseer;
using Effekseer.Internal;

/// <summary>
/// 一次性修复工具：Effekseer的.efkefc导入时，贴图引用是按"原始文件位置的相对路径"
/// 原样拼接去找的（EffekseerTextureResource.LoadAsset: dirPath + "/" + resPath），
/// 不是按文件名智能搜索——把.efkefc从原项目挪到Unity项目后，这个原始相对路径
/// （比如"../To Annwn/effects/Texture/X.png"）在新位置根本不存在，所有贴图槽位
/// 会全部落空(显示None)，特效等于没有任何贴图数据，播放出来什么都看不见。
///
/// 这里直接按贴图槽位记录的原始文件名（忽略整个路径前缀）去 Effekseer 效果自己
/// 旁边的 Texture 文件夹里找同名贴图重新关联，等价于在Inspector里把每个"None"槽位
/// 手动拖贴图进去，只是批量自动做。
/// </summary>
public static class SkyPrisonEffekseerTextureRelinker
{
    [MenuItem("Assets/Sky Prison/修复Effekseer特效贴图引用（按文件名重新关联）")]
    private static void RelinkSelectedEffect()
    {
        Object selected = Selection.activeObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("没有选中对象", "请先在Project窗口选中一个Effekseer Effect Asset。", "确定");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(selected);
        EffekseerEffectAsset effect = AssetDatabase.LoadAssetAtPath<EffekseerEffectAsset>(assetPath);
        if (effect == null)
        {
            EditorUtility.DisplayDialog("类型不对", "选中的对象不是 Effekseer Effect Asset。", "确定");
            return;
        }

        string effectDir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(effectDir))
        {
            EditorUtility.DisplayDialog("失败", "取不到这个特效资源所在的文件夹路径。", "确定");
            return;
        }

        // 贴图统一放在特效资源同级的 Texture 子文件夹下——跟这次手动整理的
        // Assets/_Project/Resources/VFX/Effekseer/Texture/ 结构一致。
        string textureDir = effectDir + "/Texture";
        if (!AssetDatabase.IsValidFolder(textureDir))
        {
            EditorUtility.DisplayDialog("找不到贴图文件夹", $"没找到「{textureDir}」，请确认贴图已经放在特效同级的 Texture 子文件夹下。", "确定");
            return;
        }

        int relinked = 0;
        int alreadyOk = 0;
        int stillMissing = 0;

        void RelinkArray(EffekseerTextureResource[] resources)
        {
            if (resources == null) return;
            foreach (EffekseerTextureResource res in resources)
            {
                if (res == null) continue;
                if (res.texture != null) { alreadyOk++; continue; }

                string fileName = Path.GetFileName(res.path.Replace('\\', '/'));
                string candidatePath = textureDir + "/" + fileName;
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(candidatePath);

                if (tex != null)
                {
                    res.texture = tex;
                    relinked++;
                }
                else
                {
                    stillMissing++;
                    Debug.LogWarning($"[SkyPrisonEffekseerTextureRelinker] 贴图缺失：「{fileName}」（原引用「{res.path}」），在「{textureDir}」下没找到同名文件。");
                }
            }
        }

        RelinkArray(effect.textureResources);

        EditorUtility.SetDirty(effect);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog(
            "修复完成",
            $"重新关联：{relinked} 个\n已经有效：{alreadyOk} 个\n仍然缺失：{stillMissing} 个（详见Console）",
            "确定");
    }

    [MenuItem("Assets/Sky Prison/修复Effekseer特效贴图引用（按文件名重新关联）", true)]
    private static bool ValidateRelinkSelectedEffect()
    {
        return Selection.activeObject is EffekseerEffectAsset;
    }
}
