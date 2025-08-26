// File: Components/IntelligentTerrainSampler.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Add this component directly to your existing GreenSlopeManager GameObject
/// Integrates with your current SmartRaycastManager for optimal performance
/// </summary>
public class IntelligentTerrainSampler : MonoBehaviour
{
    [Header("Adaptive Sampling Configuration")]
    public int minSamplesFlat = 200;        // For flat areas (reduced from your 4000 max)
    public int maxSamplesComplex = 4000;    // Keep your current max for complex terrain
    public float complexityThreshold = 0.15f;
    public float edgeDetectionSensitivity = 0.05f;
    
    [Header("Integration with Existing System")]
    public bool useExistingVisualization = true;
    public bool enableAdaptiveDensity = true;
    
    [Header("Manual Control")]
    public bool overrideGreenSlopeAnalysis = true; // If true, replaces the standard FinishBoundary analysis
    
    private GreenSlopeManager parentManager;
    private SmartRaycastManager smartRaycast;
    private EnhancedSlopeVisualizer enhancedViz;
    private SlopeDebugMonitor slopeDebug;
    private AIDebugMonitor aiDebug;
    
    // Current analysis state
    private TerrainComplexityProfile currentProfile;
    private Dictionary<Vector2Int, float> currentHeightField;
    private float currentStepSize;
    
    public void Start()
    {
        // Get references to your existing components
        parentManager = GetComponent<GreenSlopeManager>();
        smartRaycast = SmartRaycastManager.Instance;
        enhancedViz = GetComponent<EnhancedSlopeVisualizer>();
        slopeDebug = FindFirstObjectByType<SlopeDebugMonitor>();
        aiDebug = FindFirstObjectByType<AIDebugMonitor>();
        
        if (!parentManager)
        {
            Debug.LogError("[IntelligentSampler] Must be attached to GreenSlopeManager!");
            enabled = false;
            return;
        }
        
        // Hook into the existing GreenSlopeManager if override is enabled
        if (overrideGreenSlopeAnalysis)
        {
            Debug.Log("[IntelligentSampler] Overriding standard analysis - use PerformIntelligentAnalysis() instead of FinishBoundary()");
        }
    }
    
    /// <summary>
    /// Call this instead of parentManager.FinishBoundary() for intelligent analysis
    /// </summary>
    public void PerformIntelligentAnalysis()
    {
        if (!ValidateForAnalysis()) return;
        
        Debug.Log("[IntelligentSampler] Starting intelligent terrain analysis...");
        
        // Phase 1: Initial terrain assessment using your existing boundary
        var boundaryPoints = GetBoundaryPoints();
        var assessmentSamples = GenerateAssessmentSamples(boundaryPoints, 25);
        currentProfile = AnalyzeTerrainComplexity(assessmentSamples);
        
        Debug.Log($"[IntelligentSampler] Terrain complexity: {currentProfile.complexity}, Variance: {currentProfile.varianceScore*100:F1}cm");
        
        // Phase 2: Determine optimal sample count based on complexity
        int optimalSampleCount = CalculateOptimalSampleCount(currentProfile);
        
        // Phase 3: Generate intelligent sample distribution
        var gridBounds = CalculateGridBounds(boundaryPoints);
        currentStepSize = CalculateOptimalStepSize(gridBounds, optimalSampleCount);
        
        Debug.Log($"[IntelligentSampler] Using {optimalSampleCount} samples with {currentStepSize:F3}m step size");
        
        // Phase 4: Generate focused sample points using your existing polygon test
        var samplePoints = GenerateFocusedSamples(gridBounds, optimalSampleCount);
        
        // Phase 5: Use your existing SmartRaycastManager for efficient height sampling
        var heightData = SampleHeightsIntelligently(samplePoints);
        
        // Phase 6: Build height field compatible with your existing system
        currentHeightField = BuildCompatibleHeightField(heightData, gridBounds);
        
        Debug.Log($"[IntelligentSampler] Successfully sampled {currentHeightField.Count} height points");
        
        // Phase 7: Generate visualization using your existing or enhanced system
        if (useExistingVisualization)
        {
            RenderUsingExistingSystem(currentHeightField, gridBounds);
        }
        else
        {
            RenderUsingEnhancedSystem(currentHeightField, gridBounds);
        }
        
        // Phase 8: Update debug monitoring
        UpdateDebugMonitors();
        
        // Phase 9: Update your existing zero isoline system
        UpdateZeroIsoline();
        
        Debug.Log("[IntelligentSampler] Intelligent analysis complete!");
    }
    
