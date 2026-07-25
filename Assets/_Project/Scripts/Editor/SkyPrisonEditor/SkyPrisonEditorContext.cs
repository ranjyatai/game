using UnityEditor;
using UnityEngine;

public class SkyPrisonEditorContext
{
    public const float ToolbarHeight = 22f;
    public const float TopTabHeight = 0f;
    public const float SplitterWidth = 4f;
    public const float MinLeftWidth = 220f;
    public const float MaxLeftWidth = 520f;
    public const float TreeRowHeight = 24f;

    public const float DrawerWidth = 220f;
    public const float DrawerButtonSize = 28f;
    public const float DrawerButtonMargin = 10f;

    public readonly EditorWindow Window;

    public float LeftPanelWidth = 300f;
    public bool DraggingSplitter = false;

    public Vector2 LeftScroll;
    public Vector2 RightScroll;

    public SkyPrisonEditorContext(EditorWindow window)
    {
        Window = window;
    }

    public void Repaint()
    {
        Window?.Repaint();
    }
}
