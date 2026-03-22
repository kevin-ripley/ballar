# True Aim MR Training Aid — Product Specification

**Version:** 1.0
**Date:** 2026-03-22
**Platform:** XREAL Air / XREAL One (Android arm64-v8a)
**Engine:** Unity 6 + AR Foundation + XREAL SDK

---

## 1. What True Aim Is (and Why It Changes the App)

True Aim is a green-reading method where the player does **not** aim at the hole. Instead they identify the **apex** — the outermost point of the break arc — and aim their putter face at that point, letting gravity bring the ball to the hole from there.

The existing app produces a static heatmap after a multi-step boundary-drawing workflow. True Aim requires a fundamentally different interaction model:

| Existing App | True Aim App |
|---|---|
| Draw boundary → wait → view map | Immediate feedback as player moves |
| Analysis is the goal | Analysis serves a training decision |
| One-time read | Before-and-after comparison per putt |
| No player tracking | Per-session improvement over time |
| No alignment tools | Shaft + stance alignment verification |

The `TrueAimPracticeScene.unity` scene and `FallLine.prefab` already exist as stubs. This spec defines what goes in them.

---

## 2. Full Step-by-Step User Journey

### Phase A — Green Setup (done once per green)

**Step 1: Look at the hole**
Player walks up and gazes at the hole. A dwell (0.5 s) or controller button confirms placement. The app drops a virtual hole marker (standard 108 mm diameter cylinder) at the AR mesh surface hit point.

**Step 2: Look at the ball**
Player gazes at their ball on the green. Same dwell/button confirmation places a virtual ball marker. The app now has the two anchor points every subsequent calculation depends on.

**What the app knows after setup:**
- Hole world position
- Ball world position
- The straight-line distance and bearing between them (the "zero-aim line")

---

### Phase B — Green Reading (player walks the green)

**Step 3: Walk around the green and read slope**
As the player moves, the AR Foundation mesh updates continuously. The app renders a live slope visualization over the entire visible green surface — not just inside a drawn boundary. No boundary-drawing step exists in this workflow.

**What is shown:**
- Slope heatmap (color-coded, % labeled on each zone)
- Fall line through the hole — the steepest downhill line, always visible
- Break direction arrows showing which way putts will bend in each area
- Slope percentage labels anchored in world space so they stay readable as the player moves

**Slope education overlay:**
A persistent side panel or world-anchored legend shows what each color band means numerically. This is intentional — the goal is to teach the player to read slope without technology so they can compete without the headset.

```
■ White   0 – 1%    Nearly flat
■ Green   1 – 3%    Gentle break, 1–3 inches per 10 feet
■ Yellow  3 – 5%    Moderate, requires apex aim
■ Orange  5 – 8%    Significant, careful read required
■ Red     8 – 12%   Steep, apex will be well outside hole
■ Purple  12%+      Extreme, consider lag putt strategy
```

---

### Phase C — Alignment (player gets behind ball)

**Step 4: Get behind ball, line up with hole**
Player walks behind the ball so ball and hole are in the same line of sight. The app detects when the player's gaze vector approximately passes through both the ball marker and the hole marker (within a configurable angular tolerance, default 3°).

**What is shown:**
- A straight virtual line from the ball marker through to the hole marker, extended behind the ball toward the player
- A real-time "alignment angle" reading: how many degrees off the ball-to-hole line the player is currently standing
- Green indicator when alignment is within 3° (well aligned), yellow for 3–8°, red for >8°

**Step 5: Shaft alignment**
Player raises their putting shaft and points it toward the hole. The player uses their XREAL controller as a proxy for the shaft — they hold it as they would hold their putter shaft extended in front of them, pointing down the line.

The app projects the controller's forward vector onto the green plane and compares it to the ball-to-hole bearing.

**What is shown:**
- A virtual shaft line extending from the controller ray, cast onto the green surface
- Angular offset between the shaft line and the ball-to-hole line (in degrees)
- Target: within 1° for competition-level alignment

**Step 6: Calibration lock-in**
When both body alignment (Step 4) and shaft alignment (Step 5) are within tolerance, a "ALIGNED" confirmation appears. Player can optionally lock this as their stance reference for the session — subsequent putts compare to this baseline.

