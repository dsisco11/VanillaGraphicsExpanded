using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>CPU admission only; consumers must still use the GPU coverage and temporal certificate.</summary>
internal readonly record struct ShadowLightDecision(ShadowSourceKey Source, int Detail, bool Selected, string Reason, bool RefreshSelected = true,
    double WaitingSeconds=0,double SecondsSinceRefreshAttempt=0);
