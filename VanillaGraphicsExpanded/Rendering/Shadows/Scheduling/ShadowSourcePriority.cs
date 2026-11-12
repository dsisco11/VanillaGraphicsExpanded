using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Current contribution and carried status used to order bounded input preparation before exact demand costs exist.</summary>
internal readonly record struct ShadowSourcePriority(ShadowSourceKey Source,double Contribution,bool Carried);
