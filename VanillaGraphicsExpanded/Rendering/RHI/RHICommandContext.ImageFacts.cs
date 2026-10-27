namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Provides semantic views of manager-published image facts without GPU queries or recovery.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Projects a current texture description without changing canonical metadata authority.</summary>
    public RHITextureDescription DescribeTexture(GpuTextureRef texture) => backend.DescribeTexture(texture);
    /// <summary>Resolves transfer channels from published format for the supplied CPU component representation.</summary>
    public RHIPixelEncoding GetTextureEncoding(GpuTextureRef texture, PixelComponentType type) => backend.GetTextureEncoding(texture, type);
    #endregion
}
