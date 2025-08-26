using UnityEngine;
using UnityEngine.InputSystem;

public class GreenSlopeInputBridge : MonoBehaviour
{
    [Header("XREAL Custom Buttons (InputActionReferences)")]
    public InputActionReference placeHoleAction;      // press = PlaceHole()
    public InputActionReference addBoundaryAction;    // press = AddBoundaryVertex()
    public InputActionReference finishBoundaryAction; // press = FinishBoundary()
    public InputActionReference clearAction;          // press = ClearAll()

    [Header("Target")]
    public GreenSlopeManager greenSlope; // drag in Inspector (or auto-find)

    [Header("Behavior")]
    [SerializeField] private float debounceSeconds = 0.15f;
    private double _lastActionTime = -1;

    private void OnEnable()
    {
        if (!greenSlope) greenSlope = FindOne<GreenSlopeManager>();

        if (placeHoleAction       != null) { placeHoleAction.action.performed        += OnPlaceHole;       placeHoleAction.action.Enable(); }
        if (addBoundaryAction     != null) { addBoundaryAction.action.performed      += OnAddBoundary;     addBoundaryAction.action.Enable(); }
        if (finishBoundaryAction  != null) { finishBoundaryAction.action.performed   += OnFinishBoundary;  finishBoundaryAction.action.Enable(); }
        if (clearAction           != null) { clearAction.action.performed            += OnClearAll;        clearAction.action.Enable(); }
    }

    private void OnDisable()
    {
        if (placeHoleAction       != null) { placeHoleAction.action.performed        -= OnPlaceHole;       placeHoleAction.action.Disable(); }
        if (addBoundaryAction     != null) { addBoundaryAction.action.performed      -= OnAddBoundary;     addBoundaryAction.action.Disable(); }
        if (finishBoundaryAction  != null) { finishBoundaryAction.action.performed   -= OnFinishBoundary;  finishBoundaryAction.action.Disable(); }
        if (clearAction           != null) { clearAction.action.performed            -= OnClearAll;        clearAction.action.Disable(); }
    }

    // --- Button callbacks ---
    private void OnPlaceHole(InputAction.CallbackContext ctx)
    {
        if (!Ready(ctx.time) || !EnsureManager()) return;
        greenSlope.PlaceHole(); // gaze-based
    }

    private void OnAddBoundary(InputAction.CallbackContext ctx)
    {
        if (!Ready(ctx.time) || !EnsureManager()) return;
        greenSlope.AddBoundaryVertex(); // gaze-based
    }

    private void OnFinishBoundary(InputAction.CallbackContext ctx)
    {
        if (!Ready(ctx.time) || !EnsureManager()) return;
        greenSlope.FinishBoundary();
    }

    private void OnClearAll(InputAction.CallbackContext ctx)
    {
        if (!Ready(ctx.time) || !EnsureManager()) return;
        greenSlope.ClearAll();
    }

    // --- Helpers ---
    private bool Ready(double now)
    {
        if (_lastActionTime > 0 && (now - _lastActionTime) < debounceSeconds) return false;
        _lastActionTime = now;
        return true;
    }

    private bool EnsureManager()
    {
        if (greenSlope) return true;
        greenSlope = FindOne<GreenSlopeManager>();
        if (!greenSlope)
        {
            Debug.LogWarning("GreenSlopeInputBridge: No GreenSlopeManager found in scene.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Unity 2023+: uses Object.FindFirstObjectByType with IncludeInactive.
    /// Older Unity: falls back to FindObjectOfType(true).
    /// </summary>
    private static T FindOne<T>() where T : Object
    {
        #if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        #else
        return Object.FindObjectOfType<T>(true);
        #endif
    }
}
