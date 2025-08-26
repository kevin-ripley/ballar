using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DefaultExecutionOrder(50)]
public class GazeReticleStable : MonoBehaviour
{
    [Header("Refs")]
    public GazeRayProvider gaze;          // AR Camera provider
    public ARRaycastManager arRaycast;    // XR Origin
    public GreenSlopeManager greenSlope;  // optional, for mode/colors

    [Header("Visuals")]
    public Transform reticleVisual;       // child quad/circle; auto-created if null
    public Material reticleMaterial;      // assign a URP/Built-in Unlit material
    public bool showRay = true;
    public Material rayMaterial;          // assign Unlit
    public float rayWidth = 0.0025f;

    [Header("Behavior")]
    public float defaultDistance = 2.0f;     // when no hit
    public float liftAboveSurface = 0.003f;  // to avoid z-fighting & occlusion
    public float sizeAt1m = 0.02f;           // 2 cm at 1 m
    public float minSize = 0.008f, maxSize = 0.06f;
    public LayerMask physicsMask = ~0;       // for fallback only

    [Header("Hit Filtering / Stability")]
    public bool preferPlanesOnly = true;     // reject depth/feature unless no planes seen recently
    public float wallRejectDot = 0.5f;       // plane.up·worldUp threshold
    public float maxJumpMeters = 0.15f;      // ignore sudden jumps
    public int   settleFrames = 2;           // require N consecutive frames before committing
    public int   holdWhenLostFrames = 6;     // keep last pose for a few frames when hits are lost
    public float minDistance = 0.25f, maxDistance = 8.0f;

    [Header("Smoothing")]
    [Range(0,1)] public float posLerp = 0.18f;
    [Range(0,1)] public float rotLerp = 0.18f;

    [Header("Colors")]
    public Color validColor     = new(0.1f, 1f, 0.4f, 0.95f);
    public Color invalidColor   = new(1f, 0.3f, 0.3f, 0.95f);
    public Color samplingColor  = new(1f, 0.85f, 0.2f, 1f);
    public Color holeModeColor  = new(0.2f, 0.8f, 1f, 1f);   // before hole placed
    public Color boundModeColor = new(1f, 0.45f, 0.1f, 1f);  // adding boundary

    [Header("Boundary Preview")]
    public bool showBoundaryPreview = true;
    public Material previewMaterial;     // unlit for preview line
    public float previewWidth = 0.008f;

    // internals
    readonly List<ARRaycastHit> _hits = new();
    LineRenderer _ray, _preview;
    Material _reticleMat, _rayMat, _previewMat;
    Vector3 _fPos; Quaternion _fRot;
    bool _inited;
    Vector3 _lastCommittedPos; Quaternion _lastCommittedRot;
    int _consistentFrames, _lostFrames;
    bool _hadPlaneRecently;

    // AI Integration - safely initialized
    private SmartRaycastManager smartRaycast;

