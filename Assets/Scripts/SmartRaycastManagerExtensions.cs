// File: Extensions/SmartRaycastManagerExtensions.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Extensions to your existing SmartRaycastManager for enhanced accuracy
/// </summary>
public static class SmartRaycastManagerExtensions
{
    /// <summary>
    /// Enhanced raycast with multi-point validation for critical measurements
    /// </summary>
    public static bool AccurateRaycast(this SmartRaycastManager manager, Ray ray, out ARRaycastHit hit, int validationSamples = 3)
    {
        hit = default;
        var results = new List<ARRaycastHit>();
        
        // Perform multiple nearby raycasts for validation
        for (int i = 0; i < validationSamples; i++)
        {
            Vector3 offset = Random.insideUnitSphere * 0.01f; // 1cm variation
            offset.y = 0; // Keep horizontal
            
            Ray validationRay = new Ray(ray.origin + offset, ray.direction);
            
            if (manager.SmartRaycast(validationRay, out ARRaycastHit validationHit))
            {
                results.Add(validationHit);
            }
        }
        
        if (results.Count == 0) return false;
        
        // Use median position for stability
        if (results.Count >= 3)
        {
// Choose the hit closest to the median position (can't set pose; choose best sample)
        var positions = results.Select(r => r.pose.position).ToList();
        Vector3 median = GetMedianPosition(positions);
        hit = results.OrderBy(r => (r.pose.position - median).sqrMagnitude).First();
        }
        else
        {
            hit = results[0];
        }
        
        return true;
    }
    
    private static Vector3 GetMedianPosition(List<Vector3> positions)
    {
        var sortedX = positions.OrderBy(p => p.x).ToList();
        var sortedY = positions.OrderBy(p => p.y).ToList();
        var sortedZ = positions.OrderBy(p => p.z).ToList();
        
        int mid = positions.Count / 2;
        
        return new Vector3(
            sortedX[mid].x,
            sortedY[mid].y,
            sortedZ[mid].z
        );
    }
}

/// <summary>
/// Enhanced performance monitor component - attach to your SmartRaycastManager
/// </summary>
public class EnhancedPerformanceMonitor : MonoBehaviour
{
    [Header("Performance Targets")]
    public float targetFPS = 60f;
    public float minimumFPS = 30f;
    public float targetTrackingQuality = 0.8f;
    
    [Header("Monitoring")]
    public bool enableGUIDisplay = true;
    public bool logPerformanceWarnings = true;
    
    private SmartRaycastManager smartRaycast;
    private GreenSlopeManager greenSlope;
    private Queue<float> fpsHistory = new Queue<float>();
    private float averageFPS;
    private int maxHistorySize = 60;
    
    public System.Action<PerformanceLevel> OnPerformanceLevelChanged;
    
    private void Start()
    {
        smartRaycast = GetComponent<SmartRaycastManager>();
        greenSlope = FindFirstObjectByType<GreenSlopeManager>();
        
        if (!smartRaycast)
        {
            Debug.LogError("[EnhancedPerformanceMonitor] Must be attached to SmartRaycastManager!");
            enabled = false;
        }
    }
    
    private void Update()
    {
        UpdateFPSHistory();
        MonitorPerformance();
    }
    
    private void UpdateFPSHistory()
    {
        float currentFPS = 1f / Time.unscaledDeltaTime;
        fpsHistory.Enqueue(currentFPS);
        
        if (fpsHistory.Count > maxHistorySize)
        {
            fpsHistory.Dequeue();
        }
        
        averageFPS = fpsHistory.Average();
    }
    
    private void MonitorPerformance()
    {
        var currentLevel = GetPerformanceLevel();
        
        if (logPerformanceWarnings)
        {
            if (averageFPS < minimumFPS)
            {
                Debug.LogWarning($"[PerformanceMonitor] FPS below minimum: {averageFPS:F1}");
            }
        }
        
        OnPerformanceLevelChanged?.Invoke(currentLevel);
    }
    
    public PerformanceLevel GetPerformanceLevel()
    {
        smartRaycast.GetPerformanceInfo(out float frameTime, out float qualityScale, out int cacheSize);
        
        if (averageFPS >= targetFPS && frameTime <= 16.67f)
            return PerformanceLevel.High;
        else if (averageFPS >= minimumFPS && frameTime <= 33.33f)
            return PerformanceLevel.Medium;
        else
            return PerformanceLevel.Low;
    }
    
    private void OnGUI()
    {
        if (!enableGUIDisplay) return;
        
        GUILayout.BeginArea(new Rect(10, 420, 300, 120));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("Enhanced Performance Monitor", GUI.skin.box);
        
        smartRaycast.GetPerformanceInfo(out float frameTime, out float qualityScale, out int cacheSize);
        
        GUILayout.Label($"Average FPS: {averageFPS:F1}");
        GUILayout.Label($"Frame Time: {frameTime:F1}ms");
        GUILayout.Label($"Quality Scale: {qualityScale:F2}");
        GUILayout.Label($"Performance: {GetPerformanceLevel()}");
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}

public enum PerformanceLevel
{
    Low,
    Medium,
    High
}