# Texture Streaming Core Architecture (PBO Uploads)

> Document: `TextureStreaming.Architecture.md`  
> Status: Draft  
> Scope: Generic texture sub-image streaming via OpenGL Pixel Unpack Buffers (PBOs).  
> Primary implementation: `VanillaGraphicsExpanded/Rendering/TextureStreamingManager.cs`

## 1. Goals

- Provide a **generic, reusable** texture upload mechanism (not tied to MaterialAtlas/LumOn/clipmaps).
- Reduce render-thread stalls by staging uploads through **Pixel Unpack Buffer** (PBO) uploads where possible.
- Prefer **persistent-mapped ring buffers** when `GL_ARB_buffer_storage` is available, with a robust fallback.
- Support **all common texture targets up front**: 1D/2D/3D, 1D/2D arrays, cube map faces, cube map arrays, rectangles.
- Enforce **per-frame upload budgets** (max uploads + max bytes) and backpressure to reduce hitching.
- Keep the producer API **thread-safe**: producers can enqueue from any thread; actual GL uploads happen on the render thread.

## 2. Non-Goals (Current Implementation)

- Compressed texture uploads (`glCompressedTexSubImage*`) are not supported.
- No automatic coalescing/deduplication of overlapping regions.
- No completion handles/callbacks (callers cannot currently know when a request has reached the GPU).
- No CPU-side deep copy at enqueue time (see “Data Lifetime”).
- No texture allocation/resizing; only sub-image updates of already-allocated textures.

## 3. Components and Files

Core:
- `VanillaGraphicsExpanded/Rendering/TextureStreamingManager.cs`
  - `TextureStreamingManager` (queue + scheduler)
  - `TextureStreamingSettings` (budgets + backend configuration)
  - `TextureStreamingDiagnostics` (counters snapshot)
  - Upload request contracts (`TextureUploadRequest`, `TextureUploadTarget`, `TextureUploadRegion`, `TextureUploadData`)
  - Backends: `PersistentMappedPboRing`, `TripleBufferedPboPool`
  - GPU sync helper: `GpuFence`

System wrapper + render loop integration:
- `VanillaGraphicsExpanded/Rendering/TextureStreamingSystem.cs` (lazy singleton, configure/tick/dispose)
- `VanillaGraphicsExpanded/Rendering/TextureStreamingManagerRenderer.cs` (IRenderer tick hook)
- `VanillaGraphicsExpanded/VanillaGraphicsExpandedModSystem.cs` (registers renderer; wires config)

Producer integration helpers:
- `VanillaGraphicsExpanded/Rendering/DynamicTexture.cs` (2D helpers, currently `EnqueueUploadData(...)` is `internal`)
- `VanillaGraphicsExpanded/Rendering/DynamicTexture3D.cs` (3D/array helpers, currently `EnqueueUploadData(...)` is `internal`)

