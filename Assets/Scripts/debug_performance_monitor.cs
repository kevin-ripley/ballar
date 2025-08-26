using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Add this component to debug the AI performance and sampling issues
/// Shows real-time info about raycast success rates and AI performance
/// </summary>
public class AIDebugMonitor : MonoBehaviour
{
    [Header("Debug UI")]
    public bool showDebugGUI = true;
    public bool logSamplingDetails = true;
    
    private SmartRaycastManager smartRaycast;
    private GreenSlopeManager greenSlope;
    
    // Stats tracking
    private int totalSampleRequests = 0;
    private int successfulSamples = 0;
    private float lastResetTime = 0f;
    
    void Start()
    {
        smartRaycast = SmartRaycastManager.Instance;
        greenSlope = FindFirstObjectByType<GreenSlopeManager>();
        lastResetTime = Time.time;
    }
    
    void Update()
    {
        // Reset stats every 10 seconds
        if (Time.time - lastResetTime > 10f)
        {
            totalSampleRequests = 0;
            successfulSamples = 0;
            lastResetTime = Time.time;
        }
    }
    
    void OnGUI()
    {
        if (!showDebugGUI) return;
        
        GUILayout.BeginArea(new Rect(10, 100, 400, 300));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("AI Debug Monitor", GUI.skin.GetStyle("label"));
        
        // SmartRaycastManager status
        if (smartRaycast != null)
        {
            smartRaycast.GetPerformanceInfo(out float frameTime, out float qualityScale, out int cacheSize);
            
            GUILayout.Label($"AI System: ACTIVE");
            GUILayout.Label($"Frame Time: {frameTime:F1}ms");
            GUILayout.Label($"Quality Scale: {qualityScale:F2}");
            GUILayout.Label($"Cache Size: {cacheSize}");
        }
        else
        {
            GUILayout.Label("AI System: NOT FOUND");
            GUILayout.Label("Using fallback raycasting");
        }
        
        GUILayout.Space(10);
        
        // GreenSlopeManager status
        if (greenSlope != null)
        {
            GUILayout.Label($"Green Slope: ACTIVE");
            GUILayout.Label($"Has Hole: {greenSlope.HasHole}");
            GUILayout.Label($"Boundary Points: {greenSlope.BoundaryCount}");
        }
        else
        {
            GUILayout.Label("Green Slope: NOT FOUND");
        }
        
        GUILayout.Space(10);
        
        // Sampling statistics
        float successRate = totalSampleRequests > 0 ? (float)successfulSamples / totalSampleRequests : 0f;
        GUILayout.Label($"Sample Success Rate: {successRate:P1}");
        GUILayout.Label($"Total Requests: {totalSampleRequests}");
        GUILayout.Label($"Successful: {successfulSamples}");
        
        GUILayout.Space(10);
        
        // Debug controls
        if (GUILayout.Button("Test Single Raycast"))
        {
            TestSingleRaycast();
        }
        
        if (GUILayout.Button("Reset Stats"))
        {
            totalSampleRequests = 0;
            successfulSamples = 0;
            lastResetTime = Time.time;
        }
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    void TestSingleRaycast()
    {
        if (!Camera.main) return;
        
        var ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        
        if (smartRaycast != null)
        {
            bool hit = smartRaycast.SmartRaycast(ray, out var arHit);
            Debug.Log($"AI Raycast Test: {hit}, Position: {arHit.pose.position}, Type: {arHit.hitType}");
        }
        else
        {
            var arRaycast = FindFirstObjectByType<ARRaycastManager>();
            if (arRaycast != null)
            {
                var hits = new System.Collections.Generic.List<UnityEngine.XR.ARFoundation.ARRaycastHit>();
                bool hit = arRaycast.Raycast(ray, hits);
                Debug.Log($"Direct AR Raycast Test: {hit}, Hits: {hits.Count}");
                if (hit && hits.Count > 0)
                {
                    Debug.Log($"Hit Position: {hits[0].pose.position}, Type: {hits[0].hitType}");
                }
            }
        }
    }
    
    // Call this method from GreenSlopeManager to track sampling success
    public void LogSampleAttempt(bool success)
    {
        totalSampleRequests++;
        if (success) successfulSamples++;
        
        if (logSamplingDetails)
        {
            Debug.Log($"Sample attempt: {success}, Total: {totalSampleRequests}, Success Rate: {(float)successfulSamples/totalSampleRequests:P1}");
        }
    }
}