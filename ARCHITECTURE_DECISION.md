# Architecture Decision: Continue vs. Rebuild

**Question:** Should we continue with the existing codebase or start fresh for the True Aim MR golf training aid?

---

## Short Answer

**Neither option in its pure form. The right path is a selective rebuild within the existing Unity project.**

Keep the Unity project shell, AR device integration, and the small handful of high-quality scripts. Discard the rest and rebuild the application layer cleanly, designed around the True Aim method from day one.

---

## What Is True Aim (in context of this decision)?

True Aim is a green-reading and putting methodology that emphasizes identifying the exact apex of a putt's break and committing to a specific target line rather than aiming at the hole. An MR training aid built on this method requires:

- **Break visualization** — the actual curved path a ball will travel based on slope
- **Apex point identification** — the highest point of the break arc
- **Aim line overlay** — the straight line from ball to apex the player must commit to
- **Fall line** — the steepest-downhill line through the hole (zero-break line)
- **Slope reading at foot level** — fast, intuitive, real-time feedback
- **Training feedback loops** — did the player aim correctly, did the ball follow the predicted path?

The current codebase solves a related but different problem: it produces a static slope heatmap after a multi-step boundary definition workflow. That's useful for analysis but too slow and complex for the moment-to-moment training loop True Aim requires.

---

## Full Assessment: What We Have

### What Works Well

| Component | Quality | Decision |
|---|---|---|
| Unity 6 + XREAL SDK setup | Proven, working on device | **Keep** |
| AR Foundation configuration | Calibrated, tested | **Keep** |
| URP renderer pipeline | Correct for XR | **Keep** |
| `LocalSlopeEstimator.cs` | 5-star math, no dependencies | **Keep** |
| `AddMeshCollider.cs` | Solid, single purpose | **Keep** |
| `ARMeshCaptureExporter.cs` | OBJ export works | **Keep** |
| `AppModeManager.cs` | Clean mode switching | **Keep** |
| `SceneLoader.cs` / `HudToggles.cs` | Trivially correct | **Keep** |
| `ballarLaser.cs` / `ballarLaserVisual.cs` | Clean, low coupling | **Keep** |
| `GazeRayProvider.cs` | Clean, focused | **Keep** |

### What Has Serious Problems

| Component | Problem | Decision |
|---|---|---|
| `GreenSlopeManager.cs` (764 lines) | God object: 9 responsibilities, creates GameObjects, manages materials, mixed concerns | **Rebuild** |
| `SmartRaycastManager.cs` (461 lines) | Singleton bottleneck, 6 concerns, 8+ hard dependents | **Rebuild** |
| `GazeReticle.cs` + `GazeReticleStable.cs` | 60% duplicated across both files, no base class | **Rebuild as one** |
| `IntelligentTerrainSampler.cs` (677 lines) | Calls private methods via reflection, messy integration | **Rebuild** |
| `PuttLineManager.cs` (428 lines) | Large state machine, sampling logic mixed with UI logic | **Rebuild** |
| `EnhancedSlopeVisualizer.cs` | Overlaps with `GreenAnalyzer.cs` contour logic | **Rebuild (unified)** |

### What Should Be Deleted Immediately

| File | Reason |
|---|---|
| `SlopeCalculator.cs` | 100% commented out — abandoned |
| `PredictiveAnalysisLoader.cs` | All terrain sampling methods are stubs — never implemented |
| `AdaptiveQualityController.cs` | Uses reflection to access a field that doesn't exist — silently broken |
| `slope_debug_monitor.cs` | Superseded by other monitors |
| `debug_performance_monitor.cs` | Duplicate of `AIDebugMonitor.cs` |

**Deleted lines freed:** ~1,100 lines of dead or broken code removed immediately.

---

## Why Not Start Completely Fresh?

Starting a new Unity project from scratch means re-solving problems that are already solved:

1. **XREAL SDK integration** — The SDK, package versions, and manifest configuration took real time to get right. The packages-lock.json records exactly what works together.
2. **AR Foundation setup** — Loader configuration, ARCore backend, XR session management are all configured and proven on device.
3. **URP pipeline** — The renderer is tuned for XR (single-pass instancing, correct depth settings).
4. **Build configuration** — Android arm64-v8a, burst compilation, 13 tested APK builds proved the pipeline works.
5. **Good domain code** — `LocalSlopeEstimator.cs` contains solid plane-fitting math that would be reimplemented identically.

Re-solving all of this is approximately 2–4 weeks of non-feature work with a high risk of introducing new device-specific bugs.

---

## Why Not Continue As-Is?

The existing application layer has structural problems that compound over time:

