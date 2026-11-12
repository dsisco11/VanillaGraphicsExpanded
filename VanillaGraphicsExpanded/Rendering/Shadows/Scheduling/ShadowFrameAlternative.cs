using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Complete conservative current demand with the corresponding bounded virtual regions and current candidate handles.</summary>
internal sealed record ShadowFrameAlternative(int Detail, bool Complete, ImmutableArray<ShadowDemandPage> Pages,
    ImmutableArray<ShadowDrawView> Views, ImmutableArray<ShadowViewCandidates> Candidates, double EstimatedMilliseconds,
    ImmutableArray<ShadowDemandPage> RetainedPages = default, bool CanRefresh = true);

