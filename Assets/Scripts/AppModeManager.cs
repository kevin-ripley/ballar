// AppModeManager.cs
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class AppModeManager : MonoBehaviour
{
    public enum Mode { ScanAnalyze, Review }
    public Mode Current { get; private set; } = Mode.ScanAnalyze;

    [Header("Core")]
    [SerializeField] ARSession arSession;                 // drag from scene
    [SerializeField] ARMeshManager meshManager;           // drag your Meshing object
    [SerializeField] ARCameraBackground arBackground;     // optional: drag if you want to toggle background
    [SerializeField] ARMeshCaptureExporter exporter;      // the exporter you added
    [SerializeField] GreenAnalyzer analyzer;              // your analyzer component
    [SerializeField] GameObject scanHudRoot;              // HUD/UI for scan/analyze
    [SerializeField] GameObject reviewHudRoot;            // HUD/UI for review

    void Awake()
    {
        // Fail-safe auto-wiring if you forget
        if (!arSession) arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Exclude);
        if (!meshManager) meshManager = FindFirstObjectByType<ARMeshManager>(FindObjectsInactive.Exclude);
        if (!arBackground) arBackground = FindFirstObjectByType<ARCameraBackground>(FindObjectsInactive.Exclude);
        if (!exporter) exporter = FindFirstObjectByType<ARMeshCaptureExporter>(FindObjectsInactive.Exclude);
        if (!analyzer) analyzer = FindFirstObjectByType<GreenAnalyzer>(FindObjectsInactive.Exclude);
    }

    public void EnterScanAnalyze()
    {
        // Keep AR running; just re-enable meshing
        if (meshManager) meshManager.enabled = true;
        if (arBackground) arBackground.enabled = true;

        // Analyzer/laser work as usual
        if (analyzer) analyzer.enabled = true;

        // HUDs
        if (scanHudRoot) scanHudRoot.SetActive(true);
        if (reviewHudRoot) reviewHudRoot.SetActive(false);

        Current = Mode.ScanAnalyze;
    }

    public void EnterReviewLatest()
    {
        // Stop generating new meshes; keep AR session (pose) alive
        if (meshManager) meshManager.enabled = false;

        // Optional: dim/disable camera background if you want a cleaner review look
        // if (arBackground) arBackground.enabled = false;

        // Load last saved OBJ and place it in front of camera
        if (exporter) exporter.EnterReviewModeLatest();

        // Analyzer can still work in Review via Physics raycasts if the loaded mesh has a MeshCollider
        if (analyzer) analyzer.enabled = true;

        if (scanHudRoot) scanHudRoot.SetActive(false);
        if (reviewHudRoot) reviewHudRoot.SetActive(true);

        Current = Mode.Review;
    }

    public void ExitReview()
    {
        if (exporter) exporter.ExitReviewMode();

        // Restore AR background & meshing
        if (meshManager) meshManager.enabled = true;
        if (arBackground) arBackground.enabled = true;

        if (scanHudRoot) scanHudRoot.SetActive(true);
        if (reviewHudRoot) reviewHudRoot.SetActive(false);

        Current = Mode.ScanAnalyze;
    }
    
    public void EnterReviewWithPath(string objPath)
{
    if (meshManager) meshManager.enabled = false;
    if (exporter) exporter.EnterReviewModeWithPath(objPath);

    if (scanHudRoot)  scanHudRoot.SetActive(false);
    if (reviewHudRoot) reviewHudRoot.SetActive(true);

    Current = Mode.Review;
}

}
