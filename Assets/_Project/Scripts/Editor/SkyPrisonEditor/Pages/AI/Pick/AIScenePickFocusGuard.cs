using UnityEditor;

public static class AIScenePickFocusGuard
{
    private static bool isPreparingScenePick;
    private static bool isScenePicking;

    public static bool IsPreparingScenePick => isPreparingScenePick;
    public static bool IsScenePicking => isScenePicking;

    public static void BeginPreparing()
    {
        isPreparingScenePick = true;
    }

    public static void EnterPicking()
    {
        isPreparingScenePick = false;
        isScenePicking = true;
    }

    public static void EndAll()
    {
        isPreparingScenePick = false;
        isScenePicking = false;
    }

    public static bool ShouldSuppressFocusSteal()
    {
        return isPreparingScenePick || isScenePicking;
    }

    public static void EndPreparingNextEditorTick()
    {
        EditorApplication.delayCall += () =>
        {
            isPreparingScenePick = false;
        };
    }
}
