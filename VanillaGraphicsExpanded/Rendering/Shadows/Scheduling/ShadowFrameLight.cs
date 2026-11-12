using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Current contribution and finite minimum-to-refined alternatives for one stable source.</summary>
internal sealed record ShadowFrameLight(ShadowSourceKey Source, double Contribution, bool Carried,
    bool RetentionEligible, ImmutableArray<ShadowFrameAlternative> Alternatives,ShadowProjectionSnapshot? CurrentProjection = null);
