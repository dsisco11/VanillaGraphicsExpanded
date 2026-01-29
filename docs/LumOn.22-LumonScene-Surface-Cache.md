# LumonScene Surface Cache (Lumen-style Cards) + Virtual Paged Atlases

> **Document**: LumOn.22-LumonScene-Surface-Cache.md  
> **Status**: Draft (proposal)  
> **Dependencies / related**:
>
> - Ray tracing background: [LumOn.04-Ray-Tracing.md](LumOn.04-Ray-Tracing.md)
> - Clipmap topology + addressing: [LumOn.17-Clipmap-Topology-and-Addressing.md](LumOn.17-Clipmap-Topology-and-Addressing.md)
> - Probe update scheduling (concepts we’ll reuse): [LumOn.19-Update-Pipeline-and-Scheduling.md](LumOn.19-Update-Pipeline-and-Scheduling.md)
> - Lumen alignment context (surface cache is the “missing half”): [LumOn.08-Pipeline-Alignment-with-Lumen.md](LumOn.08-Pipeline-Alignment-with-Lumen.md)

---

## 1. Goal

Design an advanced **LumonScene** system inspired by UE5 Lumen’s “card / surface cache”:

- Accumulate **world-space irradiance** onto surface texels over time (temporal amortization).
- Plan for a **Near** and **Far** surface-cache field (near = higher resolution, far = lower resolution).
- Primary target geometry is **voxels**, but must also support:
  - “voxel-like” static meshes that can be rotated
  - occasional arbitrary triangle meshes (including curved surfaces)
- Use **per-chunk virtual atlases** backed by a **shared physical page pool**.
- Implement **GL 4.3 first** (compute + SSBO), while leaving a clean seam for a future GL 3.3 fallback.
- First relight tracer can use **voxel DDA accuracy** (good enough for initial iteration).
- The first trace representation is a **voxel occupancy clipmap** that also stores compact lighting/material IDs.

Non-goals (for now):

- Multi-bounce GI (payload is 0-bounce irradiance accumulation).
- Perfect Lumen feature parity (software RT, HWRT, SDFs, etc.).

---

## 2. What “Lumen-card-style” means (in our terms)

Instead of storing GI in a *volume* (probes), we store it on **surfaces**:

- The world is approximated by a set of **cards/patches** (planar parameterizations).
- Each card is assigned space in a **virtual atlas**.
- A **page table** maps virtual pages → **physical page tiles** in one or more pooled atlas textures.
- We update (relight) only a budgeted subset of surface texels each frame.
- Lighting is **temporally accumulated** in the atlas, converging over time.

This is attractive for voxel worlds because:

- Surface area is sparse and view-dependent.
- Surface cache is naturally stable across frames.
- Sampling in shading is cheap: `(PatchId, patchUV)` → atlas sample.

---

## 3. Core concepts and invariants

### 3.1 Patch / Card

A **Patch** is the basic unit of surface parameterization. It is defined by:

- A local 2D domain (patch UV in [0..1]²).
- A world-space mapping (basis transform).
- A stable identity (**PatchId**) and stable virtual allocation (**VirtualHandle**).

Patch categories:

- **Voxel patches (primary)**: planar 4×4-voxel regions for one axis-aligned face orientation.
- **Mesh cards (secondary)**: planar cards extracted from static meshes (including rotated “voxel meshes”).
- **Curved mesh approximation (rare)**: multiple small cards + stored per-texel depth/normal to represent curvature.

### 3.2 PatchIds must be stable across remeshes

“Stable” means:

- If the same conceptual patch exists before/after a mesh rebuild, it should keep the same `PatchId`.
- This allows surface-cache history (irradiance) to persist across remeshing.

Implementation consequence:

- Patches are identified by a **PatchKey** that is stable (derived from chunk + spatial index + patch type), and
  `PatchKey -> PatchId` is cached for the lifetime of the chunk.

Dirtying consequence (v1 rules):

- **Voxel/block change**: mark impacted voxel face patches dirty (at least the 6 faces on the changed voxel and the 6
  opposite faces on its 6 neighbors). If the change touches a chunk boundary, also dirty the adjacent chunk.
- **Mesh instance change** (add/remove/transform): mark all patches for that `instanceStableId` dirty.
- **Material palette remap**: either (A) mark only patches referencing the changed palette entry dirty (requires a reverse
  index), or (B) conservatively mark all patches `Material|Capture` dirty.

### 3.3 Per-chunk virtual atlases backed by a shared physical pool

