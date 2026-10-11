using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 创建"相机挡视线淡出"验证用的测试墙材质。
///
/// 只是验证阶段的最小实现——纯色不透明方块，接入 SkyPrisonSightlineFade.hlsl
/// 的判定逻辑，先确认效果对不对，再考虑怎么接进真正的建筑材质。
/// </summary>
public static class SkyPrisonSightlineFadeTestWallCreator
{
    private const string LogPrefix = "[SkyPrison SightlineFadeTestWall]";
    private const string ShaderPath = "Assets/_Project/Art/Shaders/Custom/Map/SkyPrisonSightlineFadeTestWall.shader";
    private const string MaterialPath = "Assets/_Project/Art/Materials/System/M_SightlineFadeTestWall.mat";

    [MenuItem("天空囚笼/地图/创建「相机挡视线淡出」测试墙材质", false, 113)]
    public static void CreateOrRepair()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError($"{LogPrefix} 找不到着色器 {ShaderPath}，先确认它已经编译成功。");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            material.name = Path.GetFileNameWithoutExtension(MaterialPath);

            string folder = Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));

            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} 新建材质 {MaterialPath}。摆一个用它的立方体，卡在相机和玩家之间、" +
                      "比玩家更靠近相机，进 Play 走到后面看效果。Fade Radius / Fade Softness 两个参数" +
                      "可以直接在材质面板上调。", material);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} 已存在的材质着色器引用不对，已修正为 {ShaderPath}。", material);
        }
        else
        {
            Debug.Log($"{LogPrefix} 材质已存在，无需改动。", material);
        }

        Selection.activeObject = material;
        EditorGUIUtility.PingObject(material);
    }
}
