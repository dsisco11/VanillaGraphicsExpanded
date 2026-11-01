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
    /// <summary>Copies one complete retained plane into an independent two-dimensional owner for production sampler inputs.</summary>
    internal DynamicTexture2D ToPlane()
    {
        Texture.RequireOriginalContext();
        if(Texture.Depth!=1)throw new InvalidOperationException("A two-dimensional input requires one selected plane.");
        var commands=RHICommandContext.Current;
        var image=commands.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension=TextureDimension.Two,Format=Texture.InternalFormat,Width=Texture.Width,Height=Texture.Height,
            MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest,Label="Tests.History.Plane"
        }).RequireResource();
        try
        {
            var area=new RenderArea(0,0,Texture.Width,Texture.Height);
            commands.BlitImages(new(RHIAttachment.FromTexture(Texture),area,RHIAttachment.FromTexture(image),area));
            return image;
        }
        catch { image.Dispose();throw; }
    }

    /// <summary>Copies an attachment's exact image through an explicit GPU blit, including renderbuffer storage that cannot be sampled.</summary>
    internal static GpuImageSnapshot FromAttachment(GpuFramebufferAttachment source, ImageAspects aspects = ImageAspects.Color)
    {
        var commands = RHICommandContext.Current;
        if (aspects is not (ImageAspects.Color or ImageAspects.Depth))
            throw new NotSupportedException("Attachment snapshots sample color or depth, not stencil.");
        // Native framebuffer publications can expose an attachment without a managed owner wrapper.
        // Borrow its original tracked texture through the existing RHI acquisition boundary; never invent ownership.
        using var borrowed = source.Resource is null && source.TextureId > 0 ? new BorrowedTexture(source.TextureId) : null;
        RHIAttachment selected = (source.Resource ?? borrowed) switch
        {
            GpuResource<GpuTextureMetadata> texture => RHIAttachment.FromTexture(new GpuTextureRef(texture),
                new(source.MipLevel, source.Layer ?? (int?)source.CubeFace ?? 0)),
            GpuResource<GpuRenderbufferMetadata> renderbuffer => RHIAttachment.FromRenderbuffer(new GpuRenderbufferRef(renderbuffer)),
            _ => throw new InvalidOperationException("Attachment observation requires its original published backing.")
        };
        using GpuTexture copy = aspects == ImageAspects.Depth
            ? source.InternalFormat is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8
                ? new DepthStencilTexture(source.Width, source.Height, source.InternalFormat, "Tests.Assertion.Attachment")
                : new DepthTexture(source.Width, source.Height, source.InternalFormat, "Tests.Assertion.Attachment")
            : commands.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = source.InternalFormat, Width = source.Width, Height = source.Height,
            MipLevels = 1, Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            Label = "Tests.Assertion.Attachment" }).RequireResource();
        var area = new RenderArea(0, 0, source.Width, source.Height);
        commands.BlitImages(new(selected, area, RHIAttachment.FromTexture(copy), area, aspects));
        return new(copy);
    }

    /// <summary>Selects all texels and layers of one mip, shrinking depth only for a three-dimensional image.</summary>
    internal static RHITextureRegion Mip(GpuTexture source, int level) => new(new(level, 0), 0, 0, 0,
        Math.Max(1, source.Width >> level), Math.Max(1, source.Height >> level),
        source.TextureTarget == TextureDimension.Three ? Math.Max(1, source.Depth >> level) : source.Depth);

    /// <summary>Gets the context-owned, format-preserving array used by fixture samplers and retained histories.</summary>
    internal Texture3D Texture { get; }
    /// <summary>Gets the number of represented channels, independent of sampler default alpha.</summary>
    internal int Channels { get; }
    /// <summary>Gets numeric interpretation: zero for floating/normalized/depth, one for signed and two for unsigned integers.</summary>
    internal int Kind { get; }

    /// <summary>Copies every texel of the supplied domain without downloading or converting it.</summary>
    internal GpuImageSnapshot(GpuTexture source, RHITextureRegion? selection = null, bool activeAttachment = false, bool stencil = false)
    {
        source.RequireOriginalContext();
        if(stencil && source.InternalFormat is not (TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8))
            throw new ArgumentException("Stencil observation requires packed depth/stencil storage.",nameof(source));
        var region = selection ?? new RHITextureRegion(new(0, 0), 0, 0, 0, source.Width, source.Height, source.Depth);
        if (activeAttachment && (source.TextureTarget != TextureDimension.Two || region.Depth != 1))
            throw new ArgumentException("Active-pass observation accepts only a two-dimensional attachment.", nameof(source));
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
        Kind = stencil ? 2 : !integer ? 0 : source.InternalFormat.ToString().EndsWith("ui", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
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
            if (activeAttachment)
            {
                // A native observation callback must not widen the production pass boundary or change its selectors.
                // Copy only the observed 2D attachment into fixture-owned storage; no source state is mutated.
                OpenTK.Graphics.OpenGL4.GL.CopyImageSubData(source.TextureId, OpenTK.Graphics.OpenGL4.ImageTarget.Texture2D,
                    region.Subresource.MipLevel, region.X, region.Y, 0, Texture.TextureId,
                    OpenTK.Graphics.OpenGL4.ImageTarget.Texture2DArray, 0, 0, 0, 0, region.Width, region.Height, 1);
                Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, OpenTK.Graphics.OpenGL4.GL.GetError());
            }
            else RHICommandContext.Current.CopyTexture(source, region, Texture,
                new(new(0, 0), 0, 0, 0, region.Width, region.Height, region.Depth));
            // Change only the owned copy: unsigned samplers expose its stencil aspect while the
            // source keeps its depth sampling mode and both packed aspects remain bit-identical.
            if(stencil)
            {
                OpenTK.Graphics.OpenGL4.GL.TextureParameter(Texture.TextureId,
                    OpenTK.Graphics.OpenGL4.TextureParameterName.DepthStencilTextureMode,
                    (int)OpenTK.Graphics.OpenGL4.PixelFormat.StencilIndex);
                Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError,OpenTK.Graphics.OpenGL4.GL.GetError());
            }
        }
        catch { Texture.Dispose(); throw; }
    }
    #endregion


}