We maintain a **virtual address space per chunk**, but physical memory is global:

- Each chunk owns:
  - patch registry
  - virtual allocation space (`VirtualPagedAtlas`)
  - page table (virtual → physical mapping)
- All chunks share:
  - a global physical tile pool (`PhysicalPagePool`)
  - the backing atlas textures (“physical atlases”)

### 3.4 Multi-layer atlases

The paged system supports multiple layers that share residency:

- **Depth** (surface reconstruction)
- **Material** (enough to relight & shade)
- **Lighting** (RGB irradiance + temporal weight/age)

Layers are stored in separate textures but share a single page-table mapping so a physical tile allocation supplies
all layers for that tile.

---

## 4. High-level architecture (CPU ↔ GPU)

### 4.0 Threading / context model (requirements)

- **Render thread (GL context)**: owns all GL object creation/destruction, shader dispatch, and GPU readbacks.
- **Game/update thread(s)**: may observe chunk/block/instance changes and enqueue “world events” to the render thread.
- **Hard rule**: no GL calls outside the render thread.

Practical implication:

- `ChunkLumonScene` state should be mutated on the render thread (or guarded by a single owner + lock-free queue of
  change events). This keeps residency/page-table state deterministic and avoids races with feedback processing.

### 4.1 CPU-side ownership (per chunk)

**`ChunkLumonScene`** (conceptual) owns:

- `PatchRegistry`
  - `Dictionary<PatchKey, PatchId>`
  - patch list with: axis/type, rect, world transform, `VirtualHandle`, dirty flags, last-used frame, etc.
- `VirtualPagedAtlas`
  - `VirtualSpaceAllocator` (per chunk)
  - allocates stable virtual rects for patches (typically fixed-size per patch type)
- `ChunkPageTableState`
  - CPU mirror of residency + physical page bindings
  - eviction bookkeeping (LRU/age)

### 4.2 CPU-side shared ownership (global)

**`PhysicalPagePool`** (global singleton/service):

- owns the physical tile free list + LRU eviction across all chunks
- allocates physical page IDs (tile coordinates) from a fixed budget
- returns freed tiles back to the pool

Pooling note:

- The pool and per-chunk transient lists should use .NET pooling (`ArrayPool<T>` and/or an object pool) to avoid GC
  spikes when chunks stream in/out or when feedback bursts occur.

### 4.3 GPU-side resources (global + per-chunk slots)

Minimal set (proposed):

- **Page table**: `uimage2DArray PageTable` (RGBA32UI)
  - array layer = chunk slot
  - mip level = virtual mip (mip 0 = finest)
- **Patch metadata**: SSBO `PatchMetadataBuffer`
  - either global with per-chunk base offsets, or per-chunk slice indexing
- **Physical atlases** (global pooled textures), each addressed by physical tile + in-tile UV:
  - Physical atlas resolution: `4096×4096` texels (fixed in this proposal)
  - Physical tile size: field-dependent (`tileSizeTexels = texelsPerVoxelFaceEdge * patchSizeVoxels` for voxel patches)
  - Allocate **only enough physical pages** for the active Near/Far chunk windows (plus re-anchor margin), then allocate
    the minimum number of physical atlas textures required to store that many pages (up to a cap).
  - Max atlas textures (cap/target): `64` atlases
    - `DepthAtlas` (format TBD; e.g., R16F/R32F)
    - `MaterialAtlas` (format TBD; packed normal + material IDs)
    - `IrradianceAtlas` (RGBA16F recommended; RGB=irradiance, A=accum weight or “confidence”)
- **Feedback / work queues** (SSBO append / atomic counters):
  - `PageRequestBuffer` (deduped or partially deduped)
  - `CaptureWorkBuffer`
  - `RelightWorkBuffer`
- **Optional (but very useful)**:
  - `PatchIdGBuffer` (during primary geometry pass): stores `(chunkSlot, patchId, patchUV)`

### 4.4 ChunkSlot lifecycle (requirements)

We need a stable way to address per-chunk GPU state (page-table slices, patch metadata ranges, etc.).

Concept: allocate a **`chunkSlot`** from a bounded slot pool, where each slot has a **generation** counter.

- On chunk load:
  - allocate `(chunkSlot, generation)`
  - allocate/initialize per-chunk CPU state (`ChunkLumonScene`)
  - clear GPU page-table slice for that slot (all pages invalid)
