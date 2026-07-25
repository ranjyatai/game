// 菜单：SkyPrison / Rendering / 修复掉落物 Build 打包
// 1. 把两个 Shader 加入 Always Included Shaders
// 2. 把 LootDropModelLibrary 资产加入 Preloaded Assets（Build 启动时自动加载，无需 Resources 文件夹）
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LootDropShaderInclude
{
    private static readonly string[] k_ShaderNames =
    {
        "Sky Prison/LootDrop Hologram",
        "Hidden/SP/VFXHueShift",
        "Hidden/SP/CharPresenceCapture",
    };

    [MenuItem("SkyPrison/Rendering/修复掉落物 Build 打包 ★")]
    public static void FixBuildIncludes()
    {
        FixAlwaysIncludedShaders();
        FixPreloadedAssets();
        Debug.Log("[LootDropBuildFix] 完成！请重新 Build。");
    }

    private static void FixAlwaysIncludedShaders()
    {
        var graphicsSettings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>(
            "ProjectSettings/GraphicsSettings.asset");
        SerializedObject so = new SerializedObject(graphicsSettings);
        SerializedProperty always = so.FindProperty("m_AlwaysIncludedShaders");
        bool changed = false;

        foreach (string name in k_ShaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) { Debug.LogWarning($"[LootDropBuildFix] 找不到 Shader '{name}'"); continue; }

            bool exists = false;
            for (int i = 0; i < always.arraySize; i++)
                if (always.GetArrayElementAtIndex(i).objectReferenceValue == shader) { exists = true; break; }

            if (!exists)
            {
                always.arraySize++;
                always.GetArrayElementAtIndex(always.arraySize - 1).objectReferenceValue = shader;
                changed = true;
                Debug.Log($"[LootDropBuildFix] Shader 已加入 Always Included：{name}");
            }
        }

        if (changed) { so.ApplyModifiedProperties(); AssetDatabase.SaveAssets(); }
    }

    private static void FixPreloadedAssets()
    {
        // 找到 LootDropModelLibrary（不管在哪个文件夹）
        string[] guids = AssetDatabase.FindAssets("t:LootDropModelLibrary");
        if (guids.Length == 0)
        {
            Debug.LogError("[LootDropBuildFix] 找不到 LootDropModelLibrary 资产！");
            return;
        }

        var lib = AssetDatabase.LoadAssetAtPath<LootDropModelLibrary>(
            AssetDatabase.GUIDToAssetPath(guids[0]));

        // 同时处理两个 Shader（如果已有材质引用则跳过）
        var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());

        bool changed = false;
        if (!preloaded.Contains(lib))
        {
            preloaded.Add(lib);
            changed = true;
            Debug.Log($"[LootDropBuildFix] LootDropModelLibrary 已加入 Preloaded Assets：{AssetDatabase.GUIDToAssetPath(guids[0])}");
        }
        else
        {
            Debug.Log("[LootDropBuildFix] LootDropModelLibrary 已在 Preloaded Assets 中");
        }

        if (changed)
        {
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            AssetDatabase.SaveAssets();
        }
    }
}
