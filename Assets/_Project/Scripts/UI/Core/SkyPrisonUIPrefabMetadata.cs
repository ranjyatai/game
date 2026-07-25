using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    public enum SkyPrisonUIPrefabKindV1
    {
        HUD = 0,
        Window = 1,
        Popup = 2,
        Overlay = 3,
        Tooltip = 4,
        Debug = 5,
    }

    public enum SkyPrisonUIInputModeV1
    {
        None = 0,
        Gameplay = 1,
        UI = 2,
        Dialogue = 3,
        Cutscene = 4,
    }

    /// <summary>
    /// Metadata stored on the root of every UI prefab edited by the Sky Prison UI Workbench.
    /// This component is data only. It does not build UI, install UI, or mutate layout.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonUIPrefabMetadata_V1 : MonoBehaviour
    {
        [Header("Identity")]
        public string uiId = "battle_hud";
        public string displayName = "Battle HUD";
        public SkyPrisonUIPrefabKindV1 kind = SkyPrisonUIPrefabKindV1.HUD;

        [Header("Runtime Visibility")]
        public bool defaultVisible = true;
        public bool allowMultipleInstances = false;
        public int sortingOrder = 1000;

        [Header("Input / Cursor")]
        public bool blocksRaycasts = false;
        public bool lockGameplayInput = false;
        public bool showMouseCursor = false;
        public SkyPrisonUIInputModeV1 inputModeWhenOpen = SkyPrisonUIInputModeV1.Gameplay;

        [Header("Close Rules")]
        public bool closeOnEscape = false;
        public bool hideHudBehindThisWindow = false;

        [Header("Workbench")]
        public Vector2 referenceResolution = new Vector2(3840f, 2160f);
        public string note = "";
    }
}
