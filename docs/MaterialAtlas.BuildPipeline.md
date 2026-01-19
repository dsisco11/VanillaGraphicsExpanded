# Material Atlas Build Pipeline

> Document: MaterialAtlas.BuildPipeline.md
> Status: Draft
> Scope: Execution flow for material params and normal/depth atlases.

## 1. Build Inputs

- AtlasSnapshot (page sizes, positions, reload iteration)
- Material registry (material definitions, scales, overrides)
- Config flags (async enable, normal/depth enable, budgets)
  - `MaterialAtlasAsyncBudgetMs`, `MaterialAtlasAsyncMaxUploadsPerFrame`
  - `NormalDepthAtlasAsyncBudgetMs`, `NormalDepthAtlasAsyncMaxUploadsPerFrame`

## 2. Build Plan Generation

MaterialAtlasBuildPlanner performs:

1. Normalize asset keys for atlas lookup via MaterialAtlasKeyResolver.
2. Resolve atlas positions and convert UVs to AtlasRect tiles.
3. Build per-tile jobs for:
   - Material params (RGB16F)
   - Normal/depth (RGBA16F, optional)
4. Attach per-tile override metadata (material params overrides, normal/height overrides).

The build plan is deterministic for a fixed registry + atlas snapshot.

## 3. Material Params Tile Pipeline (CPU)

For each material params tile:

1. Generate base RGB triplets from material definition.
2. Apply per-tile noise and scale.
3. If an override exists:
   - Load override texture (MaterialOverrideTextureLoader).
   - Convert RGBA to RGB triplets.
   - Apply override scale (if any).
4. Upload tile to the MaterialAtlasTextureStore.

Notes:

- Default fill values exist for unmapped tiles.
- Override failures log once and fall back to procedural output.

## 4. Normal/Depth Tile Pipeline (GPU)

For each normal/depth tile:

1. Ensure page was cleared once before per-tile bake.
2. Run GPU bake using MaterialAtlasNormalDepthBuildRunner.
3. If a normal/height override exists:
   - Load override texture.
   - Upload override data to the atlas region.

Notes:

- Tiles smaller than the minimum size can be skipped (leave defaults).
- All GPU operations occur on the render thread.
- When async is enabled, disk cache reads are performed on worker threads and enqueue GPU jobs:
  - cached upload (hit)
  - bake (miss)
  - override apply (if present)

## 5. Sync vs Async Execution

Sync build:

- MaterialAtlasSystem iterates the build plan and uploads all tiles in one pass.
- Normal/depth bake runs inline on render thread.

Async build:

- MaterialAtlasBuildScheduler dispatches CPU tile work on worker threads.
- Render thread uploads results within a per-frame budget.
- Normal/depth bake tiles can be scheduled in a similar budgeted path.

Both paths share the same AtlasBuildPlan and tile data contracts.

## 6. Ordering and Determinism

Per tile ordering:

- Material params base -> material params override.
- Normal/depth bake -> normal/height override.

Determinism:

- Seeded noise uses asset keys and stable salts.
- Atlas rect conversion is consistent and clamped.

## 7. Failure Handling

- Missing atlas positions: skip tile.
- Invalid overrides: log once and proceed with base output.
- GPU bake failures: leave defaults, continue build.
- Stale async jobs: dropped when generation id changes.

## 8. Outputs

Material params atlas:

- Format: RGB16F
- Channels: roughness, metallic, emissive

Normal/depth atlas:

- Format: RGBA16F
- Channels: normal.xyz (0..1), height (signed in A)

## 9. Validation Targets

- Visual parity vs pre-refactor outputs.
- Deterministic outputs across runs with identical assets.
- No render-thread stalls under async budget.
