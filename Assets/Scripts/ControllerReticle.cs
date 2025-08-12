// ControllerReticle.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
public class ControllerReticle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform rayOrigin;        // assign your RayOrigin (under Right Controller)
    [SerializeField] private GameObject reticleDefault;  // optional: child "default"
    [SerializeField] private GameObject reticleHover;    // optional: child "hover"
    [SerializeField] private ARRaycastManager raycastManager; // drag from scene (XR Origin)

    [Header("Raycast")]
    [SerializeField] private float maxDistance = 20f;
    [SerializeField] private LayerMask physicsMask = ~0;
    [SerializeField] private bool usePhysicsFallback = true;
    [SerializeField] private TrackableType trackables =
        TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated |
        TrackableType.FeaturePoint      | TrackableType.Depth;

    [Header("Behavior")]
    [SerializeField] private float smoothTime = 0.05f;
    [SerializeField] private float defaultDistance = 2f;
    [SerializeField] private float scaleAt1m = 0.03f;  // reticle size at 1m
    [SerializeField] private float surfaceLift = 0.002f;

    public bool HasHit { get; private set; }
    public Pose HitPose { get; private set; }

    readonly List<ARRaycastHit> _hits = new();
    Vector3 _velPos;
    Quaternion _velRot;

    void Awake()
    {
        // Auto-wire if user forgot to assign
        if (!raycastManager)
        {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
            raycastManager = FindFirstObjectByType<ARRaycastManager>(FindObjectsInactive.Exclude);
#else
            raycastManager = FindObjectOfType<ARRaycastManager>();
#endif
        }

        if (!reticleDefault && transform.Find("default")) reticleDefault = transform.Find("default").gameObject;
        if (!reticleHover   && transform.Find("hover"))   reticleHover   = transform.Find("hover").gameObject;
    }

    void LateUpdate()
    {
        if (!rayOrigin) return;

        var ray = new Ray(rayOrigin.position, rayOrigin.forward);

        // 1) Prefer AR (depth/planes/features)
        bool arHit = raycastManager && raycastManager.Raycast(ray, _hits, trackables);
        if (arHit)
        {
            HitPose = _hits[0].pose;
            HasHit = true;
        }
        // 2) Physics fallback (needs MeshCollider on AR meshes)
        else if (usePhysicsFallback && Physics.Raycast(ray, out var phit, maxDistance, physicsMask))
        {
            // Align plane to surface normal; forward projected from controller dir
            var up = phit.normal;
            var fwd = Vector3.ProjectOnPlane(ray.direction, up).normalized;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; // guard
            HitPose = new Pose(phit.point, Quaternion.LookRotation(fwd, up));
            HasHit = true;
        }
        // 3) Nothing—park a billboard in front of camera
        else
        {
            var cam = Camera.main ? Camera.main.transform : rayOrigin;
            HitPose = new Pose(ray.GetPoint(defaultDistance),
                               Quaternion.LookRotation(-cam.forward, Vector3.up));
            HasHit = false;
        }

        // Smooth & lift
        var targetPos = HasHit ? HitPose.position + HitPose.up * surfaceLift : HitPose.position;
        var targetRot = HasHit ? HitPose.rotation : HitPose.rotation;

        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _velPos, smoothTime);
        transform.rotation = SmoothDampRotation(transform.rotation, targetRot, ref _velRot, smoothTime);

        // Scale with distance
        var camT = Camera.main ? Camera.main.transform : rayOrigin;
        float d = Vector3.Distance(camT.position, transform.position);
        float s = Mathf.Max(0.001f, scaleAt1m * d);
        transform.localScale = Vector3.one * s;

        // Toggle visuals like XREAL sample
        if (reticleDefault) reticleDefault.SetActive(!HasHit);
        if (reticleHover)   reticleHover.SetActive(HasHit);
    }

    static Quaternion SmoothDampRotation(Quaternion current, Quaternion target, ref Quaternion deriv, float time)
    {
        if (time <= 0f) return target;
        return Quaternion.Slerp(current, target, 1f - Mathf.Exp(-Time.deltaTime / time));
    }

    // Public setters so you can wire from other scripts if needed
    public void SetRayOrigin(Transform t) => rayOrigin = t;
    public void SetRaycastManager(ARRaycastManager m) => raycastManager = m;
}
