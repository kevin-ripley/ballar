using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using TMPro; // For displaying text, ensure you have TextMeshPro imported
 // For XR Interaction Toolkit events

public class SlopeCalculator : MonoBehaviour
{
//     public ARPlaneManager arPlaneManager;
//     public TextMeshProUGUI slopeText; // Assign a UI TextMeshPro element in the Inspector
//     public GameObject slopeIndicatorPrefab; // Assign a prefab to visualize the slope
//     public UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor; // Assign the XR Ray Interactor from your controller

//     private ARPlane selectedPlane;

//     void Start()
//     {
//         if (arPlaneManager == null)
//         {
//             Debug.LogError("ARPlaneManager is not assigned. Please assign it in the Inspector.");
//             return;
//         }

//         arPlaneManager.planesChanged += OnPlanesChanged;

//         if (slopeText == null)
//         {
//             Debug.LogError("SlopeText (TextMeshProUGUI) is not assigned. Please assign it in the Inspector.");
//         }

//         if (rayInteractor != null)
//         {
//             // Subscribe to the Select (Trigger) event from the XR Ray Interactor
//             rayInteractor.selectAction.action.performed += ctx => OnControllerTriggerPressed();
//         }
//         else
//         {
//             Debug.LogWarning("XRRayInteractor is not assigned. Controller interaction for selection will not work.");
//         }
//     }

//     void OnDestroy()
//     {
//         if (arPlaneManager != null)
//         {
//             arPlaneManager.planesChanged -= OnPlanesChanged;
//         }
//         if (rayInteractor != null)
//         {
//             rayInteractor.selectAction.action.performed -= ctx => OnControllerTriggerPressed();
//         }
//     }

//     private void OnPlanesChanged(ARPlanesChangedEventArgs args)
//     {
//         // For initial setup, we might still implicitly select the first horizontal plane.
//         // In a real app, the user would explicitly select with the controller.
//         if (selectedPlane == null && args.added.Count > 0)
//         {
//             foreach (var plane in args.added)
//             {
//                 if (plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown)
//                 {
//                     selectedPlane = plane;
//                     Debug.Log($"Initial plane selected with ID: {selectedPlane.trackableId}");
//                     CalculateAndDisplaySlope(selectedPlane);
//                     break;
//                 }
//             }
//         }
//     }

//     public void CalculateAndDisplaySlope(ARPlane plane)
//     {
//         if (plane == null)
//         {
//             Debug.LogWarning("No plane selected to calculate slope.");
//             if (slopeText != null) slopeText.text = "No plane selected.";
//             return;
//         }

//         Quaternion planeRotation = plane.transform.rotation;
//         Vector3 planeUp = planeRotation * Vector3.up;

//         float angleRad = Vector3.Angle(planeUp, Vector3.up) * Mathf.Deg2Rad;
//         float angleDegrees = angleRad * Mathf.Rad2Deg;

//         float slopeDecimal = Mathf.Tan(angleRad);
//         float slopePercentage = slopeDecimal * 100f;

//         Vector3 steepestDescentDirection = Vector3.ProjectOnPlane(Vector3.down, planeUp).normalized;

//         if (slopeText != null)
//         {
//             slopeText.text = $"Slope: {slopePercentage:F2}% ({angleDegrees:F2}°)";
//             slopeText.text += $"\nSteepest Descent: {steepestDescentDirection}";
//         }

//         if (slopeIndicatorPrefab != null)
//         {
//             GameObject existingIndicator = GameObject.Find("SlopeIndicator(Clone)");
//             if (existingIndicator != null)
//             {
//                 Destroy(existingIndicator);
//             }

//             GameObject indicator = Instantiate(slopeIndicatorPrefab, plane.transform.position, plane.transform.rotation);
//             indicator.name = "SlopeIndicator";
//             indicator.transform.localScale = Vector3.one * 0.2f;
//         }
//     }

//     // This method is now called via the XR Interaction Toolkit's select action
//     public void OnControllerTriggerPressed()
//     {
//         Debug.Log("Controller Trigger Pressed (via XR Interaction Toolkit)!");

//         // When the trigger is pressed, we want to perform a raycast from the controller
//         // and select the plane it hits.
//         if (rayInteractor != null && rayInteractor.TryGetCurrentUIRaycastResult(out UnityEngine.UI.Extensions.UIRaycastResult uiRaycastResult))
//         {
//             // If you have a custom ARPlane collider or visualizer that is a UGUI element
//             // This is less common for plane selection directly.
//             Debug.Log($"UI Hit: {uiRaycastResult.gameObject.name}");
//             // Handle UI element hit if necessary
//         }
//         else if (rayInteractor != null && rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
//         {
//             // Check if the raycast hit an ARPlane
//             ARPlane hitPlane = hit.collider.GetComponentInParent<ARPlane>(); // Get ARPlane from parent if visualizer is a child
//             if (hitPlane != null)
//             {
//                 selectedPlane = hitPlane;
//                 Debug.Log($"Selected new plane with ID: {selectedPlane.trackableId} via controller raycast.");
//                 CalculateAndDisplaySlope(selectedPlane);
//             }
//             else
//             {
//                 Debug.Log("Controller raycast hit something, but not an ARPlane.");
//                 if (slopeText != null) slopeText.text = "Point at an AR Plane.";
//             }
//         }
//         else
//         {
//             Debug.Log("Controller raycast did not hit anything.");
//             if (slopeText != null) slopeText.text = "Point controller at a surface.";
//         }
//     }
}