    private bool ValidateForAnalysis()
    {
        if (!parentManager.HasHole)
        {
            Debug.LogWarning("[IntelligentSampler] No hole placed - cannot analyze");
            return false;
        }
        
        if (parentManager.BoundaryCount < 3)
        {
            Debug.LogWarning("[IntelligentSampler] Need at least 3 boundary points");
            return false;
        }
        
        return true;
    }
    
    private List<Vector3> GetBoundaryPoints()
    {
        var points = new List<Vector3>();
        
        for (int i = 0; i < parentManager.BoundaryCount; i++)
        {
            if (parentManager.TryGetBoundaryPoint(i, out var point))
            {
                points.Add(point);
            }
        }
        
        return points;
    }
    
    private List<Vector3> GenerateAssessmentSamples(List<Vector3> boundary, int count)
    {
        var samples = new List<Vector3>();
        var bounds = CalculateGridBounds(boundary);
        
        // Use your existing point-in-polygon test
        for (int i = 0; i < count; i++)
        {
            // Generate random point within bounds
            float x = Random.Range(bounds.min.x, bounds.max.x);
            float z = Random.Range(bounds.min.y, bounds.max.y);
            Vector2 testPoint = new Vector2(x, z);
            
            // Use existing PointInPolygon method from GreenSlopeManager
            if (IsPointInBoundary(testPoint, boundary))
            {
                samples.Add(new Vector3(x, bounds.center.y, z));
            }
        }
        
        return samples.Count < count ? GenerateBackupSamples(boundary, count) : samples;
    }
    
    private List<Vector3> GenerateBackupSamples(List<Vector3> boundary, int count)
    {
        // If random sampling fails, use systematic sampling
        var samples = new List<Vector3>();
        var bounds = CalculateGridBounds(boundary);
        
        int gridSize = Mathf.CeilToInt(Mathf.Sqrt(count));
        float stepX = (bounds.max.x - bounds.min.x) / gridSize;
        float stepZ = (bounds.max.y - bounds.min.y) / gridSize;
        
        for (int ix = 0; ix < gridSize; ix++)
        {
            for (int iz = 0; iz < gridSize; iz++)
            {
                float x = bounds.min.x + ix * stepX;
                float z = bounds.min.y + iz * stepZ;
                Vector2 testPoint = new Vector2(x, z);
                
                if (IsPointInBoundary(testPoint, boundary))
                {
                    samples.Add(new Vector3(x, bounds.center.y, z));
                    if (samples.Count >= count) break;
                }
            }
            if (samples.Count >= count) break;
        }
        
        return samples;
    }
    
    private bool IsPointInBoundary(Vector2 point, List<Vector3> boundary)
    {
        // Replicate the PointInPolygon logic from your GreenSlopeManager
        bool inside = false;
        for (int i = 0, j = boundary.Count - 1; i < boundary.Count; j = i++)
        {
            Vector2 pi = new Vector2(boundary[i].x, boundary[i].z);
            Vector2 pj = new Vector2(boundary[j].x, boundary[j].z);
            
            if (((pi.y > point.y) != (pj.y > point.y)) &&
                (point.x < (pj.x - pi.x) * (point.y - pi.y) / ((pj.y - pi.y) + 1e-6f) + pi.x))
            {
                inside = !inside;
            }
        }
        return inside;
    }
    
