namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines client pixel interpretation; row and slice pitches belong to the payload.</summary>
public readonly record struct RHIPixelEncoding(RHIPixelChannels Channels, RHIPixelType Type);
