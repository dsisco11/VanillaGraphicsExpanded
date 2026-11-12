namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>One complete-detail alternative with costs supplied by bounded demand and native geometry producers.</summary>
internal readonly record struct ShadowDetailOffer(int Detail, ShadowWork Cost, double EstimatedMilliseconds);
