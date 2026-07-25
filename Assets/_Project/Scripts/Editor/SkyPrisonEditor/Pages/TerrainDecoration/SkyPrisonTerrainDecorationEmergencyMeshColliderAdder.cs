using UnityEditor;
using UnityEngine;

/// <summary>
/// Deprecated no-op.
/// Emergency MeshCollider 修复器不再参与主流程，避免绕过 Definition / Builder 生成契约。
/// 旧实例处理以后统一走 Migration / Validator。
/// </summary>
public static class SkyPrisonTerrainDecorationEmergencyMeshColliderAdder
{
    private const string MenuPath = "Tools/Sky Prison/Diagnostics/Terrain Decoration/旧 Emergency MeshCollider 修复器已禁用";

    [MenuItem(MenuPath, true)]
    private static bool ValidateForceAddToSelection()
    {
        return true;
    }

    [MenuItem(MenuPath)]
    public static void ForceAddToSelection()
    {
        Debug.LogWarning("[TD_EMERGENCY_MESH_COLLIDER_DEPRECATED] 旧 Emergency MeshCollider 修复器已禁用。请先用新 Builder 生成实例，旧数据以后通过 Migration 处理。", Selection.activeObject);
    }

    public static int ForceAddMeshColliders(GameObject rootOrProxy, bool useUndo)
    {
        return 0;
    }
}
