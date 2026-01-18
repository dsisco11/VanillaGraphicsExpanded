using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// A pure planning result that describes what atlas pages and per-tile work should be executed.
/// This is intended to be produced by a planner and then consumed by sync/async execution pipelines.
/// </summary>
internal sealed record class AtlasBuildPlan(
    AtlasSnapshot Snapshot,
    IReadOnlyList<AtlasBuildPlan.AtlasPagePlan> Pages,
    IReadOnlyList<AtlasBuildPlan.MaterialParamsTileJob> MaterialParamsTiles,
    IReadOnlyList<AtlasBuildPlan.MaterialParamsOverrideJob> MaterialParamsOverrides,
    IReadOnlyList<AtlasBuildPlan.NormalDepthTileJob> NormalDepthTiles,
    IReadOnlyList<AtlasBuildPlan.NormalDepthOverrideJob> NormalDepthOverrides,
    AtlasBuildPlan.AtlasBuildStats Stats)
{
    internal readonly record struct AtlasPagePlan(int AtlasTextureId, int Width, int Height);

    internal readonly record struct MaterialParamsTileJob(
        int AtlasTextureId,
        AtlasRect Rect,
        Vintagestory.API.Common.AssetLocation Texture,
        PbrMaterialDefinition Definition,
        PbrOverrideScale Scale,
        int Priority);

    internal readonly record struct MaterialParamsOverrideJob(
        int AtlasTextureId,
        AtlasRect Rect,
        Vintagestory.API.Common.AssetLocation TargetTexture,
        Vintagestory.API.Common.AssetLocation OverrideTexture,
        string? RuleId,
        Vintagestory.API.Common.AssetLocation? RuleSource,
        PbrOverrideScale Scale,
        int Priority);

    internal readonly record struct NormalDepthTileJob(
        int AtlasTextureId,
        AtlasRect Rect,
        Vintagestory.API.Common.AssetLocation Texture,
        float NormalScale,
        float DepthScale,
        int Priority);

    internal readonly record struct NormalDepthOverrideJob(
        int AtlasTextureId,
        AtlasRect Rect,
        Vintagestory.API.Common.AssetLocation TargetTexture,
        Vintagestory.API.Common.AssetLocation OverrideTexture,
        string? RuleId,
        Vintagestory.API.Common.AssetLocation? RuleSource,
        float NormalScale,
        float DepthScale,
        int Priority);

    internal readonly record struct AtlasBuildStats(
        int MaterialParamsTiles,
        int MaterialParamsOverrides,
        int NormalDepthTiles,
        int NormalDepthOverrides,
        int MissingAtlasPositions,
        int EmptyRects);

    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
}
