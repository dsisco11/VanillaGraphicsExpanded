using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>Shares the original immutable dependency snapshots across one completed CPU batch awaiting publication.</summary>
internal sealed record SurfaceFallbackCommitDependencies(
    SurfaceFallbackLifetime Lifetime,
    ImmutableArray<SurfaceFallbackPage> Pages,
    ImmutableArray<SurfaceFallbackDependency> Chunks,
    IBlockAccessor Accessor);
