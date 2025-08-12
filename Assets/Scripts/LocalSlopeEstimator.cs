// LocalSlopeEstimator.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class LocalSlopeEstimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARRaycastManager raycastManager; // drag from XR Origin
    [SerializeField] private bool usePhysicsFallback = true;
    [SerializeField] private LayerMask physicsMask = ~0;

    [Header("Sampling")]
    [SerializeField] private float sampleRadius = 0.25f; // tighter neighborhood reduces curvature/noise
    [SerializeField] private int rings = 2;
    [SerializeField] private int samplesPerRing = 12;
    [SerializeField] private float overhead = 0.8f;     // cast from above, straight down
    [SerializeField] private TrackableType trackables =
        TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated |
        TrackableType.FeaturePoint | TrackableType.Depth;

    [Header("Robust fit")]
    [SerializeField] private float madK = 2.5f;         // reject samples farther than k * MAD from the plane

    readonly List<ARRaycastHit> _hits = new();

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
    }

    /// <summary>
    /// Estimates local plane at 'center', returning:
    ///  - slopePercent (total) = tan(tilt)*100
    ///  - crossSlopePercent   = component of slope perpendicular to putt line
    ///  - downhillDir (on plane, XZ-ish) = direction of steepest descent
    /// Supply lineDirWorld (normalized) to get cross-slope; if zero, crossSlopePercent= slopePercent.
    /// </summary>
    public bool TryEstimateSlopeAt(Vector3 center,
                                   Vector3 lineDirWorld,
                                   out float slopePercent,
                                   out float crossSlopePercent,
                                   out Vector3 downhillDir)
    {
        slopePercent = 0f; crossSlopePercent = 0f; downhillDir = Vector3.zero;
        if (!raycastManager) return false;

        // 1) Sample a horizontal disc around center
        var pts = new List<Vector3>(1 + rings * samplesPerRing) { center };

        for (int r = 1; r <= rings; r++)
        {
            float rad = (sampleRadius * r) / rings;
            for (int i = 0; i < samplesPerRing; i++)
            {
                float ang = (i / (float)samplesPerRing) * Mathf.PI * 2f;
                var offsetXZ = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                var from = center + Vector3.up * overhead + offsetXZ;
                var ray = new Ray(from, Vector3.down);

                if (raycastManager.Raycast(ray, _hits, trackables))
                    pts.Add(_hits[0].pose.position);
                else if (usePhysicsFallback && Physics.Raycast(ray, out var phit, overhead + 1.0f, physicsMask))
                    pts.Add(phit.point);
            }
        }
        if (pts.Count < 6) return false;

        // 2) First LSQ plane fit (y = a*x + b*z + c)
        if (!FitPlaneLSQ(pts, out double a, out double b, out double c)) return false;

        // 3) Robustify: compute residuals (perp distance), reject outliers via MAD, refit
        var residuals = new List<float>(pts.Count);
        PlaneFromABC(a,b,c, out Vector3 n0, out float d0);
        foreach (var p in pts) residuals.Add(PerpDistance(n0, d0, p));

        float med = Median(residuals);
        var absDev = new List<float>(residuals.Count);
        for (int i=0;i<residuals.Count;i++) absDev.Add(Mathf.Abs(residuals[i]-med));
        float MAD = Median(absDev);
        if (MAD > 1e-5f)
        {
            float thresh = med + madK * MAD;
            var inliers = new List<Vector3>(pts.Count);
            for (int i=0;i<pts.Count;i++) if (residuals[i] <= thresh) inliers.Add(pts[i]);
            if (inliers.Count >= 6 && FitPlaneLSQ(inliers, out a, out b, out c))
            {
                PlaneFromABC(a,b,c, out n0, out d0);
                pts = inliers;
            }
        }

        // 4) Use gravity for "up"
        Vector3 up = (-Physics.gravity).sqrMagnitude > 0.001f ? -Physics.gravity.normalized : Vector3.up;

        // plane normal (ensure pointing "up")
        Vector3 n = n0;
        if (Vector3.Dot(n, up) < 0f) n = -n;

        // 5) Slope % = tan(tilt) * 100, where tilt = angle between plane & horizontal
        // angle between normal and up:
        float cos = Mathf.Clamp(Vector3.Dot(n.normalized, up), -1f, 1f);
        float tiltRad = Mathf.Acos(cos);                 // 0 = flat
        float slope = Mathf.Tan(tiltRad);                // rise/run
        slopePercent = slope * 100f;

        // 6) Downhill direction: project gravity onto plane
        Vector3 gravityDown = Physics.gravity.sqrMagnitude > 0.001f ? Physics.gravity.normalized : Vector3.down;
        Vector3 downOnPlane = Vector3.ProjectOnPlane(gravityDown, n);
        if (downOnPlane.sqrMagnitude < 1e-6f) downOnPlane = Vector3.ProjectOnPlane(Vector3.down, n);
        downhillDir = downOnPlane.normalized;

        // 7) Cross-slope component relative to putt line
        if (lineDirWorld.sqrMagnitude > 1e-6f)
        {
            Vector3 lineDir = lineDirWorld; lineDir.y = 0f; lineDir.Normalize();
            // unit vector perpendicular to line (to the "right" of the line)
            Vector3 crossDir = Vector3.Cross(Vector3.up, lineDir).normalized;
            float component = Mathf.Abs(Vector3.Dot(downhillDir, crossDir)); // 0..1
            crossSlopePercent = slopePercent * component;
        }
        else
        {
            crossSlopePercent = slopePercent;
        }

        return true;
    }

    // -------- helpers --------

    static bool FitPlaneLSQ(List<Vector3> pts, out double a, out double b, out double c)
    {
        a=b=c=0;
        double Suu=0, Suv=0, Su=0, Svv=0, Sv=0, S1=pts.Count;
        double Suw=0, Svw=0, Sw=0;
        for (int i=0;i<pts.Count;i++)
        {
            var p = pts[i];
            double u=p.x, v=p.z, w=p.y;
            Suu += u*u;  Suv += u*v;  Su += u;
            Svv += v*v;  Sv  += v;    Sw += w;
            Suw += u*w;  Svw += v*w;
        }
        return Solve3x3(
            Suu, Suv, Su,
            Suv, Svv, Sv,
            Su,  Sv,  S1,
            Suw, Svw, Sw,
            out a, out b, out c);
    }

    static void PlaneFromABC(double a, double b, double c, out Vector3 n, out float d)
    {
        // y = a x + b z + c  ->  a x - y + b z + c = 0  => normal = (a, -1, b)
        n = new Vector3((float)a, -1f, (float)b).normalized;
        // plane form n·p + d = 0; choose a point p0 with x=0,z=0,y=c => d = n·p0
        d = Vector3.Dot(n, new Vector3(0f, (float)c, 0f));
    }

    static float PerpDistance(Vector3 n, float d, Vector3 p)
    {
        // |n·p + d| (n is unit)
        return Mathf.Abs(Vector3.Dot(n, p) + d);
    }

    static bool Solve3x3(double a11,double a12,double a13,
                         double a21,double a22,double a23,
                         double a31,double a32,double a33,
                         double b1,double b2,double b3,
                         out double x1, out double x2, out double x3)
    {
        double det = a11*(a22*a33 - a23*a32) - a12*(a21*a33 - a23*a31) + a13*(a21*a32 - a22*a31);
        if (Mathf.Abs((float)det) < 1e-10f) { x1=x2=x3=0; return false; }

        double det1 = b1*(a22*a33 - a23*a32) - a12*(b2*a33 - a23*b3) + a13*(b2*a32 - a22*b3);
        double det2 = a11*(b2*a33 - a23*b3) - b1*(a21*a33 - a23*a31) + a13*(a21*b3 - b2*a31);
        double det3 = a11*(a22*b3 - b2*a32) - a12*(a21*b3 - b2*a31) + b1*(a21*a32 - a22*a31);

        x1 = det1 / det; x2 = det2 / det; x3 = det3 / det;
        return true;
    }

    static float Median(List<float> xs)
    {
        if (xs.Count == 0) return 0f;
        xs.Sort();
        int mid = xs.Count / 2;
        return (xs.Count % 2 == 1) ? xs[mid] : 0.5f*(xs[mid-1]+xs[mid]);
    }
}
