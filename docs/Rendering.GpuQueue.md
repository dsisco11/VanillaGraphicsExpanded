# GPU queue ownership

`Rendering/GpuQueue<T>` owns one bounded SSBO submission through upload, fence polling, scoped
mapping and retirement. Its render-thread caller owns the shader dispatch and required memory
barriers. The queue uses `GpuShaderStorageBuffer` and `GpuFence`; domain code no longer duplicates
their synchronization/readback lifecycle.

The migrated consumers retain their separate responsibilities:

| Consumer | Responsibility |
| --- | --- |
| Relight renderer fallback capture | Owns `GpuQueue<SurfaceFallbackRequest>`, writes admission metadata and projects immutable requests |
| Relight renderer hit capture | Owns `GpuQueue<SurfaceHitCapture>` and its submission lifetime |
| `SurfaceHitCaptureCodec` | Packs capture/suppression metadata and decodes complete geometric samples without owning GPU resources |
| `SurfaceLightingQueryBatch` | Binds the lighting-query pipeline, dispatches the active range and projects answers |

Fallback and hit capture use the generic queue directly, with no domain queue wrappers.

## Protocols

For GPU append output, construct the queue with a positive header size. The first header word is
the GPU-produced record count; remaining words belong to the domain. The queue preallocates the
complete bounded record area. The caller writes the header, dispatches its producer, then calls
`Submit()`. Readback clamps the count to capacity, preserving the existing capture overflow policy.

For CPU-authored queries, use a zero-byte header. `WriteRecords` grows storage only to the requested
record count, retaining the previous on-demand allocation behavior. The caller binds the exact
active range, dispatches, then calls `Submit(count)`. Readback uses that submitted count rather than
retained buffer capacity. Both protocols require fresh prepared input for each submission.

`TryRead` polls without waiting. Once signaled, it invokes a `GpuQueueReader<T, TResult>` callback
with a borrowed read-only span. The callback projects records directly into the consumer's result;
the hit-capture path does not allocate an intermediate copy of its large fixed-size GPU records.
An empty completed batch invokes the callback with an empty span. Idle/pending calls return false.

The callback cannot return a span through `TResult`. It must not retain pointers into the mapping.
The queue remains busy through decoding and unmapping, then retires the fence and becomes reusable.
Its exposed `Buffer` is borrowed for binding and GPU access, not CPU uploads, resizing or disposal.

## Lifetime and failure behavior

The explicit states are idle, pending, reading, faulted and disposed. Header/record writes and new
submissions require idle ownership. Nested readback and disposal while a mapping is borrowed are
rejected. Normal disposal can retire pending or faulted storage without blocking for completion.

Submission marks storage busy before inserting its fence: a fence insertion failure cannot make
already submitted GPU storage appear reusable. Fence/map/decoder failures retain faulted ownership
until disposal. This tightens failure handling; a failed queue cannot silently resume or retry using
the same storage. Existing domain owners already dispose failed batches and preserve displayed light.

GPU layouts, bindings, shader dispatches, capacity limits, scheduling and lighting formulas are
unchanged. The capture projections still return immutable arrays, and lighting queries retain their
existing array result API. No game process is started for verification.

## Validation

After removing the domain queue wrappers, the subagent-run queue, codec, hit-retry and fallback
selection passed 102 tests with no failures or skips. The production build passed with zero
warnings and errors. Receipts: `artifacts/wrapper-removal-focused.log`,
`artifacts/TestResults/wrapper-removal-focused.trx`, and `artifacts/wrapper-removal-build.log`.

The initial shared-queue extraction had the following broader validation:

Subagent-run validation passed 170 distinct tests, including all 10 queue lifecycle cases and
consumer coverage. The production build passed with zero warnings and zero errors.
Receipts: `artifacts/gpu-queue-regression.log`, `artifacts/gpu-queue-final.log`, and
`artifacts/gpu-queue-build.log`, with test results under `artifacts/TestResults`.

One transport test reproducibly failed: `FailedCaptureRetriesAfterGeometryBecomesAvailable`
at line 55 expects the capture-work failure bit while geometry is unavailable. This assertion
precedes use of the migrated queues; its baseline status was not independently established.
The broader run therefore remains 170 passed and one failed, rather than a clean suite.
