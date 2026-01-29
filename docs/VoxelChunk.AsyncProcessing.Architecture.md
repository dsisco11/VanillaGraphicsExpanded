# Voxel Chunk Async Processing (Background Artifacts)

This document proposes a generic, reusable system for asynchronously processing voxel chunks on background threads using **.NET 8** built-in primitives (`Task`, ThreadPool, `CancellationToken`, `System.Threading.Channels`). The output of processing is an **artifact** (an alternate representation of the chunk’s voxel data), and the architecture is intentionally abstract to support very different artifact formats (e.g., packed material IDs, signed distance fields, occupancy masks, derived fields for meshing, etc.).

Core constraints:

- **Never block the main/render thread** for background processing work.
- **Stale results must not publish** (chunk versioning is an `int` incrementor).
- **Snapshotting is by copy** (processors run over a copied, immutable snapshot).
- **Do not reinvent a scheduler** (use .NET tasks + ThreadPool + channels and bounded concurrency).

## Glossary

- **Chunk**: the live, mutable voxel container owned by the main thread / world system.
- **ChunkKey**: a stable identifier for a chunk (world + chunk coordinates).
- **ChunkVersion**: an `int` incremented on any voxel mutation.
- **Snapshot**: an immutable copy of chunk voxel data taken at a specific `ChunkVersion`.
- **Processor**: a plugin that computes an artifact from a snapshot.
- **Artifact**: derived representation computed from snapshot voxels.
- **Coalescing**: “latest wins” admission policy for queued requests.

## Goals / Non-goals

Goals:

- Pluggable processors, each producing its own artifact type.
- Bounded background throughput (configurable worker count + bounded queue).
- Fast request path with cache hits and in-flight deduplication.
- Robust staleness handling using `ChunkVersion`.
- Memory-aware snapshot management (pooled buffers) and artifact caching (budgeted).

Non-goals:

- Defining artifact formats themselves.
- Cross-process distribution (single process).
- A custom thread system or custom priority scheduler (channels + worker tasks are sufficient).

## Thread-Affinity Contract

The pipeline is explicitly split to keep thread-affinity rules simple:

1. **Snapshot (main thread)**:
   - Copy live chunk voxel data into a pooled buffer.
   - Capture `ChunkKey`, `ChunkVersion`, and dimensions.
   - Snapshot creation happens where the chunk is safely readable.

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

The service accepts a snapshot factory so the caller controls where the copy happens (usually main thread) while allowing the service to share/dispose snapshots reliably.

```csharp
public sealed class ChunkWorkOptions
{
    public int Priority { get; init; } = 0;       // 0 normal, higher = more urgent
    public bool CoalesceByKey { get; init; } = true;
}

public interface IChunkProcessingService
{
    Task<TArtifact> RequestAsync<TArtifact>(
        ChunkKey key,
        int version,
        Func<IChunkSnapshot> snapshotFactory,
        IChunkProcessor<TArtifact> processor,
        ChunkWorkOptions? options = null,
        CancellationToken ct = default);
}
```

## Keys: Cache, Dedup, and Snapshot Sharing

Two keyspaces govern correctness and reuse:

- **SnapshotKey** = `(ChunkKey, Version)`
  - Snapshot copies are expensive; share them across multiple processors for the same chunk+version.

- **ArtifactKey** = `(ChunkKey, Version, ProcessorId)`
  - Artifacts are processor-specific; deduplicate in-flight work and cache completed results by this key.

## Scheduling Model (Built-in .NET Primitives)

### Bounded admission and worker pool

Use:

- `System.Threading.Channels.Channel<WorkItem>` for a bounded async queue.
- A fixed number of worker tasks started via `Task.Run` that read from the channel.
- Concurrency bounded by worker count (and optionally a global `SemaphoreSlim` for “in-flight”).

Recommended default worker count:

- `workers = max(1, Environment.ProcessorCount - 1)` (reserve CPU for the main thread).

### Priority without custom schedulers

If priority is required, use multiple bounded channels:

- High / Normal / Low

Workers prefer draining higher priority channels first, falling back to lower ones. This avoids implementing a custom priority heap while achieving practical priority behavior.

## Backpressure and Coalescing (“Latest Wins”)

Chunk processing is often driven by frequent edits and streaming. The system must not grow unbounded queues.

Admission policy:

- The work queue is **bounded**.
- When full, either:
  - **CoalesceByKey**: keep only the newest pending request for `(ChunkKey, ProcessorId)` (older versions are dropped), or
  - Reject/fail fast (only if callers can tolerate it), or
  - Await capacity (not recommended for main thread).

Recommended default:

- **CoalesceByKey = true** for main-thread calls to avoid stalls and prefer current data.

## Staleness Rules (Version-Based Publish Guard)

ChunkVersion is the authoritative invalidation mechanism.

Publish rule:

- A computed artifact for `(ChunkKey, Version)` is only eligible to publish/cache if:
  - `versionProvider.GetCurrentVersion(key) == Version`

If stale:

- The computed artifact is discarded and the request completes as canceled or as a dedicated stale error (choose one behavior and keep it consistent).

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
- Memory-budgeted (e.g., `MaxBytes`) with LRU (or approximate LRU) eviction.
- Do not cache stale results.

Optional enhancement:

- Let artifacts report size:
  - `long EstimatedBytes { get; }`
  - Helps evict large volumes (e.g., SDFs) more intelligently than count-based policies.

## Failure Handling

- Processor exceptions fault the request task and are not cached.
- Ensure in-flight entries and snapshot leases are released in `finally` blocks.
- Cancellation is cooperative via `CancellationToken`.

## Observability (Recommended Hooks)

Expose metrics/log hooks:

- Queue length per priority
- Enqueue drops (coalescing events)
- In-flight count
- Cache hit rate and eviction count
- Snapshot share rate (leases per snapshot)
- Processing time per `ProcessorId`
- Stale discard count

## Configuration (Recommended Defaults)

- `WorkerCount`: `max(1, CPU - 1)` (configurable)
- `QueueCapacity`: 512–4096 (tune based on churn and expected latency)
- `PriorityBands`: 1 (start) or 3 (High/Normal/Low)
- `CoalesceByKey`: true for main-thread callers
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

