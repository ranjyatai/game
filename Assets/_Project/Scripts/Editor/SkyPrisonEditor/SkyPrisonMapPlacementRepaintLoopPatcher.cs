#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Safe compatibility stub only.
// Keep this only until Unity forgets the old compile reference; it registers no repaint loop.
public static class SkyPrisonMapPlacementRepaintLoopPatcher
{
    [MenuItem("Tools/Sky Prison/Debug/Repaint Patcher Stub Loaded")]
    private static void StubLoaded()
    {
        Debug.Log("[SkyPrisonMapPlacementRepaintLoopPatcher] Stub only. No repaint loop registered.");
    }
}
#endif
