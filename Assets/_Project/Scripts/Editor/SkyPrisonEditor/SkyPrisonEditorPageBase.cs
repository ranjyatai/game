using UnityEngine;

public abstract class SkyPrisonEditorPageBase
{
    protected readonly SkyPrisonEditorContext Context;

    protected SkyPrisonEditorPageBase(SkyPrisonEditorContext context)
    {
        Context = context;
    }

    public abstract string TabName { get; }

    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
    public virtual void Refresh() { }
    public virtual void OnGUILeft() { }
    public virtual void OnGUIRight() { }
    public virtual void HandleGlobalShortcuts() { }
    public virtual void HandlePostGUI() { }

    // 允许主窗口把对象交给某个分页处理，例如：
    // 从单位页跳到 AI 页时，自动选中对应的 AIBehaviorPackage
    public virtual bool TrySelectObject(Object obj) { return false; }
}