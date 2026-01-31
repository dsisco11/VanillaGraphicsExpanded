# LumOn Phase 22 Report: Proper Multi-`chunkSlot` Support

## Summary

Today, LumonScene “surface cache” effectively runs in a **single virtual address space**:

- `PatchIdGBuffer.x` (`chunkSlot`) is currently written as **0** for chunk terrain. (`VanillaGraphicsExpanded/PBR/VanillaShaderPatches.cs`)
- The feedback pipeline explicitly ignores non-zero chunk slots:
  - GPU: `lumonscene_feedback_mark_pages.csh` early-outs on `chunkSlot != 0`.
  - CPU: `LumonSceneFeedbackRequestProcessor` skips requests where `req.ChunkSlot != 0`.

This means **all chunks share the same per-chunk patchId namespace**, causing heavy aliasing:
multiple visible chunks can repeatedly “fight” over the same virtual pages, yielding the observed
symptom: *only a small subset of material tiles appear populated* even with many chunks visible.

Proper multi-`chunkSlot` support requires treating `(chunkSlot, virtualPageIndex)` as the virtual address,
with deterministic, stable mapping from world chunk coords → chunk slot indices, plus lifecycle handling
when slots are reused.


## Definitions / Intended Semantics

### `chunkSlot`

`chunkSlot` is an **indirection index** identifying which chunk’s virtual page table slice a surface belongs to.

- It is *not* a chunk coordinate.
- It is an index in `[0 .. chunkSlotCount-1]` for the active field (Near or Far).
- It selects the Z-layer of `PageTableMip0` (a `Texture2DArray`) and any per-slot metadata buffers.

### Virtual Address

The correct virtual address for the surface cache is:

```
VirtualAddress := (field, chunkSlot, virtualPageIndex, mip)
```

where `virtualPageIndex ∈ [0..16383]` (128×128) per chunk.


## Current Implementation Gaps

1. **No real chunk identity in PatchId encoding**
   - `lumonscene_patchid.glsl` explicitly says the mapping is chunk-local and does not encode chunk identity.

2. **Feedback dedup is global (single slot)**
   - `vge_pageUsageStamp` is a single 128×128 R32UI texture. If multiple chunk slots were emitted, they would collide.

3. **CPU residency maps ignore `chunkSlot`**
   - Current dictionaries are keyed only by `virtualPageIndex` (implicitly “slot 0”).

4. **Capture/relight shaders don’t have per-slot chunk transforms**
   - With real multi-slot, capture must know which chunk the patch belongs to (origin/transform) to reconstruct texel positions.


## Recommended Approach (Deterministic Slot Mapping)

### Why deterministic mapping?

We need the fragment shader to output a `chunkSlot` for every pixel. The most robust approach is a
deterministic mapping that both CPU and GPU can compute from:

- current Near/Far field anchor (in chunk coords), and
- field window dimensions / ring offset.

This avoids per-chunk draw-call uniform plumbing (often difficult with the engine’s chunk renderer).

Conceptually: treat chunk slots like a **ring-buffered 2D/3D window**, similar to the TraceScene occupancy clipmap.

> Alternate approach (LRU-assigned slots) is viable, but typically requires CPU-side per-draw state to set `chunkSlot`,
> or a GPU-visible hash table to map chunkCoord→slot, which is more complex.


## Required Work (by System)

### 1) Field Slot Topology (CPU + GPU contract)

Add a “slot topology” for each field:

- `chunkSlotCount` (already exists in `LumonSceneSurfaceCacheGpuResources.ConfigureFrom`)
- window size in chunks (e.g., `W×H×D` or at least `W×D` + Y policy)
- `originMinChunk` (world chunk coord of the window min corner)
- `ring` (ring offset in chunk units; keeps physical slot indices stable under movement)

GPU needs this to compute:

```
chunkSlot = MapChunkCoordToSlot(chunkCoord, originMinChunk, ring, windowDims)
```

**Decision needed:** is the surface cache window 2D (XZ) or 3D (XYZ)?
- The patchId mapping is 32³, so full 3D is consistent, but 2D may be acceptable for a “surface cache”
  if you only care about terrain surfaces and can derive Y from worldPos without needing a separate chunk slot.


### 2) PatchIdGBuffer Encoding (PBR chunk shaders)

Update the chunk shader injection so:

- `chunkSlot` is computed per-fragment (or provided per-draw if possible)
- `patchId` remains chunk-local for now
- optionally also output a **slot generation** to prevent stale reuse (see below)

Suggested encoding:

```
PatchIdGBuffer = uvec4(chunkSlotPacked, patchId, packedPatchUv, misc)

chunkSlotPacked:
  bits 0..15  = chunkSlot
  bits 16..31 = slotGeneration (wrap ok)
```

If you prefer to keep `.x` purely chunkSlot, you can place generation in `.w`.


