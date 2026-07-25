using System.Collections.Generic;
using UnityEditor;

public static class AIModalWindowStack
{
    private static readonly List<EditorWindow> stack = new List<EditorWindow>();

    public static void Register(EditorWindow window)
    {
        if (window == null)
            return;

        stack.Remove(window);
        stack.Add(window);
    }

    public static void Unregister(EditorWindow window)
    {
        if (window == null)
            return;

        stack.Remove(window);
    }

    public static bool IsTop(EditorWindow window)
    {
        if (window == null)
            return false;

        CleanupNulls();
        if (stack.Count == 0)
            return false;

        return stack[stack.Count - 1] == window;
    }

    public static void FocusTop()
    {
        if (AIScenePickFocusGuard.ShouldSuppressFocusSteal())
            return;

        CleanupNulls();
        if (stack.Count == 0)
            return;

        EditorWindow top = stack[stack.Count - 1];
        if (top != null)
            top.Focus();
    }

    private static void CleanupNulls()
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i] == null)
                stack.RemoveAt(i);
        }
    }
}
