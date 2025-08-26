using UnityEngine;
using UnityEngine.InputSystem;

public class GreenSlopeHUDlessControls : MonoBehaviour
{
    [Header("XREAL Custom Buttons (InputActionReferences)")]
    public InputActionReference toggleHighlightAction; // tap = toggle on/off
    public InputActionReference thresholdUpAction;     // tap = +step; hold = +scrub
    public InputActionReference thresholdDownAction;   // tap = -step; hold = -scrub
    public InputActionReference thresholdResetAction;  // optional

    [Header("Target")]
    public GreenSlopeManager greenSlope; // assign or auto-find

    [Header("Behavior")]
    [Tooltip("Step size in percent (e.g., 0.05 => 0.05%) per tap")]
    public float stepSize = 0.05f;
    [Tooltip("Scrub speed in percent per second while holding")]
    public float holdRate = 0.30f;
    [Tooltip("Min/Max clamp (percent)")]
    public float minPercent = 0.00f, maxPercent = 1.00f;
    [Tooltip("Default reset value (percent)")]
    public float defaultPercent = 0.30f;
    [Tooltip("Debounce for toggle (seconds)")]
    public float toggleDebounce = 0.18f;

    [Header("New Intelligent Analysis Control")]
    public InputActionReference intelligentModeAction; // New: toggle intelligent vs standard analysis

    // internal
    bool _holdUp, _holdDown;
    double _lastToggleTime = -1;

    void OnEnable()
    {
        if (!greenSlope) greenSlope = FindOne<GreenSlopeManager>();

        // Toggle
        if (toggleHighlightAction != null)
        {
            toggleHighlightAction.action.performed += OnToggle;
            toggleHighlightAction.action.Enable();
        }

        // Up
        if (thresholdUpAction != null)
        {
            thresholdUpAction.action.started += OnUpStarted;
            thresholdUpAction.action.canceled += OnUpCanceled;
            thresholdUpAction.action.performed += OnUpPerformed; // some devices only send performed
            thresholdUpAction.action.Enable();
        }

        // Down
        if (thresholdDownAction != null)
        {
            thresholdDownAction.action.started += OnDownStarted;
            thresholdDownAction.action.canceled += OnDownCanceled;
            thresholdDownAction.action.performed += OnDownPerformed;
            thresholdDownAction.action.Enable();
        }

        // Reset (optional)
        if (thresholdResetAction != null)
        {
            thresholdResetAction.action.performed += OnReset;
            thresholdResetAction.action.Enable();
        }

        if (intelligentModeAction != null)
        {
            intelligentModeAction.action.performed += OnIntelligentModeToggle;
            intelligentModeAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (toggleHighlightAction != null) toggleHighlightAction.action.performed -= OnToggle;

        if (thresholdUpAction != null)
        {
            thresholdUpAction.action.started -= OnUpStarted;
            thresholdUpAction.action.canceled -= OnUpCanceled;
            thresholdUpAction.action.performed -= OnUpPerformed;
        }

        if (thresholdDownAction != null)
        {
            thresholdDownAction.action.started -= OnDownStarted;
            thresholdDownAction.action.canceled -= OnDownCanceled;
            thresholdDownAction.action.performed -= OnDownPerformed;
        }

        if (thresholdResetAction != null) thresholdResetAction.action.performed -= OnReset;
    }

    void Update()
    {
        if (!greenSlope) return;

        float dt = Time.deltaTime;
        float v = greenSlope.ZeroThreshold;

        if (_holdUp) v += holdRate * dt;
        if (_holdDown) v -= holdRate * dt;

        v = Mathf.Clamp(v, minPercent, maxPercent);

        if (_holdUp || _holdDown)
            greenSlope.ZeroThreshold = v; // updates isoline immediately
    }

    // --- Button callbacks ---
    void OnToggle(InputAction.CallbackContext ctx)
    {
        if (!greenSlope) return;
        if (_lastToggleTime > 0 && ctx.time - _lastToggleTime < toggleDebounce) return;
        greenSlope.ZeroHighlightEnabled = !greenSlope.ZeroHighlightEnabled;
        _lastToggleTime = ctx.time;
    }

    void OnUpStarted(InputAction.CallbackContext ctx) { _holdUp = true; }
    void OnUpCanceled(InputAction.CallbackContext ctx) { _holdUp = false; }
    void OnDownStarted(InputAction.CallbackContext ctx) { _holdDown = true; }
    void OnDownCanceled(InputAction.CallbackContext ctx) { _holdDown = false; }

    // For devices that only send performed
    void OnUpPerformed(InputAction.CallbackContext ctx)
    {
        if (!greenSlope) return;
        float v = Mathf.Clamp(greenSlope.ZeroThreshold + stepSize, minPercent, maxPercent);
        greenSlope.ZeroThreshold = v;
    }
    void OnDownPerformed(InputAction.CallbackContext ctx)
    {
        if (!greenSlope) return;
        float v = Mathf.Clamp(greenSlope.ZeroThreshold - stepSize, minPercent, maxPercent);
        greenSlope.ZeroThreshold = v;
    }

    void OnReset(InputAction.CallbackContext ctx)
    {
        if (!greenSlope) return;
        greenSlope.ZeroThreshold = Mathf.Clamp(defaultPercent, minPercent, maxPercent);
    }

    // --- Unity 2023+/Unity 6 safe find ---
    private static T FindOne<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
        return Object.FindObjectOfType<T>(true);
#endif
    }
    
    private void OnIntelligentModeToggle(InputAction.CallbackContext ctx)
{
    var intelligentSampler = greenSlope?.GetComponent<IntelligentTerrainSampler>();
    if (intelligentSampler)
    {
        intelligentSampler.enableAdaptiveDensity = !intelligentSampler.enableAdaptiveDensity;
        Debug.Log($"Intelligent sampling: {(intelligentSampler.enableAdaptiveDensity ? "ON" : "OFF")}");
    }
}
}
