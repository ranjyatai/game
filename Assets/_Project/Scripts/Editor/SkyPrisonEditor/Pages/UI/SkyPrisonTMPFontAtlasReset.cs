using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 清空 TMP 动态字体图集(Dynamic Atlas)运行时状态——今天这轮测试在同一个字体资产上
/// 塞进了大量之前从没出现过的新字符(各种名字、注音文字来回切换)，把动态图集挤满
/// 触发重新打包；已经生成好网格的文字物体还拿着旧的图集 UV 坐标去采样，显示的就是
/// 图集重排之后别的字符——表现为"看起来像乱码，字数差不多但每个字都不对"。这种
/// 图集状态在编辑器里跑 Play 模式时可能会被写回字体资产本身，不是靠重进 Play
/// 就能自动恢复的，需要显式清一次。
/// </summary>
public static class SkyPrisonTMPFontAtlasReset
{
    private const string FontAssetPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";

    [MenuItem("Tools/Sky Prison/UI/清空对话字体动态图集")]
    public static void Reset()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
        {
            Debug.LogError($"[字体图集清理] 找不到字体资产：{FontAssetPath}");
            return;
        }

        font.ClearFontAssetData(true);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();

        Debug.Log($"[字体图集清理] {font.name} 的动态图集已清空。下次进 Play 会用干净图集重新生成，之前显示错字的文字物体也会跟着自动刷新。");
    }
}
