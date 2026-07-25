using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Player status HUD local style settings stored directly on PlayerStatusArea.
    /// This is not a profile source. The workbench edits it and runtime HUD may read it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonPlayerStatusLocalStyleSettings : MonoBehaviour
    {
        [Header("Load Status Text")]
        public string lightStatusText = "LIGHT";
        public string normalStatusText = "HEAVY";
        public string overloadStatusText = "OVERWEIGHT";

        [Header("Load Status Colors")]
        public Color lightStatusColor = new Color(0.64f, 1.00f, 0.78f, 0.95f);
        public Color normalStatusColor = new Color(1.00f, 0.94f, 0.55f, 0.95f);
        public Color overloadStatusColor = new Color(1.00f, 0.62f, 0.58f, 0.95f);

        [Header("Thresholds")]
        [Range(0f, 100f)] public float lightThreshold = 30f;
        [Range(0f, 150f)] public float overloadThreshold = 90f;

        [Header("LP Empty Penalty Flash")]
        [Tooltip("LP 为空并处于惩罚状态时，让 LP 槽位底色淡红闪烁。") ]
        public bool enableLpEmptyPenaltyFlash = true;
        [Tooltip("LP 惩罚闪烁使用的淡红色。Alpha 建议较低，避免塑料感。") ]
        public Color lpEmptyPenaltyFlashColor = new Color(1.0f, 0.22f, 0.18f, 0.42f);
        [Tooltip("每秒闪烁次数。") ]
        [Range(0.1f, 8f)] public float lpEmptyPenaltyFlashSpeed = 2.2f;
        [Tooltip("闪烁最低强度。") ]
        [Range(0f, 1f)] public float lpEmptyPenaltyFlashMin = 0.08f;
        [Tooltip("闪烁最高强度。") ]
        [Range(0f, 1f)] public float lpEmptyPenaltyFlashMax = 0.38f;
    }
}
