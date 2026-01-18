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
    IReadOnlyList<AtlasBuildPlan.AtlasTilePlan> Tiles,
    AtlasBuildPlan.AtlasBuildStats Stats)
{
    internal readonly record struct AtlasPagePlan(int AtlasTextureId, int Width, int Height);

    internal readonly record struct AtlasTilePlan(int AtlasTextureId, AtlasRect Rect, int Priority);

    internal readonly record struct AtlasBuildStats(int TotalTiles, int TotalOverrides);

    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
}
