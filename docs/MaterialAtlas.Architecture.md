# Material Atlas Core Architecture

> Document: MaterialAtlas.Architecture.md
> Status: Draft
> Scope: Refactored MaterialAtlas* systems (PBR material params + normal/depth sidecar).

## 1. Goals

- Separate planning, execution, and storage concerns.
- Support async/iterative builds with render-thread GL usage by default.
- Allow disk cache reuse to skip recompute when inputs are stable.
- Keep deterministic, reproducible outputs for a fixed asset set.

## 2. Naming Convention

- Generic, reusable systems use the "Atlas*" prefix.
- Material-specific systems use the "MaterialAtlas*" prefix.
- "System" is used for top-level orchestrators.
- "Build" is used for production pipelines.
- "Tile" is used for per-rect jobs.

## 3. Component Overview

Primary systems:

- MaterialAtlasSystem
- MaterialAtlasTextureStore
- MaterialAtlasBuildPlanner
- MaterialAtlasBuildScheduler (async)
- MaterialAtlasParamsBuilder
- MaterialAtlasNormalDepthBuildRunner
- MaterialOverrideTextureLoader
- MaterialAtlasDiskCache (optional)

Generic contracts:

- AtlasSnapshot
- AtlasRect, AtlasRectResolver
- AtlasBuildPlan
- AtlasCacheKey

## 4. High-Level Flow

1. MaterialAtlasSystem requests an AtlasSnapshot from the block atlas.
2. MaterialAtlasBuildPlanner creates an AtlasBuildPlan from snapshot + registry.
3. If cache is enabled, MaterialAtlasDiskCache resolves hits for tiles.
4. Remaining tiles are executed via sync or async build paths.
5. MaterialAtlasTextureStore owns all DynamicTexture lifetimes and lookups.
6. Terrain shader hooks query the store for current texture IDs.

## 5. Component Responsibilities

MaterialAtlasSystem:
- Owns lifecycle and event wiring.
- Chooses sync vs async execution.
- Tracks build status and provides "IsInitialized"/"IsBuildComplete" state.

MaterialAtlasTextureStore:
- Creates per-page DynamicTexture objects.
- Tracks material params and normal/depth pages by base atlas texture ID.
- Handles placeholder/default fills.

MaterialAtlasBuildPlanner:
- Resolves atlas positions, scales, and overrides into a plan.
- Produces per-tile jobs for material params and normal/depth build.
- Has no side effects (pure planning).

MaterialAtlasBuildScheduler:
- Runs budgeted async builds each frame.
- Dispatches CPU tile work on worker threads.
- Uploads results on render thread.

MaterialAtlasParamsBuilder:
- Generates per-tile RGB16F buffers for roughness/metallic/emissive.
- Applies per-tile overrides after procedural output.

MaterialAtlasNormalDepthBuildRunner:
- Executes GPU bake for normal/depth tiles (RGBA16F).
- Clears atlas pages once per build as needed.
- Applies per-tile normal/height overrides after bake.

MaterialOverrideTextureLoader:
- Loads override textures (.png, .dds) with caching.
- Provides float RGBA data for override apply steps.

MaterialAtlasDiskCache (optional):
- Stores/retrieves baked tile buffers.
- Handles versioned cache keys and eviction policy.

## 6. Threading Model

- Planning (MaterialAtlasBuildPlanner) is CPU-only and can run on main thread.
- CPU tile generation can run on worker threads.
- All GL calls (DynamicTexture, GPU bake, texture uploads) run on the render thread unless a managed shared-context path is introduced with explicit synchronization.
- Disk cache IO can run on worker threads; uploads must be queued to render thread.

## 7. Lifecycle and Events

StartClientSide:
- MaterialAtlasSystem creates textures (no-op until atlas exists).

BlockTexturesLoaded:
- MaterialAtlasSystem builds or schedules the build plan.

ReloadTextures:
- MaterialAtlasSystem cancels active sessions and requests rebuild.

Dispose:
- Cancel build sessions.
- Dispose textures, schedulers, and caches.

## 8. Integration Points

- Material registry provides material definitions and overrides.
- Block texture atlas provides page textures and positions.
- Terrain shader binding hook queries MaterialAtlasTextureStore for per-page IDs.
- Config controls async budget, normal/depth enable, and cache toggles.

## 9. Data Contracts

AtlasSnapshot:
- Pages (atlas texture IDs, sizes)
- Positions array and reload iteration
- Non-null position count

AtlasBuildPlan:
- Tile jobs (material params, normal/depth)
- Override jobs
- Page-level metadata for progress and logging

AtlasCacheKey:
- Versioned inputs (config + registry + asset fingerprints)
- Tile rect size and content identifiers

## 10. Diagnostics

- Report build progress (tiles completed, overrides applied).
- Track cache hit/miss counts.
- Log warnings on invalid overrides or missing atlas positions.

## 11. File Layout (Target)

VanillaGraphicsExpanded/
  PBR/Materials/
    MaterialAtlasSystem.cs
    MaterialAtlasTextureStore.cs
    MaterialAtlasBuildPlanner.cs
    MaterialAtlasParamsBuilder.cs
    MaterialAtlasNormalDepthBuildRunner.cs
    MaterialOverrideTextureLoader.cs
    AtlasSnapshot.cs
    AtlasRectResolver.cs
    AtlasBuildPlan.cs
    AtlasCacheKey.cs
    Async/
      MaterialAtlasBuildScheduler.cs
      MaterialAtlasBuildSession.cs
      MaterialAtlasTileJob.cs
      MaterialAtlasTileUpload.cs
      MaterialAtlasOverrideUpload.cs
      MaterialAtlasBuildPageState.cs
  docs/
    MaterialAtlas.Architecture.md
    MaterialAtlas.BuildPipeline.md
    MaterialSystem.Cache.Architecture.md
