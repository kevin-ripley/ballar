// LocalSlopeEstimator.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class LocalSlopeEstimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform rayOrigin;                 // controller RayOrigin
    [SerializeField] private ControllerReticle reticle;           // optional
    [SerializeField] private ARRaycastManager raycastManager;     // drag from scene
    [SerializeField] private bool usePhysicsFallback = true;
    [SerializeField] private LayerMask physicsMask = ~0;

    [Header("Sampling")]
    [SerializeField] private float sampleRadius = 0.35f;  // m
    [SerializeField] private int rings = 2;
    [SerializeField] private int samplesPerRing = 12;
    [SerializeField] private float overhead = 0.5f;       // cast from above, straight down
    [SerializeField] private TrackableType trackables =
        TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated |
        TrackableType.FeaturePoint      | TrackableType.Depth;

    [Header("Debug")]
    [SerializeField] private LineRenderer fallLineDebug; // optional

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

    public bool TryEstimateSlope(out float slopePercent, out Vector3 downhillDir, out Vector3 center)
    {
        slopePercent = 0f; downhillDir = Vector3.zero; center = Vector3.zero;
        if (!raycastManager || (!reticle && !rayOrigin)) return false;

        // Center hit: prefer existing reticle hit (stable), else ray from controller
        Pose centerPose;
        if (reticle && reticle.HasHit) centerPose = reticle.HitPose;
        else
        {
            var centerRay = new Ray(rayOrigin.position, rayOrigin.forward);
            if (!raycastManager.Raycast(centerRay, _hits, trackables))
            {
                if (!(usePhysicsFallback && Physics.Raycast(centerRay, out var phit, 10f, physicsMask)))
                    return false;

                var up = phit.normal;
                var fwd = Vector3.ProjectOnPlane(centerRay.direction, up).normalized;
                centerPose = new Pose(phit.point, Quaternion.LookRotation(fwd, up));
            }
            else centerPose = _hits[0].pose;
        }

        center = centerPose.position;

        // Collect samples on horizontal disc
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

        // Fit plane: y = a*x + b*z + c  (world Y is up)
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

        // Gradient magnitude → slope (rise/run). Convert to percent.
        float m = Mathf.Sqrt((float)(a*a + b*b));
        slopePercent = m * 100f;

        // Downhill direction on XZ (negative gradient)
        downhillDir = new Vector3(-(float)a, 0f, -(float)b).normalized;

        if (fallLineDebug)
        {
            fallLineDebug.useWorldSpace = true;
            fallLineDebug.positionCount = 2;
            fallLineDebug.startWidth = fallLineDebug.endWidth = 0.01f;
            fallLineDebug.SetPosition(0, center);
            fallLineDebug.SetPosition(1, center + downhillDir * 0.5f);
            fallLineDebug.enabled = true;
        }

        return true;
    }

    static bool Solve3x3(double a11,double a12,double a13, double a21,double a22,double a23,
                         double a31,double a32,double a33, double b1,double b2,double b3,
                         out double x1, out double x2, out double x3)
    {
        double det = a11*(a22*a33 - a23*a32) - a12*(a21*a33 - a23*a31) + a13*(a21*a32 - a22*a31);
        if (Mathf.Abs((float)det) < 1e-8f) { x1=x2=x3=0; return false; }

        double det1 = b1*(a22*a33 - a23*a32) - a12*(b2*a33 - a23*b3) + a13*(b2*a32 - a22*b3);
        det1 = b1*(a22*a33 - a23*a32) - a12*(b2*a33 - a23*b3) + a13*(b2*a32 - a22*b3);

        double det2 = a11*(b2*a33 - a23*b3) - b1*(a21*a33 - a23*a31) + a13*(a21*b3 - b2*a31);
        double det3 = a11*(a22*b3 - b2*a32) - a12*(a21*b3 - b2*a31) + b1*(a21*a32 - a22*a31);

        x1 = det1 / det; x2 = det2 / det; x3 = det3 / det;
        return true;
    }
}
