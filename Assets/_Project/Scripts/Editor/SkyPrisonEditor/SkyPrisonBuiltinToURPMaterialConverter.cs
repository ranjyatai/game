using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把内置管线的 Standard 材质批量转成 URP/Lit。
///
/// 商店里大量资产包（尤其偏写实的场景包）仍然只提供内置管线材质，导入 URP 项目
/// 后全部渲染成品红。Unity 自带的 Render Pipeline Converter 能做这件事，但它是
/// 整项目扫描式的、不好只针对一个包，而且不同 Unity 版本菜单路径还不一样。
/// 这里做成"选中文件夹 → 转换"，可重复使用，以后每买一个包跑一次。
///
/// 转换是**不可逆**的（直接改材质资产）。跑之前先提交一次 git。
/// </summary>
public static class SkyPrisonBuiltinToURPMaterialConverter
{
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

    [MenuItem("天空囚笼/渲染/把选中文件夹里的内置管线材质转成 URP", false, 197)]
    public static void ConvertSelection()
    {
        string[] guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            EditorUtility.DisplayDialog("转换材质", "先在 Project 窗口里选中一个文件夹或若干材质。", "确定");
            return;
        }

        Shader urpLit = Shader.Find(UrpLitShaderName);
        if (urpLit == null)
        {
            EditorUtility.DisplayDialog("转换材质", $"找不到着色器 {UrpLitShaderName}，项目可能没装 URP。", "确定");
            return;
        }

        var materialPaths = new List<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string mGuid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                    materialPaths.Add(AssetDatabase.GUIDToAssetPath(mGuid));
            }
            else if (path.EndsWith(".mat"))
            {
                materialPaths.Add(path);
            }
        }

        if (materialPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("转换材质", "选中范围内没有找到材质。", "确定");
            return;
        }

        if (!EditorUtility.DisplayDialog("转换材质",
                $"将把 {materialPaths.Count} 个材质转成 URP/Lit。\n\n" +
                "这个操作直接修改材质资产，不可撤销。建议先提交一次 git。\n\n继续？",
                "转换", "取消"))
            return;

        int converted = 0, skipped = 0;
        var log = new System.Text.StringBuilder();

        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < materialPaths.Count; i++)
            {
                string p = materialPaths[i];
                EditorUtility.DisplayProgressBar("转换材质", p, (float)i / materialPaths.Count);

                Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null || m.shader == null) { skipped++; continue; }

                string sn = m.shader.name;
                if (sn != "Standard" && sn != "Standard (Specular setup)" && sn != "Autodesk Interactive")
                {
                    skipped++;
                    continue;
                }

                ConvertOne(m, urpLit);
                converted++;
                log.AppendLine($"  {System.IO.Path.GetFileName(p)}  ({sn} → URP/Lit)");
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[材质转换] 完成：转换 {converted} 个，跳过 {skipped} 个（跳过的通常本来就不是内置管线材质）。\n{log}");
        EditorUtility.DisplayDialog("转换材质", $"完成。\n转换 {converted} 个，跳过 {skipped} 个。\n详情见 Console。", "确定");
    }

    private static void ConvertOne(Material m, Shader urpLit)
    {
        // 先把内置管线那套属性全读出来，换了 shader 之后旧属性就取不到了。
        Texture mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        Vector2 mainScale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
        Vector2 mainOffset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
        Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

        Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
        float bumpScale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;

        Texture metalGloss = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
        float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;

        // Standard 的 _Glossiness 就是光滑度；配了 MetallicGlossMap 时改用
        // _GlossMapScale 当倍率，而 URP 两种情况都用 _Smoothness 这一个属性。
        float glossiness = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
        float glossMapScale = m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale") : 1f;
        float smoothness = metalGloss != null ? glossMapScale : glossiness;

        Texture occlusion = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
        float occlusionStrength = m.HasProperty("_OcclusionStrength") ? m.GetFloat("_OcclusionStrength") : 1f;

        Texture emissionMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
        Color emissionColor = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
        bool hasEmission = m.IsKeywordEnabled("_EMISSION") && emissionColor.maxColorComponent > 0.0001f;

        float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;

        m.shader = urpLit;

        m.SetTexture("_BaseMap", mainTex);
        m.SetTextureScale("_BaseMap", mainScale);
        m.SetTextureOffset("_BaseMap", mainOffset);
        m.SetColor("_BaseColor", color);
        // URP/Lit 内部仍然读 _MainTex/_Color 作为兼容别名，一并写上，避免某些
        // 走旧属性名的脚本/工具取到空值。
        m.SetTexture("_MainTex", mainTex);
        m.SetColor("_Color", color);

        m.SetFloat("_WorkflowMode", 1f); // Metallic

        if (bump != null)
        {
            m.SetTexture("_BumpMap", bump);
            m.SetFloat("_BumpScale", bumpScale);
            m.EnableKeyword("_NORMALMAP");
        }
        else m.DisableKeyword("_NORMALMAP");

        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (metalGloss != null)
        {
            m.SetTexture("_MetallicGlossMap", metalGloss);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else m.DisableKeyword("_METALLICSPECGLOSSMAP");

        if (occlusion != null)
        {
            m.SetTexture("_OcclusionMap", occlusion);
            m.SetFloat("_OcclusionStrength", occlusionStrength);
            m.EnableKeyword("_OCCLUSIONMAP");
        }
        else m.DisableKeyword("_OCCLUSIONMAP");

        if (hasEmission)
        {
            m.SetTexture("_EmissionMap", emissionMap);
            m.SetColor("_EmissionColor", emissionColor);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        m.SetFloat("_Cutoff", cutoff);

        EditorUtility.SetDirty(m);
    }
}
