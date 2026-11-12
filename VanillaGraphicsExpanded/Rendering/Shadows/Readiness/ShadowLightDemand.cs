using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

/// <summary>Whole-light demand publication; unfinished producers cannot certify readiness even with no requests.</summary>
internal sealed record ShadowLightDemand(ShadowSourceKey Source, int Detail, bool Selected, bool Complete,
    ImmutableArray<ShadowDemandPage> Pages, ImmutableArray<ShadowDemandPage> RetainedPages = default, bool Contributing = true);
