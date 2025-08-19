using UnityEngine;

public class GazeRayProvider : MonoBehaviour
{
    [SerializeField] private Camera xrCamera;          // Drag XR Origin > Camera
    [SerializeField] private bool useViewportRay = true; // Uses camera intrinsics

    [Header("Stabilization")]
    [Range(0f,1f)] public float posSmoothing = 0.15f;  // 0 = no smoothing
    [Range(0f,1f)] public float rotSmoothing = 0.15f;

    private Vector3 _fPos;   // filtered pose
    private Quaternion _fRot;
    private bool _inited;

    public Ray GetRay()
    {
        if (!xrCamera) xrCamera = Camera.main;
        if (!_inited)
        {
            _fPos = xrCamera.transform.position;
            _fRot = xrCamera.transform.rotation;
            _inited = true;
        }
        // Exponential smoothing to reduce micro head jitter
        _fPos = Vector3.Lerp(_fPos, xrCamera.transform.position, posSmoothing);
        _fRot = Quaternion.Slerp(_fRot, xrCamera.transform.rotation, rotSmoothing);

        if (useViewportRay)
            return xrCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return new Ray(_fPos, _fRot * Vector3.forward);
    }
}