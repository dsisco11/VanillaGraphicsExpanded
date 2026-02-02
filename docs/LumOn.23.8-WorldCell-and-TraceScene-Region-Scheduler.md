# LumOn 23.8 — WorldCell + TraceSceneRegionScheduler (Architecture Plan)

## Motivation
TraceScene’s current “enqueue region coords into a FIFO queue” approach creates recurring failure modes:

- **Duplicate work** (same region requested repeatedly with no central arbitration).
- **Unbounded churn on missing chunks** (`ChunkUnavailable` / chunk missing) that can consume the per-frame budgets forever.
- **Poor convergence behavior** (holes that never fill because the queue order does not reflect importance, staleness, or availability).
- **Hard to extend** to other “sub-partitions of world space” beyond TraceScene regions (e.g., surface-cache capture/relight partitions).

This document proposes a general `WorldCell` abstraction plus a dedicated `TraceSceneRegionScheduler` that:

- Maintains **exactly one authoritative entry per cell** (no duplicates).
- Uses an **indexed priority queue** that supports updating priorities in-place.
- Introduces **eligibility/cooldown** semantics so missing chunks don’t thrash budgets.
- Exposes clear diagnostics (why work was/wasn’t scheduled).

## Terminology
- **World cell**: A sub-partition of world space used as a unit of streaming / processing / caching.
- **Region cell** (TraceScene v1): A 32³ “world-cell region” aligned to Vintage Story chunk coordinates (`chunkX/chunkY/chunkZ`).
- **Loadedness**: Whether a cell is currently likely to exist on the client (loaded chunk) vs missing/unavailable.

## Goals
- Provide a reusable `IWorldCell` / `WorldCell` base that can represent “some sub-partition of the world”.
- Encapsulate TraceScene scheduling policy into `TraceSceneRegionScheduler`.
- Ensure bounded, predictable per-frame CPU cost via time budgets.
- Ensure forward progress: missing/unavailable cells must not permanently prevent other work from being scheduled.

## Non-goals (initial)
- Rewriting LumonScene surface-cache scheduling to fully use `WorldCell` in the first iteration.
- Perfect Lumen parity (this is about scheduling and robustness, not algorithmic GI parity).

---

## Core Abstractions

### `IWorldCell`
Cells are polymorphic, but they must share a consistent scheduling contract.

Proposed shape (names are indicative):

- Identity
  - `WorldCellKey Key` (stable, hashable)
  - `WorldCellKind Kind` (e.g., `TraceSceneRegion`, `LumonSceneNearCapture`, ...)
- Scheduling state (owned by scheduler)
  - `WorldCellScheduleState ScheduleState` (`Pending`, `InFlight`, `Applied`, `Suppressed`)
  - `float Priority`
  - `long NextEligibleTick` (cooldown/backoff gate)
  - `int HeapIndex` (indexed PQ support)
- Versioning / staleness
  - `int CurrentVersion` (from a version provider; may be 0 when unknown)
  - `int AppliedVersion` (last applied to GPU cache)

Priority contract:

- `float CalculatePriority(in WorldCellPriorityContext ctx)`
  - Must be **pure** (no side effects).
  - Must be **cheap** (called many times over time).
  - Should incorporate:
    - distance/importance (camera, anchors)
    - staleness (not applied, older version)
    - availability hints (recently loaded, recently failed)

Eligibility/backoff contract:

- Scheduler controls `NextEligibleTick`.
- Cell can expose hints like `bool IsHardInvalid(in ctx)` if desired (optional).

### `WorldCellKey`
We need a single key type that supports:

- dictionary lookups
- stable equality
- optional packing into `ulong` (fast)

For TraceScene region cells, `ChunkKey` already provides a packed 64-bit key.

Proposed:

- `readonly record struct WorldCellKey(ulong Packed)`
  - Contains kind + packed coords.
  - For `TraceSceneRegion` kind, reuse the existing `ChunkKey.Packed` encoding (or embed it).

### `WorldCellPriorityContext`
Provides the data needed by `CalculatePriority`, without leaking renderer internals:

- Camera position (block units, int or double)
- Current TraceScene clipmap window bounds (regionMin/regionMax)
- Current time/frame tick (Stopwatch ticks + frame index)
- Optional “loadedness” map (see below)

Important: avoid VintageStory vector types in new logic; prefer `VanillaGraphicsExpanded.Numerics` vector types.

---

## TraceSceneRegionScheduler

### Responsibilities
`TraceSceneRegionScheduler` is a self-contained module that owns the *policy* and the *data structures* needed to decide “what to do next”.

It must:

1. Maintain a single authoritative record per region cell:
   - `Dictionary<ChunkKey, TraceSceneRegionCell>` (or `WorldCellKey -> IWorldCell`)
2. Maintain an indexed priority queue of *eligible* cells.
3. Track in-flight requests and applied versions.
4. Apply backoff for failures (especially `ChunkUnavailable` / snapshot unavailable).
5. Expose metrics for overlay/debugging.

