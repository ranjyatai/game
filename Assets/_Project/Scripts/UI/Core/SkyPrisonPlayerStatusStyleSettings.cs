using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Player status HUD local style settings stored directly on PlayerStatusArea.
    /// This is not a profile source. The workbench edits it and runtime HUD may read it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonPlayerStatusStyleSettings_V1 : MonoBehaviour
    {
        [Header("Load Status Text")]
        public string lightStatusText = "LIGHT";
        public string normalStatusText = "HEAVY";
        public string overloadStatusText = "OVERWEIGHT";

        [Header("Load Status Colors")]
        public Color lightStatusColor = new Color(0.64f, 1.00f, 0.78f, 0.92f);
        public Color normalStatusColor = new Color(0.98f, 0.98f, 0.70f, 0.92f);
        public Color overloadStatusColor = new Color(1.00f, 0.38f, 0.32f, 0.95f);

        [Header("Thresholds")]
        [Range(0f, 100f)] public float lightThreshold = 30f;
        [Range(0f, 150f)] public float overloadThreshold = 90f;
    }
}
