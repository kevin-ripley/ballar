// Add to any GameObject to see performance info
using UnityEngine;

public class AIPerformanceMonitor : MonoBehaviour
{
    void OnGUI()
    {
        if (SmartRaycastManager.Instance == null) return;
        
        SmartRaycastManager.Instance.GetPerformanceInfo(
            out float frameTime, 
            out float qualityScale, 
            out int cacheSize
        );
        
        GUI.Label(new Rect(10, 10, 300, 20), $"Frame Time: {frameTime:F1}ms");
        GUI.Label(new Rect(10, 30, 300, 20), $"Quality Scale: {qualityScale:F2}");
        GUI.Label(new Rect(10, 50, 300, 20), $"Cache Hits: {cacheSize}");
    }
}