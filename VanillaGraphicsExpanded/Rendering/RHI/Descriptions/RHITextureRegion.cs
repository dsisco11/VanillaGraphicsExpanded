namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects a bounded spatial region; layered resources count layers along depth.</summary>
public readonly record struct RHITextureRegion(RHITextureSubresource Subresource, int X, int Y, int Z, int Width, int Height, int Depth = 1);
