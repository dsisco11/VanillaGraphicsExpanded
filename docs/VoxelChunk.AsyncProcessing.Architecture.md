# Voxel Chunk Async Processing (Background Artifacts)

This document proposes a generic, reusable system for asynchronously processing voxel chunks on background threads using **.NET 8** built-in primitives (`Task`, ThreadPool, `CancellationToken`, `System.Threading.Channels`). The output of processing is an **artifact** (an alternate representation of the chunk’s voxel data), and the architecture is intentionally abstract to support very different artifact formats (e.g., packed material IDs, signed distance fields, occupancy masks, derived fields for meshing, etc.).

Core constraints:

- **Never block the main/render thread** for background processing work.
- **Stale results must not publish** (chunk versioning is an `int` incrementor).
- **Snapshotting is by copy** (processors run over a copied, immutable snapshot).
- **Do not reinvent a scheduler** (use .NET tasks + ThreadPool + channels; bound concurrency, not the queue).

## Glossary

- **Chunk**: the live, mutable voxel container owned by the main thread / world system.
- **ChunkKey**: a stable identifier for a chunk (world + chunk coordinates).
- **ChunkVersion**: an `int` incremented on any voxel mutation.
- **Snapshot**: an immutable copy of chunk voxel data taken at a specific `ChunkVersion`.
- **Processor**: a plugin that computes an artifact from a snapshot.
- **Artifact**: derived representation computed from snapshot voxels.
- **Superseded**: a queued request whose version is no longer current at execution time.

## Goals / Non-goals

Goals:

- Pluggable processors, each producing its own artifact type.
- Bounded background throughput (configurable worker count).
- Fast request path with cache hits and in-flight deduplication.
- Robust staleness handling using `ChunkVersion`.
- Memory-aware snapshot management (pooled buffers) and artifact caching (budgeted).

Non-goals:

- Defining artifact formats themselves.
- Cross-process distribution (single process).
- A custom thread system or custom priority scheduler (channels + worker tasks are sufficient).

## Thread-Affinity Contract

The pipeline is explicitly split to keep thread-affinity rules simple:

1. **Acquire Snapshot (worker thread)**:
   - Copy live chunk voxel data into a pooled buffer without blocking the main/render thread.
   - Chunk concurrency is handled externally; snapshot acquisition assumes the caller/snapshot source has a safe way to read/copy without torn reads.
   - Snapshot creation is deferred until the work item starts, so queued items do not “hold” large snapshot memory.

2. **Compute (worker thread)**:
   - Run processor logic over the snapshot.
   - No reads from live chunk state; snapshot only.

3. **Publish (worker thread, or main thread if required)**:
   - Before publishing/caching, validate that the chunk version is still current.
   - Store into cache and/or schedule an apply on main thread if needed by integration.

If an artifact requires main-thread application (e.g., registering into a world-visible structure with strict thread affinity), expose an optional “apply” stage scheduled via your existing main-thread enqueue mechanism.

## Public Interfaces (Proposed)

### Chunk identity and versioning

`ChunkKey` and a current-version lookup are intentionally minimal:

- `ChunkKey`: opaque identity type (coordinates, world id).
- `IVersionProvider`: service that can return “current version” for a chunk key.

```csharp
public readonly record struct ChunkKey(int WorldId, int X, int Y, int Z);

public interface IChunkVersionProvider
{
    int GetCurrentVersion(ChunkKey key);
}
```

### Result contract (no exceptions)

Requests return a status wrapper rather than throwing exceptions. This wrapper is also used for “superseded” work (requested version is no longer current), cancellation, and missing chunks.

```csharp
public enum ChunkWorkStatus
{
    Success = 0,
    Superseded = 1,
    Canceled = 2,
    Failed = 3,
    ChunkUnavailable = 4,
}

public enum ChunkWorkError
{
    None = 0,
    SnapshotFailed = 1,
    ProcessorFailed = 2,
    Unknown = 3,
}

public readonly record struct ChunkWorkResult<TArtifact>(
    ChunkWorkStatus Status,
    ChunkKey Key,
    int RequestedVersion,
    string ProcessorId,
    TArtifact? Artifact = default,
    ChunkWorkError Error = ChunkWorkError.None,
    string? Reason = null);
```

### Snapshot contract (copy-based, pooled)

Snapshots are immutable views backed by pooled memory; they must be disposable so buffers return to the pool.

