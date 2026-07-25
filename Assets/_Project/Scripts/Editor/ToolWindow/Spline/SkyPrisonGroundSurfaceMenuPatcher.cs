using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonGroundSurfaceMenuPatcher
{
    [MenuItem("Tools/Sky Prison/Debug/修复地表材质新建菜单")]
    public static void PatchGroundSurfaceCreateMenu()
    {
        Type targetType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == "SkyPrisonGroundSurfaceMaterialPage");

        if (targetType == null)
        {
            Debug.LogError("[GroundSurface MenuPatcher] 没找到 SkyPrisonGroundSurfaceMaterialPage 类型。当前工程没有编译这个页面类。");
            return;
        }

        MonoScript targetScript = Resources.FindObjectsOfTypeAll<MonoScript>()
            .FirstOrDefault(script => script != null && script.GetClass() == targetType);

        if (targetScript == null)
        {
            Debug.LogError("[GroundSurface MenuPatcher] 找到了类型，但没有找到对应 MonoScript。可能来自 DLL 或脚本引用异常。");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(targetScript);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogError("[GroundSurface MenuPatcher] MonoScript 没有 AssetDatabase 路径。");
            return;
        }

        string fullPath = Path.GetFullPath(assetPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[GroundSurface MenuPatcher] 文件不存在：{fullPath}");
            return;
        }

        string source = File.ReadAllText(fullPath, Encoding.UTF8);
        string original = source;

        source = ReplaceTitle(source);
        source = ReplaceCreateMenuMethod(source);

        if (source == original)
        {
            Debug.LogWarning($"[GroundSurface MenuPatcher] 文件内容没有变化，可能已经是新版。路径：{assetPath}", targetScript);
        }
        else
        {
            File.WriteAllText(fullPath, source, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log($"[GroundSurface MenuPatcher] 已直接修复真实脚本：{assetPath}\n现在标题会显示：地表材质定义工作台（菜单简化版/真实路径已修复）\n新建菜单只保留：地面纹理、随机纹理、印章、大图", targetScript);
        }

        EditorGUIUtility.PingObject(targetScript);
        Selection.activeObject = targetScript;
    }

    private static string ReplaceTitle(string source)
    {
        source = source.Replace(
            "EditorGUILayout.LabelField(\"地表材质定义工作台（菜单简化版）\", EditorStyles.boldLabel);",
            "EditorGUILayout.LabelField(\"地表材质定义工作台（菜单简化版/真实路径已修复）\", EditorStyles.boldLabel);");

        source = source.Replace(
            "EditorGUILayout.LabelField(\"地表材质定义工作台\", EditorStyles.boldLabel);",
            "EditorGUILayout.LabelField(\"地表材质定义工作台（菜单简化版/真实路径已修复）\", EditorStyles.boldLabel);");

        return source;
    }

    private static string ReplaceCreateMenuMethod(string source)
    {
        const string methodName = "private void ShowCreateDefinitionMenu()";
        int methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            Debug.LogError("[GroundSurface MenuPatcher] 找不到 private void ShowCreateDefinitionMenu() 方法。请检查方法名是否被改过。");
            return source;
        }

        int openBrace = source.IndexOf('{', methodIndex);
        if (openBrace < 0)
        {
            Debug.LogError("[GroundSurface MenuPatcher] 找不到 ShowCreateDefinitionMenu 的左大括号。");
            return source;
        }

        int depth = 0;
        int closeBrace = -1;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    closeBrace = i;
                    break;
                }
            }
        }

        if (closeBrace < 0)
        {
            Debug.LogError("[GroundSurface MenuPatcher] 找不到 ShowCreateDefinitionMenu 的右大括号。");
            return source;
        }

        string replacement = @"private void ShowCreateDefinitionMenu()
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent(""地面纹理""), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.SeamlessTiling));

        menu.AddItem(new GUIContent(""随机纹理""), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.RandomScatter));

        menu.AddItem(new GUIContent(""印章""), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.StampDecal));

        menu.AddItem(new GUIContent(""大图""), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.SingleLarge));

        menu.ShowAsContext();
    }";

        return source.Substring(0, methodIndex) + replacement + source.Substring(closeBrace + 1);
    }
}
