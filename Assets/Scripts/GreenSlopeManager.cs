using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARSubsystems;

public class GreenSlopeManager : MonoBehaviour
{
    [Header("AR")]
    public ARRaycastManager arRaycast;
    public LayerMask physicsMask = ~0;

    public bool HasHole => holeWorld.HasValue;
    public int BoundaryCount => boundary.Count;

    [Header("Inputs")]
    public GazeRayProvider gaze;

    // --- HUD-less control state (works even if UI widgets are null) ---
    [Header("Zero-slope Highlight (HUD-less)")]
    [SerializeField] private bool _zeroHighlightEnabled = false;
    [SerializeField] private float _zeroThreshold = 0.30f;  // percent
    [SerializeField] private float _zeroThresholdMin = 0.00f;
    [SerializeField] private float _zeroThresholdMax = 1.00f;

    [Header("XR / Parenting")]
    [SerializeField] private XROrigin xrOrigin;          // drag your XR Origin (XR Rig / XR Origin (AR))
    [SerializeField] private ARPlaneManager planeManager; // optional; if it's on the rig
    [SerializeField] private Transform contentParent;     // BEST: create child "ARContent" under XR Origin and drag it here


    [Header("Enhanced Visualization")]
    public EnhancedSlopeVisualizer slopeVisualizer; // Assign in inspector or auto-create
    public bool useEnhancedVisualization = true;

    [Header("Analysis Debug / Guards")]
    [SerializeField] bool debugSampling = true;
    [SerializeField] float minPolygonAreaM2 = 0.5f;     // don't analyze a tiny triangle
    [SerializeField] float[] extraDowncastHeights = new float[] { 0f, 0.4f, 0.8f }; // try higher shots if first misses

    [Header("UI (optional)")]
    public Toggle zeroHighlightToggle;
    public Slider zeroThresholdSlider; // 0.0 .. ~0.8 (%)
    public TMP_Text statusText;

    [Header("Viz")]
    public Material cellMat;               // unlit, transparent gradient
    public Material zeroLineMat;           // unlit, bright
    public float gridStepMeters = 0.1f;    // sampling density
    public float sampleHeight = 0.6f;      // downcast height above terrain
    public int maxCells = 4000;            // protection

    // Runtime state
    private Vector3? holeWorld;            // world pos of hole
    private readonly List<Vector3> boundary = new(); // polygon (world)
    private readonly List<GameObject> cellQuads = new();
    private LineRenderer zeroIsoline;
    private readonly List<ARRaycastHit> _hits = new();

    [Header("Markers & Lines")]
    public Material holeMat;          // URP Unlit (or Built-in Unlit)
    public Material boundaryMat;      // for boundary vertex markers
    public Material boundaryLineMat;  // for the polyline
    public float holeDiameter = 0.108f;   // ~4.25" (meters)
    public float holeThickness = 0.008f;  // height of the ring
    public float vertexSize = 0.04f;      // sphere radius-ish

    GameObject holeGO;
    readonly List<GameObject> boundaryMarkers = new();
    LineRenderer boundaryLine;

    // AI Integration
    private SmartRaycastManager smartRaycast;

    // Add this field with other internals
    private EnhancedSlopeVisualizer enhancedViz;

    void Awake()
    {
        // UI (optional)
        if (zeroHighlightToggle) zeroHighlightToggle.onValueChanged.AddListener(_ => UpdateZeroIsoline());
        if (zeroThresholdSlider) zeroThresholdSlider.onValueChanged.AddListener(_ => UpdateZeroIsoline());

        // 0% isoline (parent under content)
        var isoGo = new GameObject("ZeroIsoline");
        var parent = GetContentParent();
        if (parent) isoGo.transform.SetParent(parent, true);
        zeroIsoline = isoGo.AddComponent<LineRenderer>();
        zeroIsoline.useWorldSpace = true;
        zeroIsoline.loop = false;
        zeroIsoline.positionCount = 0;
        zeroIsoline.widthMultiplier = 0.01f;
        if (zeroLineMat) zeroIsoline.material = zeroLineMat;

        // boundary polyline (parent once)
        var bl = new GameObject("BoundaryLine");
        if (parent) bl.transform.SetParent(parent, true);
        boundaryLine = bl.AddComponent<LineRenderer>();
        boundaryLine.useWorldSpace = true;
        boundaryLine.positionCount = 0;
        boundaryLine.widthMultiplier = 0.01f;
        if (boundaryLineMat) boundaryLine.material = boundaryLineMat;
        boundaryLine.enabled = false;

        // Get reference to AI manager
        smartRaycast = SmartRaycastManager.Instance;

        // Initialize enhanced visualization
        if (!slopeVisualizer)
        {
            var vizGo = new GameObject("EnhancedSlopeVisualizer");
            var parnt = GetContentParent();
            if (parnt) vizGo.transform.SetParent(parnt, true);
            enhancedViz = vizGo.AddComponent<EnhancedSlopeVisualizer>();
        }
        else
        {
            enhancedViz = slopeVisualizer;
        }

        if (enhancedViz) enhancedViz.SetContentParent(GetContentParent());

        SetStatus("Look at the hole and press your 'Place Hole' button.");
    }

