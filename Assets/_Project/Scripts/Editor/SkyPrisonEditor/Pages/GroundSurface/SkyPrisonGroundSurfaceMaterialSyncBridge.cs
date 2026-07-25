using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地表材质页刷新后，用来同步地图摆放窗口 / 样条几何绘制器的轻量桥接。
/// 不依赖具体窗口的公开 API；优先反射调用常见刷新方法，最后 Repaint。
/// </summary>
public static class SkyPrisonGroundSurfaceMaterialSyncBridge
{
    private static double lastSyncTime;
    private const double MinIntervalSeconds = 0.15d;

    public static void SyncAfterGroundSurfaceRefresh()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - lastSyncTime < MinIntervalSeconds)
            return;

        lastSyncTime = now;

        // 先保存当前 ScriptableObject 修改，避免摆放窗口拿到旧字段。
        AssetDatabase.SaveAssets();

        // 延后一帧，让 SerializedObject.ApplyModifiedProperties / AssetDatabase 写入完成。
        EditorApplication.delayCall += () =>
        {
            SyncWindow("SkyPrisonMapObjectPlacementToolWindow");
            SyncWindow("SkyPrisonGroundOverlaySplineGeometryTool");
            SyncWindow("SkyPrisonGroundSplineGeometryTool");
            SceneView.RepaintAll();
        };
    }

    private static void SyncWindow(string typeName)
    {
        Type type = FindType(typeName);
        if (type == null || !typeof(EditorWindow).IsAssignableFrom(type))
            return;

        UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(type);
        foreach (UnityEngine.Object obj in windows)
        {
            if (obj is not EditorWindow window)
                continue;

            InvokeOptional(window, "SyncSelectedSurfaceMaterialFromAsset");
            InvokeOptional(window, "ReloadSelectedSurfaceMaterial");
            InvokeOptional(window, "RefreshSelectedSurfaceMaterial");
            InvokeOptional(window, "RefreshGroundSurfaceMaterials");
            InvokeOptional(window, "Refresh");

            window.Repaint();
        }
    }

    private static void InvokeOptional(EditorWindow window, string methodName)
    {
        MethodInfo method = window.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        if (method == null)
            return;

        try
        {
            method.Invoke(window, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GroundSurface SyncBridge] {window.GetType().Name}.{methodName} failed: {ex.GetBaseException().Message}");
        }
    }

    private static Type FindType(string typeName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = asm.GetType(typeName);
            if (type != null)
                return type;
        }

        return null;
    }
}
