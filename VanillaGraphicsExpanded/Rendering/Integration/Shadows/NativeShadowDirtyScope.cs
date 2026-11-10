namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Restores nested native dirty-event ownership without discarding center/edge specificity.</summary>
internal sealed record NativeShadowDirtyScope(NativeShadowWorldAdapter? Owner, long? Previous);
