#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Safe compatibility stub only.
// Keep this only until Unity forgets the old compile reference; it registers no repaint loop.
public static class SkyPrisonEditorRepaintLoopScanner
{
    [MenuItem("Tools/Sky Prison/Debug/Repaint Scanner Stub Loaded")]
    private static void StubLoaded()
    {
        Debug.Log("[SkyPrisonEditorRepaintLoopScanner] Stub only. No repaint loop registered.");
    }
}
#endif