    private TerrainComplexityProfile AnalyzeTerrainComplexity(List<Vector3> samples)
    {
        if (samples.Count < 3)
        {
            return new TerrainComplexityProfile
            {
                complexity = TerrainComplexity.Flat,
                varianceScore = 0f,
                maxSlope = 0f,
                edgePoints = new List<Vector3>()
            };
        }
        
        // Sample heights using your SmartRaycastManager
        var heights = new List<float>();
        var validSamples = new List<Vector3>();
        
        foreach (var sample in samples)
        {
            if (TryGetHeightAtPosition(sample, out float height))
            {
                heights.Add(height);
                validSamples.Add(new Vector3(sample.x, height, sample.z));
            }
        }
        
        if (heights.Count < 3)
        {
            return new TerrainComplexityProfile
            {
                complexity = TerrainComplexity.Unknown,
                varianceScore = 0f,
                maxSlope = 0f,
                edgePoints = new List<Vector3>()
            };
        }
        
        // Calculate terrain metrics
        float variance = CalculateHeightVariance(heights);
        float maxSlope = CalculateMaxSlopeBetweenPoints(validSamples);
        var edgePoints = DetectEdgePoints(validSamples);
        
        // Determine complexity
        TerrainComplexity complexity = TerrainComplexity.Flat;
        if (variance > 0.01f || maxSlope > 2f || edgePoints.Count > 2) // 1cm variance or 2% slope
            complexity = TerrainComplexity.Moderate;
        if (variance > 0.05f || maxSlope > 5f || edgePoints.Count > 5) // 5cm variance or 5% slope
            complexity = TerrainComplexity.Complex;
        
        return new TerrainComplexityProfile
        {
            complexity = complexity,
            varianceScore = variance,
            maxSlope = maxSlope,
            edgePoints = edgePoints
        };
    }
    
    private bool TryGetHeightAtPosition(Vector3 position, out float height)
    {
        height = position.y;
        
        // Try multiple height offsets like your original system
        float[] heightOffsets = { 0f, 0.4f, 0.8f };
        
        foreach (float offset in heightOffsets)
        {
            Vector3 rayStart = new Vector3(position.x, position.y + 0.6f + offset, position.z);
            Ray ray = new Ray(rayStart, Vector3.down);
            
            // Use your SmartRaycastManager if available
            if (smartRaycast != null)
            {
                if (smartRaycast.SmartRaycast(ray, out var hit))
                {
                    height = hit.pose.position.y;
                    return true;
                }
            }
            
            // Fallback to physics like your original system
            if (Physics.Raycast(ray, out var physHit, 2f))
            {
                height = physHit.point.y;
                return true;
            }
        }
        
        return false;
    }
    
    private float CalculateHeightVariance(List<float> heights)
    {
        if (heights.Count < 2) return 0f;
        
        float mean = heights.Average();
        float variance = heights.Sum(h => (h - mean) * (h - mean)) / heights.Count;
        return Mathf.Sqrt(variance);
    }
    