- On chunk remesh:
  - patch IDs must remain stable (via `PatchKey -> PatchId`)
  - mark affected patches dirty; do **not** recycle PatchIds for “same key” patches
  - invalidate/capture pages for dirty patches as needed
- On chunk unload:
  - return all resident physical tiles for that chunk back to the global pool
  - release the slot (increment generation so stale GPU references can be detected/ignored)

Implementation note (v1): the CPU-side bridge between chunk streaming events and the global physical pools is
`VanillaGraphicsExpanded.LumOn.Scene.LumonSceneChunkResidencyManager`. It allocates at least one physical page per
active chunk (per field), returns tiles on unload, and can evict under pool pressure while emitting a `PageReleased`
event to drive future page-table unbinds.

Shader-side safety:

- `PatchIdGBuffer` should encode `chunkSlot` and `patchId`, and (optionally) a `slotGeneration` so any stale pixels
  after streaming can be rejected.

### 4.5 Budgets / knobs (requirements)

This system must be explicitly budgeted; surface-cache work should never “run away” under camera movement or streaming.

Recommended budget categories:

- **Residency**:
  - max new tile allocations per frame
  - max evictions per frame
- **Capture**:
  - max pages captured per frame (voxel + mesh)
- **Relight**:
  - max pages relit per frame, and/or max texels relit per frame
  - rays per texel (typically small, amortized)
- **Feedback**:
  - max page requests processed per frame (CPU)
  - GPU-side request buffer cap + overflow behavior (drop + rely on next frame)

Pool sizing requirement (v1):

- Size the **physical page pool** to cover the number of chunks in each field plus a re-anchor margin:
  - `coveredChunks = (2R + 1)^2` where `R` is the field radius in chunks (Chebyshev distance in chunk XZ).
  - `edgeChunks = (2R + 1)`
  - `extraChunks = edgeChunks * 2`
  - `totalChunks = coveredChunks + extraChunks`
- Guaranteed residency rule (v1): allocate at least `totalChunks` physical pages for the field (1 guaranteed page per chunk).
- Rationale: allows a 1-step diagonal re-anchor to allocate “new edge” chunk work without relying on immediate eviction.

Far field note:

- If Far is implemented as an annulus (chunks with distance in `(nearRadius, farRadius]`), use:
  - `coveredChunks = covered(farRadius) - covered(nearRadius)`
  - keep `extraChunks` based on the far field edge (`2*farRadius+1`)

Decision: Far field chunk budgeting uses the **annulus** `(NearRadiusChunks, FarRadiusChunks]` to avoid double-allocating
guaranteed pages for chunks already covered by Near.

---

## 5. VirtualPagedAtlas and page tables

### 5.1 Virtual address model

**Page tile size (per field)**: `texelsPerVoxelFaceEdge * patchSizeVoxels`.

For voxel patches (`patchSizeVoxels = 4`):

- Near default `4×4` texels/voxel → tile size `4*4 = 16` texels → `16×16`
- Far default `1×1` texels/voxel → tile size `1*4 = 4` texels → `4×4`

**Physical atlas size**: `4096×4096` texels per atlas (fixed).

For a given field, tiles per axis are:

- `tilesPerAxis = 4096 / tileSizeTexels`
- `tilesPerAtlas = tilesPerAxis * tilesPerAxis`

**Pool sizing**:

- We size the pool in **pages**, not “always N atlases”.
- For each field compute required `pageBudget` from the field’s chunk window (see §4.5).
- Allocate enough atlas textures to hold `pageBudget` pages:
  - `atlasCount = ceil(pageBudget / tilesPerAtlas)`
  - clamp to a configured/max cap (e.g., 64)

**Per-chunk virtual address space (v1)**:

- Virtual page table resolution: `128×128` pages → `16384` virtual pages max per chunk.
- v1 mapping: **1 patch = 1 virtual page**, deterministically mapped from `PatchId`.

### 5.1.1 Near vs Far fields (no virtual mips initially)

We treat the surface cache as two fields instead of virtual mips:

- **Near field**: higher resolution (default `4×4` texels per voxel face at mip 0; user-configurable).
- **Far field**: lower resolution (default `1×1` texels per voxel face at mip 0; user-configurable).

Because tile size is derived from field resolution, Near and Far should be treated as **separate atlases/page tables**
(or separate “field” indices inside the same system).

Proposed config keys (client):