    // -------------------- Public controls --------------------

    public void PlaceHole()
    {
        if (!TryGazeHit(out var pos, out var up)) { SetStatus("No surface under gaze – try again."); return; }

        if (!holeGO)
        {
            holeGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = holeGO.GetComponent<Collider>(); if (col) Destroy(col);
            var parent = GetContentParent();
            if (parent) holeGO.transform.SetParent(parent, true);
            if (holeMat) holeGO.GetComponent<Renderer>().material = new Material(holeMat);
        }

        // Seat the ring on the plane (offset along plane normal, not world up)
        float h = holeThickness; // meters
        holeGO.transform.SetPositionAndRotation(
            pos + up * (h * 0.5f + 0.002f),
            Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up), up)
        );
        holeGO.transform.localScale = new Vector3(holeDiameter, h * 0.5f, holeDiameter);

        holeWorld = pos;
        SetStatus("Hole placed. Add boundary points, then Finish.");
    }

    public void AddBoundaryVertex()
    {
        if (!holeWorld.HasValue) { SetStatus("Place the hole first."); return; }
        if (!TryGazeHit(out var pos, out var up)) { SetStatus("No surface under gaze."); return; }

        boundary.Add(pos);

        var v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var col = v.GetComponent<Collider>(); if (col) Destroy(col);
        var parent = GetContentParent();
        if (parent) v.transform.SetParent(parent, true);
        v.transform.position = pos + up * 0.002f;          // tiny lift along the plane normal
        v.transform.localScale = Vector3.one * vertexSize;
        if (boundaryMat) v.GetComponent<Renderer>().material = new Material(boundaryMat);
        boundaryMarkers.Add(v);

        UpdateBoundaryLine(loop: false);
        SetStatus($"Boundary points: {boundary.Count}.");
    }

    public void FinishBoundary()
    {
        if (!holeWorld.HasValue || boundary.Count < 3)
        {
            SetStatus("Need hole + ≥3 boundary points.");
            if (debugSampling) Debug.LogWarning($"[GreenSlope] FinishBoundary blocked: hole? {holeWorld.HasValue}, pts={boundary.Count}");
            return;
        }

        // area check (XZ plane)
        float area = PolyAreaXZ(boundary);
        if (area < minPolygonAreaM2)
        {
            SetStatus($"Boundary too small for analysis (area {area:F2} m²). Add more spaced-out points.");
            if (debugSampling) Debug.LogWarning($"[GreenSlope] FinishBoundary blocked: small area {area:F3} m²");
            return;
        }

        EnsureClockwise(boundary);
        UpdateBoundaryLine(loop: true);       // close the loop visually

        if (debugSampling) Debug.Log($"[GreenSlope] FinishBoundary: pts={boundary.Count}, area={area:F2} m² – analyzing…");
        AnalyzeAndRender();
    }

    float PolyAreaXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 3) return 0f;
        double sum = 0;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            sum += (double)(pts[j].x * pts[i].z - pts[i].x * pts[j].z);
        return Mathf.Abs((float)(0.5 * sum));
    }

    public void ClearAll()
    {
        holeWorld = null;
        boundary.Clear();

        if (holeGO) Destroy(holeGO); holeGO = null;

        foreach (var b in boundaryMarkers) if (b) Destroy(b);
        boundaryMarkers.Clear();

        boundaryLine.positionCount = 0; boundaryLine.enabled = false;

        ClearCells();
        zeroIsoline.positionCount = 0;

        SetStatus("Cleared. Place the hole again.");
    }

    // -------------------- Zero highlight (HUD-less) --------------------

    public bool ZeroHighlightEnabled
    {
        get => zeroHighlightToggle ? zeroHighlightToggle.isOn : _zeroHighlightEnabled;
        set
        {
            _zeroHighlightEnabled = value;
            if (zeroHighlightToggle) zeroHighlightToggle.isOn = value;
            UpdateZeroIsoline();
        }
    }

    // value in percent, e.g., 0.30 => 0.30%
    public float ZeroThreshold
    {
        get => zeroThresholdSlider ? zeroThresholdSlider.value : _zeroThreshold;
        set
        {
            var clamped = Mathf.Clamp(value, _zeroThresholdMin, _zeroThresholdMax);
            _zeroThreshold = clamped;
            if (zeroThresholdSlider) zeroThresholdSlider.value = clamped;
            UpdateZeroIsoline();
        }
    }

    // -------------------- Content parenting --------------------

    Transform GetContentParent()
    {
        if (contentParent) return contentParent;       // explicit override wins
        if (planeManager) return planeManager.transform; // managers spawn trackables here
        if (xrOrigin) return xrOrigin.transform;  // safe fallback
        return null;                                   // last resort (scene root)
    }

    void UpdateBoundaryLine(bool loop)
    {
        if (boundary.Count < 2) { boundaryLine.enabled = false; return; }

        var pts = new List<Vector3>(boundary.Count + (loop ? 1 : 0));
        pts.AddRange(boundary.Select(b => b + Vector3.up * 0.001f));
        if (loop) pts.Add(boundary[0] + Vector3.up * 0.001f);

        boundaryLine.positionCount = pts.Count;
        boundaryLine.SetPositions(pts.ToArray());
        boundaryLine.enabled = true;
    }

    // Tries multiple start heights and returns a hit position if any succeed.
    // Guarantees 'hit' is assigned on every return path.
    bool TryDowncastAtHeights(float x, float z, float baseY, out Vector3 hit)
    {
        // If you serialized this earlier, great. If not, a sensible default:
        float[] heightsToTry = (extraDowncastHeights != null && extraDowncastHeights.Length > 0)
            ? extraDowncastHeights
            : new float[] { 0f, 0.4f, 0.8f };

        foreach (var extra in heightsToTry)
        {
            var start = new Vector3(x, baseY + sampleHeight + extra, z);
            if (Downcast(start, out hit))
                return true;
        }

        hit = default; // definite assignment
        return false;
    }

    // -------------------- Analysis & rendering (AI OPTIMIZED) --------------------

    // Replace the AnalyzeAndRender method in your GreenSlopeManager.cs with this corrected version:

