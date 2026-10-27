namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects a texture image, access and format without exposing texture ownership or native tokens.</summary>
public readonly record struct RHIImageBinding(GpuTextureRef Texture, ImageAccess Access = ImageAccess.ReadOnly,
    int Level = 0, bool Layered = false, int Layer = 0, TextureFormat? Format = null);