```csharp
public interface IChunkSnapshot : IDisposable
{
    ChunkKey Key { get; }
    int Version { get; }

    int SizeX { get; }
    int SizeY { get; }
    int SizeZ { get; }

    ReadOnlyMemory<int> Voxels { get; } // element type is project-specific
}
```

Notes:

- Prefer a flat voxel buffer for cache-friendly iteration:
  - `index = x + SizeX * (y + SizeY * z)`
- Use `ArrayPool<T>.Shared` (or `MemoryPool<T>`) to reduce GC pressure.

### Processor contract (artifact producer)

Each processor has a stable identifier used for caching and deduplication.

```csharp
public interface IChunkProcessor<TArtifact>
{
    string Id { get; } // stable identifier for caching/dedup
    ValueTask<TArtifact> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct);
}
```

### Processing service contract

The service returns a result wrapper and always accepts a `CancellationToken`.

```csharp
public sealed class ChunkWorkOptions
{
    public int Priority { get; init; } = 0;       // 0 normal, higher = more urgent
}

public interface IChunkProcessingService
{
    Task<ChunkWorkResult<TArtifact>> RequestAsync<TArtifact>(
        ChunkKey key,
        int version,
        IChunkProcessor<TArtifact> processor,
        ChunkWorkOptions? options = null,
        CancellationToken ct = default);
}
```

### Snapshot acquisition service (off-thread)

To avoid snapshot memory explosion from queued items, snapshots should be created only when a work item is actively executing. The processing service therefore depends on a snapshot provider that can safely copy chunk data on a worker thread.

```csharp
public interface IChunkSnapshotSource
{
    ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct);
}
```

Notes:

- This API must not require the main thread. Any synchronization needed to copy safely is owned by the caller/world system (not this processing system).
- `null` indicates the chunk could not be snapshotted (unloaded, not found, etc.). The processing service maps that to `ChunkWorkStatus.ChunkUnavailable`.

## Keys: Cache, Dedup, and Snapshot Sharing

Two keyspaces govern correctness and reuse:

- **SnapshotKey** = `(ChunkKey, Version)`
  - Snapshot copies are expensive; share them across multiple processors for the same chunk+version.

- **ArtifactKey** = `(ChunkKey, Version, ProcessorId)`
  - Artifacts are processor-specific; deduplicate in-flight work and cache completed results by this key.

## Scheduling Model (Built-in .NET Primitives)

### Unbounded admission and worker pool

Use:

- `System.Threading.Channels.Channel<WorkItem>` for an unbounded async queue.
- A fixed number of worker tasks started via `Task.Run` that read from the channel.
- Concurrency bounded by worker count (and optionally a global `SemaphoreSlim` for “in-flight”).

Recommended default worker count:

- `workers = max(1, Environment.ProcessorCount - 1)` (reserve CPU for the main thread).

### Priority without custom schedulers

If priority is required, use multiple channels:

- High / Normal / Low

Workers prefer draining higher priority channels first, falling back to lower ones. This avoids implementing a custom priority heap while achieving practical priority behavior.

## Queue Policy (No Drops)

This architecture assumes the queue must be unbounded and the system must not drop or reject work.

Implications:

- Work items must be lightweight (store only keys/ids/options; do not capture large buffers or chunk references).
- Snapshots must be created just-in-time on execution, not at enqueue time.
- Staleness short-circuit (below) becomes critical to avoid wasting CPU on old versions.

Deduplication that is still compatible with “no drops”:

- **Exact-request dedup**: if the same `(ChunkKey, Version, ProcessorId)` is already queued or in-flight, return the existing task rather than enqueueing a duplicate item.

Superseding older queued versions (required):

- For each `(ChunkKey, ProcessorId)`, when a newer `Version` request arrives, any older **queued** requests for that same pair should be completed immediately as `ChunkWorkStatus.Superseded` (no snapshot allocation, no compute).
- This preserves the “no drops” requirement by producing a result for every request, while ensuring only the newest version does real work.
- Implementation-wise this is typically done with a per-key “latest requested version” map and/or a pending-item index so older queued items can be marked complete without waiting to be dequeued.

## Staleness Rules (Version-Based Publish Guard)

ChunkVersion is the authoritative invalidation mechanism.

Publish rule:

- A computed artifact for `(ChunkKey, Version)` is only eligible to publish/cache if:
  - `versionProvider.GetCurrentVersion(key) == Version`

If stale:

