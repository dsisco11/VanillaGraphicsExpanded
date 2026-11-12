using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>CPU admission estimates and actual owned bytes; GPU readiness remains a separate diagnosed publication.</summary>
internal sealed record ShadowFrameDiagnostics(long Frame, ShadowWork Used, ShadowWork Limit,
    int BudgetRejections, long ResidentBytes, long DrainingBytes, int RequestedPages, int WritablePages,
    ImmutableArray<ShadowLightDecision> Decisions, string? Failure,double? LastGpuMilliseconds = null,
    long LastGpuFrame = 0,long MissingTimingSamples = 0,string? BudgetRejectionCause = null,long MissingPrioritySamples=0);