    private float CalculateMaxSlopeBetweenPoints(List<Vector3> points)
    {
        float maxSlope = 0f;
        
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                Vector3 p1 = points[i];
                Vector3 p2 = points[j];
                
                float horizontalDist = Vector3.Distance(
                    new Vector3(p1.x, 0, p1.z),
                    new Vector3(p2.x, 0, p2.z)
                );
                
                if (horizontalDist > 0.1f)
                {
                    float slope = Mathf.Abs(p2.y - p1.y) / horizontalDist * 100f; // Convert to percentage
                    maxSlope = Mathf.Max(maxSlope, slope);
                }
            }
        }
        
        return maxSlope;
    }
    
    private List<Vector3> DetectEdgePoints(List<Vector3> points)
    {
        var edgePoints = new List<Vector3>();
        float edgeThreshold = edgeDetectionSensitivity;
        
        foreach (var point in points)
        {
            // Find nearby points
            var nearbyPoints = points.Where(p => 
                Vector3.Distance(p, point) < 1f && p != point
            ).ToList();
            
            if (nearbyPoints.Count >= 2)
            {
                float heightVariation = nearbyPoints.Max(p => p.y) - nearbyPoints.Min(p => p.y);
                if (heightVariation > edgeThreshold)
                {
                    edgePoints.Add(point);
                }
            }
        }
        
        return edgePoints;
    }
    
    private int CalculateOptimalSampleCount(TerrainComplexityProfile profile)
    {
        int baseSamples = minSamplesFlat;
        
        switch (profile.complexity)
        {
            case TerrainComplexity.Flat:
                baseSamples = minSamplesFlat;
                break;
            case TerrainComplexity.Moderate:
                baseSamples = Mathf.RoundToInt((minSamplesFlat + maxSamplesComplex) * 0.5f);
                break;
            case TerrainComplexity.Complex:
                baseSamples = maxSamplesComplex;
                break;
            default:
                baseSamples = minSamplesFlat;
                break;
        }
        
        // Adjust based on detected edges
        float edgeMultiplier = 1f + (profile.edgePoints.Count * 0.1f);
        baseSamples = Mathf.RoundToInt(baseSamples * edgeMultiplier);
        
        // Clamp to reasonable limits
        return Mathf.Clamp(baseSamples, minSamplesFlat, maxSamplesComplex);
    }
    
    private Rect CalculateGridBounds(List<Vector3> boundary)
    {
        if (boundary.Count == 0) return new Rect(0, 0, 10, 10);
        
        float minX = boundary.Min(p => p.x);
        float maxX = boundary.Max(p => p.x);
        float minZ = boundary.Min(p => p.z);
        float maxZ = boundary.Max(p => p.z);
        
        return new Rect(minX, minZ, maxX - minX, maxZ - minZ);
    }
    
    private float CalculateOptimalStepSize(Rect bounds, int sampleCount)
    {
        float area = bounds.width * bounds.height;
        float idealStepSize = Mathf.Sqrt(area / sampleCount);
        
        // Clamp to reasonable values
        return Mathf.Clamp(idealStepSize, 0.02f, 0.5f);
    }
    
    private List<Vector3> GenerateFocusedSamples(Rect bounds, int targetCount)
    {
        var samples = new List<Vector3>();
        var boundary = GetBoundaryPoints();
        
        // Generate grid points within boundary
        int gridX = Mathf.CeilToInt(bounds.width / currentStepSize);
        int gridZ = Mathf.CeilToInt(bounds.height / currentStepSize);
        
        for (int ix = 0; ix <= gridX; ix++)
        {
            for (int iz = 0; iz <= gridZ; iz++)
            {
                float x = bounds.x + ix * currentStepSize;
                float z = bounds.y + iz * currentStepSize;
                Vector2 testPoint = new Vector2(x, z);
                
                if (IsPointInBoundary(testPoint, boundary))
                {
                    samples.Add(new Vector3(x, 0, z)); // Y will be filled by height sampling
                    
                    if (samples.Count >= targetCount) break;
                }
            }
            if (samples.Count >= targetCount) break;
        }
        
        // Add focused samples near detected edge points
        if (currentProfile.edgePoints.Count > 0)
        {
            foreach (var edgePoint in currentProfile.edgePoints)
            {
                // Add additional samples around edge points
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI * 0.5f;
                    float radius = currentStepSize * 0.5f;
                    Vector3 nearbyPoint = edgePoint + new Vector3(
                        Mathf.Cos(angle) * radius,
                        0,
                        Mathf.Sin(angle) * radius
                    );
                    
                    if (IsPointInBoundary(new Vector2(nearbyPoint.x, nearbyPoint.z), boundary))
                    {
                        samples.Add(nearbyPoint);
                    }
                }
            }
        }
        
        return samples;
    }
    
    private List<Vector3> SampleHeightsIntelligently(List<Vector3> samplePoints)
    {
        if (smartRaycast != null)
        {
            // Use your existing SmartRaycastManager's terrain sampling
            Debug.Log($"[IntelligentSampler] Using SmartRaycastManager for {samplePoints.Count} points");
            return smartRaycast.SmartTerrainSample(samplePoints, maxSamplesComplex);
        }
        else
        {
            // Fallback to individual sampling
            Debug.Log($"[IntelligentSampler] Using fallback sampling for {samplePoints.Count} points");
            var results = new List<Vector3>();
            
            foreach (var point in samplePoints)
            {
                if (TryGetHeightAtPosition(point, out float height))
                {
                    results.Add(new Vector3(point.x, height, point.z));
                }
            }
            
            return results;
        }
    }
    
    private Dictionary<Vector2Int, float> BuildCompatibleHeightField(List<Vector3> heightData, Rect bounds)
    {
        var heightField = new Dictionary<Vector2Int, float>();
        
        foreach (var point in heightData)
        {
            // Convert world position to grid indices (compatible with your existing system)
            int ix = Mathf.RoundToInt((point.x - bounds.x) / currentStepSize);
            int iz = Mathf.RoundToInt((point.z - bounds.y) / currentStepSize);
            
            var gridPos = new Vector2Int(ix, iz);
            heightField[gridPos] = point.y;
        }
        
        return heightField;
    }
    
    private void RenderUsingExistingSystem(Dictionary<Vector2Int, float> heightField, Rect bounds)
    {
        // Call your existing GreenSlopeManager's ClearCells method
        try
        {
            var clearCellsMethod = parentManager.GetType().GetMethod("ClearCells", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            clearCellsMethod?.Invoke(parentManager, null);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[IntelligentSampler] Could not call ClearCells: {e.Message}");
        }
        
        // Render cells with existing logic
        RenderCellsWithExistingLogic(heightField, bounds);
    }
    
    private void RenderUsingEnhancedSystem(Dictionary<Vector2Int, float> heightField, Rect bounds)
    {
        if (!enhancedViz) return;
        
        // Use your EnhancedSlopeVisualizer with the new height data
        enhancedViz.GenerateContourLines(heightField, new Vector2(bounds.x, bounds.y), currentStepSize);
        
        // Also render cells if needed
        RenderCellsWithExistingLogic(heightField, bounds);
    }
    
    private void RenderCellsWithExistingLogic(Dictionary<Vector2Int, float> heightField, Rect bounds)
    {
        // Get references to GreenSlopeManager private fields through reflection
        var cellQuadsField = parentManager.GetType().GetField("cellQuads",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cellMaterialField = parentManager.GetType().GetField("cellMat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var getContentParentMethod = parentManager.GetType().GetMethod("GetContentParent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var slopeToColorMethod = parentManager.GetType().GetMethod("SlopeToColor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var setMatColorMethod = parentManager.GetType().GetMethod("SetMatColor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (cellQuadsField == null || cellMaterialField == null) 
        {
            Debug.LogWarning("[IntelligentSampler] Could not access GreenSlopeManager private fields");
            return;
        }
        
        var cellQuads = cellQuadsField.GetValue(parentManager) as List<GameObject>;
        var cellMat = cellMaterialField.GetValue(parentManager) as Material;
        var parent = getContentParentMethod?.Invoke(parentManager, null) as Transform;
        
        if (cellQuads == null || cellMat == null) return;
        
        // Calculate elevation range for enhanced visualization
        float minElevation = heightField.Values.Count > 0 ? heightField.Values.Min() : 0f;
        float maxElevation = heightField.Values.Count > 0 ? heightField.Values.Max() : 0f;
        
        Debug.Log($"[IntelligentSampler] Rendering {heightField.Count} cells, elevation range: {(maxElevation-minElevation)*100:F1}cm");
        
        // Render cells using your existing approach but with intelligent sampling
        foreach (var kvp in heightField)
        {
            var ij = kvp.Key;
            var height = kvp.Value;
            
            // Check for neighbors (required for slope calculation)
            if (!heightField.TryGetValue(new Vector2Int(ij.x - 1, ij.y), out var hL)) continue;
            if (!heightField.TryGetValue(new Vector2Int(ij.x + 1, ij.y), out var hR)) continue;
            if (!heightField.TryGetValue(new Vector2Int(ij.x, ij.y - 1), out var hD)) continue;
            if (!heightField.TryGetValue(new Vector2Int(ij.x, ij.y + 1), out var hU)) continue;
            
            // Calculate slope using your existing central difference method
            float dhdx = (hR - hL) / (2f * currentStepSize);
            float dhdz = (hU - hD) / (2f * currentStepSize);
            float slopePct = 100f * Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz);
            
            // Log to your existing slope debug monitor
            if (slopeDebug)
            {
                Vector3 cellCenter = new Vector3(
                    bounds.x + ij.x * currentStepSize, 
                    height, 
                    bounds.y + ij.y * currentStepSize
                );
                slopeDebug.LogSlopeCalculation(slopePct, dhdx, dhdz, currentStepSize, cellCenter);
            }
            
            // Create cell quad using your existing approach
            var center = new Vector3(bounds.x + ij.x * currentStepSize, height, bounds.y + ij.y * currentStepSize);
            
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SlopeCell";
            if (parent) quad.transform.SetParent(parent, true);
            quad.transform.position = center + Vector3.up * 0.001f;
            quad.transform.rotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = new Vector3(currentStepSize, currentStepSize, 1);
            var col = quad.GetComponent<Collider>(); 
            if (col) Object.Destroy(col);
            
            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = cellMat;
            var instance = new Material(cellMat);
            
            // Get color using your existing or enhanced method
            Color cellColor;
            if (enhancedViz)
            {
                Vector2 slopeDirection = new Vector2(dhdx, dhdz).normalized;
                cellColor = enhancedViz.GetCellColor(slopePct, height, minElevation, maxElevation, slopeDirection);
            }
            else
            {
                // Use reflection to call your existing SlopeToColor method
                var colorResult = slopeToColorMethod?.Invoke(parentManager, new object[] { slopePct });
                cellColor = colorResult != null ? (Color)colorResult : Color.white;
            }
            
            setMatColorMethod?.Invoke(parentManager, new object[] { instance, cellColor });
            renderer.material = instance;
            
            cellQuads.Add(quad);
        }
    }
    
    private void UpdateZeroIsoline()
    {
        try
        {
            var updateZeroIsolineMethod = parentManager.GetType().GetMethod("UpdateZeroIsoline",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            updateZeroIsolineMethod?.Invoke(parentManager, null);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[IntelligentSampler] Could not call UpdateZeroIsoline: {e.Message}");
        }
    }
    
    private void UpdateDebugMonitors()
    {
        if (slopeDebug)
        {
            slopeDebug.AnalyzeHeightField(currentHeightField);
        }
        
        if (aiDebug)
        {
            // Log successful analysis
            Debug.Log($"[IntelligentSampler] Analysis complete - " +
                     $"Complexity: {currentProfile.complexity}, " +
                     $"Samples: {currentHeightField.Count}, " +
                     $"Step: {currentStepSize:F3}m");
        }
    }
    
    // Public getters for other components
    public TerrainComplexityProfile GetCurrentProfile() => currentProfile;
    public Dictionary<Vector2Int, float> GetCurrentHeightField() => currentHeightField;
    public float GetCurrentStepSize() => currentStepSize;
}

public enum TerrainComplexity
{
    Unknown,
    Flat,
    Moderate,
    Complex
}

public struct TerrainComplexityProfile
{
    public TerrainComplexity complexity;
    public float varianceScore;      // Height variance in meters
    public float maxSlope;          // Maximum slope in percentage
    public List<Vector3> edgePoints; // Points with significant elevation change
}