// Replace the AnalyzeAndRender method in your GreenSlopeManager.cs with this corrected version:

void AnalyzeAndRender()
{
    // Validate materials so we don't silently fail
    if (!cellMat)
    {
        SetStatus("cellMat not assigned – cannot render slope. Assign a URP Unlit material.");
        Debug.LogError("[GreenSlope] cellMat missing.");
        return;
    }
    if (!arRaycast && smartRaycast == null)
    {
        SetStatus("ARRaycastManager not assigned and SmartRaycastManager not available.");
        Debug.LogError("[GreenSlope] No raycast system available.");
        return;
    }

    ClearCells();
    zeroIsoline.positionCount = 0;

    // Build a tight AABB around the polygon
    var min = new Vector2(boundary.Min(v => v.x), boundary.Min(v => v.z));
    var max = new Vector2(boundary.Max(v => v.x), boundary.Max(v => v.z));
    float step = Mathf.Max(0.02f, gridStepMeters);
    int estCells = Mathf.CeilToInt((max.x - min.x) / step) * Mathf.CeilToInt((max.y - min.y) / step);
    if (estCells > maxCells) step *= Mathf.Sqrt((float)estCells / maxCells); // auto-thin the grid

    // Generate all potential sample points
    List<Vector3> allPoints = new List<Vector3>();
    List<Vector2Int> ijList = new List<Vector2Int>();

    int nx = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / step));
    int nz = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / step));

    for (int iz = 0; iz <= nz; iz++)
        for (int ix = 0; ix <= nx; ix++)
        {
            var x = min.x + ix * step;
            var z = min.y + iz * step;
            if (PointInPolygon(new Vector2(x, z), boundary))
            {
                allPoints.Add(new Vector3(x, holeWorld.Value.y, z));
                ijList.Add(new Vector2Int(ix, iz));
            }
        }

    // Use AI-optimized terrain sampling instead of individual raycasts
    List<Vector3> sampledHeights;
    List<Vector2Int> validIndices = new List<Vector2Int>();
    
    // Track sampling for debugging
    var debugMonitor = FindFirstObjectByType<AIDebugMonitor>();
    
    if (smartRaycast != null)
    {
        if (debugSampling) Debug.Log($"[GreenSlope] Using AI terrain sampling for {allPoints.Count} points");
        
        sampledHeights = smartRaycast.SmartTerrainSample(allPoints, maxCells);
        
        // Match successful samples back to their grid indices
        // Since AI sampling may reorder or skip points, we need to find the best matches
        for (int i = 0; i < sampledHeights.Count; i++)
        {
            // Find the closest original point for each successful sample
            float closestDist = float.MaxValue;
            int closestIndex = -1;
            
            for (int j = 0; j < allPoints.Count; j++)
            {
                float dist = Vector3.Distance(new Vector3(sampledHeights[i].x, 0, sampledHeights[i].z), 
                                             new Vector3(allPoints[j].x, 0, allPoints[j].z));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = j;
                }
            }
            
            if (closestIndex >= 0 && closestIndex < ijList.Count)
            {
                validIndices.Add(ijList[closestIndex]);
            }
            
            if (debugMonitor) debugMonitor.LogSampleAttempt(true);
        }
        
        if (debugSampling) Debug.Log($"[GreenSlope] AI sampling result: {sampledHeights.Count} successful samples");
    }
    else
    {
        // Fallback to original method
        sampledHeights = new List<Vector3>();
        int arOK = 0, arMiss = 0;

        for (int i = 0; i < allPoints.Count; i++)
        {
            var point = allPoints[i];
            bool success = TryDowncastAtHeights(point.x, point.z, point.y, out var hit);
            
            if (success)
            {
                sampledHeights.Add(hit);
                validIndices.Add(ijList[i]);
                arOK++;
            }
            else
            {
                arMiss++;
            }
            
            if (debugMonitor) debugMonitor.LogSampleAttempt(success);
        }

        if (debugSampling)
            Debug.Log($"[GreenSlope] Fallback sampling: OK={arOK}, Miss={arMiss}");
    }

    if (sampledHeights.Count < 20)
    {
        SetStatus("Not enough samples – try increasing area, stepping slower, or enable Depth (Occlusion).");
        if (debugSampling) Debug.LogWarning($"[GreenSlope] Samples too low: {sampledHeights.Count}");
        return;
    }

    // Build a height field dictionary for central differences
    var H = new Dictionary<Vector2Int, float>();
    for (int k = 0; k < sampledHeights.Count && k < validIndices.Count; k++)
    {
        H[validIndices[k]] = sampledHeights[k].y;
    }

    // Calculate elevation range for enhanced visualization
    float minElevation = H.Values.Count > 0 ? H.Values.Min() : 0f;
    float maxElevation = H.Values.Count > 0 ? H.Values.Max() : 0f;
    float elevationRange = maxElevation - minElevation;

    // Debug monitoring
    var slopeDebug = FindFirstObjectByType<SlopeDebugMonitor>();
    if (slopeDebug) 
    {
        slopeDebug.ResetStatistics();
        slopeDebug.AnalyzeHeightField(H);
    }

    // Parent for all cells
    var parent = GetContentParent();

    // Render cells with enhanced visualization
    foreach (var kv in H)
    {
        var ij = kv.Key;
        if (!H.TryGetValue(new Vector2Int(ij.x - 1, ij.y), out var hL)) continue;
        if (!H.TryGetValue(new Vector2Int(ij.x + 1, ij.y), out var hR)) continue;
        if (!H.TryGetValue(new Vector2Int(ij.x, ij.y - 1), out var hD)) continue;
        if (!H.TryGetValue(new Vector2Int(ij.x, ij.y + 1), out var hU)) continue;

        float dhdx = (hR - hL) / (2f * step);
        float dhdz = (hU - hD) / (2f * step);
        float slopePct = 100f * Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz);
        
        // Calculate slope direction for advanced visualization
        Vector2 slopeDirection = new Vector2(dhdx, dhdz).normalized;

        var center = new Vector3(min.x + ij.x * step, H[ij], min.y + ij.y * step);
        
        // Debug slope calculation
        if (slopeDebug) 
        {
            slopeDebug.LogSlopeCalculation(slopePct, dhdx, dhdz, step, center);
        }

       

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "SlopeCell";
        if (parent) q.transform.SetParent(parent, true);
        q.transform.position = center + Vector3.up * 0.001f;
        q.transform.rotation = Quaternion.Euler(90, 0, 0);
        q.transform.localScale = new Vector3(step, step, 1);
        var col = q.GetComponent<Collider>(); if (col) Destroy(col);

        var r = q.GetComponent<Renderer>();
        r.sharedMaterial = cellMat;
        var inst = new Material(cellMat);
        
        // Use enhanced visualization
        Color cellColor;
        if (useEnhancedVisualization && enhancedViz)
        {
            cellColor = enhancedViz.GetCellColor(slopePct, H[ij], minElevation, maxElevation, slopeDirection);
        }
        else
        {
            // Fallback to original simple coloring
            cellColor = SlopeToColor(slopePct);
        }
        
        SetMatColor(inst, cellColor);
        r.material = inst;

        cellQuads.Add(q);
    }

    // Generate contour lines if using enhanced visualization
    // CORRECTED: Use 'step' variable (which is your local gridStep equivalent)
    if (useEnhancedVisualization && enhancedViz && elevationRange > 0.01f)
    {
        enhancedViz.GenerateContourLines(H, min, step);
    }

    UpdateZeroIsoline();
    SetStatus($"Slope rendered. Samples: {sampledHeights.Count}. Range: {elevationRange*100:F1}cm");
    if (debugSampling) Debug.Log($"[GreenSlope] Enhanced viz: cells={cellQuads.Count}, elevation range={elevationRange*100:F1}cm");
}

    void UpdateZeroIsoline()
    {
        if (!ZeroHighlightEnabled || cellQuads.Count == 0)
        {
            zeroIsoline.positionCount = 0;
            return;
        }

        // NOTE: this simple isoline uses the color classification.
        // For a smooth contour, replace with marching squares on H.
        List<Vector3> pts = new();
        foreach (var q in cellQuads)
        {
            var mr = q.GetComponent<Renderer>();
            if (!mr) continue;
            var c = mr.material;
            Color col;
            if (c.HasProperty("_BaseColor")) col = c.GetColor("_BaseColor");
            else if (c.HasProperty("_Color")) col = c.GetColor("_Color");
            else continue;

            // Our SlopeToColor maps near-flat to white; pick bright cells as approx "0%"
            if (col.r > 0.85f && col.g > 0.85f && col.b > 0.85f)
                pts.Add(q.transform.position + Vector3.up * 0.002f);
        }

        pts = pts.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
        if (pts.Count < 2) { zeroIsoline.positionCount = 0; return; }
        zeroIsoline.positionCount = pts.Count;
        zeroIsoline.SetPositions(pts.ToArray());
        zeroIsoline.enabled = true;
    }

    // -------------------- Helpers --------------------

    bool Downcast(Vector3 start, out Vector3 pos)
    {
        pos = default;

        // Use AI-optimized raycast if available
        if (smartRaycast != null)
        {
            if (smartRaycast.SmartRaycast(new Ray(start, Vector3.down), out var hit,
                TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
            {
                pos = hit.pose.position;
                return true;
            }
        }
        // Fallback to direct AR raycast
        else if (arRaycast && arRaycast.Raycast(new Ray(start, Vector3.down), _hits,
            TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
        {
            pos = _hits[0].pose.position;
            return true;
        }

        // Physics fallback
        if (Physics.Raycast(start, Vector3.down, out var h, 2f, physicsMask))
        {
            pos = h.point;
            return true;
        }

        return false;
    }

    // Basic hit (position only) – leave for any callers that use it
    bool TryGazeHit(out Vector3 hit)
    {
        hit = default;
        if (!gaze) return false;

        var ray = gaze.GetRay();

        // Use AI-optimized raycast if available
        if (smartRaycast != null)
        {
            if (smartRaycast.SmartRaycast(ray, out var arHit, TrackableType.PlaneWithinPolygon))
            {
                hit = arHit.pose.position;
                return true;
            }
            if (smartRaycast.SmartRaycast(ray, out arHit, TrackableType.FeaturePoint | TrackableType.Depth))
            {
                hit = arHit.pose.position;
                return true;
            }
        }
        // Fallback to direct AR raycast
        else if (arRaycast)
        {
            var hits = new List<ARRaycastHit>(4);
            if (arRaycast.Raycast(ray, hits, TrackableType.PlaneWithinPolygon))
            {
                hit = hits[0].pose.position;
                return true;
            }
            if (arRaycast.Raycast(ray, hits, TrackableType.FeaturePoint | TrackableType.Depth))
            {
                hit = hits[0].pose.position;
                return true;
            }
        }

        // Physics fallback
        if (Physics.SphereCast(ray, 0.01f, out var h, 20f, physicsMask))
        {
            hit = h.point;
            return true;
        }

        // Probe-forward then down (physics only)
        var probe = ray.origin + ray.direction * 1.5f;
        if (Physics.Raycast(new Ray(probe + Vector3.up * 0.6f, Vector3.down), out var h2, 3f, physicsMask))
        {
            hit = h2.point;
            return true;
        }

        return false;
    }

    // Preferred hit: position + surface normal
    bool TryGazeHit(out Vector3 pos, out Vector3 up)
    {
        pos = default; up = Vector3.up;
        if (!gaze) return false;

        var ray = gaze.GetRay();

        // Use AI-optimized raycast if available
        if (smartRaycast != null)
        {
            if (smartRaycast.SmartRaycast(ray, out var arHit, TrackableType.PlaneWithinPolygon))
            {
                pos = arHit.pose.position;
                up = arHit.pose.up;
                return true;
            }
            if (smartRaycast.SmartRaycast(ray, out arHit, TrackableType.FeaturePoint | TrackableType.Depth))
            {
                pos = arHit.pose.position;
                up = Vector3.up;
                return true;
            }
        }
        // Fallback to direct AR raycast
        else if (arRaycast)
        {
            var hits = new List<ARRaycastHit>(4);
            if (arRaycast.Raycast(ray, hits, TrackableType.PlaneWithinPolygon))
            {
                pos = hits[0].pose.position;
                up = hits[0].pose.up;
                return true;
            }
            if (arRaycast.Raycast(ray, hits, TrackableType.FeaturePoint | TrackableType.Depth))
            {
                pos = hits[0].pose.position;
                up = Vector3.up;
                return true;
            }
        }

        // Physics fallback
        if (Physics.SphereCast(ray, 0.01f, out var h, 20f, physicsMask))
        {
            pos = h.point;
            up = h.normal;
            return true;
        }

        return false;
    }

    static bool PointInPolygon(Vector2 p, List<Vector3> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 pi = new Vector2(poly[i].x, poly[i].z);
            Vector2 pj = new Vector2(poly[j].x, poly[j].z);
            if (((pi.y > p.y) != (pj.y > p.y)) &&
                (p.x < (pj.x - pi.x) * (p.y - pi.y) / ((pj.y - pi.y) + 1e-6f) + pi.x))
                inside = !inside;
        }
        return inside;
    }

    static void EnsureClockwise(List<Vector3> pts)
    {
        float area = 0f;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            area += (pts[i].x - pts[j].x) * (pts[i].z + pts[j].z);
        if (area < 0f) pts.Reverse();
    }

    Color SlopeToColor(float slopePct)
    {
        // Enhanced fallback coloring
    if (slopePct < ZeroThreshold) return Color.white;
    
    // More visible color progression
    if (slopePct < 1f) return new Color(0.9f, 1f, 0.9f, 0.8f);      // Light green
    if (slopePct < 2f) return new Color(0.8f, 1f, 0.6f, 0.8f);      // Green
    if (slopePct < 3f) return new Color(1f, 1f, 0.4f, 0.8f);        // Yellow
    if (slopePct < 5f) return new Color(1f, 0.7f, 0.2f, 0.8f);      // Orange
    return new Color(1f, 0.3f, 0.2f, 0.9f);                        // Red
    }

    void ClearCells()
{
    foreach (var q in cellQuads) if (q) Destroy(q);
    cellQuads.Clear();
    
    // Clear contour lines too
    if (enhancedViz) enhancedViz.ClearContourLines();
}
    void SetStatus(string s) { if (statusText) statusText.text = s; }

    void SetMatColor(Material m, Color c)
    {
        if (!m) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    public bool TryGetBoundaryPoint(int index, out Vector3 p)
    {
        if (index >= 0 && index < boundary.Count) { p = boundary[index]; return true; }
        p = default; return false;
    }

    public bool TryGetLastBoundaryPoint(out Vector3 p)
    {
        if (boundary.Count > 0) { p = boundary[boundary.Count - 1]; return true; }
        p = default; return false;
    }

    // Optional, if you ever want to preview closing the loop:
    public bool TryGetFirstBoundaryPoint(out Vector3 p)
    {
        if (boundary.Count > 0) { p = boundary[0]; return true; }
        p = default; return false;
    }
    
}