- The computed artifact is discarded and the request completes as `ChunkWorkStatus.Superseded`.

Recommended additional short-circuit:

- On dequeue (before snapshot copy), check `versionProvider.GetCurrentVersion(key)`.
- If it does not match the requested version, return `Superseded` immediately (no snapshot allocation, no compute).

Optional proactive cancellation:

- Maintain a per-chunk `CancellationTokenSource` that is canceled whenever the chunk version increments.
- Link this token into compute requests so stale work is likely to stop early instead of burning CPU.

## Snapshot Sharing (Copy Once Per Version)

Because snapshotting is copy-based, snapshot sharing is the main lever for reducing CPU and memory churn when multiple processors are requested for the same chunk+version.

Recommended pattern:

- A `ConcurrentDictionary<SnapshotKey, SnapshotLease>` stores the shared snapshot and a reference count.
- Each work item acquires a lease, runs compute, then releases it in `finally`.
- When the refcount drops to 0, dispose the snapshot and remove the entry, returning pooled buffers.

This yields:

- One voxel copy per `(ChunkKey, Version)` even if multiple artifact processors run.

## Artifact Cache (Budgeted)

Artifacts may be expensive to recompute, and many are stable for a given `(ChunkKey, Version)`.

Cache characteristics:

- Keyed by `ArtifactKey = (ChunkKey, Version, ProcessorId)`.
- In-memory only (no persistence across runs).
- Memory-budgeted (e.g., `MaxBytes`) with LRU (or approximate LRU) eviction.
- Do not cache stale results.

Optional enhancement:

- Let artifacts report size:
  - `long EstimatedBytes { get; }`
  - Helps evict large volumes (e.g., SDFs) more intelligently than count-based policies.

## Failure Handling

- Failures are returned as `ChunkWorkStatus.Failed` with a `ChunkWorkError` and a short `Reason` string (and are not cached).
- Avoid returning exceptions in the result object; if you catch an exception, translate to `(error code, reason)` and optionally log it separately.
- Ensure in-flight entries and snapshot leases are released in `finally` blocks.
- Cancellation is cooperative via `CancellationToken`.

## Observability (EventSource)

Expose profiling and stats using the existing EventSource-based profiling infrastructure described in `docs/Profiling.EventSource.md`.

Reuse/extend:

- Provider: `VanillaGraphicsExpanded.Profiling`
- Implementation: `VanillaGraphicsExpanded/Profiling/VgeProfilingEventSource.cs`

Recommended additions (low-cardinality; avoid raw `ChunkKey` in events):

- **Scoped timings**: wrap processor work in `Profiler.BeginScope(...)` with stable names per processor (e.g., `"ChunkProc.<ProcessorId>"`).
- **Counters** (EventCounters or PollingCounters):
  - Queue length (per priority)
  - In-flight count
  - Completed/sec by status (`Success`, `Superseded`, `Canceled`, `Failed`, `ChunkUnavailable`)
  - Snapshot bytes in-use (and/or snapshots leased)
  - Cache hit rate and evictions

Note: `ProcessorId` should remain low-cardinality. If processors are parameterized (e.g., multiple SDF resolutions), include a stable, intentionally-bucketed id (e.g., `"Sdf.R16"`, `"Sdf.R32"`) rather than embedding arbitrary values.

## Configuration (Recommended Defaults)

- `WorkerCount`: `max(1, CPU - 1)` (configurable)
- `PriorityBands`: 1 (start) or 3 (High/Normal/Low)
- `ArtifactCacheMaxBytes`: project/platform-dependent (start with a conservative budget)

## Example Use Cases

- **Packed Material IDs**:
  - Artifact: tightly packed buffer for fast sampling in later stages.
  - CPU-heavy but memory-moderate; cacheable.

- **Signed Distance Field (SDF)**:
  - Artifact: volumetric field; potentially large memory footprint.
  - Requires strict cache budgeting and likely lower priority.

- **Occupancy/Bitmask**:
  - Artifact: compact bitset used for quick occlusion/visibility tests.
  - Very cache-friendly; high reuse.

## Implementation Notes (Pragmatic)

- Prefer “one chunk request = one worker work item” for predictable throughput.
- Avoid nested `Parallel.For` inside processors unless carefully measured; global worker pool already provides parallelism.
- Use `TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously)` for request completion to avoid executing continuations on worker threads.
- Keep snapshot copy and artifact allocations pooled where feasible to reduce GC spikes.
