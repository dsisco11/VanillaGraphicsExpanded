namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Projects already published image facts without executing native discovery.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Describes a retained texture from its manager-certified canonical snapshot.</summary>
    RHITextureDescription DescribeTexture(GpuTextureRef texture);
    /// <summary>Selects semantic transfer channels from published image format and explicit CPU component type.</summary>
    RHIPixelEncoding GetTextureEncoding(GpuTextureRef texture, PixelComponentType type);
}
