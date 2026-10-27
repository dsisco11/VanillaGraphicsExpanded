using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>One output slot's format, or an unrouted hole with an explicit active-output discard policy.</summary>
public readonly record struct ColorTargetSlot(TextureFormat? Format, bool DiscardOutput = false);
