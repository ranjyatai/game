using UnityEngine;

public interface IOcclusionStateReceiver
{
    void SetOccludedBy(MonoBehaviour occluder, bool isOccluded);
}