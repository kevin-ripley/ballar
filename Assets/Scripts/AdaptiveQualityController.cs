// File: Extensions/AdaptiveQualityController.cs
using UnityEngine;
using System.Collections;

/// <summary>
/// Adaptive quality controller that works with your existing GreenSlopeManager and SmartRaycastManager
/// Automatically adjusts sampling density and visualization complexity based on performance
/// </summary>
public class AdaptiveQualityController : MonoBehaviour
{
    [Header("Quality Levels")]
    public QualitySettings highQuality = new QualitySettings
    {
        maxSampleCount = 4000,
        minStepSize = 0.02f,
        enableEnhancedVisualization = true,
        contourLineCount = 15,
        useAdaptiveSampling = true
    };
    
    public QualitySettings mediumQuality = new QualitySettings
    {
        maxSampleCount = 2000,
        minStepSize = 0.05f,
        enableEnhancedVisualization = true,
        contourLineCount = 10,
        useAdaptiveSampling = true
    };
    
    public QualitySettings lowQuality = new QualitySettings
    {
        maxSampleCount = 800,
        minStepSize = 0.1f,
        enableEnhancedVisualization = false,
        contourLineCount = 5,
        useAdaptiveSampling = false
    };
    
    [Header("Adaptation Settings")]
    public float adaptationDelay = 2f;
    public float hysteresisMargin = 0.1f;
    
    private GreenSlopeManager greenSlope;
    private SmartRaycastManager smartRaycast;
    private EnhancedPerformanceMonitor performanceMonitor;
    private IntelligentTerrainSampler intelligentSampler;
    private EnhancedSlopeVisualizer enhancedViz;
    
    private PerformanceLevel currentQualityLevel = PerformanceLevel.High;
    private float lastAdaptationTime;
    private bool isAdapting;
    
    [System.Serializable]
    public class QualitySettings
    {
        public int maxSampleCount;
        public float minStepSize;
        public bool enableEnhancedVisualization;
        public int contourLineCount;
        public bool useAdaptiveSampling;
    }
    
    private void Start()
    {
        InitializeReferences();
        SetupEventListeners();
        ApplyQualitySettings(highQuality);
    }
    
    private void InitializeReferences()
    {
        greenSlope = FindFirstObjectByType<GreenSlopeManager>();
        smartRaycast = SmartRaycastManager.Instance;
        performanceMonitor = FindFirstObjectByType<EnhancedPerformanceMonitor>();
        intelligentSampler = greenSlope?.GetComponent<IntelligentTerrainSampler>();
        enhancedViz = FindFirstObjectByType<EnhancedSlopeVisualizer>();
        
        if (!greenSlope)
        {
            Debug.LogError("[AdaptiveQualityController] GreenSlopeManager not found!");
            enabled = false;
        }
    }
    
    private void SetupEventListeners()
    {
        if (performanceMonitor != null)
        {
            performanceMonitor.OnPerformanceLevelChanged += OnPerformanceChanged;
        }
    }
    
    private void OnPerformanceChanged(PerformanceLevel newLevel)
    {
        if (isAdapting || Time.time - lastAdaptationTime < adaptationDelay)
            return;
        
        if (ShouldAdaptQuality(newLevel))
        {
            StartCoroutine(AdaptQualityGradually(newLevel));
        }
    }
    
    private bool ShouldAdaptQuality(PerformanceLevel newLevel)
    {
        // Don't adapt if already at the same level
        if (newLevel == currentQualityLevel) return false;
        
        // Don't reduce quality during active analysis
        if (IsAnalysisActive() && newLevel < currentQualityLevel)
            return false;
        
        // Apply hysteresis to prevent oscillation
        if (newLevel > currentQualityLevel)
        {
            // Improving - allow immediate upgrade
            return true;
        }
        else
        {
            // Degrading - require significant performance drop
            return performanceMonitor.GetPerformanceLevel() < currentQualityLevel;
        }
    }
    
    private bool IsAnalysisActive()
    {
        // Check if your GreenSlopeManager is currently analyzing
        return intelligentSampler != null && intelligentSampler.enabled;
    }
    
    private IEnumerator AdaptQualityGradually(PerformanceLevel targetLevel)
    {
        isAdapting = true;
        
        Debug.Log($"[AdaptiveQuality] Adapting from {currentQualityLevel} to {targetLevel}");
        
        var targetSettings = GetQualitySettings(targetLevel);
        ApplyQualitySettings(targetSettings);
        
        currentQualityLevel = targetLevel;
        lastAdaptationTime = Time.time;
        
        // Brief delay to let system stabilize
        yield return new WaitForSeconds(0.5f);
        
        isAdapting = false;
        
        Debug.Log($"[AdaptiveQuality] Adaptation complete. New level: {currentQualityLevel}");
    }
    
    private QualitySettings GetQualitySettings(PerformanceLevel level)
    {
        switch (level)
        {
            case PerformanceLevel.High: return highQuality;
            case PerformanceLevel.Medium: return mediumQuality;
            case PerformanceLevel.Low: return lowQuality;
            default: return mediumQuality;
        }
    }
    
    private void ApplyQualitySettings(QualitySettings settings)
    {
        // Apply to IntelligentTerrainSampler
        if (intelligentSampler)
        {
            intelligentSampler.maxSamplesComplex = settings.maxSampleCount;
            intelligentSampler.enableAdaptiveDensity = settings.useAdaptiveSampling;
        }
        
        // Apply to EnhancedSlopeVisualizer
        if (enhancedViz)
        {
            enhancedViz.UpdateVisualizationMode(
                settings.enableEnhancedVisualization,
                true, // Always show slope gradient
                settings.enableEnhancedVisualization
            );
        }
        
        // Apply to SmartRaycastManager (adjust cache settings)
        if (smartRaycast)
        {
            AdjustRaycastManagerSettings(settings);
        }
        
        Debug.Log($"[AdaptiveQuality] Applied settings - " +
                 $"MaxSamples: {settings.maxSampleCount}, " +
                 $"Enhanced: {settings.enableEnhancedVisualization}");
    }
    
    private void AdjustRaycastManagerSettings(QualitySettings settings)
    {
        // Access SmartRaycastManager's settings through reflection or public properties
        // Since your SmartRaycastManager has adaptive quality built-in, we can work with that
        var enableAdaptiveField = smartRaycast.GetType().GetField("enableAdaptiveQuality");
        if (enableAdaptiveField != null)
        {
            enableAdaptiveField.SetValue(smartRaycast, settings.useAdaptiveSampling);
        }
    }
    
    public void ForceQualityLevel(PerformanceLevel level)
    {
        StopAllCoroutines();
        var settings = GetQualitySettings(level);
        ApplyQualitySettings(settings);
        currentQualityLevel = level;
        isAdapting = false;
        lastAdaptationTime = Time.time;
    }
    
    public PerformanceLevel GetCurrentQualityLevel() => currentQualityLevel;
}