- `VgeConfig.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge` (default 4)
- `VgeConfig.LumOn.LumonScene.FarTexelsPerVoxelFaceEdge` (default 1)
- `VgeConfig.LumOn.LumonScene.NearRadiusChunks`
- `VgeConfig.LumOn.LumonScene.FarRadiusChunks`

**Mip strategy (v1)**: no mips.

- Sampling chooses **Near** or **Far** field (and within that field samples mip 0 only).
- On near-field re-anchor, we seed near-field irradiance by resampling/copying the far-field irradiance into the near
  field for newly covered regions (details depend on patch packing policy and the mapping).

Anchor reuse idea:

- Reuse the same *snapped-anchor* pattern as the world-probe clipmap scheduler (snapped chunk-space origin + “shift” event).

**VRAM requirement warning** (important):

- A pool of 64 full `4096×4096` textures per layer is large.
- Approx per-atlas memory:
  - `R16F` depth: ~32 MiB per atlas → ~2.0 GiB for 64 atlases
  - `RGBA8` material: ~64 MiB per atlas → ~4.0 GiB for 64 atlases
  - `RGBA16F` irradiance: ~128 MiB per atlas → ~8.0 GiB for 64 atlases
- Total for these three layers is on the order of ~14 GiB.

So **atlasCount must be configurable and clamped** (by user config and practical GPU limits). If we keep 64 as a target
cap, an “adaptive pool” policy is recommended:

- start smaller (e.g., 4–8 atlases) and grow up to the cap only if budgets/VRAM allow
- or keep 64 as a hard cap but allocate fewer atlases based on detected `MaxTextureUnits/VRAM` heuristics
  (exact VRAM query is not portable in OpenGL)

Every surface-cache lookup starts from:

- `chunkSlot` (which per-chunk atlas)
- `patchId`
- `patchUV`
- desired `mip` (based on screen derivatives / distance / roughness bias)

The `PatchMetadata` gives:

- `virtualBasePage` (page-space origin)
- `virtualSizePages` (extent in pages)
- mapping basis for reconstruction (see §6)

For **voxel patches**:

- Patch size at mip 0 is `4 voxels * texelsPerVoxelFaceEdge` texels per side (field-dependent).
- v1 packing policy: **1 voxel patch per page**, where **page tile size == patch size** (no border; no mips).
- Future option: increase the tile size to add borders once we introduce filtering/mips and want seam safety.

From that, the shader computes:

- `virtualPageCoord = virtualBasePage + floor((patchUV * patchSizeTexels) / 256)`
- `inPageTexelCoord = ...`
- `PageTable[mip][chunkSlot][virtualPageCoord] -> physicalPageId/coord + flags`

### 5.2 Page table entry format (proposal)

Store in `RGBA32UI` per virtual page:

- `R`: `physicalPageId` (0 = invalid / not resident)
- `G`: flags (resident bit, capture-valid bit, relight-valid bit, etc.)
- `B`: optional: packed “age” or last-updated frame (debug only; canonical LRU is CPU-side)
- `A`: optional: layer/mip metadata (if needed)

In the simplest model:

- `physicalPageId != 0` implies resident
- `physicalPageId` maps to `(atlasIndex, tileX, tileY)` where valid tile coordinate ranges depend on `tileSizeTexels`

One simple packing (debuggable, not mandatory):

- `tilesPerAxis = 4096 / tileSizeTexels`
- `tilesPerAtlas = tilesPerAxis * tilesPerAxis`
- `tileLinear = tileY * tilesPerAxis + tileX`
- `physicalPageId = 1 + atlasIndex * tilesPerAtlas + tileLinear`

---

## 6. Patch parameterization and reconstruction

### 6.1 Why we store a depth layer

For per-texel relighting we need, for each atlas texel:

- a reconstructed `worldPos`
- a surface `normal`
- material identifiers

To do that from a 2D parameterization we store **depth** (displacement along patch normal) plus a patch basis.

### 6.2 Patch metadata (minimum)

Per `PatchId`:

- `originWS` (float3) : patch-space (u=0,v=0,depth=0)
- `axisUWS` (float3)  : vector spanning patch U (u=1)
- `axisVWS` (float3)  : vector spanning patch V (v=1)
- `normalWS` (float3) : patch normal (unit)
- `virtualBasePage` (uint2) + `virtualSizePages` (uint2)
- `type/axis` (packed) and any needed decode params

Reconstruct:

`worldPos = originWS + u*axisUWS + v*axisVWS + depth*normalWS`

Depth meaning:

- For voxel-aligned faces: typically `depth = 0` (planar surface)
- For curved mesh cards: `depth` stores per-texel displacement from the card plane

