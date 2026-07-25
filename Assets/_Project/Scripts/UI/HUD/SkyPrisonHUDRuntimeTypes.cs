using System;
using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    public enum SkyPrisonHUDAreaIdV1
    {
        PlayerStatus = 0,
        QuickSlots = 1,
        TargetInfo = 2,
        BossInfo = 3,
        MissionInfo = 4,
        InteractionPrompt = 5,
        DamageNumbers = 6,
        StatusNotifications = 7,
        Dialogue = 8,
        SystemOverlay = 9,
        Debug = 10,
    }

    public enum SkyPrisonHUDDisplayModeV1
    {
        Combat = 0,
        Exploration = 1,
        Dialogue = 2,
        Terminal = 3,
        Scan = 4,
        Cutscene = 5,
        Hidden = 6,
    }

    [Serializable]
    public struct SkyPrisonHUDLayoutPresetV1
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;

        public static SkyPrisonHUDLayoutPresetV1 LeftBottom(float x, float y, float width, float height)
        {
            return new SkyPrisonHUDLayoutPresetV1
            {
                anchorMin = Vector2.zero,
                anchorMax = Vector2.zero,
                pivot = Vector2.zero,
                anchoredPosition = new Vector2(x, y),
                sizeDelta = new Vector2(width, height),
            };
        }
    }
}
