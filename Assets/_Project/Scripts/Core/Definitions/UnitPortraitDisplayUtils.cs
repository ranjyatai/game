using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 立绘显示的统一兜底——就算美术没能把所有表情差分做成完全一致的尺寸，
/// 显示的时候也按等比缩放限制在同一个最大尺寸内，不会把UI布局撑爆/挤变形。
/// 哪个窗口(商店/对话等)要显示立绘，接这一个方法就行，不用各自写一遍适配逻辑。
/// </summary>
public static class UnitPortraitDisplayUtils
{
    /// <summary>把 sprite 等比缩放显示进 maxSize 范围内(不超过，保持原始宽高比)。</summary>
    public static void ApplyPortrait(Image img, Sprite sprite, Vector2 maxSize)
    {
        if (img == null) return;

        img.sprite = sprite;
        img.preserveAspect = true;

        if (sprite == null) return;

        RectTransform rt = img.rectTransform;
        Vector2 native = sprite.rect.size;
        if (native.x <= 0f || native.y <= 0f) return;

        float scale = Mathf.Min(maxSize.x / native.x, maxSize.y / native.y);
        rt.sizeDelta = native * scale;
    }
}