---

### Phase D — True Aim Read

**Step 7: Gauge the slope and decide**
With the slope visualization still active, the player looks at the area between ball and hole and forms their read. The app displays:
- The slope percentage directly on the ball-to-hole corridor
- The fall line showing which way the green tilts
- Suggested break direction arrows along the putting corridor

**The app does NOT tell the player where to aim yet** — this step is about the player forming their own read, which is then compared to the computed prediction. Training only works if the player commits first.

**Step 8: Place the True Aim marker**
Player gazes at the point outside the hole where they believe the ball should enter — the apex. A button press drops the True Aim marker at that world position.

**The marker represents:**
- The direction the player intends to aim their putter face
- The entry angle they expect the ball to approach the hole from

**Step 9: Adjust the marker**
Player can nudge the marker left/right by gazing at an adjustment control or using the controller thumbstick. The app updates the proposed aim line from ball through marker in real time.

When the player confirms, the app:
1. Records the player's apex selection
2. Computes the predicted break curve (see Section 4)
3. Shows the computed optimal apex alongside the player's selection
4. Shows the angle difference between the two aim lines

---

### Phase E — Putt Execution and Feedback

**Step 10: Ball tracking**
After the player putts, one of two tracking approaches is used (see Section 5 for hardware detail):

- **Computer vision path:** App detects ball movement on camera feed, tracks trajectory
- **Manual path (Phase 1 fallback):** Player confirms outcome — "Made it," "Left edge," "Right edge," "Short," "Long"

**What is recorded per putt:**
- Player's aim line (ball → True Aim marker)
- Computed optimal aim line
- Angle difference between them
- Putt outcome (in / miss direction / miss distance)
- Whether the miss direction matched the break prediction

**Step 11: Putter contact feedback**
The app estimates putter face angle at impact and approximate ball speed (see Section 5 for technical approach). This is used to distinguish:
- "Good read, poor strike" — player aimed correctly but mishit
- "Poor read, good strike" — player struck well but wrong aim line
- "Good read, good strike" — systematic performance

---

## 3. Data Model

```
PlayerProfile
├── id (uuid)
├── name
└── sessions[]

GolfSession
├── id
├── timestamp
├── course (string, manual entry)
├── hole (int)
├── green_conditions (string: "fast" / "medium" / "slow")
├── weather (string)
├── mesh_export_path (string, OBJ file if captured)
└── putt_reads[]

PuttRead
├── id
├── session_id
├── timestamp
├── ball_position (Vector3)         ← world space
├── hole_position (Vector3)         ← world space
├── distance_feet (float)
├── slope_at_ball (float%)
├── slope_at_hole (float%)
├── avg_slope_corridor (float%)
├── fall_line_bearing (float°)      ← degrees from north of green
├── player_apex (Vector3)           ← where player placed True Aim marker
├── computed_apex (Vector3)         ← system's optimal apex
├── player_aim_angle (float°)       ← angle of player's aim line
├── computed_aim_angle (float°)     ← angle of optimal aim line
├── aim_error (float°)              ← |player - computed|
├── alignment_body_error (float°)   ← stance alignment accuracy
├── alignment_shaft_error (float°)  ← shaft alignment accuracy
└── putt_result (PuttResult)

PuttResult
├── outcome (enum: IN / LEFT / RIGHT / SHORT / LONG / SHORT_LEFT / etc.)
├── miss_distance_inches (float, 0 if IN)
├── ball_end_position (Vector3)     ← if tracking available
├── ball_path (List<Vector3>)       ← if tracking available
├── contact_face_angle (float°)     ← if sensor available
├── estimated_ball_speed (float)    ← if tracking available
└── notes (string)

PlayerProgress  [computed from PuttRead history]
├── total_putts
├── make_percentage
├── avg_aim_error_by_slope_range    ← e.g. "on 3-5% slopes, avg 1.2° off"
├── alignment_consistency
├── break_read_accuracy             ← player vs. computed apex distance
└── improvement_trend               ← rolling 10-putt vs. lifetime avg
```

---

## 4. Break Prediction Algorithm

The computed optimal apex is derived from:

