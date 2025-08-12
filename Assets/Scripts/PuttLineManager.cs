// PuttLineManager_v2.cs
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class PuttLineManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform rayOrigin;                   // RayOrigin under Right Controller (for fallback casts)
    [SerializeField] private ControllerReticle reticle;             // Optional: use current reticle hit when selecting
    [SerializeField] private ARRaycastManager raycastManager;       // Drag from XR Origin
    [SerializeField] private ballarLaser laser;                     // Optional: subscribe to laser.OnPointSelected(Vector3)

    [Header("Visuals")]
    [SerializeField] private GameObject pointMarkerPrefab;          // Shown at A and B
    [SerializeField] private Transform markerParent;                // Optional parent for markers
    [SerializeField] private LineRenderer puttLineRenderer;         // Draws A→B
    [SerializeField] private LineRenderer fallLineRenderer;         // Shows downhill at midpoint (optional)

    [Header("HUD (optional)")]
    [SerializeField] private TextMeshProUGUI distanceFtText;
    [SerializeField] private TextMeshProUGUI slopePctText;
    [SerializeField] private TextMeshProUGUI breakInchesText;

    [Header("Sampling for slope @ midpoint")]
    [SerializeField] private float sampleRadius = 0.35f;            // meters
    [SerializeField] private int rings = 2;
    [SerializeField] private int samplesPerRing = 12;
    [SerializeField] private float overhead = 0.5f;                 // cast straight down from above
    [SerializeField] private TrackableType trackables =
        TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated |
        TrackableType.FeaturePoint | TrackableType.Depth;
    [SerializeField] private bool usePhysicsFallback = true;
    [SerializeField] private LayerMask physicsMask = ~0;

    [Header("Putt line width (meters)")]
    [SerializeField] private float puttLineWidth = 0.01f;
    [SerializeField] private float fallLineWidth = 0.01f;
    [SerializeField] private float fallLineLength = 0.5f;

    // Internal state
    private bool hasA, hasB;
    private Vector3 pointA, pointB;
    private GameObject markerA, markerB;
    private readonly List<ARRaycastHit> _hits = new();

    void Awake()
    {
        if (!raycastManager)
        {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
            raycastManager = FindFirstObjectByType<ARRaycastManager>(FindObjectsInactive.Exclude);
#else
            raycastManager = FindObjectOfType<ARRaycastManager>();
#endif
        }

        // Set up line renderers safely
        if (puttLineRenderer)
        {
            puttLineRenderer.useWorldSpace = true;
            puttLineRenderer.positionCount = 2;
            puttLineRenderer.startWidth = puttLineWidth;
            puttLineRenderer.endWidth = puttLineWidth;
            puttLineRenderer.enabled = false;
        }
        if (fallLineRenderer)
        {
            fallLineRenderer.useWorldSpace = true;
            fallLineRenderer.positionCount = 2;
            fallLineRenderer.startWidth = fallLineWidth;
            fallLineRenderer.endWidth = fallLineWidth;
            fallLineRenderer.enabled = false;
        }
    }

    void OnEnable()
    {
        if (laser != null)
            laser.OnPointSelected.AddListener(HandlePointSelectedFromLaser);
    }

    void OnDisable()
    {
        if (laser != null)
            laser.OnPointSelected.RemoveListener(HandlePointSelectedFromLaser);
    }

    // If you're not using ballarLaser events, you can hook this to your input:
    public void SelectPoint()
    {
        // Prefer the reticle hit pose if available
        if (reticle && reticle.HasHit)
        {
            HandlePointSelected(reticle.HitPose.position);
            return;
        }

        // Fallback: cast a ray from the controller forward
        if (!rayOrigin || !raycastManager) return;
        var ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (raycastManager.Raycast(ray, _hits, trackables))
            HandlePointSelected(_hits[0].pose.position);
        else if (usePhysicsFallback && Physics.Raycast(ray, out var phit, 20f, physicsMask))
            HandlePointSelected(phit.point);
    }

    private void HandlePointSelectedFromLaser(Vector3 worldPos) => HandlePointSelected(worldPos);

    private void HandlePointSelected(Vector3 worldPos)
    {
        if (!hasA)
        {
            pointA = worldPos;
            hasA = true;
            SpawnOrMoveMarker(ref markerA, pointA);
            ClearLineOnly();
            return;
        }

        if (!hasB)
        {
            pointB = worldPos;
            hasB = true;
            SpawnOrMoveMarker(ref markerB, pointB);
            DrawAndCompute();
            return;
        }

        // Third selection: start a new pair (treat selection as new A)
        ResetAll();
        pointA = worldPos;
        hasA = true;
        SpawnOrMoveMarker(ref markerA, pointA);
    }

    private void DrawAndCompute()
{
    if (!hasA || !hasB) return;

    if (puttLineRenderer)
    {
        puttLineRenderer.enabled = true;
        puttLineRenderer.SetPosition(0, pointA);
        puttLineRenderer.SetPosition(1, pointB);
    }

    float distMeters = Vector3.Distance(pointA, pointB);
    float distFeet   = distMeters * 3.28084f;

    Vector3 mid = Vector3.Lerp(pointA, pointB, 0.5f);

    // NEW: use LocalSlopeEstimator_v2 (drag it into this script or FindFirstObjectByType)
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
    var slope = FindFirstObjectByType<LocalSlopeEstimator_v2>(FindObjectsInactive.Exclude);
#else
    var slope = FindObjectOfType<LocalSlopeEstimator_v2>();
#endif
    float slopePct, crossSlopePct;
    Vector3 downhill;

    // line direction on XZ for cross-slope calc
    Vector3 lineDir = (pointB - pointA); lineDir.y = 0f;
    lineDir = lineDir.sqrMagnitude > 1e-6f ? lineDir.normalized : Vector3.forward;

    if (slope && slope.TryEstimateSlopeAt(mid, lineDir, out slopePct, out crossSlopePct, out downhill))
    {
        // Use CROSS-SLOPE for break (component perpendicular to the putt path)
        float breakInches = distFeet * (crossSlopePct) / 2f;

        UpdateHud(distFeet, slopePct, breakInches); // keep showing total slope% if you want

        if (fallLineRenderer)
        {
            fallLineRenderer.enabled = true;
            fallLineRenderer.SetPosition(0, mid);
            fallLineRenderer.SetPosition(1, mid + downhill * fallLineLength);
        }
    }
    else
    {
        UpdateHud(distFeet, float.NaN, float.NaN);
        if (fallLineRenderer) fallLineRenderer.enabled = false;
    }
}

    private void UpdateHud(float distFeet, float slopePct, float breakInches)
    {
        if (distanceFtText)
            distanceFtText.text = $"{distFeet:0.0} ft";

        if (slopePctText)
            slopePctText.text = float.IsNaN(slopePct) ? "-- %" : $"{slopePct:0.0}%";

        if (breakInchesText)
            breakInchesText.text = float.IsNaN(breakInches) ? "-- in" : $"{breakInches:0.0} in";
    }

    private void SpawnOrMoveMarker(ref GameObject marker, Vector3 pos)
    {
        if (!pointMarkerPrefab) return;
        if (marker == null)
            marker = Instantiate(pointMarkerPrefab, pos, Quaternion.identity, markerParent);
        else
            marker.transform.SetPositionAndRotation(pos, Quaternion.identity);
    }

    public void ClearLineOnly()
    {
        if (puttLineRenderer) puttLineRenderer.enabled = false;
        if (fallLineRenderer) fallLineRenderer.enabled = false;
        if (distanceFtText) distanceFtText.text = "";
        if (slopePctText) slopePctText.text = "";
        if (breakInchesText) breakInchesText.text = "";
    }

    public void ResetAll()
    {
        hasA = hasB = false;
        ClearLineOnly();

        if (markerA) Destroy(markerA);
        if (markerB) Destroy(markerB);
        markerA = markerB = null;
    }

    // ---- Local plane fit around 'center' using AR depth/planes + optional physics ----
    private bool TryEstimateSlopeAt(Vector3 center, out float slopePercent, out Vector3 downhillDir)
    {
        slopePercent = 0f;
        downhillDir = Vector3.zero;

        if (!raycastManager) return false;

        // Gather samples on a horizontal disc around the center by casting down from above
        var pts = new List<Vector3>(1 + rings * samplesPerRing) { center };

        for (int r = 1; r <= rings; r++)
        {
            float rad = (sampleRadius * r) / rings;
            for (int i = 0; i < samplesPerRing; i++)
            {
                float ang = (i / (float)samplesPerRing) * Mathf.PI * 2f;
                var offsetXZ = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                var castFrom = center + Vector3.up * overhead + offsetXZ;
                var ray = new Ray(castFrom, Vector3.down);

                if (raycastManager.Raycast(ray, _hits, trackables))
                    pts.Add(_hits[0].pose.position);
                else if (usePhysicsFallback && Physics.Raycast(ray, out var phit, overhead + 1.0f, physicsMask))
                    pts.Add(phit.point);
            }
        }

        if (pts.Count < 6) return false;

        // Fit plane: y = a*x + b*z + c  (world Y up)
        double Suu=0, Suv=0, Su=0, Svv=0, Sv=0, S1=pts.Count;
        double Suw=0, Svw=0, Sw=0;

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            double u = p.x, v = p.z, w = p.y;
            Suu += u*u;  Suv += u*v;  Su  += u;
            Svv += v*v;  Sv  += v;    Sw  += w;
            Suw += u*w;  Svw += v*w;
        }

        if (!Solve3x3(Suu, Suv, Su,  Suv, Svv, Sv,  Su, Sv, S1,  Suw, Svw, Sw, out double a, out double b, out _))
            return false;

        float m = Mathf.Sqrt((float)(a*a + b*b));    // rise/run
        slopePercent = m * 100f;

        downhillDir = new Vector3(-(float)a, 0f, -(float)b).normalized;
        return true;
    }

    private static bool Solve3x3(double a11,double a12,double a13, double a21,double a22,double a23,
                                 double a31,double a32,double a33, double b1,double b2,double b3,
                                 out double x1, out double x2, out double x3)
    {
        double det = a11*(a22*a33 - a23*a32) - a12*(a21*a33 - a23*a31) + a13*(a21*a32 - a22*a31);
        if (Mathf.Abs((float)det) < 1e-8f) { x1=x2=x3=0; return false; }

        double det1 = b1*(a22*a33 - a23*a32) - a12*(b2*a33 - a23*b3) + a13*(b2*a32 - a22*b3);
        double det2 = a11*(b2*a33 - a23*b3) - b1*(a21*a33 - a23*a31) + a13*(a21*b3 - b2*a31);
        double det3 = a11*(a22*b3 - b2*a32) - a12*(a21*b3 - b2*a31) + b1*(a21*a32 - a22*a31);

        x1 = det1 / det; x2 = det2 / det; x3 = det3 / det;
        return true;
    }
}
