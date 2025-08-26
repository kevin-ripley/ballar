using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DefaultExecutionOrder(50)]
public class GazeReticle : MonoBehaviour
{
    [Header("References")]
    public GazeRayProvider gaze;              // AR Camera provider
    public ARRaycastManager arRaycast;        // XR Origin
    public PuttLineManager puttManager;       // (Optional) to color during Sampling
    public Transform reticleVisual;           // Child quad/circle (auto-created if null)

    [Header("Materials (assign to avoid pink)")]
    public Material reticleMaterialOverride;  // Assign a URP Unlit (or Built-in Unlit) material
    public Material lineMaterialOverride;     // Assign a matching Unlit material for the ray

    [Header("Ray (Line Renderer)")]
    public bool showRay = true;
    public LineRenderer debugLine;            // Auto-created if null and showRay = true
    public float lineWidth = 0.0025f;

    [Header("Physics Fallback")]
    public LayerMask physicsMask = ~0;

    [Header("Behavior")]
    public float defaultDistance = 2.0f;      // When no hit, float here
    public float sizeAt1m = 0.02f;            // Reticle size scales with distance
    public float minSize = 0.008f;
    public float maxSize = 0.06f;

    [Header("Smoothing")]
    [Range(0f, 1f)] public float positionSmoothing = 0.2f;
    [Range(0f, 1f)] public float rotationSmoothing = 0.2f;

