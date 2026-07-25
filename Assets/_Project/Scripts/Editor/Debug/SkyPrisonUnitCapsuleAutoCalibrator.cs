#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sky Prison editor utility.
/// Measures a unit visual body from renderers and calibrates its UnitBody CapsuleCollider.
///
/// Design goal:
/// - Player/Unit root may stay as foot/logical anchor.
/// - CapsuleCollider height/center expresses body height.
/// - Visual billboard must not rotate CollisionRoot/UnitBodyCollider.
///
/// Put this file under an Editor folder, for example:
/// Assets/_Project/Scripts/Editor/Debug/SkyPrisonUnitCapsuleAutoCalibrator_V1.cs
/// </summary>
public sealed class SkyPrisonUnitCapsuleAutoCalibratorWindow : EditorWindow
{
    private enum FootReferenceMode
    {
        RootYZero,
        VisualBoundsBottom
    }

    private enum RadiusMode
    {
        KeepCurrent,
        FromVisualXZ,
        FromHeightRatio
    }

    private FootReferenceMode footReference = FootReferenceMode.RootYZero;
    private RadiusMode radiusMode = RadiusMode.KeepCurrent;

    private string visualRootName = "VisualRoot";
    private string preferredBodyRootName = "SpineRoot";
    private string collisionRootName = "CollisionRoot";
    private string unitBodyColliderName = "UnitBodyCollider";

    private float heightScale = 1.00f;
    private float topPadding = 0.00f;
    private float bottomPadding = 0.00f;
    private float rootFootYOffset = 0.00f;

    private float radiusScaleFromVisualXZ = 0.34f;
    private float radiusScaleFromHeight = 0.18f;
    private float minRadius = 0.12f;
    private float maxRadius = 0.55f;

    private bool resetColliderTransformLocalRotation = true;
    private bool resetColliderTransformLocalPositionXZ = false;
    private bool ignoreDisabledRenderers = true;
    private bool excludeProxyRenderers = true;
    private bool verboseLog = true;

    [MenuItem("Tools/Sky Prison/Debug/Unit Capsule Calibrator/Open Window")]
    public static void Open()
    {
        GetWindow<SkyPrisonUnitCapsuleAutoCalibratorWindow>("Unit Capsule Calibrator");
    }

    [MenuItem("Tools/Sky Prison/Debug/Unit Capsule Calibrator/Calibrate Selected Units")]
    public static void CalibrateSelectedMenu()
    {
        CalibrateSelectedWithDefaults();
    }

    private static void CalibrateSelectedWithDefaults()
    {
        var window = CreateInstance<SkyPrisonUnitCapsuleAutoCalibratorWindow>();
        window.footReference = FootReferenceMode.RootYZero;
        window.radiusMode = RadiusMode.KeepCurrent;
        window.CalibrateSelected();
        DestroyImmediate(window);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Sky Prison Unit Capsule Auto Calibrator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Select one or more unit roots. This measures VisualRoot/SpineRoot renderers and writes CapsuleCollider height/center. " +
            "Recommended project convention: Unit root = foot/logical point; Capsule.center.y = height / 2.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Node Names", EditorStyles.boldLabel);
        visualRootName = EditorGUILayout.TextField("Visual Root Name", visualRootName);
        preferredBodyRootName = EditorGUILayout.TextField("Preferred Body Root", preferredBodyRootName);
        collisionRootName = EditorGUILayout.TextField("Collision Root Name", collisionRootName);
        unitBodyColliderName = EditorGUILayout.TextField("Unit Body Collider Name", unitBodyColliderName);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Height", EditorStyles.boldLabel);
        footReference = (FootReferenceMode)EditorGUILayout.EnumPopup("Foot Reference", footReference);
        heightScale = EditorGUILayout.FloatField("Height Scale", heightScale);
        topPadding = EditorGUILayout.FloatField("Top Padding", topPadding);
        bottomPadding = EditorGUILayout.FloatField("Bottom Padding", bottomPadding);
        rootFootYOffset = EditorGUILayout.FloatField("Root Foot Y Offset", rootFootYOffset);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Radius", EditorStyles.boldLabel);
        radiusMode = (RadiusMode)EditorGUILayout.EnumPopup("Radius Mode", radiusMode);
        radiusScaleFromVisualXZ = EditorGUILayout.FloatField("Radius From Visual XZ", radiusScaleFromVisualXZ);
        radiusScaleFromHeight = EditorGUILayout.FloatField("Radius From Height", radiusScaleFromHeight);
        minRadius = EditorGUILayout.FloatField("Min Radius", minRadius);
        maxRadius = EditorGUILayout.FloatField("Max Radius", maxRadius);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Safety", EditorStyles.boldLabel);
        resetColliderTransformLocalRotation = EditorGUILayout.Toggle("Reset Collider Local Rotation", resetColliderTransformLocalRotation);
        resetColliderTransformLocalPositionXZ = EditorGUILayout.Toggle("Reset Collider Local XZ", resetColliderTransformLocalPositionXZ);
        ignoreDisabledRenderers = EditorGUILayout.Toggle("Ignore Disabled Renderers", ignoreDisabledRenderers);
        excludeProxyRenderers = EditorGUILayout.Toggle("Exclude Proxy Renderers", excludeProxyRenderers);
        verboseLog = EditorGUILayout.Toggle("Verbose Log", verboseLog);

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preview Selected", GUILayout.Height(28)))
                PreviewSelected();

