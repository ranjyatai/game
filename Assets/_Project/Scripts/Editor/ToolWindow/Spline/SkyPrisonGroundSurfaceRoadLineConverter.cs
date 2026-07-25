using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonGroundSurfaceRoadLineConverter
{
    private const string MenuRoot = "Tools/Sky Prison/Ground Surface/";

    [MenuItem(MenuRoot + "将选中的地表材质转换为画线")]
    public static void ConvertSelectedToRoadLine()
    {
        GroundSurfaceMaterialDefinition def = Selection.activeObject as GroundSurfaceMaterialDefinition;
        if (def == null)
        {
            Debug.LogWarning("[GroundSurface RoadLine Converter] 请先在 Project 面板里选中一个 GroundSurfaceMaterialDefinition 资源。注意：地表材质页面左侧高亮不一定等于 Unity Selection。");
            return;
        }

        ConvertOne(def, true);
        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[GroundSurface RoadLine Converter] 已转换为画线：{AssetDatabase.GetAssetPath(def)}");
    }

    [MenuItem(MenuRoot + "修复误建的马路线印章")]
    public static void FixMiscreatedRoadLineStamps()
    {
        string[] guids = AssetDatabase.FindAssets("t:GroundSurfaceMaterialDefinition");
        int fixedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GroundSurfaceMaterialDefinition def = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
            if (def == null)
                continue;

            if (!LooksLikeRoadLineStamp(def))
                continue;

            ConvertOne(def, false);
            EditorUtility.SetDirty(def);
            fixedCount++;
            Debug.Log($"[GroundSurface RoadLine Converter] 修复误建马路线印章：{path}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[GroundSurface RoadLine Converter] 修复完成，共处理 {fixedCount} 个资源。");
    }

    private static bool LooksLikeRoadLineStamp(GroundSurfaceMaterialDefinition def)
    {
        if (def == null)
            return false;

        if (def.textureDistributionMode != GroundSurfaceTextureDistributionMode.StampDecal)
            return false;

        string joined = string.Join(" ", new[]
        {
            def.surfaceId,
            def.displayName,
            def.category,
            def.stampTexture != null ? def.stampTexture.name : "",
            def.stampTexture != null ? AssetDatabase.GetAssetPath(def.stampTexture) : ""
        }).ToLowerInvariant();

        return joined.Contains("roadline")
            || joined.Contains("road_line")
            || joined.Contains("路")
            || joined.Contains("路线")
            || joined.Contains("马路线")
            || joined.Contains("画线")
            || joined.Contains("spline/roadline");
    }

    private static void ConvertOne(GroundSurfaceMaterialDefinition def, bool force)
    {
        if (def == null)
            return;

        Undo.RecordObject(def, "Convert Ground Surface To RoadLine");

        Texture2D sourceTexture = def.splineTexture != null ? def.splineTexture : def.stampTexture;

        def.textureDistributionMode = GroundSurfaceTextureDistributionMode.SplinePattern;
        def.useAsTerrainSurface = false;
        def.category = "样条图案 / 画线";

        if (string.IsNullOrWhiteSpace(def.surfaceId) || force || def.surfaceId.Contains("stamp"))
            def.surfaceId = GenerateRoadLineId(def);

        if (string.IsNullOrWhiteSpace(def.displayName) || force || def.displayName.Contains("印章") || def.displayName.Contains("新地面"))
            def.displayName = "马路线";

        if (sourceTexture != null)
            def.splineTexture = sourceTexture;

        // RoadLine 默认参数：源图 2048x1024，X 为前进方向，Y 为线宽方向。
        if (def.splineWorldWidth <= 0f)
            def.splineWorldWidth = 0.35f;
        if (def.splineSegmentWorldLength <= 0f)
            def.splineSegmentWorldLength = 1.2f;
        if (def.splineStampSpacing <= 0f)
            def.splineStampSpacing = 0.25f;
        if (def.splineOpacity <= 0f)
            def.splineOpacity = 1f;

        def.splineFollowBrushDirection = true;
        def.splineContinuous = true;
        if (def.splineAngleSmoothing <= 0f)
            def.splineAngleSmoothing = 0.45f;

        EditorUtility.SetDirty(def);
    }

    private static string GenerateRoadLineId(GroundSurfaceMaterialDefinition def)
    {
        string baseName = "roadline";

        if (def != null && def.splineTexture != null)
            baseName = def.splineTexture.name;
        else if (def != null && def.stampTexture != null)
            baseName = def.stampTexture.name;
        else if (def != null && !string.IsNullOrWhiteSpace(def.displayName))
            baseName = def.displayName;

        string id = Sanitize(baseName);
        if (string.IsNullOrWhiteSpace(id) || id == "new_ground_stamp")
            id = "roadline";
        if (!id.StartsWith("roadline") && !id.StartsWith("road_line"))
            id = "roadline_" + id;

        return id;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "roadline";

        value = value.Trim().ToLowerInvariant();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                builder.Append(c);
            else if (c == '_' || c == '-' || c == ' ')
                builder.Append('_');
        }

        string result = builder.ToString().Trim('_');
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        return string.IsNullOrWhiteSpace(result) ? "roadline" : result;
    }
}
