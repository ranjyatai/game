using UnityEngine;

public enum StatusDurationFillMode
{
    FollowDisplayDirection = 0,
    LeftToRight = 1,
    RightToLeft = 2,
    BottomToTop = 3,
    TopToBottom = 4,
    RadialClockwise = 5,
    RadialCounterClockwise = 6,
    None = 7,
}

public struct StatusViewData
{
    public string key;
    public string displayName;
    public Sprite icon;
    public bool visible;
    public int stacks;

    // 1 = 刚开始，0 = 即将结束
    public float remainingNormalized;

    // 永久状态 / 不显示倒计时
    public bool hideDurationMask;

    // 如果不指定，就跟随 StatusDisplayDefinition.refreshDirection
    public StatusDurationFillMode durationFillMode;

    public static StatusViewData Create(
        string key,
        Sprite icon,
        bool visible = true,
        int stacks = 1,
        float remainingNormalized = 1f,
        bool hideDurationMask = false,
        StatusDurationFillMode durationFillMode = StatusDurationFillMode.FollowDisplayDirection,
        string displayName = "")
    {
        StatusViewData data = new StatusViewData();
        data.key = key;
        data.displayName = displayName;
        data.icon = icon;
        data.visible = visible;
        data.stacks = Mathf.Max(1, stacks);
        data.remainingNormalized = Mathf.Clamp01(remainingNormalized);
        data.hideDurationMask = hideDurationMask;
        data.durationFillMode = durationFillMode;
        return data;
    }
}
