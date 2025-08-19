using UnityEngine;
using UnityEngine.InputSystem;

public class XrealPlacementInputBridge : MonoBehaviour
{
    [Header("Input (XREAL custom button)")]
    [Tooltip("Reference your XREAL custom button action here")]
    public InputActionReference buttonAction;

    [Header("Target")]
    [Tooltip("Drag your PuttLineManager in here")]
    public PuttLineManager puttManager;

    [Header("Behavior")]
    [Tooltip("Hold this long to trigger ResetAll()")]
    [SerializeField] private float longPressSeconds = 0.6f;

    // simple debounce so we don’t double-trigger
    [SerializeField] private float debounceSeconds = 0.15f;

    private double pressStartTime = -1;
    private double lastActionTime = -1;

    private void OnEnable()
    {
        if (buttonAction != null)
        {
            var act = buttonAction.action;
            act.started  += OnStarted;   // press down
            act.canceled += OnCanceled;  // release
            act.performed += OnPerformedFallback; // some devices only fire performed
            act.Enable();
        }
    }

    private void OnDisable()
    {
        if (buttonAction != null)
        {
            var act = buttonAction.action;
            act.started  -= OnStarted;
            act.canceled -= OnCanceled;
            act.performed -= OnPerformedFallback;
            act.Disable();
        }
    }

    private void OnStarted(InputAction.CallbackContext ctx)
    {
        pressStartTime = ctx.time;
    }

    private void OnCanceled(InputAction.CallbackContext ctx)
    {
        // Button released — decide short vs long press
        if (pressStartTime < 0) return;
        var duration = ctx.time - pressStartTime;
        pressStartTime = -1;

        if (puttManager == null) return;
        if (TooSoon(ctx.time)) return;

        if (duration >= longPressSeconds)
        {
            puttManager.ResetAll();
            lastActionTime = ctx.time;
            return;
        }

        // Short press → auto-advance
        DoAutoAdvance(ctx.time);
    }

    // Fallback for devices that don’t send started/canceled reliably
    private void OnPerformedFallback(InputAction.CallbackContext ctx)
    {
        if (pressStartTime >= 0) return; // already handled normal flow
        if (puttManager == null) return;
        if (TooSoon(ctx.time)) return;

        DoAutoAdvance(ctx.time);
    }

    private void DoAutoAdvance(double now)
    {
        // Don’t interrupt the sampling coroutine
        if (puttManager.currentState == PuttLineManager.PlacementState.Sampling) return;

        switch (puttManager.currentState)
        {
            case PuttLineManager.PlacementState.Idle:
            case PuttLineManager.PlacementState.BPlaced:
                puttManager.BeginPlaceA(); // first or restart
                break;

            case PuttLineManager.PlacementState.APlaced:
                puttManager.BeginPlaceB(); // second point
                break;
        }

        lastActionTime = now;
    }

    private bool TooSoon(double now)
    {
        return (lastActionTime > 0) && (now - lastActionTime < debounceSeconds);
    }
}
