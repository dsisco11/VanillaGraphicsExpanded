# LumOn Phase 22.X — ChunkSlot Topology + Contracts (Phase 1)

This doc locks in the *initial* (v1) semantics for multi-`chunkSlot` support in the LumonScene surface cache.
It is deliberately biased toward correctness + debuggability over minimal memory.


## 1) Coordinate System + Units

- **Chunk coords**: integer chunk coordinates `(cx, cy, cz)` where each chunk is `32×32×32` blocks.
  - `chunkCoord = floor(worldPosBlocks / 32)`
- **Field**: `Near` or `Far`.
- **ChunkSlot window**: a bounded set of chunks considered “active” for a field.


## 2) Topology Decision (Phase 1)

### Decision: 3D slot window (XYZ)

We use a **3D** chunk slot window so that chunk identity is preserved across chunk Y layers.
This is required because voxel `patchId` is chunk-local and would otherwise alias across different `(cx,cy,cz)` that share the same `(cx,cz)` but differ in `cy`.

### Window dimensions

Per field:

- `radiusXZChunks` (configurable)
- `radiusYChunks` (configurable)

Derived dimensions:

```
dimX = 2*radiusXZChunks + 1
dimY = 2*radiusYChunks  + 1
dimZ = 2*radiusXZChunks + 1
chunkSlotCount = dimX * dimY * dimZ
```

Note: we keep X and Z symmetric for now; Y is independent.


## 3) Ring-Buffered Slot Mapping (Deterministic)

### Inputs (GPU-visible)

Per field:

- `originMinChunk` (ivec3): minimum chunk coord covered by the window (inclusive)
- `dims` (ivec3): `(dimX, dimY, dimZ)`
- `ring` (ivec3): ring offsets used to keep slot indices stable under window movement

### Mapping function

Given a world chunk coordinate `chunkCoord`:

```
local = chunkCoord - originMinChunk

if local is outside [0..dims-1]:
  chunkSlot = INVALID (and PatchId should be written as 0 to suppress feedback)

sx = (local.x + ring.x) mod dimX
sy = (local.y + ring.y) mod dimY
sz = (local.z + ring.z) mod dimZ

chunkSlot = (sy * dimZ + sz) * dimX + sx
```

**Row-major order**: X is the fastest-varying axis.

### Ring update rule (CPU)

Whenever the window origin changes by `deltaChunks = newOriginMinChunk - oldOriginMinChunk`:

```
ring = (ring + deltaChunks) mod dims
originMinChunk = newOriginMinChunk
```

This keeps a chunk that remains inside the window mapped to a stable slot index across movement.


## 4) PatchIdGBuffer Encoding Contract

PatchIdGBuffer is `RGBA32UI` storing:

```
x = chunkSlot                (uint, 0..chunkSlotCount-1)
y = patchId                  (uint, 1..N; 0 means invalid)
z = packedPatchUv            (uint, unchanged)
w = slotGeneration (low 16)  (uint, optional in Phase 1)
```

### Stale prevention (slot generation)

We plan to use a **per-slot generation counter** to prevent stale pixels/work-items from requesting pages for a slot that has been reused.

- `slotGeneration[chunkSlot]` increments each time the slot’s *owner chunk coord* changes (i.e., slot reuse).
- PatchIdGBuffer writes `w = slotGeneration & 0xFFFF` (other bits reserved).
- Feedback mark/compact ignores entries whose generation doesn’t match the current slot generation.

If generation is temporarily disabled, set `w = 0` and do not enforce the compare (but keep the field reserved).


## 5) GPU Resources Implications

### Page table

`PageTableMip0` remains a `Texture2DArray`:

```
width  = 128
height = 128
layers = chunkSlotCount
format = R32UI (packed PageTableEntry)
```

Lookups must use `chunkSlot` as the array layer, i.e. `texelFetch(pageTableMip0, ivec3(vx, vy, chunkSlot), 0)`.

### Feedback dedup stamp

The “mark→compact” visibility stamp must also become per-slot:

```
vge_pageUsageStamp: R32UI, size 128×128×chunkSlotCount (2D array image)
```


## 6) CPU Responsibilities / Invariants

- Maintain `originMinChunk`, `dims`, `ring` per field and upload them to GPU each frame.
- Maintain `slotGeneration[]` and upload to GPU (e.g. SSBO/UBO/TextureBuffer).
- Ensure a slot reuse triggers:
  - page table slice clear for that slot
  - release/free of physical pages associated with that slot
  - generation increment


## 7) Notes / Open Follow-ups (Phase 2+)

- Decide exact defaults:
  - recommended: `radiusYChunks` small (e.g. 1–3) to cap memory
- Define “INVALID slot” behavior in shaders:
  - if out-of-window, set `patchId=0` so feedback ignores it
- Fairness in compaction:
  - bounded `maxRequests` should avoid starving higher slots; use round-robin offset or per-slot budgets