1. **Sample the corridor** — cast a grid of raycasts between ball and hole (10 cm spacing along the line, 3 cells wide)
2. **Fit a slope plane** — use `LocalSlopeEstimator.cs` (already exists, 5-star quality) to determine the dominant slope direction and magnitude in the corridor
3. **Apply a break model:**
   ```
   lateral_break_inches = slope_pct × distance_feet × speed_factor
   ```
   Where `speed_factor` defaults to 0.9 for a "die at the hole" speed (adjustable slider for aggressive / dying pace)
4. **Compute apex position** — project laterally from the hole center by `lateral_break_inches` in the uphill direction
5. **Build the curved path** — Bezier curve from ball through apex into hole for visualization

**Displayed alongside the player's read**, never replacing it during the read phase.

---

## 5. Hardware Capabilities and Tracking

### Ball Detection

| Approach | Accuracy | Complexity | Phase |
|---|---|---|---|
| Gaze dwell on ball (manual) | Player-dependent | Low | Phase 1 (now) |
| White blob detection on camera feed | ±5 cm | Medium | Phase 2 |
| AR image marker next to ball | ±2 cm | Low | Phase 2 alt |

**Recommended Phase 1:** Gaze dwell. Player looks at ball, dwells 0.5 s, confirmed. Fast, reliable, no CV complexity.
**Recommended Phase 2:** White circle detection using the XREAL's RGB camera feed + AR Foundation's camera image API. The ball is 42.67 mm diameter white sphere on a green surface — good contrast, detectable with basic circle Hough transform.

### Ball Path Tracking

XREAL has a forward-facing RGB camera. At 30–60 fps with the ball in frame:
- Between-frame optical flow can track a moving white ball
- After the putt the player keeps their head still for 2 seconds, maintaining camera view of the ball rolling
- AR Foundation `ARCameraManager.frameReceived` gives each camera frame as a `NativeArray<byte>`

**Limitation:** Camera frame rate and ball speed may make tracking partial. Fallback is manual outcome entry.

### Putter Head Tracking

**Face angle at impact** (hardest requirement):
- Option A: **Controller as putter** — player holds XREAL controller at the grip end of the putter. The controller's IMU gives face angle at impact via quaternion at the moment of impact detection. Simple and practical.
- Option B: **Vision at address** — measure face angle while the putter is still (at address), not at impact. AR Foundation can detect the shaft line at address with reasonable accuracy.
- Option C: **Third-party Bluetooth sensor** (e.g., Arccos, Blast Motion) attached to the grip. These output impact data via BLE. Unity has BLE plugin support.

**Recommended Phase 1:** Controller-as-putter for face angle (Option A). Player holds controller against the grip. The controller's orientation at the point they press "putt" gives the face angle.
**Recommended Phase 3:** Blast Motion or equivalent BLE sensor for real impact data.

**Ball speed estimation:**
- If ball path tracking is active, speed = distance between first two tracked frames × frame rate
- If controller-as-putter, accelerometer spike at moment of "putt" button gives a relative impact force (not calibrated to actual mph without known putter mass)

---

## 6. Phased Delivery Plan

### Phase 1 — Core True Aim Loop (6–8 weeks)

**Goal:** A complete, working putt-read workflow from hole placement through outcome recording.

**Includes:**
- Gaze-based hole placement (reuses existing gaze system)
- Gaze-based ball placement
- Live slope visualization with no boundary drawing required
- Slope percentage labels anchored in world space
- Fall line through hole (continuous, not one-time)
- Alignment overlay — body and shaft (controller proxy)
- True Aim marker placement and adjustment
- Break prediction curve display
- Manual putt outcome entry (IN / MISS direction)
- Local session data persistence (JSON to `persistentDataPath`)
- Per-session accuracy summary screen

**Does NOT include in Phase 1:**
- Ball path tracking via CV
- Putter contact tracking
- Progress dashboard / history charts

**Architecture work before Phase 1 features begin:**
- Delete dead code (1–2 days, see ARCHITECTURE_DECISION.md)
- Build clean `IRaycastProvider`, `IBreakCalculator`, `IVisualizationLayer` interfaces (3–5 days)
- Refactor `GreenSlopeManager` → 5 focused classes (5–7 days)
- Total architecture prep: ~2 weeks before first Phase 1 feature

