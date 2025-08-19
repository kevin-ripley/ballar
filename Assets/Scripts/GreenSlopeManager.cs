using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;
public class GreenSlopeManager : MonoBehaviour
{
    [Header("AR")]
    public ARRaycastManager arRaycast;
    public LayerMask physicsMask = ~0;

    [Header("Inputs")]
    public GazeRayProvider gaze;

    [Header("UI")]
    public Button placeHoleBtn, addBoundaryBtn, finishBoundaryBtn, clearBtn;
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

    void Awake()
    {
        placeHoleBtn.onClick.AddListener(PlaceHole);
        addBoundaryBtn.onClick.AddListener(AddBoundaryVertex);
        finishBoundaryBtn.onClick.AddListener(FinishBoundary);
        clearBtn.onClick.AddListener(ClearAll);
        zeroHighlightToggle.onValueChanged.AddListener(_ => UpdateZeroIsoline());
        zeroThresholdSlider.onValueChanged.AddListener(_ => UpdateZeroIsoline());

        // tiny line renderer for 0% isoline
        var go = new GameObject("ZeroIsoline");
        zeroIsoline = go.AddComponent<LineRenderer>();
        zeroIsoline.useWorldSpace = true;
        zeroIsoline.loop = false;
        zeroIsoline.positionCount = 0;
        zeroIsoline.widthMultiplier = 0.01f;
        if (zeroLineMat) zeroIsoline.material = zeroLineMat;

        SetStatus("Look at the hole and tap 'Place Hole'.");
    }

    void PlaceHole()
    {
        if (!TryGazeHit(out var p))
        {
            SetStatus("No surface under gaze — try again.");
            return;
        }
        holeWorld = p;
        SetStatus("Hole placed. Walk the green boundary, gaze + 'Add Boundary Pt' to add vertices, then 'Finish Boundary'.");
    }

    void AddBoundaryVertex()
    {
        if (!holeWorld.HasValue) { SetStatus("Place the hole first."); return; }
        if (!TryGazeHit(out var p)) { SetStatus("No surface under gaze."); return; }
        boundary.Add(p);
        DrawBoundaryDebug();
        SetStatus($"Boundary points: {boundary.Count} (need ≥ 3).");
    }

    void FinishBoundary()
    {
        if (!holeWorld.HasValue || boundary.Count < 3) { SetStatus("Need hole + ≥3 boundary points."); return; }
        // ensure clockwise polygon for consistent inside test
        EnsureClockwise(boundary);
        AnalyzeAndRender();
    }

    void ClearAll()
    {
        holeWorld = null;
        boundary.Clear();
        ClearCells();
        zeroIsoline.positionCount = 0;
        SetStatus("Cleared. Place the hole again.");
    }

