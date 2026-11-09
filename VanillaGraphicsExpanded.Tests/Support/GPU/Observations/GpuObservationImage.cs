using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Borrows a completed image for one synchronous observation, owning only required interpretation resources.</summary>
internal sealed class GpuObservationImage : IDisposable
{
    private readonly GpuImageSnapshot? copy;
    private readonly GpuTextureView? view;
    private readonly BorrowedTexture? alias;

    #region Public API
    /// <summary>Retires interpretation resources without retiring the borrowed source.</summary>
    public void Dispose()
    {
        alias?.Dispose();
        view?.Dispose();
        copy?.Dispose();
    }
    #endregion

    #region Internal API
    /// <summary>Gets the array interpretation retained until synchronous observation returns.</summary>
    internal GpuTexture Texture { get; }
    /// <summary>Gets the represented channel count, excluding sampler default components.</summary>
    internal int Channels { get; }
    /// <summary>Gets floating, signed or unsigned interpretation as zero, one or two.</summary>
    internal int Kind { get; }
    /// <summary>Reports whether this interpretation required independent image storage.</summary>
    internal bool CopiesStorage => copy is not null;

    /// <summary>Selects a shared-storage view or required copy without changing source selectors or contents.</summary>
    internal GpuObservationImage(GpuTexture source, RHITextureRegion? selection = null, bool stencil = false)
    {
        source.RequireOriginalContext();
        var region = selection ?? new(new(0, 0), 0, 0, 0, source.Width, source.Height, source.Depth);
        if (region.Subresource.MipLevel < 0 || region.Subresource.MipLevel >= source.StorageMipLevels
            || region.Subresource.Layer < 0 || region.X < 0 || region.Y < 0 || region.Z < 0
            || region.Width <= 0 || region.Height <= 0 || region.Depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(selection));
        int mipWidth = Math.Max(1, source.Width >> region.Subresource.MipLevel);
        int mipHeight = Math.Max(1, source.Height >> region.Subresource.MipLevel);
        int mipDepth = source.TextureTarget == TextureDimension.Three
            ? Math.Max(1, source.Depth >> region.Subresource.MipLevel) : source.Depth;
        if ((long)region.X + region.Width > mipWidth || (long)region.Y + region.Height > mipHeight
            || (long)region.Z + region.Subresource.Layer + region.Depth > mipDepth)
            throw new ArgumentOutOfRangeException(nameof(selection));
        var encoding = RHICommandContext.Current.GetTextureEncoding(source, PixelComponentType.Float);
        Channels = encoding.Channels switch
        {
            PixelChannelLayout.Red or PixelChannelLayout.RedInteger or PixelChannelLayout.Depth or PixelChannelLayout.DepthStencil => 1,
            PixelChannelLayout.Rg or PixelChannelLayout.RgInteger => 2,
            PixelChannelLayout.Rgb or PixelChannelLayout.RgbInteger => 3,
            PixelChannelLayout.Rgba or PixelChannelLayout.RgbaInteger => 4,
            _ => throw new NotSupportedException("Observation requires a color or depth interpretation.")
        };
        bool integer = encoding.Channels is PixelChannelLayout.RedInteger or PixelChannelLayout.RgInteger
            or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger;
        Kind = !integer ? 0 : source.InternalFormat.ToString().EndsWith("ui", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        bool wholePlane = region.X == 0 && region.Y == 0 && region.Z == 0
            && region.Width == Math.Max(1, source.Width >> region.Subresource.MipLevel)
            && region.Height == Math.Max(1, source.Height >> region.Subresource.MipLevel);

        // Immutable 2D/array/cube storage supports an independent array selector, including mip/layer
        // selection and linear interpretation of stored sRGB bytes, without copying any pixel data.
        // Even an array gets its own selector: source swizzle/base-level/aspect state is not our oracle.
        bool packed = source.InternalFormat is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8;
        if (!stencil && !packed && wholePlane && source.ImmutableLayeredStorage
            && source.TextureTarget is TextureDimension.Two or TextureDimension.TwoArray or TextureDimension.Cube)
        {
            view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription
            {
                Source = source, Dimension = TextureDimension.TwoArray,
                Format = source.InternalFormat switch
                {
                    TextureFormat.Srgb8 => TextureFormat.Rgb8,
                    TextureFormat.Srgb8Alpha8 => TextureFormat.Rgba8,
                    _ => source.InternalFormat
                },
                BaseMip = region.Subresource.MipLevel, MipCount = 1,
                BaseLayer = region.Subresource.Layer, LayerCount = region.Depth,
                Label = "Tests.Observation.View"
            }).RequireResource();
            try
            {
                Texture = alias = new BorrowedTexture(view.TextureId);
                // Integer completeness and exact texel selection require nearest filtering. Configure
                // only our selector; the original owner's sampler and parameter state remain untouched.
                RHICommandContext.Current.ConfigureTexture(alias,
                    new RHISamplerDescription { MinFilter = SamplerFilter.Nearest, MagFilter = SamplerFilter.Nearest });
            }
            catch { alias?.Dispose(); view.Dispose(); throw; }
            return;
        }

        // A 3D-to-array interpretation, spatial crop, mutable storage or packed depth/stencil aspect
        // cannot use the direct array contract. Preserve its exact existing conversion semantics.
        copy = new(source, selection, stencil: stencil);
        Texture = copy.Texture;
        Channels = copy.Channels;
        Kind = copy.Kind;
    }
    #endregion
}
