// GreenAnalyzer.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class GreenAnalyzer : MonoBehaviour
{
    public enum ContourMode { Elevation, SlopePercent }

    [Header("References")]
    [SerializeField] private ARRaycastManager raycastManager;     // Drag from XR Origin
    [SerializeField] private LineRenderer contourLinePrefab;      // Prefab with LineRenderer (unlit)
    [SerializeField] private Transform contoursParent;            // Parent to keep hierarchy tidy
    [SerializeField] private bool usePhysicsFallback = true;
    [SerializeField] private LayerMask physicsMask = ~0;

    [Header("Sampling Grid")]
    [SerializeField] private float gridSpacing = 0.06f;           // meters between samples (smaller = denser)
    [SerializeField] private float overhead = 0.7f;               // cast from above
    [SerializeField] private TrackableType trackables =
        TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated |
        TrackableType.FeaturePoint      | TrackableType.Depth;
    [SerializeField] private int maxGridCells = 110000;           // safety cap

    [Header("Contours")]
    [SerializeField] private ContourMode contourMode = ContourMode.SlopePercent;
    [SerializeField] private float elevationIntervalMeters = 0.01f;   // 1 cm
    [SerializeField] private float slopeIntervalPercent = 0.5f;       // 0.5 %
    [SerializeField] private float lineWidth = 0.008f;

    private readonly List<ARRaycastHit> _hits = new();
    private readonly List<LineRenderer> _pool = new();
    private int _poolInUse;

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
        if (!contoursParent)
        {
            var go = new GameObject("Contours");
            contoursParent = go.transform;
        }
    }

    // Call this with your two points (ball/hole or manual) + padding (meters)
    public void AnalyzeBetweenPoints(Vector3 a, Vector3 b, float paddingMeters = 0.5f)
    {
        var minX = Mathf.Min(a.x, b.x) - paddingMeters;
        var maxX = Mathf.Max(a.x, b.x) + paddingMeters;
        var minZ = Mathf.Min(a.z, b.z) - paddingMeters;
        var maxZ = Mathf.Max(a.z, b.z) + paddingMeters;

        AnalyzeBounds(new Bounds(new Vector3((minX+maxX)/2f, 0f, (minZ+maxZ)/2f),
                       new Vector3((maxX-minX), 0.1f, (maxZ-minZ))));
    }

    // Or call with an explicit world-space Bounds (XZ used)
    public void AnalyzeBounds(Bounds bounds)
    {
        ClearContours();

        int nx = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / gridSpacing) + 1);
        int nz = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / gridSpacing) + 1);
        if (nx * nz > maxGridCells)
        {
            float scale = Mathf.Sqrt((float)maxGridCells / (nx * nz));
            gridSpacing /= scale;
            nx = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / gridSpacing) + 1);
            nz = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / gridSpacing) + 1);
        }

        var heights = new float[nx, nz];
        var mask = new bool[nx, nz];
        float yMaxCast = bounds.center.y + bounds.extents.y + overhead;

        // Sample heights
        for (int ix = 0; ix < nx; ix++)
        {
            float x = bounds.min.x + ix * gridSpacing;
            for (int iz = 0; iz < nz; iz++)
            {
                float z = bounds.min.z + iz * gridSpacing;
                var from = new Vector3(x, yMaxCast, z);
                var ray = new Ray(from, Vector3.down);

                if (raycastManager && raycastManager.Raycast(ray, _hits, trackables))
                {
                    heights[ix, iz] = _hits[0].pose.position.y;
                    mask[ix, iz] = true;
                }
                else if (usePhysicsFallback && Physics.Raycast(ray, out var phit, overhead + 2f, physicsMask))
                {
                    heights[ix, iz] = phit.point.y;
                    mask[ix, iz] = true;
                }
                else
                {
                    heights[ix, iz] = float.NaN;
                    mask[ix, iz] = false;
                }
            }
        }

        // Compute slope% field (central differences on height)
        var slopes = new float[nx, nz];
        float inv2dx = 1f / (2f * gridSpacing);
        float inv2dz = inv2dx;

        for (int ix = 1; ix < nx - 1; ix++)
        {
            for (int iz = 1; iz < nz - 1; iz++)
            {
                if (!mask[ix, iz] || !mask[ix - 1, iz] || !mask[ix + 1, iz] || !mask[ix, iz - 1] || !mask[ix, iz + 1])
                {
                    slopes[ix, iz] = float.NaN;
                    continue;
                }

                float dzdx = (heights[ix + 1, iz] - heights[ix - 1, iz]) * inv2dx;
                float dzdz = (heights[ix, iz + 1] - heights[ix, iz - 1]) * inv2dz;
                float m = Mathf.Sqrt(dzdx * dzdx + dzdz * dzdz); // rise/run
                slopes[ix, iz] = m * 100f; // percent
            }
        }

        // Pick field + contours
        if (contourMode == ContourMode.Elevation)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                if (mask[ix, iz])
                { min = Mathf.Min(min, heights[ix, iz]); max = Mathf.Max(max, heights[ix, iz]); }

            if (float.IsInfinity(min) || float.IsInfinity(max)) return;

            for (float level = min; level <= max; level += elevationIntervalMeters)
                DrawContourIso(heights, mask, level, bounds.min, nx, nz);
        }
        else // SlopePercent
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                if (!float.IsNaN(slopes[ix, iz]))
                { min = Mathf.Min(min, slopes[ix, iz]); max = Mathf.Max(max, slopes[ix, iz]); }

            if (float.IsInfinity(min) || float.IsInfinity(max)) return;

            for (float level = min; level <= max; level += slopeIntervalPercent)
                DrawContourIso(slopes, slopesMask: slopes, level: level, origin: bounds.min, nx: nx, nz: nz, isSlopeField: true);
        }
    }

    // ----- Contour drawing (marching squares) -----

    private void DrawContourIso(float[,] field, bool[,] mask, float level, Vector3 origin, int nx, int nz, bool isSlopeField = false)
    {
        // Marching squares per cell, collect short segments and stitch into polylines
        var segments = new List<(Vector3 a, Vector3 b)>();

        for (int ix = 0; ix < nx - 1; ix++)
        {
            for (int iz = 0; iz < nz - 1; iz++)
            {
                // Skip if any corner missing
                if (!mask[ix, iz] || !mask[ix + 1, iz] || !mask[ix, iz + 1] || !mask[ix + 1, iz + 1]) continue;

                float v00 = field[ix, iz];
                float v10 = field[ix + 1, iz];
                float v01 = field[ix, iz + 1];
                float v11 = field[ix + 1, iz + 1];

                int caseIndex = 0;
                if (v00 >= level) caseIndex |= 1;
                if (v10 >= level) caseIndex |= 2;
                if (v11 >= level) caseIndex |= 4;
                if (v01 >= level) caseIndex |= 8;

                if (caseIndex == 0 || caseIndex == 15) continue;

                Vector3 p00 = origin + new Vector3(ix * gridSpacing, 0f, iz * gridSpacing);
                Vector3 p10 = origin + new Vector3((ix + 1) * gridSpacing, 0f, iz * gridSpacing);
                Vector3 p01 = origin + new Vector3(ix * gridSpacing, 0f, (iz + 1) * gridSpacing);
                Vector3 p11 = origin + new Vector3((ix + 1) * gridSpacing, 0f, (iz + 1) * gridSpacing);

                Vector3 e0 = LerpIso(p00, p10, v00, v10, level);
                Vector3 e1 = LerpIso(p10, p11, v10, v11, level);
                Vector3 e2 = LerpIso(p11, p01, v11, v01, level);
                Vector3 e3 = LerpIso(p01, p00, v01, v00, level);

                // Cases: emit 1 or 2 segments
                switch (caseIndex)
                {
                    case 1:  case 14: segments.Add((e3, e0)); break;
                    case 2:  case 13: segments.Add((e0, e1)); break;
                    case 4:  case 11: segments.Add((e1, e2)); break;
                    case 8:  case 7:  segments.Add((e2, e3)); break;

                    case 3:  case 12: segments.Add((e3, e1)); break;
                    case 6:  case 9:  segments.Add((e0, e2)); break;

                    case 5:  // saddle: two segments
                        segments.Add((e3, e0));
                        segments.Add((e1, e2));
                        break;
                    case 10: // saddle: two segments
                        segments.Add((e0, e1));
                        segments.Add((e2, e3));
                        break;
                }
            }
        }

        // Stitch segments into polylines (very light)
        var polylines = StitchSegments(segments);

        // Draw
        foreach (var line in polylines)
        {
            var lr = GetLR();
            lr.positionCount = line.Count;
            lr.startWidth = lr.endWidth = lineWidth;
            lr.useWorldSpace = true;

            // lift a tad to avoid z-fighting with ground
            for (int i = 0; i < line.Count; i++)
                line[i] += Vector3.up * 0.002f;

            lr.SetPositions(line.ToArray());
        }
    }

    // Overload that derives mask from NaN values in slope field
    private void DrawContourIso(float[,] slopes, float[,] slopesMask, float level, Vector3 origin, int nx, int nz, bool isSlopeField)
    {
        var mask = new bool[nx, nz];
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                mask[ix, iz] = !float.IsNaN(slopes[ix, iz]);
        DrawContourIso(slopes, mask, level, origin, nx, nz, isSlopeField);
    }

    private static Vector3 LerpIso(Vector3 a, Vector3 b, float va, float vb, float level)
    {
        float t = Mathf.Approximately(va, vb) ? 0.5f : Mathf.InverseLerp(va, vb, level);
        return Vector3.LerpUnclamped(a, b, t);
    }

    private static List<List<Vector3>> StitchSegments(List<(Vector3 a, Vector3 b)> segs)
    {
        var polylines = new List<List<Vector3>>();
        var used = new HashSet<int>();

        for (int i = 0; i < segs.Count; i++)
        {
            if (used.Contains(i)) continue;

            var line = new List<Vector3> { segs[i].a, segs[i].b };
            used.Add(i);

            bool extended;
            do
            {
                extended = false;
                for (int j = 0; j < segs.Count; j++)
                {
                    if (used.Contains(j)) continue;

                    var s = segs[j];
                    if ((line[^1] - s.a).sqrMagnitude < 1e-6f)
                    { line.Add(s.b); used.Add(j); extended = true; }
                    else if ((line[^1] - s.b).sqrMagnitude < 1e-6f)
                    { line.Add(s.a); used.Add(j); extended = true; }
                    else if ((line[0] - s.a).sqrMagnitude < 1e-6f)
                    { line.Insert(0, s.b); used.Add(j); extended = true; }
                    else if ((line[0] - s.b).sqrMagnitude < 1e-6f)
                    { line.Insert(0, s.a); used.Add(j); extended = true; }
                }
            } while (extended);

            polylines.Add(line);
        }

        return polylines;
    }

    private void ClearContours()
    {
        for (int i = 0; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);
        _poolInUse = 0;
    }

    private LineRenderer GetLR()
    {
        if (_poolInUse < _pool.Count)
        {
            var lr = _pool[_poolInUse++];
            lr.gameObject.SetActive(true);
            return lr;
        }
        var inst = Instantiate(contourLinePrefab, contoursParent);
        _pool.Add(inst);
        _poolInUse++;
        return inst;
    }
}