    // -------- Core analysis --------
    void AnalyzeAndRender()
    {
        ClearCells();
        zeroIsoline.positionCount = 0;

        // Build a tight AABB around the polygon
        var min = new Vector2(boundary.Min(v => v.x), boundary.Min(v => v.z));
        var max = new Vector2(boundary.Max(v => v.x), boundary.Max(v => v.z));
        float step = Mathf.Max(0.02f, gridStepMeters);
        int estCells = Mathf.CeilToInt((max.x - min.x) / step) * Mathf.CeilToInt((max.y - min.y) / step);
        if (estCells > maxCells) step *= Mathf.Sqrt((float)estCells / maxCells); // auto-thin the grid

        // Sample points inside polygon
        List<Vector3> samples = new();
        List<Vector2Int> ijList = new();
        List<float> heights = new();

        int nx = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / step));
        int nz = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / step));
        for (int iz = 0; iz <= nz; iz++)
        for (int ix = 0; ix <= nx; ix++)
        {
            var x = min.x + ix * step;
            var z = min.y + iz * step;
            var p = new Vector3(x, (holeWorld.Value.y + sampleHeight), z);
            if (!PointInPolygon(new Vector2(x, z), boundary)) continue;

            if (Downcast(p, out var onSurf))
            {
                samples.Add(onSurf);
                ijList.Add(new Vector2Int(ix, iz));
                heights.Add(onSurf.y);
            }
        }

        if (samples.Count < 20)
        {
            SetStatus("Not enough samples inside polygon — add more vertices or rescan.");
            return;
        }

        // Build a height field dictionary for central differences
        var H = new Dictionary<Vector2Int, float>();
        for (int k = 0; k < samples.Count; k++) H[ijList[k]] = heights[k];

        // For each sample, compute slope magnitude and color a quad
        foreach (var kv in H)
        {
            var ij = kv.Key;
            if (!H.TryGetValue(new Vector2Int(ij.x-1, ij.y), out var hL)) continue;
            if (!H.TryGetValue(new Vector2Int(ij.x+1, ij.y), out var hR)) continue;
            if (!H.TryGetValue(new Vector2Int(ij.x, ij.y-1), out var hD)) continue;
            if (!H.TryGetValue(new Vector2Int(ij.x, ij.y+1), out var hU)) continue;

            // central differences
            float dhdx = (hR - hL) / (2f * step);
            float dhdz = (hU - hD) / (2f * step);
            // slope % magnitude = 100 * rise / run
            float slopePct = 100f * Mathf.Sqrt(dhdx*dhdx + dhdz*dhdz);

            var center = new Vector3(min.x + ij.x * step, H[ij], min.y + ij.y * step);

            // draw a tiny quad colored by slope magnitude
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "SlopeCell";
            q.transform.position = center + Vector3.up * 0.001f;
            q.transform.rotation = Quaternion.Euler(90, 0, 0);
            q.transform.localScale = new Vector3(step, step, 1);
            var col = q.GetComponent<Collider>(); if (col) Destroy(col);

            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = cellMat;
            r.material = new Material(cellMat); // instance per cell to set color
            r.material.SetColor("_BaseColor", SlopeToColor(slopePct));

            cellQuads.Add(q);
        }

        UpdateZeroIsoline();
        SetStatus("Slope rendered. Toggle 0% highlight or adjust threshold.");
    }

    void UpdateZeroIsoline()
    {
        if (!zeroHighlightToggle || !zeroHighlightToggle.isOn || cellQuads.Count == 0)
        {
            zeroIsoline.positionCount = 0;
            return;
        }
        float eps = Mathf.Max(0.05f, zeroThresholdSlider ? zeroThresholdSlider.value : 0.3f);

        // Build a simple polyline: pick centers of quads whose material color was close to "flat".
        // (Fast/approx; for a smooth isoline, you'd run marching squares.)
        List<Vector3> pts = new();
        foreach (var q in cellQuads)
        {
            var c = q.GetComponent<Renderer>().material.GetColor("_BaseColor");
            // Our SlopeToColor maps near-flat to near-white; pick bright cells
            if (c.r > 0.85f && c.g > 0.85f && c.b > 0.85f)
                pts.Add(q.transform.position + Vector3.up * 0.002f);
        }

        // Simple thinning
        pts = pts.OrderBy(p => p.x).ThenBy(p => p.z).ToList();
        if (pts.Count < 2) { zeroIsoline.positionCount = 0; return; }
        zeroIsoline.positionCount = pts.Count;
        zeroIsoline.SetPositions(pts.ToArray());
        zeroIsoline.enabled = true;
    }

    // -------- Helpers --------
    bool TryGazeHit(out Vector3 hit)
    {
        hit = default;
        if (gaze == null) return false;
        var ray = gaze.GetRay();
        if (arRaycast && arRaycast.Raycast(ray, _hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
        {
            hit = _hits[0].pose.position; return true;
        }
        if (Physics.SphereCast(ray, 0.01f, out var h, 20f, physicsMask)) { hit = h.point; return true; }
        return false;
    }

    bool Downcast(Vector3 start, out Vector3 pos)
    {
        pos = default;
        if (arRaycast && arRaycast.Raycast(new Ray(start, Vector3.down), _hits,
            TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
        { pos = _hits[0].pose.position; return true; }
        if (Physics.Raycast(start, Vector3.down, out var h, 2f, physicsMask))
        { pos = h.point; return true; }
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
        // 0% = white, 2% = yellow, 4% = orange, 6% = red, >8% = burgundy
        float t = Mathf.Clamp01(slopePct / 8f);
        if (slopePct < (zeroThresholdSlider ? zeroThresholdSlider.value : 0.3f)) return Color.white;
        return Color.Lerp(new Color(1f, 1f, 0f), new Color(0.6f, 0f, 0f), t); // yellow→red
    }

    void DrawBoundaryDebug()
    {
        // optional: quick gizmo dots
        for (int i = 0; i < boundary.Count; i++)
            Debug.DrawLine(boundary[i] + Vector3.up*0.01f, boundary[(i+1)%boundary.Count] + Vector3.up*0.01f, Color.cyan, 2f);
    }

    void ClearCells()
    {
        foreach (var q in cellQuads) if (q) Destroy(q);
        cellQuads.Clear();
    }

    void SetStatus(string s) { if (statusText) statusText.text = s; }
}
