using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 批量把选中的材质（或选中文件夹下的所有材质）从官方"Universal Render Pipeline/Lit"
/// 切换成"SkyPrison/Lit With Height Fade"（见该Shader文件顶部注释——属性名跟官方Lit
/// 完全一致，贴图/参数自动保留）。
///
/// 2026-07-17：高度淡出改用抖动裁切（dithered clip）之后，材质**不需要**再改
/// Surface Type/Blend State——保持 Opaque 就行，只换Shader这一步就够了。早期版本
/// 用Alpha混合方案时需要切Transparent，结果材质自身的贴图Alpha通道意外参与混合，
/// 导致贴图"看起来消失"（真实踩过的Bug），这也是弃用Alpha混合方案的原因。
/// </summary>
public static class SkyPrisonHeightFadeMaterialConverter
{
    private const string TargetShaderName = "SkyPrison/Lit With Height Fade";
    private const string SourceShaderName = "Universal Render Pipeline/Lit";

    [MenuItem("Assets/Sky Prison/转换为高度淡出Lit材质（选中的材质/文件夹）")]
    private static void ConvertSelection()
    {
        Shader targetShader = Shader.Find(TargetShaderName);
        if (targetShader == null)
        {
            EditorUtility.DisplayDialog("转换失败", $"找不到Shader「{TargetShaderName}」，确认 SkyPrisonHeightFadeLit.shader 是否存在。", "确定");
            return;
        }

        List<Material> materials = CollectMaterialsFromSelection();
        if (materials.Count == 0)
        {
            EditorUtility.DisplayDialog("没有可转换的材质",
                $"选中的对象里没有找到Shader是「{SourceShaderName}」的材质（已经转换过的、或者用别的Shader的会被跳过）。", "确定");
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (Material m in materials)
            sb.AppendLine("- " + m.name);

        bool confirmed = EditorUtility.DisplayDialog(
            "确认批量转换",
            $"即将把下面 {materials.Count} 个材质的Shader切换成「{TargetShaderName}」（保持Opaque不变）：\n\n{sb}\n" +
            "贴图/参数会自动保留。这个操作会修改材质资源文件，建议先确认版本控制里已提交当前状态，方便不满意时能撤销。",
            "确认转换", "取消");

        if (!confirmed)
            return;

        foreach (Material mat in materials)
        {
            Undo.RecordObject(mat, "Convert to Height Fade Lit");
            mat.shader = targetShader;
            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("完成", $"已转换 {materials.Count} 个材质。", "确定");
    }

    [MenuItem("Assets/Sky Prison/转换为高度淡出Lit材质（选中的材质/文件夹）", true)]
    private static bool ValidateConvertSelection()
    {
        return Selection.objects != null && Selection.objects.Length > 0;
    }

    private static List<Material> CollectMaterialsFromSelection()
    {
        var result = new List<Material>();
        var seen = new HashSet<Material>();

        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                string[] guids = AssetDatabase.FindAssets("t:Material", new[] { path });
                foreach (string guid in guids)
                {
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    TryAdd(mat, result, seen);
                }
            }
            else if (obj is Material mat)
            {
                TryAdd(mat, result, seen);
            }
        }

        return result;
    }

    private static void TryAdd(Material mat, List<Material> result, HashSet<Material> seen)
    {
        if (mat == null || mat.shader == null || seen.Contains(mat))
            return;
        // 只转换还在用官方Lit的——已经转过的/本来就用别的Shader的（比如Spine角色材质）
        // 跳过，避免误伤不该动的材质。
        if (mat.shader.name != SourceShaderName)
            return;

        seen.Add(mat);
        result.Add(mat);
    }
}
