using System;
using UnityEngine;

/// <summary>
/// V6 - 2026-06-11 - Legacy cleaner only.
///
/// The old implementation created a fake chromatic aberration overlay by duplicating
/// PlayerStatusArea into Red/Cyan shifted UI trees:
///     __ChromaticOffsetOverlay_PlayerStatusArea
///     RedChannel / CyanChannel
///     PlayerStatusArea__Red / PlayerStatusArea__Cyan
///
/// That route is now deprecated.
/// Real chromatic aberration must be handled by HUD materials / shaders, not by duplicated UI objects.
///
/// This component is intentionally kept with the same class name so existing prefabs do not break.
/// It no longer creates any overlay. It only removes legacy overlay objects if they already exist.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(11320)]
public sealed class SkyPrisonHUDChromaticOffsetOverlayPresenter : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V6 - 2026-06-11 - legacy overlay cleaner only";

    [Header("Source - Kept For Prefab Compatibility")]
    [SerializeField] private RectTransform hudRoot;
    [SerializeField] private string[] sourceModuleNames = new string[] { "PlayerStatusArea" };

    [Header("Deprecated Chromatic Overlay")]
    [SerializeField] private bool overlayEnabled = false;
    [SerializeField, Range(0f, 2f)] private float intensity = 0f;
    [SerializeField, Range(0f, 1f)] private float softness = 0f;

    [Header("Cleanup")]
    [SerializeField] private bool cleanOnEnable = true;
    [SerializeField] private bool cleanEveryLateUpdate = true;
    [SerializeField] private bool includeInactiveObjects = true;

    [Header("Runtime Diagnostic - Read Only")]
    [SerializeField] private string runtimeDiagnostic = "Legacy overlay disabled";
    [SerializeField] private int runtimeRemovedObjectCount;
    [SerializeField] private bool runtimeOverlayActive;
    [SerializeField] private RectTransform runtimeOverlayRoot;

    public string RuntimeDiagnostic => runtimeDiagnostic;
    public RectTransform RuntimeOverlayRoot => runtimeOverlayRoot;

    /// <summary>
    /// Kept for existing callers.
    /// V6 intentionally ignores chromatic settings and only cleans the legacy fake overlay.
    /// </summary>
    public void Configure(Transform newHudRoot, string[] moduleNames, bool enabled, float newIntensity, float newSoftness)
    {
        RectTransform newRoot = newHudRoot as RectTransform;
        if (newRoot != null)
            hudRoot = newRoot;

        if (moduleNames != null && moduleNames.Length > 0)
            sourceModuleNames = (string[])moduleNames.Clone();

        overlayEnabled = false;
        intensity = 0f;
        softness = 0f;
        runtimeOverlayActive = false;
        runtimeOverlayRoot = null;

        ClearRuntimeOverlay();
    }

    private void Awake()
    {
        overlayEnabled = false;
        intensity = 0f;
        softness = 0f;

        if (hudRoot == null)
            hudRoot = transform as RectTransform;
    }

    private void OnEnable()
    {
        overlayEnabled = false;
        runtimeOverlayActive = false;

        if (cleanOnEnable)
            ClearRuntimeOverlay();
    }

    private void Start()
    {
        ClearRuntimeOverlay();
    }

    private void OnDisable()
    {
        runtimeOverlayActive = false;
    }

    private void LateUpdate()
    {
        overlayEnabled = false;
        runtimeOverlayActive = false;

        if (cleanEveryLateUpdate)
            ClearRuntimeOverlay();
    }

    [ContextMenu("Chromatic Offset Overlay/Rebuild Now - Deprecated")]
    public void RebuildNow()
    {
        ClearRuntimeOverlay();
    }

    [ContextMenu("Chromatic Offset Overlay/Clear Runtime Overlay")]
    public void ClearRuntimeOverlay()
    {
        int removed = 0;

        Transform root = hudRoot != null ? hudRoot : transform;
        removed += RemoveLegacyOverlayObjectsUnder(root);

        // Also clean under this component's own transform in case hudRoot was not assigned
        // or the old overlay was created as a sibling/child of the component holder.
        if (transform != root)
            removed += RemoveLegacyOverlayObjectsUnder(transform);

        runtimeRemovedObjectCount += removed;
        runtimeOverlayRoot = null;
        runtimeOverlayActive = false;

        runtimeDiagnostic = removed > 0
            ? $"Legacy chromatic offset overlay removed. Removed {removed} object(s)."
            : "Legacy chromatic offset overlay disabled. No legacy overlay found.";
    }

    private int RemoveLegacyOverlayObjectsUnder(Transform root)
    {
        if (root == null)
            return 0;

        int removed = 0;

        // Iterate backwards because we may destroy children.
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            if (IsLegacyOverlayObject(child.name))
            {
                DestroySafe(child.gameObject);
                removed++;
                continue;
            }

            removed += RemoveLegacyOverlayObjectsUnder(child);
        }

        return removed;
    }

    private static bool IsLegacyOverlayObject(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        return
            objectName.StartsWith("__ChromaticOffsetOverlay", StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals("RedChannel", StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals("CyanChannel", StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals("BlueChannel", StringComparison.OrdinalIgnoreCase) ||
            objectName.EndsWith("__Red", StringComparison.OrdinalIgnoreCase) ||
            objectName.EndsWith("__Cyan", StringComparison.OrdinalIgnoreCase) ||
            objectName.EndsWith("__Blue", StringComparison.OrdinalIgnoreCase);
    }

    private static void DestroySafe(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
