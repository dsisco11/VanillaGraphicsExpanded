using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns a GPU copy of an explicit image domain, including selected mip, region and layers/faces.</summary>
internal sealed class GpuImageSnapshot : IDisposable
{
    #region Public API
    /// <summary>Retires the snapshot through its original resource owner.</summary>
    public void Dispose() => Texture.Dispose();
    #endregion

    #region Internal API
    /// <summary>Gets the context-owned, format-preserving array used by fixture samplers and retained histories.</summary>
    internal Texture3D Texture { get; }
    /// <summary>Gets the number of represented channels, independent of sampler default alpha.</summary>
    internal int Channels { get; }
    /// <summary>Gets numeric interpretation: zero for floating/normalized/depth, one for signed and two for unsigned integers.</summary>
    internal int Kind { get; }

    /// <summary>Copies every texel of the supplied domain without downloading or converting it.</summary>
    internal GpuImageSnapshot(GpuTexture source, RHITextureRegion? selection = null)
    {
        source.RequireOriginalContext();
        var region = selection ?? new RHITextureRegion(new(0, 0), 0, 0, 0, source.Width, source.Height, source.Depth);
        var encoding = RHICommandContext.Current.GetTextureEncoding(source, PixelComponentType.Float);
        Channels = encoding.Channels switch
        {
            PixelChannelLayout.Red or PixelChannelLayout.RedInteger or PixelChannelLayout.Depth or PixelChannelLayout.DepthStencil => 1,
            PixelChannelLayout.Rg or PixelChannelLayout.RgInteger => 2,
            PixelChannelLayout.Rgb or PixelChannelLayout.RgbInteger => 3,
            PixelChannelLayout.Rgba or PixelChannelLayout.RgbaInteger => 4,
            _ => throw new NotSupportedException("Select a color or depth image aspect for compact assertions.")
        };
        bool integer = encoding.Channels is PixelChannelLayout.RedInteger or PixelChannelLayout.RgInteger
            or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger;
        Kind = !integer ? 0 : source.InternalFormat.ToString().EndsWith("ui", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        // Texel downloads do not apply sRGB decoding. A copy-compatible linear snapshot preserves that oracle.
        var sampledFormat = source.InternalFormat switch
        { TextureFormat.Srgb8 => TextureFormat.Rgb8, TextureFormat.Srgb8Alpha8 => TextureFormat.Rgba8, _ => source.InternalFormat };
        Texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        {
            Dimension = TextureDimension.TwoArray, Format = sampledFormat, Width = region.Width,
            Height = region.Height, Depth = region.Depth, MipLevels = 1, Storage = ResourceStorageMode.Immutable,
            Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Snapshot"
        }).RequireResource();
        try
        {
            RHICommandContext.Current.CopyTexture(source, region, Texture,
                new(new(0, 0), 0, 0, 0, region.Width, region.Height, region.Depth));
        }
        catch { Texture.Dispose(); throw; }
    }
    #endregion


}
