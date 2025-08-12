using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit; // Required for XRIT
using TMPro; // Required for TextMeshPro

public class XREAL_Interaction_Calculator : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI distanceText;

    [Header("Scene References")]
    [SerializeField] private Transform userHead;

    // This function will be called by the XR Ray Interactor event.
    public void MeasureDistanceOnSelect(SelectEnterEventArgs args)
    {
        // First, check if the thing doing the interacting is our ray controller.
        if (args.interactorObject is UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor)
        {
            // Then, try to get the details of where the ray hit.
            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                // The actual point in 3D space where the ray hit the mesh.
                Vector3 hitPoint = hit.point;

                if (userHead != null && distanceText != null)
                {
                    // Calculate the distance from the user's head to the hit point.
                    float distance = Vector3.Distance(userHead.position, hitPoint);

                    // Update the UI, formatting the number to two decimal places.
                    distanceText.text = $"Distance: {distance:F2} m";
                }
            }
        }
    }
}