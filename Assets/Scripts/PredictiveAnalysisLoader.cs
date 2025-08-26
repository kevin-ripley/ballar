// File: Extensions/PredictiveAnalysisLoader.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Predictive system that pre-analyzes areas based on user gaze patterns
/// Integrates with your existing GazeRayProvider and GreenSlopeManager
/// </summary>
public class PredictiveAnalysisLoader : MonoBehaviour
{
    [Header("Prediction Settings")]
    public float predictionTimeHorizon = 2f;
    public float movementThreshold = 0.3f;
    public int maxPredictiveOperations = 2;
    
    [Header("Integration References")]
    public GazeRayProvider gazeProvider;
    public GreenSlopeManager greenSlope;
    public SmartRaycastManager smartRaycast;
    
    private Queue<PredictiveOperation> operationQueue;
    private HashSet<PredictiveOperation> activeOperations;
    private Vector3 lastGazePosition;
    private Vector3 gazeVelocity;
    private EnhancedPerformanceMonitor performanceMonitor;
    private bool isEnabled = true;
    
    private struct PredictiveOperation
    {
        public OperationType type;
        public Vector3 position;
        public float priority;
        public System.DateTime scheduledTime;
        public bool isCompleted;
        
        public override bool Equals(object obj)
        {
            if (obj is PredictiveOperation other)
            {
                return type == other.type && 
                       Vector3.Distance(position, other.position) < 1f;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return type.GetHashCode() ^ position.GetHashCode();
        }
    }
    
    private enum OperationType
    {
        PreCalculateSlope,
        PreSampleTerrain,
        PreCacheRaycast
    }
    
    private void Start()
    {
        InitializePredictiveSystem();
        StartCoroutine(PredictiveLoadingLoop());
    }
    
    private void InitializePredictiveSystem()
    {
        operationQueue = new Queue<PredictiveOperation>();
        activeOperations = new HashSet<PredictiveOperation>();
        
        // Get references
        if (!gazeProvider) gazeProvider = FindFirstObjectByType<GazeRayProvider>();
        if (!greenSlope) greenSlope = FindFirstObjectByType<GreenSlopeManager>();
        if (!smartRaycast) smartRaycast = SmartRaycastManager.Instance;
        performanceMonitor = FindFirstObjectByType<EnhancedPerformanceMonitor>();
        
        if (!gazeProvider)
        {
            Debug.LogWarning("[PredictiveLoader] GazeRayProvider not found - predictions disabled");
            enabled = false;
        }
        
        lastGazePosition = gazeProvider.GetRay().origin;
    }
    
    private void Update()
    {
        if (!isEnabled || !gazeProvider) return;
        
        UpdateGazeTracking();
        PredictUpcomingOperations();
    }
    
    private void UpdateGazeTracking()
    {
        Vector3 currentGazePos = gazeProvider.GetRay().origin;
        gazeVelocity = (currentGazePos - lastGazePosition) / Time.deltaTime;
        lastGazePosition = currentGazePos;
    }
    
    private void PredictUpcomingOperations()
    {
        // Only predict if user is moving significantly
        if (gazeVelocity.magnitude < movementThreshold) return;
        
        // Predict where user will be looking
        Vector3 predictedPosition = lastGazePosition + gazeVelocity * predictionTimeHorizon;
        
        // Check if we should schedule operations
        if (ShouldSchedulePredictiveOperations())
        {
            SchedulePredictiveOperations(predictedPosition);
        }
    }
    
    private bool ShouldSchedulePredictiveOperations()
    {
        // Don't schedule if performance is poor
        if (performanceMonitor != null)
        {
            var perfLevel = performanceMonitor.GetPerformanceLevel();
            if (perfLevel == PerformanceLevel.Low) return false;
        }
        
        // Don't schedule if already have too many active operations
        if (activeOperations.Count >= maxPredictiveOperations) return false;
        
        return true;
    }
    
    private void SchedulePredictiveOperations(Vector3 predictedPosition)
    {
        // Schedule terrain pre-sampling
        var terrainOp = new PredictiveOperation
        {
            type = OperationType.PreSampleTerrain,
            position = predictedPosition,
            priority = CalculateTerrainPriority(predictedPosition),
            scheduledTime = System.DateTime.Now.AddSeconds(predictionTimeHorizon * 0.7f)
        };
        
        if (!IsOperationAlreadyScheduled(terrainOp))
        {
            operationQueue.Enqueue(terrainOp);
        }
        
        // Schedule raycast pre-caching
        var raycastOp = new PredictiveOperation
        {
            type = OperationType.PreCacheRaycast,
            position = predictedPosition,
            priority = 1f,
            scheduledTime = System.DateTime.Now.AddSeconds(predictionTimeHorizon * 0.5f)
        };
        
        if (!IsOperationAlreadyScheduled(raycastOp))
        {
            operationQueue.Enqueue(raycastOp);
        }
    }
    
