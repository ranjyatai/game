using System;
using UnityEngine;

public enum StatusDisplayDirection
{
    LeftToRight = 0,
    RightToLeft = 1,
    BottomToTop = 2,
    TopToBottom = 3,
}

public enum StatusDisplayOverflowMode
{
    Wrap = 0,
    HideExtra = 1,
    RotatePages = 2,
}

[Serializable]
public class StatusDisplayDefinition
{
    public string key = "default_status_display";
    public string displayName = "默认状态显示";
    [TextArea(2, 4)]
    public string note = "";

    public StatusDisplayDirection refreshDirection = StatusDisplayDirection.BottomToTop;
    public StatusDisplayOverflowMode overflowMode = StatusDisplayOverflowMode.Wrap;
    public int maxLines = 1;
    public Vector2 iconSize = new Vector2(18f, 18f);
    public Vector2 spacing = new Vector2(2f, 2f);
    public float rotatePageInterval = 1.25f;

    public bool exposeToUnitUI = true;
    public string uiSemanticKey = "overhead_status";
    public bool isStandard = false;
}