1. **`GreenSlopeManager` is a wall** — Every True Aim feature (break prediction, apex identification, fall line) would be bolted onto a class already doing 9 things. It will become 12 things, then 15.
2. **The workflow doesn't match True Aim** — The current UX is: scan → place hole → draw boundary → wait for analysis → view heatmap. True Aim needs: look at ball → see break line immediately. These are fundamentally different interaction models.
3. **Duplication multiplies** — Adding a third gaze interaction pattern on top of the two existing (90% identical) implementations creates a third instance of the same bug.
4. **Two systems are silently broken** — `AdaptiveQualityController` and `PredictiveAnalysisLoader` are loaded and consuming memory at runtime while doing nothing. New engineers on the project will waste time trying to understand them.

---

## The Recommended Path: Selective Rebuild

### Step 1 — Prune (1–2 days)
Delete everything confirmed dead or broken:
- `SlopeCalculator.cs`
- `PredictiveAnalysisLoader.cs`
- `AdaptiveQualityController.cs`
- Duplicate debug monitors
- Commented-out blocks throughout remaining files

### Step 2 — Scaffold the True Aim architecture (1 week)
Design the new system around the True Aim workflow before writing any feature code:

```
IGazeProvider          → single interface, two implementations (head gaze, controller)
ITerrainSampler        → single interface (AR Foundation, Physics fallback, cached)
IBreakCalculator       → slope → ball physics → break curve prediction
IVisualizationLayer    → pluggable: heatmap, break line, fall line, aim line
TrueAimSession         → orchestrates a single put-reading session (replaces GreenSlopeManager)
```

### Step 3 — Port the keepers (2–3 days)
Move the high-quality scripts into the new structure with no changes to logic:
- `LocalSlopeEstimator.cs` — feeds `IBreakCalculator`
- `ARMeshCaptureExporter.cs` — retained as-is
- `AppModeManager.cs` — retained as-is
- `ballarLaser.*` — clean input, retained

### Step 4 — Build True Aim features on clean foundation
With the architecture right, each feature is an isolated unit:

| Feature | New Class | Depends On |
|---|---|---|
| Real-time slope reading | `SlopeReader` | `ITerrainSampler` |
| Fall line visualization | `FallLineRenderer` | `SlopeReader` |
| Break curve prediction | `BreakPredictor` | `LocalSlopeEstimator`, `SlopeReader` |
| Apex point display | `ApexMarker` | `BreakPredictor` |
| True Aim line | `AimLineRenderer` | `ApexMarker` |
| Training feedback | `PuttTracker` | `AimLineRenderer`, `BreakPredictor` |

---

## Effort Comparison

| Approach | Estimated Effort | Risk | Feature Velocity After |
|---|---|---|---|
| Continue as-is | 0 upfront | High (debt compounds) | Slows with every feature |
| Full rewrite | 6–8 weeks | High (re-solve device integration) | Fast after, but long ramp |
| **Selective rebuild (recommended)** | **2–3 weeks** | **Low (keeps what works)** | **Fast from week 3** |

---

## Open Questions

Before executing, these need answers:

1. **What devices must ship on?** XREAL only, or also Meta Quest, HoloLens, or phone-based AR? This affects how much of the XREAL-specific input layer to keep vs. abstract.

2. **What does the True Aim interaction flow look like step by step?** Specifically: does the player need to manually initiate a reading, or should the app automatically detect when they're standing over a ball? This determines whether the boundary-definition workflow survives in any form.

3. **Is offline/review mode a priority?** The `ARMeshCaptureExporter` and Review mode are solid. If coaches need to review sessions after the round, this stays prominent. If it's purely on-course real-time, it can be deprioritized.

4. **Is there a ball tracking requirement?** True Aim training feedback ideally compares the predicted break to the actual ball path. Does the hardware/budget support ball detection, or is feedback manual (player says "it broke more than expected")?

5. **Solo or paired use?** Current app is solo (one headset, one player). True Aim coaching could involve an instructor viewing the same overlay. This changes the architecture if simultaneous multi-device sync is needed.

---

## Summary

| | Continue As-Is | Full Rewrite | Selective Rebuild |
|---|---|---|---|
| Keeps working AR setup | ✅ | ❌ | ✅ |
| Removes broken code | ❌ | ✅ | ✅ |
| Fits True Aim workflow | ❌ | ✅ | ✅ |
| Lowest risk | — | ❌ | ✅ |
| Fastest path to True Aim features | ❌ | ❌ | ✅ |

**Recommendation: Selective rebuild. Keep the Unity project and its AR integration. Delete the dead code now. Redesign the application layer around the True Aim workflow before adding a single new feature.**