### 3) Feedback Dedup (“mark→compact”) for Multi-Slot

#### Stamp storage

Replace single stamp texture with per-slot stamp:

- `uimage2DArray vge_pageUsageStamp` (R32UI), size 128×128×chunkSlotCount

#### Mark pass

Write:

```
imageAtomicMax(vge_pageUsageStamp, ivec3(vx, vy, chunkSlot), frameStamp)
```

#### Compact pass

Scan all `(chunkSlot, virtualPageIndex)` pairs:

- Dispatch shape: `glDispatchCompute(ceil(16384/256), chunkSlotCount, 1)`
- Use `gl_GlobalInvocationID.x` for `virtualPageIndex` and `.y` for `chunkSlot`
- Output request: `uvec4(chunkSlot, virtualPageIndex, mip, patchIdOrFlags)`

**Fairness note:** if `vge_maxRequests` is smaller than `16384*chunkSlotCount`, you will bias toward lower
chunk slots. To avoid starvation, add a per-frame scanning offset (round robin) or a per-slot budget.


### 4) CPU Request Processing (`LumonSceneFeedbackRequestProcessor`)

Remove the `chunkSlot==0` restriction and make mappings multi-slot:

- Replace `virtualToPhysical: Dictionary<int,uint>` with `Dictionary<ulong,uint>` keyed by:
  - `key = ((ulong)chunkSlot << 32) | (uint)virtualPageIndex`
- Replace `pageTableMirror: LumonScenePageTableEntry[]` with a 2D layout:
  - `pageTableMirror[(chunkSlot * VirtualPagesPerChunk) + virtualPageIndex]`

Eviction/release must clear the correct page table slice:

- `pageTableWriter.WriteMip0(chunkSlot, vpage, 0)`

**Performance:** maintain per-slot lists of resident vpages so “clear slot” doesn’t require scanning the whole dictionary.


### 5) Slot Lifecycle / Reuse (Stale Prevention)

When a `chunkSlot` is remapped to a different world chunk coordinate (because the field re-anchors or a chunk unloads),
we must:

- release/free all physical pages owned by that slot
- clear the page table slice for that slot
- increment `slotGeneration[chunkSlot]` so old GBuffer pixels can be detected/ignored

This prevents “ghost pages” where a stale pixel requests pages for a slot that now represents a different chunk.


### 6) Capture + Relight Shaders (Per-Slot chunk origin)

Proper multi-slot means capture can no longer assume patchId implies world-space location.

Add a GPU buffer/texture:

- `ChunkSlotInfoBuffer[chunkSlot]`:
  - `ivec3 chunkCoord` or `vec3 chunkOriginBlock`
  - `uint generation`
  - optional: `mat4 chunkToWorld` or a compact transform for rotated blocks/meshlets

Capture and relight then use:

- `chunkSlot` (and generation) from work item
- slot info to reconstruct world-space ray origins / per-texel sample positions


## Test Coverage to Add/Extend

1. **Feedback multi-slot mark/compact**
   - Ensure 2DArray stamp yields independent request streams per slot.
   - Ensure compaction returns `(chunkSlot, vpage)` pairs across many slots.
   - Add starvation test: with `vge_maxRequests` less than total possible, ensure round-robin offset distributes requests over frames.

2. **Multi-frame convergence across “100 chunks visible”**
   - Once multi-slot is implemented, add a GPU test that:
     - emits 100 different `chunkSlot` values in PatchIdGBuffer
     - runs `mark→compact→CPU alloc→capture` over many frames (budgeted)
     - asserts that each slot reaches an expected minimum coverage of written tiles.

3. **Slot reuse safety**
   - Unit test: when slot is reassigned, generation increments and old requests are ignored.
   - GPU test: write old slot generation into PatchIdGBuffer and verify mark pass ignores it.


## Implementation Checklist (Concrete)

1. Define field slot window dims + mapping math (CPU & GLSL).
2. Add `chunkSlot` (and optional generation) encoding in PBR chunk shader output.
3. Upgrade dedup stamp to `Texture2DArray` and update mark/compact compute shaders.
4. Remove `chunkSlot==0` restriction in `LumonSceneFeedbackRequestProcessor` and update CPU mirrors/mappings.
5. Add slot lifecycle manager (clear slice + free pages + bump generation).
6. Add `ChunkSlotInfoBuffer` and wire capture/relight to use it for world-space reconstruction.
7. Add multi-slot + multi-frame tests (including starvation and slot reuse).


## Likely Root Cause of “Only a Few Tiles Populate” (Current Build)

Even if `chunkSlot` is always 0 (as it is for terrain), patchIds are **chunk-local** and repeat across chunks.
With many chunks visible, distinct surfaces alias into the same `(chunkSlot=0, virtualPageIndex)` space, so you do not get
“one set of tiles per chunk”; you get one set of tiles total, continuously overwritten.

