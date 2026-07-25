using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonGroundSurfaceRenameSplineMenuPatcher
{
    private const string TargetTypeName = "SkyPrisonGroundSurfaceMaterialPage";

    [MenuItem("Tools/Sky Prison/Debug/统一地表材质样条图案命名")]
    public static void Patch()
    {
        string scriptPath = FindTargetScriptPath();
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            Debug.LogError("[GroundSurface RenameSplineMenuPatcher] 没找到 SkyPrisonGroundSurfaceMaterialPage.cs。请确认页面脚本已编译。 ");
            return;
        }

        string fullPath = Path.GetFullPath(scriptPath);
        string text = File.ReadAllText(fullPath);
        string original = text;

        // Menu label: keep user-facing terminology consistent with the data mode name.
        text = text.Replace("new GUIContent(\"画线\")", "new GUIContent(\"样条图案\")");
        text = text.Replace("new GUIContent(\"新建/画线\")", "new GUIContent(\"新建/样条图案\")");

        // Common display labels / category strings introduced by previous patchers.
        text = text.Replace("分类 = \"画线\"", "分类 = \"样条图案\"");
        text = text.Replace("category = \"画线\"", "category = \"样条图案\"");
        text = text.Replace("\"画线 / RoadLine\"", "\"样条图案 / RoadLine\"");
        text = text.Replace("\"画线\"", "\"样条图案\"");

        // Preserve method/menu names in C# identifiers by only replacing string literals above.
        // The broad literal replacement above may also update labels in help text, which is intended.

        if (text == original)
        {
            Debug.LogWarning($"[GroundSurface RenameSplineMenuPatcher] 没有发现需要替换的字符串。目标路径：{scriptPath}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath));
            return;
        }

        File.WriteAllText(fullPath, text);
        AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        Debug.Log($"[GroundSurface RenameSplineMenuPatcher] 已统一命名为“样条图案”：{scriptPath}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath));
    }

    private static string FindTargetScriptPath()
    {
        Type targetType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == TargetTypeName);

        if (targetType == null)
            return null;

        foreach (MonoScript script in Resources.FindObjectsOfTypeAll<MonoScript>())
        {
            if (script == null)
                continue;

            if (script.GetClass() == targetType)
                return AssetDatabase.GetAssetPath(script);
        }

        return null;
    }
}
