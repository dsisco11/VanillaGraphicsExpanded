# Artifact Scheduler (Async Cache Artifacts)

This document describes the generic artifact scheduler used for cache regeneration jobs (e.g. BaseColor), and how it preserves the project’s locked-in constraints:

- **Never block the render thread**
- **Never drop disk writes** (Option B)
- **No GL calls off-thread** (GPU staging only)

## Thread-Affinity Contract

Each work item (`IArtifactWorkItem<TKey>`) flows through:

1. **Compute (worker thread)** via `IArtifactComputer<TKey, TOutput>.ComputeAsync`
   - Allowed: asset load/decode, CPU compute, preparing payloads
   - Not allowed: GL calls

2. **Output (worker thread)** via `IArtifactOutputStage<TKey, TOutput>.OutputAsync`
   - Disk writes happen here (off-thread)
   - GPU staging may happen here (memcpy into mapped staging buffers)

3. **Apply (main thread)** via `IArtifactApplier<TKey, TOutput>.Apply`
   - Mutate gameplay-visible shared state
   - Scheduled via `Event.EnqueueMainThreadTask`

## Sessions and Stale-Drop

The scheduler maintains a monotonic `SessionId`.

- Each `Start()` / explicit `BumpSession()` increments the session.
- Any compute/output/apply work produced under an older `SessionId` is **dropped** (no shared state mutation).

This matches the “stale result drop” behavior used by material systems on texture reload.

## Backpressure (Option B)

Backpressure is enforced at admission time:

- Before a queued item is scheduled for compute, the scheduler attempts to acquire reservation tokens.
- If capacity is unavailable, the item is **deferred** (re-queued) after a brief yield.
- The render thread is never blocked; it only receives bounded main-thread apply tasks.

Reservation pools:

- Disk: `ArtifactReservationPool` (limits concurrent disk output stages)
- GPU: `ArtifactReservationPool` or an adapter tied to existing streaming budgets

## GPU Staging Model

GPU outputs are integrated via existing wrappers:

- Preferred: `TextureStreamingSystem.StageCopy(...)` (persistent-mapped PBO ring when available)
- Alternative: `GpuTexture` upload wrapper path when ownership/lifetime requires it

Worker threads may only stage data (memcpy into mapped memory). GL commits happen on the render thread through the existing texture streaming service loop.