    [Header("Colors")]
    public Color validColor = new Color(0.1f, 1f, 0.4f, 0.9f);
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.9f);
    public Color samplingColor = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("Green Slope Integration (optional)")]
    public GreenSlopeManager greenSlope;     // assign in GreenSlopeScene
    public bool showBoundaryPreview = true;
    public LineRenderer previewLine;         // auto-created if null when preview enabled
    public Color holeModeColor = new Color(0.2f, 0.8f, 1f, 1f);  // cyan-ish while placing HOLE
    public Color boundaryModeColor = new Color(1f, 0.45f, 0.1f, 1f); // orange while adding BOUNDARY


    private readonly List<ARRaycastHit> _hits = new();
    private Vector3 _fPos;
    private Quaternion _fRot;
    private bool _inited;

    private Material _reticleMat;    // runtime instance
    private Material _lineMat;       // runtime instance

    void Awake()
    {
        // Ensure there is a visible child
        if (!reticleVisual)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ReticleVisual";
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0); // face up
            reticleVisual = quad.transform;
            var col = quad.GetComponent<Collider>(); if (col) Destroy(col);
        }

        // ----- Reticle material -----
        var rend = reticleVisual.GetComponentInChildren<Renderer>();
        if (rend)
        {
            if (reticleMaterialOverride != null)
            {
                _reticleMat = new Material(reticleMaterialOverride); // instance
            }
            else
            {
                var sh = FindBestUnlitShader();
                _reticleMat = new Material(sh) { enableInstancing = true };
            }

            EnsureTransparent(_reticleMat);
            SetMatColor(_reticleMat, validColor);
            rend.material = _reticleMat; // assign instance (avoid sharedMaterial)
        }

        // ----- Line renderer -----
        if (showRay)
        {
            if (!debugLine)
            {
                debugLine = gameObject.AddComponent<LineRenderer>();
                debugLine.positionCount = 2;
                debugLine.useWorldSpace = true;
            }
            debugLine.widthMultiplier = Mathf.Max(0.0005f, lineWidth);

            if (lineMaterialOverride != null)
            {
                _lineMat = new Material(lineMaterialOverride);
            }
            else
            {
                var sh = FindBestUnlitShader();
                _lineMat = new Material(sh) { enableInstancing = true };
            }
            EnsureOpaque(_lineMat); // thin line; opaque is fine
            SetMatColor(_lineMat, validColor);
            debugLine.material = _lineMat;
            debugLine.enabled = true;
        }
        else if (debugLine)
        {
            debugLine.enabled = false;
        }

        if (showBoundaryPreview && !previewLine)
        {
            previewLine = gameObject.AddComponent<LineRenderer>();
            previewLine.positionCount = 2;
            previewLine.useWorldSpace = true;
            previewLine.widthMultiplier = 0.008f;
            // Use a known-good material (reuse lineMaterialOverride if you have it)
            if (lineMaterialOverride) previewLine.material = new Material(lineMaterialOverride);
            previewLine.enabled = false;
        }
    }

    void LateUpdate()
    {
        if (!gaze) return;

        var camRay = gaze.GetRay();
        bool gotHit = TryARRaycast(camRay, out Vector3 hitPos, out Vector3 hitUp);

        if (!gotHit)
        {
            if (Physics.SphereCast(camRay, 0.01f, out RaycastHit h, 20f, physicsMask))
            {
                hitPos = h.point;
                hitUp = h.normal;
                gotHit = true;
            }
        }

        Vector3 targetPos;
        Quaternion targetRot;

        if (gotHit)
        {
            var cam = Camera.main ? Camera.main.transform : null;
            var fwdOnPlane = cam ? Vector3.ProjectOnPlane(cam.forward, hitUp) : Vector3.Cross(hitUp, Vector3.right);
            if (fwdOnPlane.sqrMagnitude < 1e-6f) fwdOnPlane = Vector3.forward;
            targetRot = Quaternion.LookRotation(fwdOnPlane.normalized, hitUp);
            targetPos = hitPos + hitUp * 0.002f; // lift to avoid z-fighting
        }
        else
        {
            targetPos = camRay.origin + camRay.direction * defaultDistance;
            targetRot = Quaternion.LookRotation(-camRay.direction, Vector3.up);
        }

        if (!_inited) { _fPos = targetPos; _fRot = targetRot; _inited = true; }
        _fPos = Vector3.Lerp(_fPos, targetPos, positionSmoothing);
        _fRot = Quaternion.Slerp(_fRot, targetRot, rotationSmoothing);
        transform.SetPositionAndRotation(_fPos, _fRot);

        float dist = Vector3.Distance(Camera.main ? Camera.main.transform.position : camRay.origin, _fPos);
        float s = Mathf.Clamp(sizeAt1m * Mathf.Max(0.2f, dist), minSize, maxSize);
        reticleVisual.localScale = Vector3.one * s;

        // Color
        var c = gotHit ? validColor : invalidColor;
        if (puttManager && puttManager.currentState == PuttLineManager.PlacementState.Sampling) c = samplingColor;
        // --- GreenSlope-aware coloring ---
        if (greenSlope)
        {
            // If the user hasn't placed the hole yet, use holeModeColor on valid hits
            if (!greenSlope.HasHole) c = gotHit ? holeModeColor : invalidColor;
            else c = gotHit ? boundaryModeColor : invalidColor;
        }
        if (_reticleMat) SetMatColor(_reticleMat, c);
        if (_lineMat) SetMatColor(_lineMat, c);

        // Ray line
        if (debugLine)
        {
            var camPos = Camera.main ? Camera.main.transform.position : camRay.origin;
            debugLine.SetPosition(0, camPos);
            debugLine.SetPosition(1, _fPos);
        }

        // --- Preview: line from LAST boundary point -> current gaze hit ---
        if (showBoundaryPreview && previewLine)
        {
            if (greenSlope && greenSlope.BoundaryCount > 0 && gotHit && greenSlope.TryGetLastBoundaryPoint(out var last))
            {
                var p0 = last + Vector3.up * 0.001f;
                var p1 = targetPos + Vector3.up * 0.001f;
                previewLine.SetPosition(0, p0);
                previewLine.SetPosition(1, p1);
                // color to match boundary mode
                SetMatColor(previewLine.material, boundaryModeColor);
                previewLine.enabled = true;
            }
            else
            {
                previewLine.enabled = false;
            }
        }
    }


    // ---------- Helpers ----------

    private Shader FindBestUnlitShader()
    {
        // Detect render pipeline
        bool isSRP = GraphicsSettings.currentRenderPipeline != null;
        Shader sh = null;

        if (isSRP)
        {
            // Try URP first
            sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit"); // fallback
            // Try HDRP if URP not present
            if (sh == null) sh = Shader.Find("HDRP/Unlit");
            if (sh == null) sh = Shader.Find("HDRP/Lit");
        }
        // Built-in pipeline fallbacks
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Standard");

        if (sh == null)
        {
            Debug.LogWarning("GazeReticle: Could not find an Unlit shader. Using Standard (may render pink in SRP).");
            sh = Shader.Find("Standard");
        }
        return sh;
    }

    private void EnsureTransparent(Material m)
    {
        // If the shader supports a base color, we’ll leave blending to the shader.
        // For Standard in Built-in, enable transparency so alpha works.
        if (m.shader != null && m.shader.name == "Standard")
        {
            m.SetFloat("_Mode", 3); // Transparent
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    private void EnsureOpaque(Material m)
    {
        // For line material, opaque is fine (keeps it crisp).
        if (m.shader != null && m.shader.name == "Standard")
        {
            m.SetFloat("_Mode", 0); // Opaque
            m.SetInt("_ZWrite", 1);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Geometry;
        }
    }

    private void SetMatColor(Material m, Color c)
    {
        if (!m) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);  // URP/HDRP
        else if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c); // some unlit variants
        else if (m.HasProperty("_Color")) m.SetColor("_Color", c);     // Built-in / Sprites
    }

    private bool TryARRaycast(Ray worldRay, out Vector3 hitPos, out Vector3 hitUp)
    {
        hitPos = default; hitUp = Vector3.up;
        if (arRaycast && arRaycast.Raycast(worldRay, _hits,
            TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
        {
            var h = _hits[0];
            hitPos = h.pose.position;

            bool planeHit = (h.hitType & TrackableType.PlaneWithinPolygon) != 0;
            hitUp = planeHit ? h.pose.up : Vector3.up;

            // Relaxed wall filter for planes
            if (planeHit && Vector3.Dot(hitUp, Vector3.up) < 0.5f) return false;

            return true;
        }
        return false;
    }
}
