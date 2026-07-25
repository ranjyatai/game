using System;
using UnityEngine;

[Serializable]
public class AIScenePickRestoreState
{
    public string windowType;
    public Rect windowRect;

    // 句型编辑器相关
    public int selectedTemplateIndex;
    public int selectedTagIndex;
    public string searchText;
    public string slotId;

    // 这里先保留为 object 风格字符串扩展，
    // 以后你要接更多窗口类型时不会写死在句型编辑器
    public string payloadJson;

    // 当前先给 AI 句型编辑器用
    public LogicSentenceCategory category;
    public LogicTemplateContext editorContext;
    public LogicSentenceInstance workingInstance;
}