---

### Phase 2 — Training Intelligence (4–6 weeks)

**Goal:** The app teaches the player to read slope without the headset.

**Includes:**
- Slope education mode: labeled examples of 1%, 3%, 5%, 8% slopes with visual reference
- Accuracy scoring: player's apex vs. computed apex (distance in inches, angle in degrees)
- Rolling accuracy trend (last 10 putts vs. lifetime)
- Break read accuracy broken down by slope range
- White ball detection via camera (eliminates gaze-placement step for ball)
- Session export to shareable format for coach review

---

### Phase 3 — Contact and Speed (6–8 weeks)

**Goal:** Distinguish bad reads from bad strikes.

**Includes:**
- Controller-as-putter face angle at impact
- Ball path tracking via optical flow
- Impact speed estimation
- Per-putt diagnosis: read error vs. strike error vs. combined
- BLE sensor integration stub (Blast Motion / Arccos)
- Progress dashboard with charts

---

## 7. Existing Assets to Reuse

| Asset | Status | Use in True Aim |
|---|---|---|
| `TrueAimPracticeScene.unity` | Stub scene, exists | Primary development scene |
| `FallLine.prefab` | Exists | Fall line visualization (Step 3) |
| `PuttLine.prefab` | Exists | Ball-to-apex aim line |
| `PointMarker.prefab` | Exists | Ball and hole placement markers |
| `LocalSlopeEstimator.cs` | 5-star quality | Break prediction (Step 9, Section 4) |
| `ARMeshCaptureExporter.cs` | Solid | Mesh save for review mode |
| `AppModeManager.cs` | Clean | Mode transitions |
| `GazeRayProvider.cs` | Clean | Gaze ray for all placements |
| `ballarLaser.*` | Clean | Controller shaft proxy (Step 5) |
| `AddMeshCollider.cs` | Solid | AR mesh → physics surface |

---

## 8. New Systems Required (Phase 1)

| System | Description | Replaces |
|---|---|---|
| `TrueAimSession.cs` | Orchestrates one putt-read from setup through outcome | `GreenSlopeManager` (god object) |
| `GreenReader.cs` | Continuous slope sampling, no boundary required | `GreenSlopeManager.AnalyzeAndRender` |
| `BreakPredictor.cs` | Computes apex and break curve from slope data | Nothing (new) |
| `AlignmentChecker.cs` | Body + shaft alignment verification | Nothing (new) |
| `TrueAimMarker.cs` | Placeable apex marker with adjustment controls | Nothing (new) |
| `SlopeLabel.cs` | World-anchored slope % text anchors | Nothing (new) |
| `PuttLogger.cs` | Records PuttRead + PuttResult to persistent JSON | Nothing (new) |
| `RaycastService.cs` | Single `IRaycastProvider` replacing all fallback chains | `SmartRaycastManager` (god object) |

---

## 9. Open Questions (Needs Answers Before Phase 2 Work)

| # | Question | Impact |
|---|---|---|
| 1 | What is the target ball speed / pace of play assumption? (die at hole vs. 12 inches past) | Affects break prediction formula `speed_factor` |
| 2 | Should the app show the computed break **before or after** the player commits their read? | Core training philosophy decision |
| 3 | Will a coach use this alongside a student, or is it solo only? | Determines if session sharing / multi-view is needed |
| 4 | Is the Blast Motion or similar BLE sensor already owned / budget approved? | Determines Phase 3 putter tracking approach |
| 5 | Is the review / offline mode (OBJ playback) needed for True Aim, or only the live mode? | Affects how much of `ARMeshCaptureExporter` to retain prominently |

---

## 10. Success Criteria

| Metric | Target |
|---|---|
| Setup time (hole + ball placement) | < 30 seconds |
| Slope visualization latency | < 2 seconds from walking up to green |
| Alignment feedback latency | Real-time (< 100 ms update) |
| True Aim marker placement time | < 10 seconds |
| Session data write reliability | 100% (no lost putts) |
| Frame rate during visualization | ≥ 60 fps on XREAL Air |
| Player aim accuracy (target after 10 sessions) | avg error < 2° on 3–8% slopes |