Config/UI:
- `VanillaGraphicsExpanded/LumOn/LumOnConfig.cs` (texture streaming settings live in LumOn config file)
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/config/configlib-patches.json` (ConfigLib UI entries)
- `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/lang/en.json` (labels/tooltips)

## 4. High-Level Flow

At a high level, uploads are split into **producer enqueue** and **render-thread execution**:

1. Producer creates a `TextureUploadRequest`:
   - Identifies a target texture ID + target type (2D/3D/cube face/array/etc)
   - Describes the sub-region (x/y/z, w/h/d, mip)
   - Provides a managed array containing pixel bytes (byte/ushort/Half/float) plus `PixelFormat`/`PixelType`
2. Producer calls `TextureStreamingSystem.Manager.Enqueue(request)` (thread-safe).
3. Every render frame, `TextureStreamingManagerRenderer` calls `TextureStreamingSystem.TickOnRenderThread()`.
4. The manager pulls requests from a priority FIFO, validates them, and attempts to stage:
   - Stage into a PBO backend (preferred) → `glTexSubImage*` reads from PBO offset
   - If staging fails or request is too large, optionally falls back to direct `glTexSubImage*` from client memory
   - If neither is possible, the request is deferred (requeued) and processing stops for the frame
5. The backend uses `GLsync` fences to apply backpressure and safely reuse PBO memory.

## 5. Threading Model and Ownership

- `Enqueue(...)` is safe from any thread.
  - Internally uses a locked `PriorityFifoQueue<T>` with per-priority FIFO ordering.
- `TickOnRenderThread()` **must** run on the thread that owns the active OpenGL context.
  - All GL calls (PBO mapping, `glTexSubImage*`, fences) happen here.
- `TextureStreamingSystem` owns the singleton manager lifetime.
  - `VanillaGraphicsExpandedModSystem.Dispose()` calls `TextureStreamingSystem.Dispose()`.

### Data Lifetime (Important)

`TextureUploadData` stores a reference to the caller-provided managed array. The system does **not** deep-copy at enqueue time.

The first time data is copied is when a request is staged into a mapped PBO **during** `TickOnRenderThread()`.

Implications:
- Do **not** mutate the array after enqueuing.
- Do **not** return pooled arrays (e.g., `ArrayPool<T>`) immediately after enqueue, because the upload may not have happened yet.
- If you need “caller can immediately reuse/free the source array”, the system needs either:
  - an explicit CPU-side copy into an owned buffer at enqueue time, or
  - a render-thread-only API that stages into the PBO immediately (still requires GL context), or
  - completion handles so callers can return arrays after upload completes.

### 5.1 Integration with `DynamicTexture` / `DynamicTexture3D`

The `DynamicTexture` wrappers currently expose **two different semantics**:

- `DynamicTexture.UploadData(...)` / `DynamicTexture.UploadData(..., x, y, w, h)`
  - Performs an immediate `GL.TexSubImage2D(...)` (synchronous on the render thread).
  - Safe for the caller to reuse/return the array immediately after the call returns.
- `DynamicTexture.EnqueueUploadData(...)` (currently `internal`)
  - Enqueues a `TextureUploadRequest` into `TextureStreamingSystem` (asynchronous execution on the next render ticks).
  - The array must remain valid and unmodified until the manager stages it.

`DynamicTexture3D` follows the same pattern for its enqueue helper (currently only the float path exists), while methods like `Clear(...)` still upload directly.

Note: `DynamicTexture.CreateWithData(...)` currently calls `UploadData(...)` (direct path), not the streaming queue.

## 6. Upload Request Model

### 6.1 Texture targets

`TextureUploadTarget` is a pair:
- `BindTarget`: what gets bound in `GL.BindTexture(...)`
- `UploadTarget`: what is passed to `GL.TexSubImage*`

This matters for cube maps:
- Bind: `TextureTarget.TextureCubeMap`
- Upload: `TextureTarget.TextureCubeMapPositiveX` (or other face target)

Helpers exist for:
- 1D: `TextureUploadTarget.For1D()`
- 2D: `TextureUploadTarget.For2D()`
- 3D: `TextureUploadTarget.For3D()`
- Arrays: `For1DArray()`, `For2DArray()`, `ForCubeArray()`
- Cube faces: `ForCubeFace(TextureCubeFace face)`
- Rectangle: `ForRectangle()`

### 6.2 Regions and mip levels

`TextureUploadRegion` stores:
- `X`, `Y`, `Z`
- `Width`, `Height`, `Depth`
- `MipLevel`

The manager issues `glTexSubImage1D/2D/3D` based on the *upload* target:
- 1D targets → `TexSubImage1D`
- 3D targets and array targets (`Texture3D`, `Texture2DArray`, `TextureCubeMapArray`) → `TexSubImage3D`
- Everything else (including cube faces) → `TexSubImage2D`

### 6.3 Pixel formats and data kinds

Requests include:
- `PixelFormat` (e.g., `Rgb`, `Rgba`, `Rg`, `DepthStencil`, …)
- `PixelType` (e.g., `Float`, `HalfFloat`, `UnsignedShort`, `UnsignedByte`, packed types, …)
- `TextureUploadData` (managed array + `ByteLength`)

`TextureUploadData` currently supports these managed array kinds:
- `byte[]`
- `ushort[]`
- `Half[]`
- `float[]`

The system computes bytes-per-pixel via `TextureStreamingUtils.GetBytesPerPixel(...)` and validates that the provided array has at least the required `ByteLength`.

### 6.4 PixelStore (row stride / image height / alignment)

Requests can provide unpack parameters:
- `UnpackAlignment` (defaults to 1 in the request type; applied as-is)
- `UnpackRowLength` (pixels; 0 means “use region width”)
- `UnpackImageHeight` (pixels; 0 means “use region height”)

During upload the manager:
- Sets `GL_UNPACK_ALIGNMENT`
- Sets `GL_UNPACK_ROW_LENGTH` and `GL_UNPACK_IMAGE_HEIGHT` only when needed
- Resets them after the upload call

This supports uploading a sub-rect from a larger/padded image buffer.

## 7. Scheduling and Budgets

The main loop is inside `TextureStreamingManager.TickOnRenderThread()`:

- Dequeues from a priority FIFO (higher numeric `Priority` wins).
- Per frame limits:
  - `MaxUploadsPerFrame`
  - `MaxBytesPerFrame`
  - Note: the byte budget is enforced as a loop condition (`bytes < MaxBytesPerFrame`) and can be exceeded by the last accepted upload (soft cap).
- Staging eligibility:
  - If `ByteCount > MaxStagingBytes`, the request bypasses PBO staging and may use direct upload if allowed.
- Backpressure behavior:
  - If a request cannot be staged and direct uploads are disallowed/unavailable, it is deferred (requeued) and processing stops for the frame.

## 8. Backend Strategy

Backend selection is lazy and happens on first use (`pending.Count > 0`):

1. If `EnablePboStreaming == false` → no backend (direct uploads only, if allowed).
2. Else if `GL_ARB_buffer_storage` is present and `ForceDisablePersistent == false` → persistent mapped ring backend.
3. Else → triple-buffered PBO backend.

### 8.1 PersistentMappedPboRing (preferred)

Key ideas:
- Single `GL_PIXEL_UNPACK_BUFFER` allocated via `glBufferStorage`.
- Entire buffer is mapped once with `glMapBufferRange` using:
  - `MAP_PERSISTENT_BIT | MAP_WRITE_BIT`
  - `MAP_COHERENT_BIT` if enabled, otherwise `MAP_FLUSH_EXPLICIT_BIT` + `glFlushMappedBufferRange`.
- Staging copies bytes from managed array → mapped PBO memory (CPU copy).
- Each submitted upload inserts a `GLsync` fence; completed fences advance the ring tail.

Ring allocation:
- Maintains `head` (next allocation) and `tail` (oldest safe-to-reuse position).
- Allocations align to `PboAlignment`.
- If there is no contiguous space, the allocator wraps to offset 0 when possible.

### 8.2 TripleBufferedPboPool (fallback)

Key ideas:
- Three separate PBOs (size `TripleBufferBytes`) are rotated.
- Each upload:
  - Acquires a free PBO slot (or one whose fence has already signaled).
  - Orphans/reallocates the buffer (`glBufferData(..., StreamDraw)`).
  - Maps the range (`MAP_WRITE_BIT | MAP_INVALIDATE_BUFFER_BIT`), copies, and unmaps.
  - Issues `glTexSubImage*` from PBO offset 0.
  - Inserts a `GLsync` fence; that PBO slot is considered in-flight until signaled.

This backend is simpler and widely supported but can be less efficient than persistent mapping.

## 9. Configuration and Hot Reload

Runtime settings are represented by `TextureStreamingSettings` (see `TextureStreamingManager.cs`).

These settings are configured from `LumOnConfig` via:
- `VanillaGraphicsExpandedModSystem.BuildTextureStreamingSettings(LumOnConfig cfg)`
- Called in `StartClientSide(...)` and `OnConfigReloaded(...)`

Backend reset:
- `TextureStreamingManager.UpdateSettings(...)` detects changes that require backend recreation (e.g., ring size, alignment, coherent mapping toggle).
- A reset request triggers on the next render tick:
  - `GL.Finish()` (rare; ensures safety)
  - dispose backend + recreate lazily when needed

Configuration note:
- If `EnablePboStreaming == false` **and** `AllowDirectUploads == false`, queued requests will not be able to make progress (they will be deferred every frame).

## 10. Diagnostics

`TextureStreamingManager` maintains counters:
- `Enqueued`, `Uploaded`, `UploadedBytes`
- `FallbackUploads` (direct uploads used)
- `DroppedInvalid` (rejected by validation)
- `Deferred` (times backpressure deferred a request)
- `Pending` (queue depth)
- `Backend` (None / PersistentMappedRing / TripleBuffered)

Snapshot access:
- `TextureStreamingSystem.GetDiagnosticsSnapshot()`

## 11. Known Limitations / Pitfalls

- **Array lifetime:** requests keep references to managed arrays until staged; pooling requires additional plumbing.
- **Ordering guarantees:** FIFO within a priority; no region dedupe; repeated writes may queue up.
- **Oversized uploads:** `MaxStagingBytes` can push work onto the direct path or cause deferral if direct uploads are disabled.
- **Error visibility:** GL errors are not queried; failures may be silent aside from dropped/deferred counters.
- **Global PixelStore state:** PixelStore is reset, but only for values the system changes; external code relying on non-default PixelStore state should be avoided.

## 12. Future Enhancements (Suggested)

- Completion tokens (e.g., `TextureUploadHandle`) so pooled arrays can be returned after completion.
- Optional CPU-side deep copy at enqueue time for producer safety (trade memory/bandwidth for ergonomics).
- Upload coalescing per texture/region to avoid redundant work.
- Support for compressed formats (`glCompressedTexSubImage*`) and/or immutable texture storage (`glTexStorage*`) patterns.
