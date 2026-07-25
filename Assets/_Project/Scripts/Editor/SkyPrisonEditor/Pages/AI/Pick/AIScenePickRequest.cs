using System;

[Serializable]
public class AIScenePickRequest
{
    public string requestId;
    public AIScenePickKind pickKind;

    // 谁发起的
    public string sourceWindowId;
    public string slotId;
    public string slotDisplayName;

    // Scene 中显示的提示
    public string title;
    public string hint;

    // 恢复编辑器时用
    public AIScenePickRestoreState restoreState;

    // 扩展约束，先用字符串占位，后面可以升级成更强类型
    public string constraintsJson;
}