### Data Structures
- `Dictionary<ulong, TraceSceneRegionCell>`: authoritative entries keyed by packed chunk key.
- `IndexedMaxHeap<ulong>`: keys ordered by priority; supports:
  - `Upsert(key, newPriority)`
  - `Update(key, newPriority)` in O(log N)
  - `TryPop(out key)` in O(log N)
  - Each cell stores `HeapIndex` for O(1) update.

Why not `PriorityQueue<TElement,TPriority>`?
- .NET’s built-in `PriorityQueue` does not support decrease/increase-key updates efficiently, which is required to avoid duplicates.

### Availability / Loadedness Hints
We want to bias strongly toward regions that actually exist on the client.

Sources:
- `ChunkDirty(NewlyLoaded / NewlyCreated / MarkedDirty)` observed *inside TraceScene* (not via `LumOnModSystem`)
  - Update a “recently seen” timestamp for that chunk key.
- Snapshot results:
  - If snapshot fails due to missing chunk, treat as unavailable and back off.

Represent as:
- `Dictionary<ulong, long> lastSeenLoadedTick` (or within the cell entry).
- `int missingStreak` per cell.

### Backoff Policy (initial)
When a cell cannot produce a payload (`ChunkUnavailable` / snapshot unavailable):

- Increase `missingStreak` (clamped).
- Set `NextEligibleTick = now + Backoff(missingStreak)`
  - e.g. exponential: 0.1s, 0.2s, 0.4s, ... up to 5s
- Keep the cell in the dictionary, but don’t let it dominate the heap.

### Priority Function (TraceScene v1)
Target behavior:
- Fill the clipmap window with near-camera regions first.
- Still converge the rest of the window over time.
- Avoid starvation.

Example terms:
- `P_distance`: inverse distance from camera region coord (or from clipmap anchor).
- `P_stale`: +large if `AppliedVersion != CurrentVersion` or never applied.
- `P_loaded`: +bonus if `now - lastSeenLoadedTick < threshold`.
- `P_missingPenalty`: -penalty if `missingStreak > 0` and/or not eligible yet.

### Scheduling Loop Integration
`LumonSceneOccupancyClipmapUpdateRenderer` becomes simpler:

1. Update anchors and compute window bounds.
2. Inform scheduler of window changes:
   - `scheduler.SetWindow(regionMin, regionMax)`
   - `scheduler.SeedWindowIncrementally(...)` (internally tracked)
3. Each frame:
   - `scheduler.RefreshPriorities(in ctx, budget)` (incremental refresh)
   - Issue requests until:
     - `IssueBudgetMs` exceeded
     - `MaxInFlightRegions` reached
   - Consume completed work until:
     - `DispatchBudgetMs` exceeded
     - upload/dispatch hard caps reached

All “should we enqueue” logic lives inside the scheduler.

---

## `LumonSceneRegionCell : WorldCell` (forward-looking)
Once the scheduling machinery exists, we can represent other LumonScene partitions as cells:

Potential `LumonSceneRegionCell` contents:
- Near/Far field membership
- ChunkSlot assignment + generation
- Surface-cache mapping state:
  - virtual page allocation summary
  - physical page residency summary
  - “capture needed” vs “relight needed”
- Optional “mesh card” transforms or derived capture transforms (future)

This enables a consistent scheduling language:
- capture scheduling = “cells with missing capture”
- relight scheduling = “cells with stale irradiance”
- eviction safety = “cells inside loaded radius remain eligible/resident”

---

## Metrics / Diagnostics (must-have)
To avoid repeating the current “it stalls but why?” scenario, the scheduler should publish:

- queue size (eligible) vs suppressed (cooldown) vs in-flight vs applied
- counts of:
  - popped/issued per frame
  - completed success / chunk unavailable / canceled / superseded
  - cooldown skips
- top-k highest-priority keys with reason bits (distance/stale/loaded/missing)

These should appear in the TS overlay line as additional fields.

---

## Implementation Plan (phased)

1. **WorldCell primitives**
   - Add `WorldCellKey`, `WorldCellKind`, `WorldCellPriorityContext`
   - Add `IWorldCell` / `WorldCell` base type

2. **Indexed priority queue**
   - Implement `IndexedMaxHeap<TKey>` (key->heap index map)
   - Unit tests: insert/update/remove/pop ordering, stable updates, no duplicates

3. **TraceSceneRegionScheduler**
   - Own cell dictionary + heap
   - Window set + incremental seed
   - Priority refresh (incremental)
   - Backoff/eligibility handling for missing chunks
   - Metrics API

4. **Wire into TraceScene**
   - Replace `pendingRegionsHigh/Low + pendingRegionKeys + maintenanceCursor + windowSeedCursor` usage in `LumonSceneOccupancyClipmapUpdateRenderer`
   - Keep existing time budgets and in-flight caps

5. **Diagnostics + tests**
   - Unit tests: backoff prevents starvation; missing chunks don’t dominate; near regions converge first
   - Optional GPU/integration tests later

