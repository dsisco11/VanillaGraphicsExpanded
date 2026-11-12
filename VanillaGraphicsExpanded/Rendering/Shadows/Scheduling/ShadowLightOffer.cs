using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Current source contribution and finite alternatives, ordered from minimum acceptable detail to refinement.</summary>
internal sealed record ShadowLightOffer(ShadowSourceKey Source, double Contribution, bool Carried,
    bool RetentionEligible, ImmutableArray<ShadowDetailOffer> Details, ShadowWork? RetainedCost = null,bool IncumbentReady = false);

