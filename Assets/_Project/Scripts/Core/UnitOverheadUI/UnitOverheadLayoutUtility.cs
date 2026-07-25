using System.Collections.Generic;
using UnityEngine;

public static class UnitOverheadLayoutUtility
{
    public struct LayoutResult
    {
        public Vector2 barRootPosition;
        public Vector2 barSize;
        public Vector2 statusRootPosition;
        public Vector2 statusAreaPosition;
        public Vector2 statusAreaSize;
        public Vector2 statusContentPosition;
        public Vector2 statusContentSize;
    }

    public static LayoutResult BuildLayout(OverheadBarStyleAsset style)
    {
        LayoutResult result = new LayoutResult();

        Vector2 barSize = style != null ? style.barSize : new Vector2(150f, 14f);
        Vector2 hpBarOffset = style != null ? style.hpBarOffset : Vector2.zero;

        Vector2 statusAreaOffset = style != null ? style.statusAreaOffset : new Vector2(0f, 3f);
        Vector2 statusAreaSize = style != null ? style.statusAreaSize : new Vector2(120f, 28f);

        result.barRootPosition = new Vector2(hpBarOffset.x, -hpBarOffset.y);
        result.barSize = barSize;

        result.statusRootPosition = result.barRootPosition;

        float downOffset = (barSize.y * 0.5f) + (statusAreaSize.y * 0.5f) - statusAreaOffset.y;
        result.statusAreaPosition = new Vector2(statusAreaOffset.x, -downOffset);
        result.statusAreaSize = statusAreaSize;

        result.statusContentPosition = Vector2.zero;
        result.statusContentSize = statusAreaSize;

        return result;
    }

    public static Vector2 ResolveStatusIconSize(OverheadBarStyleAsset style)
    {
        if (style != null && style.statusIconSize.x > 0f && style.statusIconSize.y > 0f)
            return style.statusIconSize;

        return new Vector2(22f, 22f);
    }

    public static Vector2 ResolveStatusSpacing(OverheadBarStyleAsset style)
    {
        if (style != null)
            return style.statusIconSpacing;

        return new Vector2(2f, 2f);
    }

    public static TextAnchor ResolveStatusAlignment(OverheadBarStyleAsset style)
    {
        return style != null ? style.statusAreaAlignment : TextAnchor.MiddleCenter;
    }

    public static int ResolveStatusMaxLines(StatusDisplayDefinition displayDefinition)
    {
        return displayDefinition != null ? Mathf.Max(1, displayDefinition.maxLines) : 1;
    }

    public static StatusDisplayDirection ResolveStatusDirection(StatusDisplayDefinition displayDefinition)
    {
        return displayDefinition != null ? displayDefinition.refreshDirection : StatusDisplayDirection.LeftToRight;
    }

    public static void LayoutStatusIcons(
        IReadOnlyList<RectTransform> icons,
        Vector2 areaSize,
        Vector2 iconSize,
        Vector2 spacing,
        int maxLines,
        StatusDisplayDirection direction,
        TextAnchor alignment)
    {
        if (icons == null || icons.Count == 0)
            return;

        int count = icons.Count;
        maxLines = Mathf.Max(1, maxLines);

        int rows;
        int cols;

        switch (direction)
        {
            case StatusDisplayDirection.TopToBottom:
            case StatusDisplayDirection.BottomToTop:
                rows = Mathf.Min(maxLines, count);
                cols = Mathf.CeilToInt(count / (float)rows);
                break;

            default:
                cols = Mathf.CeilToInt(count / (float)maxLines);
                rows = Mathf.Min(maxLines, count);
                break;
        }

        float blockWidth = cols * iconSize.x + Mathf.Max(0, cols - 1) * spacing.x;
        float blockHeight = rows * iconSize.y + Mathf.Max(0, rows - 1) * spacing.y;

        Vector2 origin = ResolveBlockOriginFromCenter(areaSize, new Vector2(blockWidth, blockHeight), alignment);

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = icons[i];
            if (rt == null)
                continue;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = iconSize;

            int row;
            int col;

            switch (direction)
            {
                case StatusDisplayDirection.LeftToRight:
                    row = i / cols;
                    col = i % cols;
                    break;

                case StatusDisplayDirection.RightToLeft:
                    row = i / cols;
                    col = cols - 1 - (i % cols);
                    break;

                case StatusDisplayDirection.TopToBottom:
                    col = i / rows;
                    row = i % rows;
                    break;

                case StatusDisplayDirection.BottomToTop:
                    col = i / rows;
                    row = rows - 1 - (i % rows);
                    break;

                default:
                    row = i / cols;
                    col = i % cols;
                    break;
            }

            float x = origin.x + col * (iconSize.x + spacing.x) + iconSize.x * 0.5f;
            float y = origin.y - row * (iconSize.y + spacing.y) - iconSize.y * 0.5f;

            rt.anchoredPosition = new Vector2(x, y);
        }
    }

    private static Vector2 ResolveBlockOriginFromCenter(Vector2 areaSize, Vector2 blockSize, TextAnchor alignment)
    {
        float left = -areaSize.x * 0.5f;
        float right = areaSize.x * 0.5f;
        float top = areaSize.y * 0.5f;
        float bottom = -areaSize.y * 0.5f;

        float x;
        float y;

        switch (alignment)
        {
            case TextAnchor.UpperLeft:
                x = left;
                y = top;
                break;
            case TextAnchor.UpperCenter:
                x = -blockSize.x * 0.5f;
                y = top;
                break;
            case TextAnchor.UpperRight:
                x = right - blockSize.x;
                y = top;
                break;
            case TextAnchor.MiddleLeft:
                x = left;
                y = blockSize.y * 0.5f;
                break;
            case TextAnchor.MiddleCenter:
                x = -blockSize.x * 0.5f;
                y = blockSize.y * 0.5f;
                break;
            case TextAnchor.MiddleRight:
                x = right - blockSize.x;
                y = blockSize.y * 0.5f;
                break;
            case TextAnchor.LowerLeft:
                x = left;
                y = bottom + blockSize.y;
                break;
            case TextAnchor.LowerCenter:
                x = -blockSize.x * 0.5f;
                y = bottom + blockSize.y;
                break;
            case TextAnchor.LowerRight:
                x = right - blockSize.x;
                y = bottom + blockSize.y;
                break;
            default:
                x = -blockSize.x * 0.5f;
                y = blockSize.y * 0.5f;
                break;
        }

        return new Vector2(x, y);
    }
}