            if (GUILayout.Button("Calibrate Selected", GUILayout.Height(28)))
                CalibrateSelected();
        }
    }

    private void PreviewSelected()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            if (!TryMeasureUnit(go.transform, out Measurement measurement, out string error))
            {
                Debug.LogWarning($"[UnitCapsuleCalibrator] Preview failed for {go.name}: {error}", go);
                continue;
            }

            Debug.Log(
                $"[UnitCapsuleCalibrator] Preview {go.name}\n" +
                $"  visualRoot={measurement.visualRootPath}\n" +
                $"  localBounds min={measurement.localBounds.min} max={measurement.localBounds.max} size={measurement.localBounds.size}\n" +
                $"  proposedHeight={measurement.proposedHeight:F4} proposedCenterY={measurement.proposedCenterY:F4} proposedRadius={measurement.proposedRadius:F4}",
                go);
        }
    }

    private void CalibrateSelected()
    {
        int changed = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (!TryMeasureUnit(go.transform, out Measurement measurement, out string error))
            {
                Debug.LogWarning($"[UnitCapsuleCalibrator] Calibrate failed for {go.name}: {error}", go);
                continue;
            }

            Undo.RecordObject(measurement.capsule, "Calibrate Unit Capsule");
            Undo.RecordObject(measurement.capsule.transform, "Calibrate Unit Capsule Transform");

            if (resetColliderTransformLocalRotation)
                measurement.capsule.transform.localRotation = Quaternion.identity;

            if (resetColliderTransformLocalPositionXZ)
            {
                Vector3 lp = measurement.capsule.transform.localPosition;
                lp.x = 0f;
                lp.z = 0f;
                measurement.capsule.transform.localPosition = lp;
            }

            measurement.capsule.direction = 1; // Y axis.
            measurement.capsule.height = measurement.proposedHeight;
            measurement.capsule.center = new Vector3(
                measurement.capsule.center.x,
                measurement.proposedCenterY,
                measurement.capsule.center.z);

            if (radiusMode != RadiusMode.KeepCurrent)
                measurement.capsule.radius = measurement.proposedRadius;

            EditorUtility.SetDirty(measurement.capsule);
            EditorUtility.SetDirty(measurement.capsule.transform);
            changed++;

            if (verboseLog)
            {
                Debug.Log(
                    $"[UnitCapsuleCalibrator] Calibrated {go.name}: height={measurement.proposedHeight:F4}, centerY={measurement.proposedCenterY:F4}, radius={measurement.capsule.radius:F4}, footMode={footReference}",
                    go);
            }
        }

        if (changed > 0)
            Debug.Log($"[UnitCapsuleCalibrator] Calibrated {changed} selected unit(s). Save prefab/scene if result is correct.");
    }

    private bool TryMeasureUnit(Transform unitRoot, out Measurement measurement, out string error)
    {
        measurement = default;
        error = null;

        if (unitRoot == null)
        {
            error = "unitRoot is null.";
            return false;
        }

        Transform visualRoot = FindDeepChildByName(unitRoot, visualRootName);
        if (visualRoot == null)
        {
            error = $"Cannot find {visualRootName}.";
            return false;
        }

        Transform bodyMeasureRoot = FindDeepChildByName(visualRoot, preferredBodyRootName) ?? visualRoot;
        CapsuleCollider capsule = FindUnitBodyCapsule(unitRoot);
        if (capsule == null)
        {
            error = "Cannot find CapsuleCollider under CollisionRoot/UnitBodyCollider or unit children.";
            return false;
        }

        if (!TryGetRendererLocalBounds(unitRoot, bodyMeasureRoot, out Bounds localBounds))
        {
            error = $"No valid renderer bounds under {GetPath(bodyMeasureRoot)}.";
            return false;
        }

        float rawHeight = Mathf.Max(0.01f, localBounds.size.y);
        float height = Mathf.Max(0.01f, rawHeight * Mathf.Max(0.01f, heightScale) + topPadding + bottomPadding);

        float bottomY;
        if (footReference == FootReferenceMode.RootYZero)
        {
            bottomY = rootFootYOffset + bottomPadding;
        }
        else
        {
            bottomY = localBounds.min.y + bottomPadding;
        }

        // Convert bottom/top in unit-root local space into capsule-local center Y.
        float centerRootY = bottomY + height * 0.5f;
        Vector3 centerWorld = unitRoot.TransformPoint(new Vector3(0f, centerRootY, 0f));
        Vector3 centerCapsuleLocal = capsule.transform.InverseTransformPoint(centerWorld);

        float visualXZ = Mathf.Max(localBounds.size.x, localBounds.size.z);
        float radius = capsule.radius;
        if (radiusMode == RadiusMode.FromVisualXZ)
            radius = Mathf.Clamp(visualXZ * radiusScaleFromVisualXZ, minRadius, maxRadius);
        else if (radiusMode == RadiusMode.FromHeightRatio)
            radius = Mathf.Clamp(height * radiusScaleFromHeight, minRadius, maxRadius);

        measurement = new Measurement
        {
            unitRoot = unitRoot,
            visualRootPath = GetPath(bodyMeasureRoot),
            capsule = capsule,
            localBounds = localBounds,
            proposedHeight = height,
            proposedCenterY = centerCapsuleLocal.y,
            proposedRadius = radius
        };
        return true;
    }

    private CapsuleCollider FindUnitBodyCapsule(Transform unitRoot)
    {
        Transform collisionRoot = FindDeepChildByName(unitRoot, collisionRootName);
        Transform searchRoot = collisionRoot != null ? collisionRoot : unitRoot;

        Transform unitBody = FindDeepChildByName(searchRoot, unitBodyColliderName);
        if (unitBody != null)
        {
            CapsuleCollider cap = unitBody.GetComponent<CapsuleCollider>();
            if (cap != null)
                return cap;
        }

        CapsuleCollider[] capsules = searchRoot.GetComponentsInChildren<CapsuleCollider>(true);
        if (capsules.Length == 0)
            return null;

        foreach (CapsuleCollider cap in capsules)
        {
            if (cap == null)
                continue;
            if (cap.name.IndexOf("UnitBody", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return cap;
        }

        return capsules[0];
    }

    private bool TryGetRendererLocalBounds(Transform unitRoot, Transform measureRoot, out Bounds bounds)
    {
        bounds = default;
        bool hasAny = false;

        Renderer[] renderers = measureRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;
            if (ignoreDisabledRenderers && !r.enabled)
                continue;
            if (excludeProxyRenderers && IsProxyLike(r.transform))
                continue;

            Bounds wb = r.bounds;
            Vector3 min = wb.min;
            Vector3 max = wb.max;

            Vector3[] corners = new Vector3[8]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };

            foreach (Vector3 c in corners)
            {
                Vector3 local = unitRoot.InverseTransformPoint(c);
                if (!hasAny)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return hasAny;
    }

    private static bool IsProxyLike(Transform t)
    {
        Transform cur = t;
        while (cur != null)
        {
            string n = cur.name;
            if (n.IndexOf("Proxy", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Highlight", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Overhead", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            cur = cur.parent;
        }
        return false;
    }

    private static Transform FindDeepChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        Queue<Transform> q = new Queue<Transform>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            Transform t = q.Dequeue();
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.name == childName)
                    return c;
                q.Enqueue(c);
            }
        }
        return null;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "<null>";
        var names = new Stack<string>();
        Transform cur = t;
        while (cur != null)
        {
            names.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", names.ToArray());
    }

    private struct Measurement
    {
        public Transform unitRoot;
        public string visualRootPath;
        public CapsuleCollider capsule;
        public Bounds localBounds;
        public float proposedHeight;
        public float proposedCenterY;
        public float proposedRadius;
    }
}
#endif
