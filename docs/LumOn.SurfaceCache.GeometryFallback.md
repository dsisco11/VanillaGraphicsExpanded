# Surface Cache geometry fallback

This implements the outside-coverage and unsupported-geometry fallback task in
[the Surface Cache throughput checklist](LumOn.WorldProbeSurfaceLighting.todo).

The indirect producer now queues a bounded set of unresolved texels when GPU tracing encounters
`OUTSIDE` or `UNSUPPORTED` geometry. A worker retraces their original cosine-sampled batches against
the existing `BlockAccessorWorldProbeTraceScene`, with vanilla lighting queries disabled. CPU hits
then use the existing asynchronous `SurfaceLightingQueryBatch` to resolve outgoing Surface Cache
radiance. Complete estimates return through the normal indirect accumulation, combine and outgoing
atlas publication path.

## Backend and completion contract

This is full-segment CPU fallback, not a continuation from an unverified exit point. Retrying from
the integer start cell and sub-block fraction lets the collision tracer handle partial block shapes
and conservatively recheck the entire segment. The request retains the producer's seed, normal and
ray count; the worker uses the existing Squirrel3 implementation and the same cosine distribution.
CPU and GPU floating-point trigonometry need not be bit-identical at geometric boundaries.

Only these outcomes complete a ray:

- A loaded collision-free path through the authoritative upper world boundary establishes sky.
  It contributes zero **additional indirect** energy because direct lighting already includes the
  effective sky/sun seed. It remains in the full ray denominator.
- A collision hit with a valid, initialized Surface Cache lookup contributes that outgoing radiance.

Missing chunks, unsupported/unpublished GPU data, finite distance limits and exhausted traversal
limits never become sky or artificial black samples. Missing hit lighting leaves that texel unresolved.
The geometry fallback queue does not admit ordinary GPU budget exhaustion, distance limits,
unpublished geometry or unready hit lighting. Limits use the separate
[traversal retry policy](LumOn.SurfaceCache.TraversalBudget.md); complete geometry with unready hit
lighting now uses [dependency-aware cache queries](LumOn.SurfaceCache.HitLightingRetries.md).

The CPU tracer uses the producer's configurable distance (default 512 blocks) and a 1,024-visited-cell
limit after the [traversal-budget follow-up](LumOn.SurfaceCache.TraversalBudget.md). Reaching either limit
remains unresolved. Hits outside published GPU geometry still cannot resolve the existing GPU cache
query's material/identity validation; fallback can establish distant sky, but does not invent an
uncaptured far surface or an alternate lighting source. This preserves the shared cache contract.

## Bounded ownership and work

| Resource/work | Ceiling |
| --- | --- |
| Outstanding fallback batch | One across GPU collection, worker and cache-query completion |
| Texels per batch | 16 |
| Rays per texel | 64 |
| Retained ray/query descriptors | 1,024 |
| CPU ray starts | 64 per producer frame; unused credit does not accumulate |
| CPU traversal | 1,024 visited cells per ray; at most 65,536 visits from a frame's starts |
| Distinct chunk dependencies | 512 per batch |
| GPU request storage | 16-byte header plus sixteen 64-byte records |
| GPU commit storage | Sixteen 32-byte records |

The worker is asynchronous and never touches GPU resources. A cancelled batch retains its admission
until terrain access returns, preventing a replacement worker from overlapping it. Completed but
undrained work also retains admission. World leave cancels without waiting. GPU request collection
uses `GpuShaderStorageBuffer` and `GpuFence`; it polls before mapping, and a failed/unknown completion
retires the storage rather than reusing it. Existing cache-query ownership supplies the second fence.

Queue opportunities rotate across pages and the selected texel bucket instead of repeatedly admitting
the first lanes. A hashed collection serial varies the texel offset independently of cycling lighting
buckets, avoiding a fixed subset caused by those cycles advancing in lockstep. Saturation leaves
lighting unchanged. Completed fallback pages consume the normal
page publication allowance before new lighting work is selected, preserving the existing combine/copy
ceiling. These are count bounds, not a wall-time guarantee for engine collision access or dependency
checks. A whole CPU texel batch completes atomically; individual rays are not separately published.

## Delayed-result validity

