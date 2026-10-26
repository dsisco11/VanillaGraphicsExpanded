namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects a mip and first face/layer in the referenced object's own coordinate system.</summary>
public readonly record struct RHITextureSubresource(int MipLevel = 0, int Layer = 0);
