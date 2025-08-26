using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Enhanced terrain visualization that shows clear contours, elevation changes, and slope gradients
/// Replaces the simple SlopeToColor method in GreenSlopeManager with advanced visualization
/// </summary>
public class EnhancedSlopeVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private bool showElevationContours = true;
    [SerializeField] private bool showSlopeGradient = true;
    [SerializeField] private bool showDirectionalArrows = false;
    
    [Header("Contour Lines")]
    [SerializeField] private float contourInterval = 0.02f; // 2cm elevation intervals
    [SerializeField] private Color contourLineColor = Color.black;
    [SerializeField] private float contourLineWidth = 0.003f;
    [SerializeField] private Material contourLineMaterial;
    
    [Header("Elevation Colors")]
    [SerializeField] private bool useElevationColoring = true;
    [SerializeField] private Color lowElevationColor = new Color(0.2f, 0.4f, 0.8f, 0.8f);  // Blue
    [SerializeField] private Color highElevationColor = new Color(0.8f, 0.2f, 0.2f, 0.8f); // Red
    
    [Header("Slope Gradient")]
    [SerializeField] private float maxSlopeForVisualization = 10f; // 10% slope = full red
    [SerializeField] private Color flatColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);      // Green
    [SerializeField] private Color steepColor = new Color(0.9f, 0.1f, 0.1f, 0.9f);     // Red
    [SerializeField] private Color moderateColor = new Color(0.9f, 0.7f, 0.1f, 0.8f);  // Yellow
    
    [Header("Advanced Features")]
    [SerializeField] private bool showWaterFlow = false;
    [SerializeField] private bool adaptiveContourDensity = true;
    [SerializeField] private float minimumVisibleSlope = 0.1f; // Lowered from 0.5% to 0.1%
    [SerializeField] private bool debugColoring = true; // Add debug output
    
    private readonly List<LineRenderer> contourLines = new List<LineRenderer>();
    private Transform contentParent;
    private float currentStepSize; // Store the current step size for calculations
    
    /// <summary>
    /// Enhanced slope-to-color conversion with multiple visualization modes
    /// </summary>
    public Color GetCellColor(float slopePct, float elevation, float minElevation, float maxElevation, Vector2 slopeDirection)
    {
        Color finalColor = Color.white;
        
        if (useElevationColoring && showElevationContours)
        {
            // Elevation-based coloring
            float elevationT = Mathf.InverseLerp(minElevation, maxElevation, elevation);
            finalColor = Color.Lerp(lowElevationColor, highElevationColor, elevationT);
        }
        
        if (showSlopeGradient)
        {
            // Slope-based coloring
            Color slopeColor = GetSlopeColor(slopePct);
            
            if (useElevationColoring)
            {
                // Blend elevation and slope colors
                finalColor = Color.Lerp(finalColor, slopeColor, 0.6f);
            }
            else
            {
                finalColor = slopeColor;
            }
        }
        
        // Add contour line effects by darkening near contour elevations
        if (showElevationContours)
        {
            float contourEffect = GetContourLineEffect(elevation);
            finalColor = Color.Lerp(finalColor, Color.black, contourEffect * 0.3f);
        }
        
        return finalColor;
    }
    
    /// <summary>
    /// Advanced slope coloring with smooth gradients and meaningful thresholds
    /// </summary>
    private Color GetSlopeColor(float slopePct)
    {
        if (debugColoring && slopePct > 0.01f) // Log any slope above 0.01%
        {
            Debug.Log($"[SlopeViz] Slope: {slopePct:F2}% -> Color calculation");
        }
        
        if (slopePct < minimumVisibleSlope)
        {
            return flatColor;
        }
        
        // More sensitive slope categories for subtle terrain:
        // 0-0.5%: Flat (Green)
        // 0.5-1%: Very Gentle (Light Green) 
        // 1-2%: Gentle (Yellow-Green)
        // 2-3%: Moderate (Yellow)
        // 3-5%: Steep (Orange)
        // 5%+: Very Steep (Red)
        
        if (slopePct <= 0.5f)
        {
            float t = slopePct / 0.5f;
            return Color.Lerp(flatColor, new Color(0.6f, 0.9f, 0.6f, 0.8f), t);
        }
        else if (slopePct <= 1f)
        {
            float t = (slopePct - 0.5f) / 0.5f;
            return Color.Lerp(new Color(0.6f, 0.9f, 0.6f, 0.8f), new Color(0.8f, 1f, 0.4f, 0.8f), t);
        }
        else if (slopePct <= 2f)
        {
            float t = (slopePct - 1f) / 1f;
            return Color.Lerp(new Color(0.8f, 1f, 0.4f, 0.8f), moderateColor, t);
        }
        else if (slopePct <= 3f)
        {
            float t = (slopePct - 2f) / 1f;
            return Color.Lerp(moderateColor, new Color(1f, 0.6f, 0.2f, 0.8f), t);
        }
        else if (slopePct <= 5f)
        {
            float t = (slopePct - 3f) / 2f;
            return Color.Lerp(new Color(1f, 0.6f, 0.2f, 0.8f), steepColor, t);
        }
        else
        {
            return steepColor;
        }
    }
    
    /// <summary>
    /// Calculate contour line effect - darkens cells near elevation contours
    /// </summary>
    private float GetContourLineEffect(float elevation)
    {
        if (!adaptiveContourDensity) return 0f;
        
        float contourLevel = Mathf.Round(elevation / contourInterval) * contourInterval;
        float distanceToContour = Mathf.Abs(elevation - contourLevel);
        float contourThreshold = contourInterval * 0.1f; // 10% of contour interval
        
        if (distanceToContour < contourThreshold)
        {
            return 1f - (distanceToContour / contourThreshold);
        }
        
        return 0f;
    }
    
    /// <summary>
    /// Generate 3D contour lines for the terrain
    /// </summary>
    public void GenerateContourLines(Dictionary<Vector2Int, float> heightField, Vector2 gridMin, float stepSize)
    {
        if (!showElevationContours) return;
        
        ClearContourLines();
        
        if (heightField.Count == 0) return;
        
        // Store step size for use in other methods
        currentStepSize = stepSize;
        
        float minHeight = heightField.Values.Min();
        float maxHeight = heightField.Values.Max();
        
        // Generate contour lines at regular elevation intervals
        for (float elevation = minHeight; elevation <= maxHeight; elevation += contourInterval)
        {
            var contourPoints = ExtractContourLine(heightField, gridMin, stepSize, elevation);
            
            if (contourPoints.Count >= 2)
            {
                CreateContourLineRenderer(contourPoints, elevation);
            }
        }
    }
    
    /// <summary>
    /// Extract contour line points at a specific elevation using marching squares-like algorithm
    /// </summary>
    private List<Vector3> ExtractContourLine(Dictionary<Vector2Int, float> heightField, Vector2 gridMin, float stepSize, float targetElevation)
    {
        var contourPoints = new List<Vector3>();
        var processedEdges = new HashSet<string>();
        
        foreach (var kvp in heightField)
        {
            var gridPos = kvp.Key;
            var height = kvp.Value;
            
            // Check horizontal edge (to the right)
            var rightPos = new Vector2Int(gridPos.x + 1, gridPos.y);
            if (heightField.TryGetValue(rightPos, out float rightHeight))
            {
                string edgeKey = $"H_{gridPos.x}_{gridPos.y}";
                if (!processedEdges.Contains(edgeKey))
                {
                    var intersection = GetContourIntersection(
                        new Vector3(gridMin.x + gridPos.x * stepSize, height, gridMin.y + gridPos.y * stepSize),
                        new Vector3(gridMin.x + rightPos.x * stepSize, rightHeight, gridMin.y + rightPos.y * stepSize),
                        targetElevation
                    );
                    
                    if (intersection.HasValue)
                    {
                        contourPoints.Add(intersection.Value);
                    }
                    processedEdges.Add(edgeKey);
                }
            }
            
            // Check vertical edge (upward)
            var upPos = new Vector2Int(gridPos.x, gridPos.y + 1);
            if (heightField.TryGetValue(upPos, out float upHeight))
            {
                string edgeKey = $"V_{gridPos.x}_{gridPos.y}";
                if (!processedEdges.Contains(edgeKey))
                {
                    var intersection = GetContourIntersection(
                        new Vector3(gridMin.x + gridPos.x * stepSize, height, gridMin.y + gridPos.y * stepSize),
                        new Vector3(gridMin.x + upPos.x * stepSize, upHeight, gridMin.y + upPos.y * stepSize),
                        targetElevation
                    );
                    
                    if (intersection.HasValue)
                    {
                        contourPoints.Add(intersection.Value);
                    }
                    processedEdges.Add(edgeKey);
                }
            }
        }
        
        // Sort contour points to create smoother lines
        return SortContourPoints(contourPoints);
    }
    
    /// <summary>
    /// Find intersection point where contour line crosses an edge
    /// </summary>
    private Vector3? GetContourIntersection(Vector3 p1, Vector3 p2, float targetElevation)
    {
        float h1 = p1.y;
        float h2 = p2.y;
        
        // Check if the target elevation is between the two heights
        if ((h1 <= targetElevation && h2 >= targetElevation) || (h1 >= targetElevation && h2 <= targetElevation))
        {
            if (Mathf.Abs(h2 - h1) < 1e-6f) return null; // Avoid division by zero
            
            float t = (targetElevation - h1) / (h2 - h1);
            Vector3 intersection = Vector3.Lerp(p1, p2, t);
            intersection.y = targetElevation + 0.002f; // Lift slightly above surface
            return intersection;
        }
        
        return null;
    }
    
    /// <summary>
    /// Sort contour points to create continuous lines
    /// </summary>
    private List<Vector3> SortContourPoints(List<Vector3> points)
    {
        if (points.Count <= 2) return points;
        
        var sorted = new List<Vector3>();
        var remaining = new List<Vector3>(points);
        
        // Start with first point
        sorted.Add(remaining[0]);
        remaining.RemoveAt(0);
        
        // Connect closest points to create continuous lines
        while (remaining.Count > 0)
        {
            Vector3 lastPoint = sorted[sorted.Count - 1];
            int closestIndex = 0;
            float closestDist = Vector3.Distance(lastPoint, remaining[0]);
            
            for (int i = 1; i < remaining.Count; i++)
            {
                float dist = Vector3.Distance(lastPoint, remaining[i]);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }
            }
            
            // Only connect if points are reasonably close (avoid jumping across terrain)
            if (closestDist < currentStepSize * 2f)
            {
                sorted.Add(remaining[closestIndex]);
            }
            remaining.RemoveAt(closestIndex);
        }
        
        return sorted;
    }
    
    /// <summary>
    /// Create a LineRenderer for a contour line
    /// </summary>
    private void CreateContourLineRenderer(List<Vector3> points, float elevation)
    {
        if (points.Count < 2) return;
        
        var lineObj = new GameObject($"Contour_{elevation:F3}m");
        if (contentParent) lineObj.transform.SetParent(contentParent);
        
        var lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.material = contourLineMaterial ? contourLineMaterial : CreateDefaultContourMaterial();
        lineRenderer.widthMultiplier = contourLineWidth;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
        
        // Set color through material instead of direct color property
        if (lineRenderer.material != null)
        {
            lineRenderer.material.color = contourLineColor;
        }
        
        contourLines.Add(lineRenderer);
    }
    
    /// <summary>
    /// Create default material for contour lines
    /// </summary>
    private Material CreateDefaultContourMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var material = new Material(shader);
        material.color = contourLineColor;
        return material;
    }
    
    /// <summary>
    /// Clear all existing contour lines
    /// </summary>
    public void ClearContourLines()
    {
        foreach (var line in contourLines)
        {
            if (line && line.gameObject) DestroyImmediate(line.gameObject);
        }
        contourLines.Clear();
    }
    
    /// <summary>
    /// Set the parent transform for contour lines
    /// </summary>
    public void SetContentParent(Transform parent)
    {
        contentParent = parent;
    }
    
    /// <summary>
    /// Update visualization settings at runtime
    /// </summary>
    public void UpdateVisualizationMode(bool elevation, bool slope, bool contours)
    {
        useElevationColoring = elevation;
        showSlopeGradient = slope;
        showElevationContours = contours;
    }
}