### 6.3 Voxel patch keys (4×4 voxels)

Voxel patch granularity is fixed to **4×4 voxels** in the patch plane.

**Texel density (mip 0)** is user-configurable per field:

- Near default: `4×4` texels per voxel face → voxel patch `4*4 = 16` texels wide → `16×16` texels per patch
- Far default: `1×1` texels per voxel face → voxel patch `4*1 = 4` texels wide → `4×4` texels per patch

Stable `PatchKey` (conceptual):

- `chunkCoord` (or chunk UID)
- `axis` (±X, ±Y, ±Z)
- `planeIndex` along axis (voxel coordinate)
- `patchUIndex`, `patchVIndex` (each is voxelCoord>>2 in the other two axes)

This is stable across remesh because it depends only on chunk coordinates + integer voxel indices.

Implementation note:

- `PatchRegistry` is per-chunk, so the stored key can omit `chunkCoord` (it is implied by the owning `ChunkLumonScene`).

### 6.4 Rotated/static meshes and arbitrary meshes

We treat these as **mesh cards**:

- Offline (or load-time) preprocess meshes into a small set of planar cards:
  - “mostly axis-aligned quads” become 1–N cards trivially
  - arbitrary curved meshes become multiple small cards
- Each card has its own patch basis in model space.
- At runtime, per-instance transform converts model-space card basis into world space.

Stable `PatchKey` for mesh cards (conceptual):

- `chunkCoord` (or owning spatial partition)
- `instanceStableId` (must not change across remesh of chunk meshes)
- `cardIndex` (stable per asset)

Implementation note:

- As with voxel patches, a per-chunk registry can omit `chunkCoord`; `instanceStableId` must still be stable for the
  lifetime of the chunk (and for as long as we want surface-cache history to persist).

---

## 7. Frame pipeline (proposed)

### 7.1 Primary geometry pass (writes PatchIdGBuffer)

During normal rendering, output per-pixel:

- `chunkSlot`
- `patchId`
- `patchUV`

This is the bridge between visible shaded pixels and the surface cache.

### 7.2 Feedback: request missing pages

We need a mechanism to request pages that are sampled but not resident (or not up-to-date).

GL 4.3 approach:

- A compute pass scans the `PatchIdGBuffer` (or a downsampled version) and appends `PageRequest` entries to an SSBO.
- Optional GPU-side dedup:
  - per-page “request stamp” image/bitset using `atomicCompSwap`
  - or approximate dedup by requesting per 8×8 screen tiles

This answers “could geometry shader request pages?”:

- Yes, but we prefer **post-pass feedback** over geometry shader emission to reduce duplicates and keep control in one stage.

### 7.3 CPU allocation + residency update

CPU consumes `PageRequestBuffer`:

1. For each request, find the owning chunk’s `VirtualPagedAtlas` and virtual page coordinate.
2. If not resident:
   - allocate a physical page from `PhysicalPagePool` (evict LRU if needed)
   - update CPU page table mirror
   - enqueue a **capture** task for that page
3. If resident but stale (dirty):
   - enqueue capture
4. Enqueue **relight** tasks for pages needing lighting updates.

Finally, upload page table deltas and work lists to GPU.

### 7.4 Capture pass (populate depth/material)

Capture writes the non-lighting layers for newly resident or dirty pages:

- **Voxel patches**: can be filled procedurally (depth=0, normal=axis, material IDs from chunk data), or rasterized from the chunk mesh.
- **Mesh cards**: rasterize the relevant meshlets/triangles into the page tile using the card basis projection.

Outputs:

- `DepthAtlas` (+ optional `Normal` in `MaterialAtlas`)
- `MaterialAtlas`

### 7.5 Relight pass (GPU per-texel, amortized)

Compute shader processes `RelightWorkBuffer`:

For each scheduled page tile:

1. For each texel (or a subset per frame):
   - reconstruct `worldPos`/`normal` using patch basis + `DepthAtlas`
   - choose sample directions (blue-noise / PMJ / hashed)
   - trace rays using the occupancy clipmap (initially voxel DDA)
   - integrate “0-bounce” lighting samples into `IrradianceAtlas`
2. Temporal accumulate:
   - store RGB irradiance + a weight/age channel
   - use clamping/validation similar to probe temporal logic when needed

### 7.6 Shading integration

During shading:

