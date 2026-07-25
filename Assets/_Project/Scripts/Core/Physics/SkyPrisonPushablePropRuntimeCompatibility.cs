using UnityEngine;

/// <summary>
/// Temporary compatibility shim for old SkyPrisonUnitPhysicsProbe versions that still call:
/// pushable.ApplyPush(direction, impulse, this)
/// while the runtime's main ApplyPush method expects a Vector3 source position.
/// Keep this file until all old probe scripts/cached versions are gone.
/// </summary>
public static class SkyPrisonPushablePropRuntimeCompatibility
{
    public static void ApplyPush(
        this SkyPrisonPushablePropRuntime pushable,
        Vector3 pushDirection,
        float impulse,
        SkyPrisonUnitPhysicsProbe sourceProbe)
    {
        if (pushable == null)
            return;

        Vector3 sourcePosition = sourceProbe != null
            ? sourceProbe.transform.position
            : pushable.transform.position - pushDirection.normalized;

        pushable.ApplyPush(pushDirection, impulse, sourcePosition);
    }

    public static void Push(
        this SkyPrisonPushablePropRuntime pushable,
        Vector3 pushDirection,
        float impulse,
        SkyPrisonUnitPhysicsProbe sourceProbe)
    {
        if (pushable == null)
            return;

        Vector3 sourcePosition = sourceProbe != null
            ? sourceProbe.transform.position
            : pushable.transform.position - pushDirection.normalized;

        pushable.ApplyPush(pushDirection, impulse, sourcePosition);
    }
}
