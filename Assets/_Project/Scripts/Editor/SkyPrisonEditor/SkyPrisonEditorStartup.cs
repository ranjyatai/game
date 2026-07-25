using UnityEditor;

/// <summary>
/// 编译/域重载后自动清理静态缓存，确保模板库变更立即生效。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonEditorStartup
{
    static SkyPrisonEditorStartup()
    {
        AILogicSentenceTemplateLibrary.InvalidateCache();
    }
}
