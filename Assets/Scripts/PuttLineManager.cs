using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro; // If you use legacy UI.Text, swap types accordingly.

public class PuttLineManager : MonoBehaviour
{
    public enum PlacementState { Idle, Sampling, APlaced, BPlaced }

    [Header("Core Refs")]
    [SerializeField] private GazeRayProvider gaze;          // Attach from AR Camera
    [SerializeField] private ARRaycastManager arRaycast;    // On XR Origin
    [SerializeField] private ARAnchorManager anchorManager; // On XR Origin (optional)
    [SerializeField] private ARPlaneManager planeManager;   // Optional, not required

    [Header("Visuals")]
    [SerializeField] private LineRenderer puttLine;         // Draws the line A→B (surface polyline)
    [SerializeField] private Transform markerA;             // Optional: small marker transforms
    [SerializeField] private Transform markerB;

    [Header("UI")]
    [SerializeField] private TMP_Text statusText;           // "Sampling A…", errors, etc.
    [SerializeField] private TMP_Text distanceText;         // "Putt: 10.2 ft"
    [SerializeField] private TMP_Text slopeText;            // "Slope: 2.1%"

    [Header("Sampling & Filters")]
    [Tooltip("Seconds to dwell while collecting head-gaze hits")]
    [SerializeField] private float sampleSeconds = 0.4f;
    [SerializeField] private LayerMask physicsMask = ~0;    // Physics fallback layers

    [Header("Polyline & Units")]
    [SerializeField] private int lineSteps = 24;            // Segments for surface polyline
    [SerializeField] private bool useFeet = true;           // UI distance units

    [SerializeField] private float minSamplesRequired = 6; // was implicitly 8

    // Internal
    private readonly List<ARRaycastHit> _arHits = new();
    private ARAnchor anchorA, anchorB;
    private Vector3 pointA, pointB;                         // Raw points if no anchors
    public PlacementState currentState { get; private set; } = PlacementState.Idle;

    // Store last AR raycast hit during sampling (useful if you later want plane-attach)
    private ARRaycastHit? _lastArHitDuringSample = null;

    // AI Integration
    private SmartRaycastManager smartRaycast;

    #region Public entry points (hook to buttons or input)
    public void BeginPlaceA()
    {
        if (currentState == PlacementState.Idle ||
            currentState == PlacementState.APlaced ||
            currentState == PlacementState.BPlaced)
        {
            StartCoroutine(GetPrecisePoint(true));
        }
    }

    public void BeginPlaceB()
    {
        if (currentState == PlacementState.APlaced)
            StartCoroutine(GetPrecisePoint(false));
    }

    public void ResetAll()
    {
        currentState = PlacementState.Idle;

        if (anchorA) Destroy(anchorA.gameObject); anchorA = null;
        if (anchorB) Destroy(anchorB.gameObject); anchorB = null;

        pointA = pointB = Vector3.zero;

        if (markerA) markerA.gameObject.SetActive(false);
        if (markerB) markerB.gameObject.SetActive(false);

        if (puttLine) { puttLine.positionCount = 0; }

        SetStatus("Tap A to start.");
        SetHUD("", "");
    }
    #endregion

    private void Awake()
    {
        if (!puttLine)
        {
            // Create a simple line renderer if not assigned
            var lr = gameObject.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.01f;
            lr.useWorldSpace = true;
            lr.positionCount = 0;
            puttLine = lr;
        }
    }

    private void Start()
    {
        if (markerA) markerA.gameObject.SetActive(false);
        if (markerB) markerB.gameObject.SetActive(false);
        
        // Get reference to AI manager
        smartRaycast = SmartRaycastManager.Instance;
        
        SetStatus("Tap A to start.");
    }

    private void SetStatus(string msg) { if (statusText) statusText.text = msg; }
    private void SetHUD(string dist, string slope)
    {
        if (distanceText) distanceText.text = dist;
        if (slopeText) slopeText.text = slope;
    }