The render-thread owner retains:

- The GPU scene identity and invalidation revision, plus the lighting resource dependency revision.
- Origin page physical/virtual ownership, chunk-slot generation and capture revision.
- The same ownership information for ready hit pages used by asynchronous cache queries.
- A world edit epoch observed through `ChunkDirty`, including chunks outside GPU coverage.
- The loaded chunk object observed at each visited source chunk.

It rechecks those dependencies before querying and before committing. Chunk replacement without an
edit notification is detected by object identity. The edit epoch is deliberately conservative: an
unrelated chunk edit may reject the small outstanding fallback batch, but never clears displayed
lighting. Retry eligibility returns through ordinary page scheduling. Coverage movement alone does
not reset retained lighting; replaced source identity does reject delayed work.

Valid texels on partially seeded pages may use fallback. Unready hit pages reject their own query
result rather than suppressing other completed texels. GPU commit also matches page, slot, patch and
texel and requires initialized direct lighting. It applies the same reciprocal previous-history
weighting as normal indirect samples. Failed work changes neither the previous estimate nor its
weight, and no fallback path publishes uncaptured texels.

## Observability and scope

The producer self-check adds cumulative `fallbackAdmitted` texels, `fallbackCommitted` validated
commit submissions and `fallbackRejected` obsolete/failed batches. Existing indirect GPU diagnostics
count the commit dispatch separately from ray traversal; CPU rays are not included in GPU ray counts.
No per-ray logging or synchronous waiting was added to the render-thread fallback path.

Delayed completed commits now share the independent indirect allocation and retain excess results
until publication credit is available, with lifetime revalidation; see
[update budgets](LumOn.SurfaceCache.UpdateBudgets.md).

The initial fallback implementation left coverage, traversal and lighting budgets unchanged; the
linked traversal-budget follow-up subsequently aligned ray distance and cell limits. This is an additional
bounded source of completed samples, not evidence of a gameplay frame-time or convergence gain.
The later user-run acceptance and separate throughput review tasks remain open.

## Validation limits and broader failures

Subagent-run focused validation passed **86/86 cases**, with no skips. It covers worker admission,
frame credit, cancellation without overlap, dependency changes, integer hit addressing, transactional
estimates, GPU queue bounds and cycling-bucket admission, temporal weighting, CPU sky and captured
wall-hit completion through production publication, edits, atlas replacement and world teardown.
Receipts: `artifacts/surface-fallback-final.log` and
`artifacts/TestResults/surface-fallback-final.trx`.

The final production build/deployment passed with zero warnings and zero errors
(`artifacts/surface-fallback-deploy.log`). The test build reported five existing analyzer warnings
outside the new tests. Shader generation compiled 105 stages and 250 variants before the focused run.

The broader Surface Cache regression run was stopped after recording 19 failed cases in
`artifacts/surface-fallback-regression.log`. It did not produce a completed suite total or TRX.
The user confirmed that these failures predate this work; they remain unresolved.
Assertions were not weakened or removed.

| Test class | Failed methods | Variants |
| --- | --- | --- |
| `SurfaceLightingDisplayBoundaryTests` | `DisplayGradingDoesNotEnterSceneLinearLighting` | SH9 |
| `SurfaceLightingNumericalRuntimeTests` | `ConstantRadianceMatchesBothGatherConventions` | SH9 |
| `SurfaceLightingPbrLifetimeTests` | `SealedNeighborRejectsLocalizedLeakage`, `SourceChangesReachRetainedComposition`, `CacheReplacementInvalidatesComposedHistory` | Both |
| `SurfaceLightingPbrRuntimeTests` | `RegisteredCompositionRespectsReceiverRegions` | SH9 |
| `SurfaceLightingRuntimeScenariosTests` | `RecreatedDarkCacheRejectsRetainedFinalLighting`, `UnavailableGeometryDiffersFromValidDarkness`, `DoorwayClosureAndReopeningReachRuntimePixels`, `ProgressiveBounceReachesRuntimePixels`, `RetainedHistoryFollowsSourceLighting` | Both |

The final task in the linked checklist tracks investigation and fixes for these pre-existing failures.
A clean broader regression suite remains unverified. No gameplay process was launched.
