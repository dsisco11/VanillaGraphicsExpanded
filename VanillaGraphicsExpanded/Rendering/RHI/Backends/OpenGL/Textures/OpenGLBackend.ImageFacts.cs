using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Translates complete canonical image facts without issuing native reads.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHITextureDescription DescribeTexture(GpuTextureRef texture)
    {
        RequireContext();
        var data = texture.RequireMetadata();
        var image = texture.RequireImage(0);
        int levels = 0;
        foreach (var level in data.Levels)
            if (level.Allocated) levels = System.Math.Max(levels, level.Level + 1);
        return new() { Dimension = data.Target, Format = image.Format,
            Width = image.Width, Height = image.Height, Depth = image.Depth, MipLevels = levels, Samples = image.Samples,
            BaseLevel = data.BaseLevel, MaxLevel = data.MaxLevel,
            Storage = data.ImmutableStorage ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable };
    }
    /// <inheritdoc />
    public RHIPixelEncoding GetTextureEncoding(GpuTextureRef texture, PixelComponentType type)
    {
        RequireContext();
        var format = texture.RequireImage(0).Format;
        return new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(format), type);
    }
    #endregion
}
