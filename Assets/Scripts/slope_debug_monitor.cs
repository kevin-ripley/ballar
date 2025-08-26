using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Debug component to monitor slope calculations and identify why terrain appears flat
/// Add this to diagnose slope calculation issues
/// </summary>
public class SlopeDebugMonitor : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool enableDebugGUI = true;
    public bool logSlopeStatistics = true;
    public int maxLoggedSlopes = 10; // Limit console spam
    
    private List<float> recentSlopes = new List<float>();
    private float minSlope = float.MaxValue;
    private float maxSlope = float.MinValue;
    private float avgSlope = 0f;
    private int totalSlopeSamples = 0;
    private int loggedSlopeCount = 0;
    
    private GreenSlopeManager greenSlope;
    private EnhancedSlopeVisualizer slopeViz;
    
    void Start()
    {
        greenSlope = FindFirstObjectByType<GreenSlopeManager>();
        slopeViz = FindFirstObjectByType<EnhancedSlopeVisualizer>();
        
        if (!greenSlope)
        {
            Debug.LogWarning("[SlopeDebug] No GreenSlopeManager found!");
        }
        
        if (!slopeViz)
        {
            Debug.LogWarning("[SlopeDebug] No EnhancedSlopeVisualizer found!");
        }
    }
    
    /// <summary>
    /// Call this from GreenSlopeManager when calculating slopes
    /// </summary>
    public void LogSlopeCalculation(float slopePct, float dhdx, float dhdz, float stepSize, Vector3 position)
    {
        totalSlopeSamples++;
        recentSlopes.Add(slopePct);
        
        // Keep only last 100 slopes
        if (recentSlopes.Count > 100)
        {
            recentSlopes.RemoveAt(0);
        }
        
        // Update statistics
        if (slopePct < minSlope) minSlope = slopePct;
        if (slopePct > maxSlope) maxSlope = slopePct;
        
        // Calculate running average
        avgSlope = recentSlopes.Average();
        
        // Log detailed info for first few samples
        if (logSlopeStatistics && loggedSlopeCount < maxLoggedSlopes)
        {
            Debug.Log($"[SlopeDebug] Sample {loggedSlopeCount + 1}: " +
                     $"Slope={slopePct:F3}%, dhdx={dhdx:F4}, dhdz={dhdz:F4}, " +
                     $"step={stepSize:F3}m, pos=({position.x:F2}, {position.z:F2})");
            loggedSlopeCount++;
        }
        
        // Log if we find any significant slopes
        if (slopePct > 1f && loggedSlopeCount < maxLoggedSlopes * 2)
        {
            Debug.Log($"[SlopeDebug] SIGNIFICANT SLOPE FOUND: {slopePct:F2}% at ({position.x:F2}, {position.z:F2})");
        }
    }
    
    /// <summary>
    /// Reset statistics when starting new analysis
    /// </summary>
    public void ResetStatistics()
    {
        recentSlopes.Clear();
        minSlope = float.MaxValue;
        maxSlope = float.MinValue;
        avgSlope = 0f;
        totalSlopeSamples = 0;
        loggedSlopeCount = 0;
        
        Debug.Log("[SlopeDebug] Statistics reset for new analysis");
    }
    
    void OnGUI()
    {
        if (!enableDebugGUI) return;
        
        GUILayout.BeginArea(new Rect(Screen.width - 350, 10, 340, 250));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("Slope Analysis Debug", GUI.skin.box);
        
        if (totalSlopeSamples > 0)
        {
            GUILayout.Label($"Total Samples: {totalSlopeSamples}");
            GUILayout.Label($"Min Slope: {minSlope:F3}%");
            GUILayout.Label($"Max Slope: {maxSlope:F3}%");
            GUILayout.Label($"Avg Slope: {avgSlope:F3}%");
            GUILayout.Label($"Recent Samples: {recentSlopes.Count}");
            
            // Show slope distribution
            int flatCount = recentSlopes.Count(s => s < 0.5f);
            int gentleCount = recentSlopes.Count(s => s >= 0.5f && s < 2f);
            int moderateCount = recentSlopes.Count(s => s >= 2f && s < 5f);
            int steepCount = recentSlopes.Count(s => s >= 5f);
            
            GUILayout.Space(5);
            GUILayout.Label("Slope Distribution:");
            GUILayout.Label($"  Flat (<0.5%): {flatCount}");
            GUILayout.Label($"  Gentle (0.5-2%): {gentleCount}");
            GUILayout.Label($"  Moderate (2-5%): {moderateCount}");
            GUILayout.Label($"  Steep (>5%): {steepCount}");
            
            // Show color coding effectiveness
            if (maxSlope < 0.1f)
            {
                GUILayout.Space(5);
                GUIStyle warningStyle = new GUIStyle(GUI.skin.label);
                warningStyle.normal.textColor = Color.red;
                GUILayout.Label("WARNING: All slopes < 0.1%", warningStyle);
                GUILayout.Label("Terrain may be too flat for", warningStyle);
                GUILayout.Label("meaningful visualization", warningStyle);
            }
        }
        else
        {
            GUILayout.Label("No slope data available");
            GUILayout.Label("Run boundary analysis first");
        }
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Reset Statistics"))
        {
            ResetStatistics();
        }
        
        if (GUILayout.Button("Force Slope Test"))
        {
            TestSlopeCalculation();
        }
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    /// <summary>
    /// Test slope calculation with known values
    /// </summary>
    void TestSlopeCalculation()
    {
        Debug.Log("[SlopeDebug] Running slope calculation test...");
        
        // Simulate a 1% slope over 0.1m step
        float testStep = 0.1f;
        float testDhdx = 0.01f; // 1% slope in X direction
        float testDhdz = 0.0f;  // No slope in Z direction
        float expectedSlope = 100f * Mathf.Sqrt(testDhdx * testDhdx + testDhdz * testDhdz);
        
        Debug.Log($"[SlopeDebug] Test: dhdx={testDhdx:F4}, dhdz={testDhdz:F4}, step={testStep:F3}m");
        Debug.Log($"[SlopeDebug] Expected slope: {expectedSlope:F2}%");
        
        LogSlopeCalculation(expectedSlope, testDhdx, testDhdz, testStep, Vector3.zero);
    }
    
    /// <summary>
    /// Analyze height field to identify potential issues
    /// </summary>
    public void AnalyzeHeightField(Dictionary<Vector2Int, float> heightField)
    {
        if (heightField.Count == 0) return;
        
        var heights = heightField.Values.ToList();
        float minHeight = heights.Min();
        float maxHeight = heights.Max();
        float heightRange = maxHeight - minHeight;
        
        Debug.Log($"[SlopeDebug] Height Analysis:");
        Debug.Log($"  Min Height: {minHeight:F4}m");
        Debug.Log($"  Max Height: {maxHeight:F4}m");
        Debug.Log($"  Range: {heightRange * 100:F1}cm");
        
        if (heightRange < 0.01f) // Less than 1cm range
        {
            Debug.LogWarning($"[SlopeDebug] Very small height range ({heightRange * 100:F1}cm) - " +
                           "this may result in flat visualization. Consider:");
            Debug.LogWarning("  1. Moving to area with more elevation change");
            Debug.LogWarning("  2. Reducing contourInterval in EnhancedSlopeVisualizer");
            Debug.LogWarning("  3. Lowering minimumVisibleSlope threshold");
        }
        else if (heightRange < 0.05f) // Less than 5cm range
        {
            Debug.LogWarning($"[SlopeDebug] Small height range ({heightRange * 100:F1}cm) - " +
                           "consider adjusting visualization sensitivity");
        }
        else
        {
            Debug.Log($"[SlopeDebug] Good height range for visualization ({heightRange * 100:F1}cm)");
        }
    }
}