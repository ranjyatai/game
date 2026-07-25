using UnityEditor;
using UnityEngine;

/// <summary>
/// Deprecated legacy terrain-decoration placement window.
///
/// 旧版本文件曾包含完整放置、地面刷、ApplyDefinition、矫正、SceneGUI 等逻辑。
/// 新规则下它不能再参与真实放置流程，避免和 SkyPrisonMapObjectPlacementToolWindow 并行污染。
///
/// 真实入口：SkyPrisonMapObjectPlacementToolWindow
/// </summary>
public class SkyPrisonTerrainDecorationPlacementPage : EditorWindow
{
    private const string MenuPath = "Tools/Sky Prison/Map/模块/地形装饰物/旧入口-已废弃";

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        SkyPrisonTerrainDecorationPlacementPage window = GetWindow<SkyPrisonTerrainDecorationPlacementPage>("地形装饰物-旧入口");
        window.minSize = new Vector2(360f, 160f);
        window.Show();
    }

    public static void OpenWindowWithDefinition(TerrainDecorationDefinition definition)
    {
        SkyPrisonMapObjectPlacementToolWindow.OpenWindowWithDefinition(definition);
    }

    public static void OpenWindowWithDefinitionAndEnterPlacement(TerrainDecorationDefinition definition)
    {
        SkyPrisonMapObjectPlacementToolWindow.OpenWindowWithDefinitionAndEnterPlacement(definition);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "此旧入口已废弃，不再参与地形装饰物放置、ApplyDefinition、矫正、SceneGUI 或任何后台修复。\n\n" +
            "请使用新的地图对象放置工具：SkyPrisonMapObjectPlacementToolWindow。",
            MessageType.Warning);

        GUILayout.Space(8f);
        if (GUILayout.Button("打开地图对象放置工具", GUILayout.Height(28f)))
            SkyPrisonMapObjectPlacementToolWindow.OpenWindow();
    }
}
