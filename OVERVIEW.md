# Ballar — Mixed Reality Application Overview

## What Is Ballar?

Ballar is a Mixed Reality (MR) golf slope analysis application for Android XR headsets (XREAL). It overlays real-time terrain analysis onto a physical golf green, helping golfers understand slope gradients, elevation changes, and putting line characteristics.

The core workflow is:
1. **Scan** the green using AR depth sensing
2. **Place** a hole marker and define an analysis boundary
3. **Visualize** slope and elevation as a color-coded heatmap
4. **Measure** a two-point putt line with slope and distance data

---

## Tech Stack

| Layer | Technology |
|---|---|
| Engine | Unity 6.0.30f1 |
| Platform | Android (arm64-v8a) |
| AR Framework | AR Foundation 6.0.6 / ARCore 6.0.6 |
| XR Device SDK | XREAL (com.xreal.xr) |
| Rendering | Universal Render Pipeline (URP) 17.0.3 |
| Hand Tracking | XR Hands 1.6.1 |
| Interaction | XR Interaction Toolkit 3.0.8 |
| Input | Unity Input System 1.11.2 |
| UI Text | TextMesh Pro |

---

## Scenes

| Scene | Purpose |
|---|---|
| `Menu.unity` | Main menu — entry point, scene selection |
| `GreenSlopeScene.unity` | Primary live AR slope analysis experience |
| `SlopeCalculating.unity` | Transition/processing state during analysis |
| `TrueAimPracticeScene.unity` | Practice/training mode |

---

## Application Modes

### ScanAnalyze Mode
Live AR mode. The device's depth sensors sample the terrain in real time. The user places markers, defines a boundary, and views the slope heatmap overlaid on the physical green.

### Review Mode
Plays back a previously captured OBJ mesh. The same analysis tools work on the saved mesh without needing to be physically on the course.

Mode switching is managed by `AppModeManager.cs`.

---

## Core Systems

### 1. Green Slope Analysis — `GreenSlopeManager.cs` (764 lines)

The central analysis engine.

