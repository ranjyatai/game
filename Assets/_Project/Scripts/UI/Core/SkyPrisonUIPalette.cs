using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 系统 UI 配色。规定：白色 + 冷绿强调色。
    /// 强调元素（货币、选中辉光等）统一用 ColdGreen；常规文字/线框用 White。
    /// </summary>
    public static class SkyPrisonUIPalette
    {
        public static readonly Color White = Color.white;

        /// <summary>系统强调色：冷绿（略微提白，更柔的薄荷绿）。</summary>
        public static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);

        /// <summary>"数值越高越差"这类亏损属性（负暴击率/负暴击伤害等）专用的暖红色——
        /// G/B通道压得比R低不少，避免被Bloom冲淡后看起来发白/像是跟ColdGreen一样的正面强调色。</summary>
        public static readonly Color WarmRed = new Color(0.94f, 0.48f, 0.58f, 1f);

        /// <summary>物品品级配色（商店/背包等展示品级用）——普通白，往上依次绿/蓝/紫/橙金。</summary>
        public static Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon:  return new Color(0.40f, 0.85f, 0.45f, 1f);
                case ItemRarity.Rare:      return new Color(0.35f, 0.65f, 0.95f, 1f);
                case ItemRarity.Epic:      return new Color(0.72f, 0.45f, 0.95f, 1f);
                case ItemRarity.Legendary: return new Color(0.98f, 0.70f, 0.25f, 1f);
                default:                   return Color.white;
            }
        }
    }
}
