using System;
using System.Collections.Generic;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Pure planning output for normal+depth GPU bakes and post-bake override application.
/// </summary>
internal sealed record class MaterialAtlasNormalDepthBuildPlan(
    AtlasSnapshot Snapshot,
    IReadOnlyList<MaterialAtlasNormalDepthBuildPlan.PagePlan> Pages,
    IReadOnlyList<MaterialAtlasNormalDepthBuildPlan.BakeJob> BakeJobs,
    IReadOnlyList<MaterialAtlasNormalDepthBuildPlan.OverrideJob> OverrideJobs,
    MaterialAtlasNormalDepthBuildPlan.Stats PlanStats)
{
    internal readonly record struct PagePlan(int AtlasTextureId, int Width, int Height);

    internal readonly record struct BakeJob(
        int AtlasTextureId,
        AtlasRect Rect,
        float NormalScale,
        float DepthScale,
        AssetLocation? SourceTexture,
        int Priority);

    internal readonly record struct OverrideJob(
        int AtlasTextureId,
        AtlasRect Rect,
        AssetLocation TargetTexture,
        AssetLocation OverrideTexture,
        float NormalScale,
        float DepthScale,
        string? RuleId,
        AssetLocation? RuleSource,
        int Priority);

    internal readonly record struct Stats(
        int BakeJobs,
        int OverrideJobs,
        int SkippedByOverrides,
        int MissingAtlasPositions,
        int EmptyRects);
}
