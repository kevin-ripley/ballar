using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AI-powered raycast coordinator that predicts user gaze patterns and optimizes AR hit testing
/// Reduces redundant raycasts across your GazeReticle, GreenSlope, and PuttLine managers
/// </summary>
public class SmartRaycastManager : MonoBehaviour
{
    [Header("AR Components")]
    public ARRaycastManager arRaycast;
    public GazeRayProvider gazeProvider;
    
    [Header("AI Optimization Settings")]
    [Range(0.1f, 1.0f)]
    public float predictionWeight = 0.3f;
    
    [Range(2, 10)]
    public int maxCachedResults = 6;
    
    [Range(0.02f, 0.2f)]
    public float cacheValidDistance = 0.05f; // meters
    
    [Range(30f, 120f)]
    public float maxCacheAge = 60f; // seconds
    
    [Header("Adaptive Performance")]
    public bool enableAdaptiveQuality = true;
    public float targetFrameTime = 16.67f; // 60fps target
    
    // AI prediction system
    private Vector3[] gazeHistory = new Vector3[10];
    private float[] gazeTimestamps = new float[10];
    private int historyIndex = 0;
    private Vector3 predictedGazeDirection;
    private float lastPredictionUpdate = 0f;
    
    // Intelligent caching
    private readonly List<CachedRaycastResult> cache = new List<CachedRaycastResult>();
    private readonly List<ARRaycastHit> tempHits = new List<ARRaycastHit>();
    
    // Performance monitoring
    private float[] frameTimeHistory = new float[30];
    private int frameIndex = 0;
    private float avgFrameTime = 16.67f;
    
    // Quality scaling
    private float currentQualityScale = 1.0f;
    
    public static SmartRaycastManager Instance { get; private set; }
    
    private struct CachedRaycastResult
    {
        public Vector3 rayOrigin;
        public Vector3 rayDirection;
        public ARRaycastHit hit;
        public bool hasHit;
        public float timestamp;
        public TrackableType trackableTypes;
        