    void Awake()
    {
        // Ensure a visible child
        if (!reticleVisual)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ReticleVisual";
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0); // face up
            var col = quad.GetComponent<Collider>(); if (col) Destroy(col);
            reticleVisual = quad.transform;
        }

        // Materials
        if (!reticleMaterial) reticleMaterial = PickUnlitShaderMaterial();
        _reticleMat = new Material(reticleMaterial) { enableInstancing = true };
        var rend = reticleVisual.GetComponentInChildren<Renderer>(); if (rend) rend.material = _reticleMat;

        if (showRay)
        {
            _ray = gameObject.AddComponent<LineRenderer>();
            _ray.useWorldSpace = true; _ray.positionCount = 2; _ray.widthMultiplier = rayWidth;
            if (!rayMaterial) rayMaterial = PickUnlitShaderMaterial();
            _rayMat = new Material(rayMaterial) { enableInstancing = true };
            _ray.material = _rayMat; _ray.enabled = true;
        }

        if (showBoundaryPreview)
        {
            _preview = gameObject.AddComponent<LineRenderer>();
            _preview.useWorldSpace = true; _preview.positionCount = 2; _preview.widthMultiplier = previewWidth;
            if (!previewMaterial) previewMaterial = PickUnlitShaderMaterial();
            _previewMat = new Material(previewMaterial) { enableInstancing = true };
            _preview.material = _previewMat; _preview.enabled = false;
        }
    }

    void Start()
    {
        // SAFE: Get reference to AI manager in Start() instead of Awake()
        // This ensures SmartRaycastManager has been initialized first
        TryGetSmartRaycast();
    }

    void TryGetSmartRaycast()
    {
        if (smartRaycast == null)
        {
            smartRaycast = SmartRaycastManager.Instance;
            if (smartRaycast == null)
            {
                // Try finding it manually as backup
                smartRaycast = FindFirstObjectByType<SmartRaycastManager>();
            }
        }
    }

    void LateUpdate()
    {
        if (!gaze) return;

        // Ensure we have AI reference (in case it was added later)
        if (smartRaycast == null) TryGetSmartRaycast();

        // 1) Propose a target pose from gaze
        var camRay = gaze.GetRay();
        Vector3 tgtPos; Vector3 surfaceUp; bool gotHit, planeHit;
        (gotHit, planeHit, tgtPos, surfaceUp) = TryGetStableHit(camRay);

        // default float if no hit
        if (!gotHit)
        {
            tgtPos = camRay.origin + camRay.direction * defaultDistance;
            surfaceUp = Vector3.up;
        }

        // 2) Clamp distance
        var camPos = Camera.main ? Camera.main.transform.position : camRay.origin;
        float d = Vector3.Distance(camPos, tgtPos);
        if (d < minDistance) { tgtPos = camPos + (tgtPos - camPos).normalized * minDistance; }
        if (d > maxDistance) { tgtPos = camPos + (tgtPos - camPos).normalized * maxDistance; }

        // 3) Build a nice rotation
        var cam = Camera.main ? Camera.main.transform : null;
        var forwardOnPlane = cam ? Vector3.ProjectOnPlane(cam.forward, surfaceUp) : Vector3.forward;
        if (forwardOnPlane.sqrMagnitude < 1e-6f) forwardOnPlane = Vector3.forward;
        var tgtRot = Quaternion.LookRotation(forwardOnPlane.normalized, surfaceUp);

        // 4) Initialize smoothing once
        if (!_inited) { _fPos = tgtPos; _fRot = tgtRot; _lastCommittedPos = tgtPos; _lastCommittedRot = tgtRot; _inited = true; }

        // 5) Hysteresis / stickiness
        bool accept = (Vector3.Distance(tgtPos, _lastCommittedPos) <= maxJumpMeters);
        if (gotHit && accept)
        {
            _consistentFrames++;
            if (_consistentFrames >= Mathf.Max(1, settleFrames))
            {
                _lastCommittedPos = tgtPos;
                _lastCommittedRot = tgtRot;
                _lostFrames = 0;
            }
        }
        else
        {
            // lost or big jump: hold last
            _lostFrames++;
            if (_lostFrames > holdWhenLostFrames)
            {
                // after holding a bit, allow new target even if jumpy
                _lastCommittedPos = tgtPos;
                _lastCommittedRot = tgtRot;
                _lostFrames = 0;
            }
            _consistentFrames = 0;
        }

        // 6) Smooth towards committed pose
        _fPos = Vector3.Lerp(_fPos, _lastCommittedPos + surfaceUp * liftAboveSurface, posLerp);
        _fRot = Quaternion.Slerp(_fRot, _lastCommittedRot, rotLerp);
        transform.SetPositionAndRotation(_fPos, _fRot);

        // 7) Scale reticle with distance
        float dist = Vector3.Distance(camPos, _fPos);
        float s = Mathf.Clamp(sizeAt1m * Mathf.Max(0.25f, dist), minSize, maxSize);
        reticleVisual.localScale = Vector3.one * s;

        // 8) Colors & lines
        var c = gotHit ? validColor : invalidColor;
        if (greenSlope)
            c = !greenSlope.HasHole ? (gotHit ? holeModeColor : invalidColor)
                                    : (gotHit ? boundModeColor : invalidColor);

        var putt = FindFirstObjectByType<PuttLineManager>(FindObjectsInactive.Include);
        if (putt && putt.currentState == PuttLineManager.PlacementState.Sampling) c = samplingColor;

        SetMatColor(_reticleMat, c);
        if (_ray) { SetMatColor(_rayMat, c); _ray.SetPosition(0, camPos); _ray.SetPosition(1, _fPos); _ray.enabled = showRay; }

        // Boundary preview
        if (_preview && greenSlope && greenSlope.BoundaryCount > 0 && gotHit && greenSlope.TryGetLastBoundaryPoint(out var last))
        {
            _preview.enabled = true;
            _preview.SetPosition(0, last + Vector3.up * liftAboveSurface);
            _preview.SetPosition(1, _fPos);
            SetMatColor(_previewMat, boundModeColor);
        }
        else if (_preview) _preview.enabled = false;
    }

    // ----------------- Hit logic with stability (AI OPTIMIZED) -----------------
    (bool gotHit, bool planeHit, Vector3 pos, Vector3 up) TryGetStableHit(Ray camRay)
    {
        Vector3 hitPos = default, hitUp = Vector3.up;
        bool got = false, plane = false;
        
        // Use AI-optimized raycast if available
        if (smartRaycast != null && smartRaycast.GetStableHit(camRay, out hitPos, out hitUp, out plane))
        {
            if (plane && Vector3.Dot(hitUp, Vector3.up) < wallRejectDot)
            {
                got = false;
            }
            else
            {
                got = true;
                if (plane) _hadPlaneRecently = true;
            }
        }
        // Fallback to direct AR raycast
        else if (arRaycast && arRaycast.Raycast(camRay, _hits,
            (preferPlanesOnly ? TrackableType.PlaneWithinPolygon
                              : TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth)))
        {
            var h = _hits[0];
            hitPos = h.pose.position;
            plane  = (h.hitType & TrackableType.PlaneWithinPolygon) != 0;
            hitUp  = plane ? h.pose.up : Vector3.up;
            if (plane && Vector3.Dot(hitUp, Vector3.up) < wallRejectDot) { got = false; }
            else { got = true; }
            if (plane) _hadPlaneRecently = true;
        }

        // optional fallback to depth/feature if no plane recently and planes-only is on
        if (!got && preferPlanesOnly && !_hadPlaneRecently)
        {
            if (smartRaycast != null)
            {
                if (smartRaycast.SmartRaycast(camRay, out var hit, TrackableType.FeaturePoint | TrackableType.Depth))
                {
                    hitPos = hit.pose.position; hitUp = Vector3.up; got = true; plane = false;
                }
            }
            else if (arRaycast && arRaycast.Raycast(camRay, _hits, TrackableType.FeaturePoint | TrackableType.Depth))
            {
                var h = _hits[0];
                hitPos = h.pose.position; hitUp = Vector3.up; got = true; plane = false;
            }
        }

        // physics fallback (only if we have colliders in scene)
        if (!got)
        {
            if (Physics.SphereCast(camRay, 0.01f, out var ph, 20f, physicsMask))
            {
                hitPos = ph.point; hitUp = ph.normal; got = true; plane = false;
            }
        }
        return (got, plane, hitPos, hitUp);
    }

    // ----------------- Material helpers -----------------
    Material PickUnlitShaderMaterial()
    {
        Shader sh = null;
        bool isSRP = GraphicsSettings.currentRenderPipeline != null;
        if (isSRP)
        {
            sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("HDRP/Unlit");
        }
        if (!sh) sh = Shader.Find("Unlit/Color");
        if (!sh) sh = Shader.Find("Sprites/Default");
        if (!sh) sh = Shader.Find("Standard");
        var m = new Material(sh) { enableInstancing = true };
        if (m.shader && m.shader.name == "Standard")
        {
            // Transparent for reticle
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }

    void SetMatColor(Material m, Color c)
    {
        if (!m) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c);
        else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }
}