    // ==========================
    // Sampling with head-gaze
    // ==========================
    private IEnumerator GetPrecisePoint(bool isPointA)
    {
        currentState = PlacementState.Sampling;
        SetStatus(isPointA ? "Sampling A (gaze)…" : "Sampling B (gaze)…");

        var pts = new List<Vector3>(128);
        var ups = new List<Vector3>(128);
        _lastArHitDuringSample = null;

        float tEnd = Time.time + sampleSeconds;
        while (Time.time < tEnd)
        {
            // Head-gaze ray
            var cam = gaze ? gaze.GetRay() : new Ray(Camera.main.transform.position, Camera.main.transform.forward);

            if (TryARRaycast(cam, out ARRaycastHit arHit, out Vector3 hitPos, out Vector3 hitUp))
            {
                if (Vector3.Dot(hitUp, Vector3.up) >= 0.6f)
                {
                    pts.Add(hitPos);
                    ups.Add(hitUp.normalized);
                    _lastArHitDuringSample = arHit;
                }
            }
            else if (Physics.SphereCast(cam, 0.01f, out var h, 20f, physicsMask))
            {
                if (Vector3.Dot(h.normal, Vector3.up) >= 0.6f)
                {
                    pts.Add(h.point);
                    ups.Add(h.normal.normalized);
                }
            }
            yield return null; // frame
        }

        if (pts.Count < minSamplesRequired)
        {
            SetStatus("Not enough samples – try again.");
            currentState = PlacementState.Idle; yield break;
        }

        // --- Outlier rejection (MAD) ---
        Vector3 med = MedianVector(pts);
        float[] dist = new float[pts.Count];
        for (int i = 0; i < pts.Count; i++) dist[i] = (pts[i] - med).magnitude;
        float mad = MedianScalar(dist);
        float gate = Mathf.Max(0.01f, 2.5f * mad);

        var keptIdx = new List<int>(pts.Count);
        for (int i = 0; i < pts.Count; i++) if (dist[i] <= gate) keptIdx.Add(i);

        Vector3 centroid = Vector3.zero;
        Vector3 nSum = Vector3.zero;
        for (int k = 0; k < keptIdx.Count; k++)
        {
            int i = keptIdx[k];
            centroid += pts[i];
            nSum     += ups[i];
        }
        centroid /= Mathf.Max(1, keptIdx.Count);
        Vector3 n = nSum.sqrMagnitude > 1e-8f ? nSum.normalized : Vector3.up;

        // Vertical re-project
        Plane plane = new Plane(n, centroid);
        Ray vRay = new Ray(centroid + Vector3.up * 0.5f, Vector3.down);
        if (!plane.Raycast(vRay, out float enter))
        {
            SetStatus("Plane miss – try again.");
            currentState = PlacementState.Idle; yield break;
        }
        Vector3 precise = vRay.GetPoint(enter);

        OnPointPlaced(isPointA, precise);
    }

    private void OnPointPlaced(bool isA, Vector3 pos)
    {
        // Anchoring (AF 6.x: AddAnchor removed; use AddComponent<ARAnchor>())
        if (anchorManager)
        {
            var go = new GameObject(isA ? "AnchorA" : "AnchorB");
            go.transform.SetParent(anchorManager.transform, worldPositionStays: true);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);

            var a = go.AddComponent<ARAnchor>(); // may disable itself if provider can't create one
            if (a == null || !go.activeInHierarchy)
            {
                Debug.LogWarning("Failed to create ARAnchor; falling back to raw position.");
                Destroy(go);
                if (isA) pointA = pos; else pointB = pos;
            }
            else
            {
                if (isA) { if (anchorA) Destroy(anchorA.gameObject); anchorA = a; pointA = pos; }
                else     { if (anchorB) Destroy(anchorB.gameObject); anchorB = a; pointB = pos; }
            }
        }
        else
        {
            if (isA) pointA = pos; else pointB = pos;
        }

