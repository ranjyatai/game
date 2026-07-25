using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Safe diagnostic utility for locating the actual SkyPrisonMapObjectPlacementToolWindow script compiled by Unity.
/// Put this file anywhere under an Editor folder.
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonMapPlacementToolProbe
{
    static SkyPrisonMapPlacementToolProbe()
    {
        Debug.Log("[MapPlacement Probe] Loaded. Menu: Tools/Sky Prison/Debug/定位地图对象放置工具脚本");
    }

    [MenuItem("Tools/Sky Prison/Debug/定位地图对象放置工具脚本")]
    public static void LocateMapPlacementTool()
    {
        Type targetType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == "SkyPrisonMapObjectPlacementToolWindow");

        if (targetType == null)
        {
            Debug.LogError("[MapPlacement Probe] 没找到 SkyPrisonMapObjectPlacementToolWindow 类型。当前工程没有成功编译这个类，或者类名不是这个。");
            return;
        }

        Debug.Log($"[MapPlacement Probe] Type = {targetType.FullName}");
        Debug.Log($"[MapPlacement Probe] Assembly = {targetType.Assembly.FullName}");
        Debug.Log($"[MapPlacement Probe] Module = {targetType.Module.FullyQualifiedName}");

        MonoScript[] scripts = Resources.FindObjectsOfTypeAll<MonoScript>();
        foreach (MonoScript script in scripts)
        {
            if (script == null)
                continue;

            Type cls = script.GetClass();
            if (cls == targetType)
            {
                string path = AssetDatabase.GetAssetPath(script);
                Debug.Log($"[MapPlacement Probe] MonoScript Path = {path}", script);
                EditorGUIUtility.PingObject(script);
                Selection.activeObject = script;
                return;
            }
        }

        Debug.LogWarning("[MapPlacement Probe] 找到了类型，但找不到对应 MonoScript。可能来自 DLL、asmdef 编译产物，或脚本引用异常。");
    }
}