**Workflow:**
1. User places a hole marker (3.56" cylinder, matching golf hole diameter)
2. User defines a boundary polygon by placing 3+ gaze-selected points
3. On confirmation, the system casts a dense grid of raycasts downward within the polygon
4. Each cell's height is sampled; central-difference derivatives compute slope
5. Slope percentage is mapped to a color and rendered as a flat heatmap quad

**Key parameters:**
- Grid spacing: 0.1 m (configurable)
- Raycast fallback heights: 0 m → 0.4 m → 0.8 m (for surface coverage)
- Zero-slope isoline: highlighted bright line showing near-flat zones

**Slope color scale:**
```
White (0%) → Green → Yellow → Orange → Red (steep)
```

### 2. Putt Line Measurement — `PuttLineManager.cs` (428 lines)

Two-point measurement system for analyzing a specific putting path.

**Workflow:**
1. User gazes at start point (A); 0.4-second dwell confirms placement
2. User gazes at end point (B); same dwell confirms
3. A surface-following polyline renders from A to B
4. HUD displays: distance (feet or meters), slope %, cross-slope component

**Precision features:**
- Multi-sample averaging over the dwell window
- Outlier rejection using Median Absolute Deviation (MAD)
- AR anchor support for persistent placement across small head movements
- Configurable dwell time (default 0.4 s)

### 3. Smart Raycast Manager — `SmartRaycastManager.cs`

A performance optimization layer that sits between all AR consumers and the AR Foundation raycast API.

**Capabilities:**
- **Spatial/temporal caching:** Re-uses recent hits for rays pointing at already-sampled locations
- **Gaze prediction:** Uses a 10-frame gaze history buffer to pre-cast likely next rays
- **Adaptive quality scaling:** Monitors frame time; reduces raycast density when frame budget is tight
- **Batch terrain sampling:** Accepts a grid of points and processes them in a single pass

Target: sustained 60 fps on Snapdragon XR hardware.

### 4. Enhanced Slope Visualizer — `EnhancedSlopeVisualizer.cs` / `enhanced_slope_visualization.cs`

Advanced visualization modes beyond the basic heatmap.

**Modes:**
- **Elevation contours:** Lines of constant height at 2 cm intervals (marching squares algorithm)
- **Slope gradient:** Smooth gradient from green (flat) to red (steep)
- **Elevation coloring:** Blue (low) → Red (high)
- **Directional flow:** Arrows or lines indicating downhill direction

### 5. Green Analyzer — `GreenAnalyzer.cs` (316 lines)

A standalone analysis component used for both live and review modes. Handles:
- Polygon-bounded grid sampling
- Contour extraction (elevation or slope percent)
- LineRenderer pooling for efficient contour rendering
- Dynamic sampling density adjustment

### 6. AR Mesh Capture & Export — `ARMeshCaptureExporter.cs`

Captures the live AR mesh and serializes it to an OBJ file in `Application.persistentDataPath` with a timestamp. The saved file can be reloaded in Review mode for offline analysis.

---

## Input & Interaction

### Head-Gaze System
- `GazeRayProvider.cs` — Produces a stabilized head-gaze ray using exponential smoothing
- `GazeReticle.cs` / `GazeReticleStable.cs` — Visual reticles for gaze feedback
- Dwell-time confirmation (default 0.4 s) replaces button taps for hands-free operation

### Controller System
- `ballarLaser.cs` — Laser pointer ray from XREAL controller
- `ControllerReticle.cs` — Visual feedback for controller ray
- `GreenSlopeInputBridge.cs` — Maps XREAL physical buttons to actions:
  - Place hole marker
  - Add boundary point
  - Finish/confirm boundary
- `XrealPlacementInputBridge.cs` — Maps buttons for putt line actions:
  - Auto-advance placement
  - Reset line

All input handling includes debouncing (default 0.15 s) and state-based validation to prevent accidental triggers.

---

## Visualization Pipeline

```
AR Depth Data
     │
     ▼
SmartRaycastManager (cached, predicted, batched)
     │
     ▼
GreenSlopeManager / GreenAnalyzer
  └─ Grid height field
  └─ Central difference slope calculation
     │
     ▼
EnhancedSlopeVisualizer
  └─ Color mapping (slope % → RGBA)
  └─ Contour generation (marching squares)
  └─ LineRenderer pooling
     │
     ▼
URP Rendered quads / lines overlaid on physical green
```

---

## Performance & Quality

### Adaptive Quality Controller — `AdaptiveQualityController.cs`
Dynamically adjusts:
- Raycast grid density
- Contour line resolution
- Visualization update frequency

Based on measured frame time vs. the 60 fps target budget.

### Debug & Monitoring Tools
| Script | Purpose |
|---|---|
| `AIPerformanceMonitor.cs` | Frame time, cache hit rate, quality level display |
| `SlopeDebugMonitor.cs` | Slope calculation statistics |
| `debug_performance_monitor.cs` | General performance overlay |
| `slope_debug_monitor.cs` | Slope-specific analytics HUD |

---

## Project File Structure

```
ballar/
├── Assets/
│   ├── Scripts/          # 31 C# files, ~6,200 lines
│   ├── Scenes/           # 4 Unity scenes
│   ├── Prefabs/          # Laser, placement points, contour lines
│   ├── Materials/        # URP materials for mesh and heatmap
│   ├── Textures/         # Ground / mesh textures
│   ├── Settings/         # URP renderer and XR settings
│   ├── XR/               # AR Foundation loader configuration
│   └── XRI/              # XR Interaction Toolkit presets
├── Packages/
│   ├── manifest.json     # Package dependencies
│   └── packages-lock.json
├── ProjectSettings/      # Unity project configuration
├── *.apk                 # 13 iterative build variants (~67 MB each)
└── .git/
```

---

## Script Inventory

### Core Managers
| File | Lines | Role |
|---|---|---|
| `GreenSlopeManager.cs` | 764 | Slope analysis engine |
| `PuttLineManager.cs` | 428 | Two-point putt measurement |
| `GreenAnalyzer.cs` | 316 | Standalone polygon analyzer |
| `SmartRaycastManager.cs` | — | AI-optimized raycast coordinator |
| `AppModeManager.cs` | — | Scan/Review mode switching |

### Visualization
| File | Role |
|---|---|
| `EnhancedSlopeVisualizer.cs` | Contour lines, gradient coloring |
| `enhanced_slope_visualization.cs` | Color mapping utilities |

### Input & Interaction
| File | Role |
|---|---|
| `GazeRayProvider.cs` | Head-gaze ray with smoothing |
| `GazeReticle.cs` | Gaze reticle visual |
| `GazeReticleStable.cs` | Stabilized gaze reticle |
| `GreenSlopeInputBridge.cs` | XREAL button → slope actions |
| `XrealPlacementInputBridge.cs` | XREAL button → putt line actions |
| `ballarLaser.cs` | Controller laser pointer |
| `ControllerReticle.cs` | Controller ray visual |
| `XREAL_Interaction_Calculator.cs` | XREAL-specific calculations |

### AR & Mesh
| File | Role |
|---|---|
| `ARMeshCaptureExporter.cs` | OBJ mesh export |
| `AddMeshCollider.cs` | Attach MeshCollider to AR mesh |

### Analysis Utilities
| File | Role |
|---|---|
| `LocalSlopeEstimator.cs` | Neighborhood slope fitting |
| `IntelligentTerrainSampler.cs` | Adaptive sample distribution |
| `SlopeCalculator.cs` | Legacy slope calculations |

### Performance
| File | Role |
|---|---|
| `AdaptiveQualityController.cs` | Dynamic quality scaling |
| `AIPerformanceMonitor.cs` | Performance HUD |
| `debug_performance_monitor.cs` | General frame tracking |
| `slope_debug_monitor.cs` | Slope analytics HUD |

### UI & Utility
| File | Role |
|---|---|
| `SceneLoader.cs` | Scene transitions |
| `HudToggles.cs` | UI visibility toggles |
| `ReviewBrowserUI.cs` | Review mode file browser |
| `PredictiveAnalysisLoader.cs` | Pre-load analysis data |
| `GreenSlopeHUDlessControls.cs` | No-UI mode support |
| `SmartRaycastManagerExtensions.cs` | Extension methods |

---

## Build Variants

13 APK files are tracked in the repository, each representing an iteration of development:

| APK | Focus |
|---|---|
| `greenslope_initial.apk` | Initial stable baseline |
| `greenslope_solo.apk` | Single-player variant |
| `greenslopeboundary_solo.apk` | Boundary system testing |
| `AIPerformance1implementation_initial.apk` | AI raycast optimization pass 1 |
| `AIPerformance2implementation_initial.apk` | AI raycast optimization pass 2 |
| `laserraytest.apk` / `laserraytest2.apk` | Laser pointer system testing |
| `menu_manslope.apk` / `menu_slope_green.apk` | Menu UI variants |
| `meshertest.apk` | Mesh export testing |
| `slopecalculate_working.apk` | Slope calculation verification |
| `updatedlaser.apk` / `updatedlaser_v2.apk` | Laser system refinements |

---

## Key Algorithms

### Slope Calculation (Central Differences)
```
dh/dx = (h[x+1,z] - h[x-1,z]) / (2 * gridSpacing)
dh/dz = (h[x,z+1] - h[x,z-1]) / (2 * gridSpacing)
slope_pct = 100 * sqrt((dh/dx)² + (dh/dz)²)
```

### Outlier Rejection (MAD)
```
median = median(samples)
MAD = median(|samples - median|)
valid = samples where |sample - median| < k * MAD  (k ≈ 2.5)
```

### Contour Generation (Marching Squares)
Iterates over the height field grid, classifies each 2×2 cell by threshold crossing, and interpolates edge intersection points to build contour line segments.

---

## Deployment

- **Target device:** XREAL Air / XREAL One (Snapdragon XR SoC)
- **OS:** Android, arm64-v8a
- **AR backend:** ARCore with XREAL SDK overlay
- **Persistent storage:** `Application.persistentDataPath` for OBJ exports
- **Frame budget:** 60 fps target; adaptive quality fallback to maintain smoothness