- use `(chunkSlot, patchId, patchUV)` to sample irradiance from the paged atlas
- apply material response (diffuse albedo etc.) from the main material system (surface cache stores lighting, not full shading)
- if page missing: sample fallback (black or low-res mip) and rely on feedback to converge

---

## 8. Trace representation: occupancy clipmap with light + material indirection

### 8.1 Clipmap voxel payload (R32UI packed)

We store enough info in the trace representation to shade ray hits cheaply.

Per voxel cell: one packed `uint`:

- bits `0..5`   : `blockLevel` (0–32)
- bits `6..11`  : `sunLevel` (0–32)
- bits `12..17` : `lightId` (0–63)
- bits `18..31` : `materialPaletteIndex` (0–16383)

Notes:

- `lightId` is reduced from 0–255 to 0–63 to afford a larger material palette index.
- If we later need >64 `lightId`s, add a second small `R8UI` clipmap for `lightId` only.

### 8.2 Indirection tables

On GPU:

- `LightColorLut[64] -> rgb` (light “tint”)
- `BlockLevelScalarLut[33] -> scalar` (0..32 brightness curve)
- `SunLevelScalarLut[33] -> scalar` (0..32 brightness curve)
- `MaterialPalette[materialPaletteIndex] -> per-face materials`
  - e.g., 6 face material IDs + flags (emissive, translucent, etc.)

### 8.3 “Outside face” sampling convention (matches WorldProbe behavior)

For a DDA hit at voxel `hitCell` with face normal `hitN`:

- sample at `sampleCell = hitCell + hitN` (outside the surface)
- decode packed payload there for `block/sun/light/material`

This mirrors the CPU WorldProbe convention of sampling light at the “outside” cell.

---

## 9. GL 4.3-first implementation (and GL 3.3 seam)

### 9.1 GL 4.3 path requirements

We rely on:

- Compute shaders for:
  - feedback gathering
  - relight tracing + atlas writes
- SSBO append / atomic counters for work queues
- `imageLoad/imageStore` (or FBO rendering for capture layers)

### 9.2 Designing for a future GL 3.3 fallback

Keep these seams:

- Page tables remain textures (`RGBA32UI`) rather than “SSBO-only” structures.
- Indirection tables should be representable as:
  - SSBO (GL4.3) and Texture Buffer Object / 1D texture (GL3.3 later)
- Shared GLSL include code:
  - pack/unpack helpers
  - virtual→physical address translation
  - ray/clipmap sampling functions

The GL 3.3 fallback can later replace compute stages with:

- raster/fragment passes into FBOs (work compaction via downsampled request tiles)
- reduced feature set and lower budgets

---

## 10. Open questions / knobs (for refinement)

1. **Tile size and texel density**
   - physical atlas size: `4096×4096` texels (fixed)
   - atlas pool size (target cap): `64` atlases
   - page tile size (per field): `texelsPerVoxelFaceEdge * patchSizeVoxels`
   - total page budget (field-dependent): `(4096/tileSize)^2 * atlasCount`
   - voxel density (user-configurable):
     - near default: `4×4` texels per voxel face
     - far default: `1×1` texels per voxel face
   - near/far radii: expressed in chunk distance (user-configurable)
   - packing policy: 1 patch per page initially; optional borders later when enabling filtering/mips
2. **Layer formats**
   - Depth precision (R16F vs R32F)
   - Normal/material packing choices
   - Irradiance (RGBA16F vs RGB11F10F + weight)
3. **Patch cardinality**
   - how many voxel patches per chunk in worst case (memory/binding constraints)
   - how mesh cards are partitioned (quality vs count)
4. **Request dedup**
   - stamp image vs approximate screen-tile requests vs CPU-side hashing
5. **Eviction policy**
   - global LRU vs per-chunk quotas + global fallback
6. **Temporal accumulation policy**
   - weight/age representation
   - disocclusion detection for pages when geometry changes

---

## 11. Suggested implementation milestones

1. **Data plumbing**
   - PatchIdGBuffer output and debug visualizations
   - per-chunk slots (page table slice + patch metadata ranges)
2. **Paging skeleton**
   - virtual allocations per patch
   - page table updates + global physical pool (no capture yet)
3. **Voxel capture**
   - procedural depth/normal/material fill for voxel patches
4. **Occupancy clipmap v1**
   - build/update clipmap + packed payload described in §8
5. **Relight v1**
   - per-texel DDA tracer writing irradiance into atlas with temporal accumulation
6. **Mesh cards**
   - card extraction for rotated/static meshes + capture into atlas
