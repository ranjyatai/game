using System;
using UnityEngine;

/// <summary>
/// Experimental component for 2.5D screen-space occlusion sampling.
/// It does NOT change materials. It only tests whether several semantic character points
/// are blocked by Terrain / world occluders from the camera view.
///
/// Intended first pass:
/// - Attach to Player.
/// - Assign Head / Body / Leg sample transforms, or let it use local fallback offsets.
/// - Observe debug rays and Inspector values.
/// </summary>
[DisallowMultipleComponent]
public class SpinePartOcclusionSampler : MonoBehaviour
{
    [Serializable]
    public class OcclusionPartSample
    {
        public string key = "body";
        public Transform point;
        public Vector3 fallbackLocalOffset = new Vector3(0f, 1f, 0f);

        [Tooltip("Positive means this part is treated as farther from the camera. Negative means closer to camera.")]
        public float viewDepthOffset = 0f;

        [NonSerialized] public bool occluded;
        [NonSerialized] public Collider lastCollider;
        [NonSerialized] public Vector3 worldPoint;
        [NonSerialized] public Vector3 hitPoint;
        [NonSerialized] public float viewDepth;
        [NonSerialized] public float blockerViewDepth;
    }

    [Header("Camera / Target")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Transform targetRoot;

    [Header("Occluders")]
    [SerializeField] private LayerMask occluderMask = ~0;
    [SerializeField] private bool terrainOnly = true;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Sampling")]
    [Tooltip("How far behind the camera-side start point to cast from. Larger values are safer for orthographic camera.")]
    [SerializeField] private float castBackDistance = 100f;

    [Tooltip("Extra distance beyond sample point. Useful when sample point is very close to the occluder.")]
    [SerializeField] private float castPastPointDistance = 2f;

    [Tooltip("A positive depth bias makes the character harder to mark as occluded.")]
    [SerializeField] private float depthBias = 0.02f;

    [Tooltip("Ignore hits very close to the sample point to reduce self/contact noise.")]
    [SerializeField] private float nearSampleIgnoreDistance = 0.02f;

    [Header("Part Samples")]
    [SerializeField] private OcclusionPartSample[] samples =
    {
        new OcclusionPartSample { key = "head", fallbackLocalOffset = new Vector3(0f, 1.55f, 0f), viewDepthOffset = 0f },
        new OcclusionPartSample { key = "body", fallbackLocalOffset = new Vector3(0f, 0.95f, 0f), viewDepthOffset = 0f },
        new OcclusionPartSample { key = "leg",  fallbackLocalOffset = new Vector3(0f, 0.35f, 0f), viewDepthOffset = 0f },
    };

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;
    [SerializeField] private bool logOnChange = false;

    public OcclusionPartSample[] Samples => samples;
    public bool AnyOccluded => lastAnyOccluded;
    public int OccludedPartCount { get; private set; }
    public string LastSummary => BuildSummary();

    private bool lastAnyOccluded;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    private void Reset()
    {
        targetRoot = transform;
        viewCamera = Camera.main;
    }

    private void Awake()
    {
        if (targetRoot == null)
            targetRoot = transform;

        if (viewCamera == null)
            viewCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (viewCamera == null || targetRoot == null || samples == null)
            return;

        Vector3 camForward = viewCamera.transform.forward;
        bool anyOccluded = false;
        int occludedCount = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            OcclusionPartSample sample = samples[i];
            if (sample == null)
                continue;

            Vector3 sampleWorld = sample.point != null
                ? sample.point.position
                : targetRoot.TransformPoint(sample.fallbackLocalOffset);

            sample.worldPoint = sampleWorld;
            sample.lastCollider = null;
            sample.hitPoint = Vector3.zero;
            sample.occluded = false;

            // View depth along camera forward. Larger dot value means farther along camera forward.
            // We compare using the same projection for both sample and occluder hit.
            float sampleDepth = Vector3.Dot(sampleWorld, camForward) + sample.viewDepthOffset;
            sample.viewDepth = sampleDepth;

            Vector3 rayOrigin = sampleWorld - camForward * castBackDistance;
            float rayDistance = castBackDistance + castPastPointDistance;
            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                camForward,
                hitBuffer,
                rayDistance,
                occluderMask,
                triggerInteraction);

            float bestBlockerDepth = float.NegativeInfinity;
            RaycastHit bestHit = default;
            bool found = false;

            for (int h = 0; h < hitCount; h++)
            {
                RaycastHit hit = hitBuffer[h];
                Collider col = hit.collider;
                if (col == null)
                    continue;

                if (terrainOnly && col.GetComponent<TerrainCollider>() == null)
                    continue;

                float hitDepth = Vector3.Dot(hit.point, camForward);

                // Hit must be closer to camera than the part sample.
                // Since ray goes camera-side -> sample-side, valid occluder lies before sample along camForward.
                if (hitDepth >= sampleDepth - depthBias)
                    continue;

                float distToSample = Vector3.Distance(hit.point, sampleWorld);
                if (distToSample <= nearSampleIgnoreDistance)
                    continue;

                // Choose the hit closest to the character sample among valid blockers.
                if (!found || hitDepth > bestBlockerDepth)
                {
                    found = true;
                    bestBlockerDepth = hitDepth;
                    bestHit = hit;
                }
            }

            if (found)
            {
                sample.occluded = true;
                sample.lastCollider = bestHit.collider;
                sample.hitPoint = bestHit.point;
                sample.blockerViewDepth = bestBlockerDepth;
                anyOccluded = true;
                occludedCount++;
            }
            else
            {
                sample.blockerViewDepth = 0f;
            }

            if (drawDebug)
            {
                Color c = sample.occluded ? Color.red : Color.green;
                Debug.DrawLine(rayOrigin, sampleWorld, c);
                Debug.DrawRay(sampleWorld, Vector3.up * 0.12f, c);

                if (sample.occluded)
                {
                    Debug.DrawLine(sample.hitPoint, sampleWorld, Color.magenta);
                    Debug.DrawRay(sample.hitPoint, Vector3.up * 0.18f, Color.yellow);
                }
            }
        }

        if (logOnChange && anyOccluded != lastAnyOccluded)
        {
            Debug.Log($"[SpinePartOcclusionSampler] AnyOccluded={anyOccluded} Summary={BuildSummary()}", this);
        }

        OccludedPartCount = occludedCount;
        lastAnyOccluded = anyOccluded;
    }

    private string BuildSummary()
    {
        if (samples == null)
            return "-";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i] == null)
                continue;

            if (sb.Length > 0)
                sb.Append(", ");

            sb.Append(samples[i].key);
            sb.Append(samples[i].occluded ? ":1" : ":0");
        }
        return sb.ToString();
    }

}