        if (isA)
        {
            currentState = PlacementState.APlaced;
            if (markerA) { markerA.position = pos; markerA.gameObject.SetActive(true); }
            SetStatus("Tap B to place end point.");
        }
        else
        {
            currentState = PlacementState.BPlaced;
            if (markerB) { markerB.position = pos; markerB.gameObject.SetActive(true); }
            BuildAndShowLine();
            UpdateMeasurementsUI();
            SetStatus("A/B placed – tap A or Reset to re-measure.");
        }
    }

    private void BuildAndShowLine()
    {
        Vector3 a = anchorA ? anchorA.transform.position : pointA;
        Vector3 b = anchorB ? anchorB.transform.position : pointB;
        if (lineSteps < 2) lineSteps = 2;

        var pts = BuildSurfacePolylinePoints(a, b, lineSteps);
        if (pts.Count >= 2)
        {
            puttLine.positionCount = pts.Count;
            puttLine.SetPositions(pts.ToArray());
        }
    }

    private void UpdateMeasurementsUI()
    {
        Vector3 a = anchorA ? anchorA.transform.position : pointA;
        Vector3 b = anchorB ? anchorB.transform.position : pointB;

        var (slopePct, horizontalRun) = SlopeAlongAB(a, b, nineSamples: 9);
        float surf = SurfaceDistance(a, b, steps: 24);

        string distStr = useFeet ? $"Putt: {(surf * 3.28084f):0.0} ft" : $"Putt: {surf:0.00} m";
        string slopeStr = $"Slope: {slopePct:0.0}%";
        SetHUD(distStr, slopeStr);
    }

    // ==========================
    // Core math helpers
    // ==========================
    private (float slopePct, float horizRun) SlopeAlongAB(Vector3 a, Vector3 b, int nineSamples)
    {
        Vector3 aH = new Vector3(a.x, 0, a.z);
        Vector3 bH = new Vector3(b.x, 0, b.z);
        float L = Vector3.Distance(aH, bH);
        if (L < 1e-3f) return (0f, 0f);

        int M = Mathf.Max(3, nineSamples);
        var xs = new List<float>(M);
        var ys = new List<float>(M);
        for (int i = 0; i < M; i++)
        {
            float t = (M == 1) ? 0f : i / (float)(M - 1);
            Vector3 pH = Vector3.Lerp(aH, bH, t);
            Vector3 start = new Vector3(pH.x, Mathf.Max(a.y, b.y) + 0.6f, pH.z);

            if (TryARRaycast(new Ray(start, Vector3.down), out _, out Vector3 hitPos, out _))
            {
                xs.Add(t * L);
                ys.Add(hitPos.y);
            }
            else if (Physics.Raycast(start, Vector3.down, out var h, 5f, physicsMask))
            {
                xs.Add(t * L);
                ys.Add(h.point.y);
            }
        }

        if (xs.Count < 3) return (0f, L);

        float xbar = 0f, ybar = 0f; for (int i = 0; i < xs.Count; i++) { xbar += xs[i]; ybar += ys[i]; }
        xbar /= xs.Count; ybar /= xs.Count;
        float num = 0f, den = 0f;
        for (int i = 0; i < xs.Count; i++) { float dx = xs[i] - xbar; num += dx * (ys[i] - ybar); den += dx * dx; }
        float dy_dx = (den > 1e-6f) ? num / den : 0f;
        return (dy_dx * 100f, L);
    }

    private float SurfaceDistance(Vector3 a, Vector3 b, int steps)
    {
        var pts = BuildSurfacePolylinePoints(a, b, Mathf.Max(2, steps));
        float sum = 0f;
        for (int i = 1; i < pts.Count; i++) sum += Vector3.Distance(pts[i - 1], pts[i]);
        return sum;
    }

    private List<Vector3> BuildSurfacePolylinePoints(Vector3 a, Vector3 b, int steps)
    {
        var list = new List<Vector3>(steps + 1);
        list.Add(ProjectDown(a, a, b));
        Vector3 aH = new Vector3(a.x, 0, a.z);
        Vector3 bH = new Vector3(b.x, 0, b.z);
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            Vector3 pH = Vector3.Lerp(aH, bH, t);
            Vector3 start = new Vector3(pH.x, Mathf.Max(a.y, b.y) + 0.6f, pH.z);
            list.Add(ProjectDown(start, a, b));
        }
        list.Add(ProjectDown(b, a, b));
        return list;
    }

    private Vector3 ProjectDown(Vector3 start, Vector3 a, Vector3 b)
    {
        if (TryARRaycast(new Ray(start + Vector3.up * 0.01f, Vector3.down), out _, out Vector3 hitPos, out _)) return hitPos;
        if (Physics.Raycast(start + Vector3.up * 0.01f, Vector3.down, out var h, 5f, physicsMask)) return h.point;
        // Fallback: linear height interpolation (rare)
        float t = HorizontalT(start, a, b);
        return new Vector3(start.x, Mathf.Lerp(a.y, b.y, t), start.z);
    }

    private float HorizontalT(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 aH = new Vector3(a.x, 0, a.z);
        Vector3 bH = new Vector3(b.x, 0, b.z);
        Vector3 pH = new Vector3(p.x, 0, p.z);
        float L = Vector3.Distance(aH, bH);
        if (L < 1e-4f) return 0f;
        float t = Vector3.Dot(pH - aH, (bH - aH).normalized) / L;
        return Mathf.Clamp01(t);
    }

    // ==========================
    // Raycast helper (AR-first) - AI OPTIMIZED
    // ==========================
    private bool TryARRaycast(Ray worldRay, out ARRaycastHit arHit, out Vector3 hitPos, out Vector3 hitUp)
    {
        arHit = default;
        hitPos = default;
        hitUp = Vector3.up;

        // Use AI-optimized raycast if available
        if (smartRaycast != null)
        {
            if (smartRaycast.SmartRaycast(worldRay, out arHit,
                TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
            {
                hitPos = arHit.pose.position;

                // If it's a plane, use plane up. Otherwise (feature/depth), don't over-filter.
                bool planeHit = (arHit.hitType & TrackableType.PlaneWithinPolygon) != 0;
                hitUp = planeHit ? arHit.pose.up : Vector3.up;

                if (planeHit)
                {
                    // Relaxed wall rejection
                    if (Vector3.Dot(hitUp, Vector3.up) < 0.5f) return false;
                }
                return true;
            }
        }
        // Fallback to direct AR raycast if AI system unavailable
        else if (arRaycast &&
            arRaycast.Raycast(worldRay, _arHits,
                TrackableType.PlaneWithinPolygon |
                TrackableType.FeaturePoint |
                TrackableType.Depth))
        {
            var h = _arHits[0];
            arHit = h;
            hitPos = h.pose.position;
            hitUp = h.pose.up;

            // If it's a plane, use plane up. Otherwise (feature/depth), don't over-filter.
            bool planeHit = (h.hitType & TrackableType.PlaneWithinPolygon) != 0;
            hitUp = planeHit ? h.pose.up : Vector3.up;

            if (planeHit)
            {
                // Relaxed wall rejection
                if (Vector3.Dot(hitUp, Vector3.up) < 0.5f) return false;
            }
            return true;
        }
        return false;
    }

    // ==========================
    // Median helpers for MAD gate
    // ==========================
    private static Vector3 MedianVector(List<Vector3> a)
    {
        return new Vector3(MedianScalar(a, v => v.x), MedianScalar(a, v => v.y), MedianScalar(a, v => v.z));
    }
    private static float MedianScalar(List<Vector3> a, System.Func<Vector3, float> sel)
    {
        var tmp = new List<float>(a.Count);
        for (int i = 0; i < a.Count; i++) tmp.Add(sel(a[i]));
        tmp.Sort();
        int m = tmp.Count >> 1; return (tmp.Count % 2 == 1) ? tmp[m] : 0.5f * (tmp[m - 1] + tmp[m]);
    }
    private static float MedianScalar(float[] arr)
    {
        var tmp = new List<float>(arr);
        tmp.Sort();
        int m = tmp.Count >> 1; return (tmp.Count % 2 == 1) ? tmp[m] : 0.5f * (tmp[m - 1] + tmp[m]);
    }
}