    private float CalculateTerrainPriority(Vector3 position)
    {
        float priority = 1f;
        
        // Higher priority if near existing boundary or hole
        if (greenSlope && greenSlope.HasHole)
        {
            // Estimate distance to analysis area
            for (int i = 0; i < greenSlope.BoundaryCount; i++)
            {
                if (greenSlope.TryGetBoundaryPoint(i, out var boundaryPoint))
                {
                    float distance = Vector3.Distance(position, boundaryPoint);
                    priority += Mathf.Exp(-distance / 10f); // Exponential decay over 10 meters
                }
            }
        }
        
        return priority;
    }
    
    private bool IsOperationAlreadyScheduled(PredictiveOperation operation)
    {
        return activeOperations.Contains(operation) || operationQueue.Contains(operation);
    }
    
    private IEnumerator PredictiveLoadingLoop()
    {
        while (true)
        {
            if (isEnabled && operationQueue.Count > 0)
            {
                var operation = operationQueue.Dequeue();
                
                // Check if it's time to execute
                if (System.DateTime.Now >= operation.scheduledTime)
                {
                    StartCoroutine(ExecutePredictiveOperation(operation));
                }
                else
                {
                    // Put it back if not time yet
                    operationQueue.Enqueue(operation);
                }
            }
            
            yield return new WaitForSeconds(0.1f);
        }
    }
    
    private IEnumerator ExecutePredictiveOperation(PredictiveOperation operation)
    {
        activeOperations.Add(operation);
        
        try
        {
            switch (operation.type)
            {
                case OperationType.PreSampleTerrain:
                    yield return StartCoroutine(PreSampleTerrain(operation));
                    break;
                    
                case OperationType.PreCacheRaycast:
                    yield return StartCoroutine(PreCacheRaycast(operation));
                    break;
                    
                case OperationType.PreCalculateSlope:
                    yield return StartCoroutine(PreCalculateSlope(operation));
                    break;
            }
        }
        finally
        {
            activeOperations.Remove(operation);
        }
    }
    
    private IEnumerator PreSampleTerrain(PredictiveOperation operation)
    {
        if (smartRaycast == null) yield break;
        
        // Pre-sample terrain around the predicted position
        var samplePoints = new List<Vector3>();
        float radius = 2f;
        int sampleCount = 9; // Light pre-sampling
        
        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (float)i / sampleCount * 2f * Mathf.PI;
            float distance = radius * Random.Range(0.3f, 1f);
            
            Vector3 samplePoint = operation.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );
            
            samplePoints.Add(samplePoint);
        }
        
        // Use your SmartRaycastManager to pre-sample
        var results = smartRaycast.SmartTerrainSample(samplePoints, sampleCount);
        
        Debug.Log($"[PredictiveLoader] Pre-sampled {results.Count} terrain points at {operation.position}");
        
        yield return null;
    }
    
    private IEnumerator PreCacheRaycast(PredictiveOperation operation)
    {
        if (smartRaycast == null || gazeProvider == null) yield break;
        
        // Pre-cache raycast results for the predicted gaze direction
        Vector3 gazeDirection = (operation.position - lastGazePosition).normalized;
        Ray predictedRay = new Ray(operation.position, gazeDirection);
        
        // Perform raycast to populate cache
        smartRaycast.SmartRaycast(predictedRay, out var hit);
        
        Debug.Log($"[PredictiveLoader] Pre-cached raycast at {operation.position}");
        
        yield return null;
    }
    
    private IEnumerator PreCalculateSlope(PredictiveOperation operation)
    {
        // This could integrate with your IntelligentTerrainSampler
        // to pre-calculate slope data for areas the user might analyze
        
        Debug.Log($"[PredictiveLoader] Pre-calculating slope at {operation.position}");
        
        yield return null;
    }
    
    public void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
        
        if (!enabled)
        {
            // Clear pending operations
            operationQueue.Clear();
            activeOperations.Clear();
        }
    }
    
    public void ClearPredictiveOperations()
    {
        operationQueue.Clear();
        activeOperations.Clear();
    }
}