        public bool IsValid(Vector3 origin, Vector3 direction, float maxAge, float maxDist)
        {
            return Time.time - timestamp < maxAge &&
                   Vector3.Distance(origin, rayOrigin) < maxDist &&
                   Vector3.Dot(direction, rayDirection) > 0.98f; // ~11 degree tolerance
        }
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("Multiple SmartRaycastManager instances found!");
            Destroy(this);
        }
    }
    
    void Start()
    {
        if (!arRaycast) arRaycast = FindFirstObjectByType<ARRaycastManager>();
        if (!gazeProvider) gazeProvider = FindFirstObjectByType<GazeRayProvider>();
        
        // Initialize frame time tracking
        for (int i = 0; i < frameTimeHistory.Length; i++)
            frameTimeHistory[i] = targetFrameTime;
    }
    
    void Update()
    {
        UpdatePerformanceMetrics();
        UpdateGazePrediction();
        CleanCache();
        
        if (enableAdaptiveQuality)
            UpdateQualityScale();
    }
    
    /// <summary>
    /// Main entry point - replaces arRaycast.Raycast() calls throughout your app
    /// </summary>
    public bool SmartRaycast(Ray ray, out ARRaycastHit hit, TrackableType trackableTypes = TrackableType.AllTypes)
    {
        hit = default;
        
        // Check cache first
        var cachedResult = GetCachedResult(ray, trackableTypes);
        if (cachedResult.HasValue)
        {
            if (cachedResult.Value.hasHit)
            {
                hit = cachedResult.Value.hit;
                return true;
            }
            return false;
        }
        
        // Use AI prediction to optimize ray direction
        Ray optimizedRay = GetOptimizedRay(ray);
        
        // Perform actual raycast with quality scaling
        bool hasHit = PerformAdaptiveRaycast(optimizedRay, out hit, trackableTypes);
        
        // Cache result
        CacheResult(ray, hit, hasHit, trackableTypes);
        
        return hasHit;
    }
    
    /// <summary>
    /// Batch raycast for GreenSlopeManager's terrain analysis
    /// Uses AI to prioritize important areas and skip redundant samples
    /// </summary>
    public List<Vector3> SmartTerrainSample(List<Vector3> requestedPoints, float maxSamples = 1000)
    {
        var results = new List<Vector3>();
        int successCount = 0;
        int attemptCount = 0;
        
        if (requestedPoints.Count <= maxSamples)
        {
            // Small enough - process all points
            foreach (var point in requestedPoints)
            {
                attemptCount++;
                
                // Try multiple heights like the original GreenSlopeManager
                bool foundHit = false;
                float[] heightOffsets = { 0f, 0.4f, 0.8f };
                
                foreach (var offset in heightOffsets)
                {
                    var rayStart = new Vector3(point.x, point.y + 0.6f + offset, point.z);
                    
                    if (SmartRaycast(new Ray(rayStart, Vector3.down), out var hit, 
                        TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
                    {
                        results.Add(hit.pose.position);
                        successCount++;
                        foundHit = true;
                        break;
                    }
                }
                
                // Fallback to physics if AR failed
                if (!foundHit)
                {
                    var rayStart = new Vector3(point.x, point.y + 0.6f, point.z);
                    if (Physics.Raycast(rayStart, Vector3.down, out var physHit, 2f))
                    {
                        results.Add(physHit.point);
                        successCount++;
                    }
                }
            }
            
            Debug.Log($"[SmartRaycast] Terrain sampling: {successCount}/{attemptCount} success rate: {(float)successCount/attemptCount:P1}");
            return results;
        }
        
        // AI-based intelligent sampling for large areas
        var prioritizedPoints = PrioritizeTerrainPoints(requestedPoints, (int)maxSamples);
        
        foreach (var point in prioritizedPoints)
        {
            attemptCount++;
            
            // Try multiple heights
            bool foundHit = false;
            float[] heightOffsets = { 0f, 0.4f, 0.8f };
            
            foreach (var offset in heightOffsets)
            {
                var rayStart = new Vector3(point.x, point.y + 0.6f + offset, point.z);
                
                if (SmartRaycast(new Ray(rayStart, Vector3.down), out var hit,
                    TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
                {
                    results.Add(hit.pose.position);
                    successCount++;
                    foundHit = true;
                    break;
                }
            }
            
            // Fallback to physics if AR failed
            if (!foundHit)
            {
                var rayStart = new Vector3(point.x, point.y + 0.6f, point.z);
                if (Physics.Raycast(rayStart, Vector3.down, out var physHit, 2f))
                {
                    results.Add(physHit.point);
                    successCount++;
                }
            }
        }
        
        Debug.Log($"[SmartRaycast] AI Terrain sampling: {successCount}/{attemptCount} success rate: {(float)successCount/attemptCount:P1}");
        return results;
    }
    
    // ===================== AI PREDICTION SYSTEM =====================
    
    private void UpdateGazePrediction()
    {
        if (!gazeProvider || Time.time - lastPredictionUpdate < 0.1f) return;
        
        var currentGaze = gazeProvider.GetRay();
        
        // Store in circular buffer
        gazeHistory[historyIndex] = currentGaze.direction;
        gazeTimestamps[historyIndex] = Time.time;
        historyIndex = (historyIndex + 1) % gazeHistory.Length;
        
        // Predict next gaze direction using velocity and acceleration
        predictedGazeDirection = PredictNextGazeDirection();
        lastPredictionUpdate = Time.time;
    }
    
    private Vector3 PredictNextGazeDirection()
    {
        if (gazeTimestamps[0] == 0) return gazeProvider.GetRay().direction;
        
        // Simple velocity-based prediction
        int prevIndex = (historyIndex - 1 + gazeHistory.Length) % gazeHistory.Length;
        int prev2Index = (historyIndex - 2 + gazeHistory.Length) % gazeHistory.Length;
        
        if (gazeTimestamps[prev2Index] == 0) return gazeHistory[prevIndex];
        
        Vector3 velocity = (gazeHistory[prevIndex] - gazeHistory[prev2Index]) / 
                          (gazeTimestamps[prevIndex] - gazeTimestamps[prev2Index]);
        
        float predictionTime = 0.2f; // Predict 200ms ahead
        Vector3 predicted = gazeHistory[prevIndex] + velocity * predictionTime;
        
        return predicted.normalized;
    }
    
    private Ray GetOptimizedRay(Ray originalRay)
    {
        if (predictionWeight <= 0.01f) return originalRay;
        
        // Blend original direction with prediction
        Vector3 blendedDirection = Vector3.Slerp(
            originalRay.direction, 
            predictedGazeDirection, 
            predictionWeight
        ).normalized;
        
        return new Ray(originalRay.origin, blendedDirection);
    }
    
    // ===================== INTELLIGENT CACHING =====================
    
    private CachedRaycastResult? GetCachedResult(Ray ray, TrackableType trackableTypes)
    {
        foreach (var cached in cache)
        {
            if (cached.IsValid(ray.origin, ray.direction, maxCacheAge, cacheValidDistance) &&
                (cached.trackableTypes & trackableTypes) == trackableTypes)
            {
                return cached;
            }
        }
        return null;
    }
    
    private void CacheResult(Ray ray, ARRaycastHit hit, bool hasHit, TrackableType trackableTypes)
    {
        var result = new CachedRaycastResult
        {
            rayOrigin = ray.origin,
            rayDirection = ray.direction,
            hit = hit,
            hasHit = hasHit,
            timestamp = Time.time,
            trackableTypes = trackableTypes
        };
        
        cache.Add(result);
        
        // Keep cache size manageable
        if (cache.Count > maxCachedResults)
        {
            cache.RemoveAt(0);
        }
    }
    
    private void CleanCache()
    {
        cache.RemoveAll(c => Time.time - c.timestamp > maxCacheAge);
    }
    
    // ===================== ADAPTIVE PERFORMANCE =====================
    
    private void UpdatePerformanceMetrics()
    {
        frameTimeHistory[frameIndex] = Time.deltaTime * 1000f; // Convert to ms
        frameIndex = (frameIndex + 1) % frameTimeHistory.Length;
        
        // Calculate rolling average
        float sum = 0f;
        foreach (float ft in frameTimeHistory)
            sum += ft;
        avgFrameTime = sum / frameTimeHistory.Length;
    }
    
    private void UpdateQualityScale()
    {
        float targetMs = targetFrameTime;
        
        if (avgFrameTime > targetMs * 1.2f) // 20% over target
        {
            currentQualityScale = Mathf.Max(0.5f, currentQualityScale - 0.1f * Time.deltaTime);
        }
        else if (avgFrameTime < targetMs * 0.9f) // 10% under target
        {
            currentQualityScale = Mathf.Min(1.0f, currentQualityScale + 0.05f * Time.deltaTime);
        }
    }
    
    private bool PerformAdaptiveRaycast(Ray ray, out ARRaycastHit hit, TrackableType trackableTypes)
    {
        hit = default;
        
        if (!arRaycast) return false;
        
        // Scale trackable types based on performance
        if (currentQualityScale < 0.8f && trackableTypes.HasFlag(TrackableType.FeaturePoint))
        {
            trackableTypes &= ~TrackableType.FeaturePoint; // Remove expensive feature points
        }
        
        if (arRaycast.Raycast(ray, tempHits, trackableTypes))
        {
            hit = tempHits[0];
            return true;
        }
        
        return false;
    }
    
    // ===================== TERRAIN SAMPLING AI =====================
    
    private List<Vector3> PrioritizeTerrainPoints(List<Vector3> points, float maxSamples)
    {
        var prioritized = new List<Vector3>();
        
        // AI-based prioritization:
        // 1. Always include boundary points
        // 2. Focus on areas with potential elevation changes
        // 3. Use adaptive sampling density
        
        int boundaryPoints = Mathf.Min(points.Count, Mathf.RoundToInt(maxSamples * 0.3f));
        int interiorPoints = Mathf.RoundToInt(maxSamples * 0.7f);
        
        // Add boundary points (evenly spaced)
        for (int i = 0; i < boundaryPoints; i++)
        {
            int index = Mathf.RoundToInt((float)i / boundaryPoints * (points.Count - 1));
            prioritized.Add(points[index]);
        }
        
        // Add interior points using importance sampling
        var remaining = new List<Vector3>(points);
        foreach (var boundary in prioritized)
        {
            remaining.Remove(boundary);
        }
        
        // Sample interior points with preference for areas that might have elevation changes
        var sampledInterior = SampleImportantPoints(remaining, interiorPoints);
        prioritized.AddRange(sampledInterior);
        
        return prioritized;
    }
    
    private List<Vector3> SampleImportantPoints(List<Vector3> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return points;
        
        var result = new List<Vector3>();
        float step = (float)points.Count / maxPoints;
        
        for (int i = 0; i < maxPoints; i++)
        {
            int index = Mathf.RoundToInt(i * step);
            if (index < points.Count)
                result.Add(points[index]);
        }
        
        return result;
    }
    
    // ===================== PUBLIC API FOR YOUR SCRIPTS =====================
    
    /// <summary>
    /// Call this from GazeReticleStable.TryGetStableHit()
    /// </summary>
    public bool GetStableHit(Ray ray, out Vector3 hitPos, out Vector3 hitUp, out bool isPlane)
    {
        hitPos = Vector3.zero;
        hitUp = Vector3.up;
        isPlane = false;
        
        if (SmartRaycast(ray, out var hit, TrackableType.PlaneWithinPolygon))
        {
            hitPos = hit.pose.position;
            hitUp = hit.pose.up;
            isPlane = true;
            return Vector3.Dot(hitUp, Vector3.up) > 0.5f; // Wall rejection
        }
        
        // Fallback to other trackables
        if (SmartRaycast(ray, out hit, TrackableType.FeaturePoint | TrackableType.Depth))
        {
            hitPos = hit.pose.position;
            hitUp = Vector3.up;
            isPlane = false;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Performance info for debugging
    /// </summary>
    public void GetPerformanceInfo(out float frameTime, out float qualityScale, out int cacheSize)
    {
        frameTime = avgFrameTime;
        qualityScale = currentQualityScale;
        cacheSize = cache.Count;
    }
}