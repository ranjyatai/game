using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonGroundSurfaceAddRoadLineMenuPatcher
{
    [MenuItem("Tools/Sky Prison/Debug/修复地表材质新建画线入口")]
    public static void Patch()
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
            Debug.LogError("[GroundSurface AddRoadLineMenuPatcher] 没找到 SkyPrisonGroundSurfaceMaterialPage 类型。");
            return;
        }

        string assetPath = null;
        foreach (MonoScript script in Resources.FindObjectsOfTypeAll<MonoScript>())
        {
            if (script == null)
                continue;

            if (script.GetClass() == targetType)
            {
                assetPath = AssetDatabase.GetAssetPath(script);
                break;
            }
        }

        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("[GroundSurface AddRoadLineMenuPatcher] 找到了类型，但没找到对应 MonoScript 路径。可能来自 DLL 或脚本引用异常。");
            return;
        }

        string fullPath = Path.GetFullPath(assetPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[GroundSurface AddRoadLineMenuPatcher] 文件不存在: {fullPath}");
            return;
        }

        string code = File.ReadAllText(fullPath);

        if (code.Contains("new GUIContent(\"画线\")") || code.Contains("new GUIContent(\"新建/画线\")"))
        {
            Debug.Log($"[GroundSurface AddRoadLineMenuPatcher] 已经存在画线入口，无需修改: {assetPath}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath));
            return;
        }

        if (!code.Contains("GroundSurfaceTextureDistributionMode.SplinePattern"))
        {
            Debug.LogError("[GroundSurface AddRoadLineMenuPatcher] 当前工程的 GroundSurfaceTextureDistributionMode 里似乎没有 SplinePattern。请先确认 GroundSurfaceMaterialDefinition.cs 已升级。修改已取消。");
            return;
        }

        string insertLine = "        menu.AddItem(new GUIContent(\"画线\"), false, () => CreateDefinition(GroundSurfaceTextureDistributionMode.SplinePattern));\n";

        // 优先插在“大图”前，得到：地面纹理 / 随机纹理 / 印章 / 画线 / 大图
        string marker = "menu.AddItem(new GUIContent(\"大图\")";
        int markerIndex = code.IndexOf(marker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            // 兼容旧层级菜单，插在“特殊/整张大图”前。
            marker = "menu.AddItem(new GUIContent(\"新建/特殊/整张大图\")";
            markerIndex = code.IndexOf(marker, StringComparison.Ordinal);
            insertLine = "        menu.AddItem(new GUIContent(\"新建/Overlay/画线\"), false, () => CreateDefinition(GroundSurfaceTextureDistributionMode.SplinePattern));\n";
        }

        if (markerIndex < 0)
        {
            Debug.LogError("[GroundSurface AddRoadLineMenuPatcher] 没找到“大图”菜单入口，无法自动插入画线。请把 ShowCreateDefinitionMenu 方法发给我，我给你做精确版。");
            return;
        }

        int lineStart = code.LastIndexOf('\n', markerIndex);
        if (lineStart < 0)
            lineStart = 0;
        else
            lineStart += 1;

        code = code.Insert(lineStart, insertLine);

        File.WriteAllText(fullPath, code);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        Debug.Log($"[GroundSurface AddRoadLineMenuPatcher] 已加入“画线”新建入口: {assetPath}");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath));
    }
}
