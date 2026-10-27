namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns native texture allocation lowering for existing wrapper construction and resize policy.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void AllocateTextureOwner(GpuTexture owner, in GpuTextureInitialization request, TextureFilterMode filter, string? label)
    {
        RequireContext();
        var description = new RHITextureDescription { Dimension = OpenGLImageValues.Dimension(request.Target),
            Format = OpenGLImageValues.Format(request.InternalFormat), Width = request.Width, Height = request.Height,
            Depth = request.Depth, MipLevels = request.Levels, BaseLevel = request.BaseLevel, MaxLevel = request.MaxLevel,
            Storage = request.AllocationMode == GpuTextureAllocationMode.Immutable ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable,
            Filter = filter, Label = label };
        if (owner.ResourceId == 0)
        {
            InitializeTextureOwner(owner, description);
            // Allocation and filter policy are complete before dependents see replacement.
            // A missing image selection must not delete the successful owner allocation.
            GpuResourceManager.PublishOwnerImages(owner, true, reportMissingImages: true);
        }
        else AllocateTextureStorage(owner, description);
    }
    /// <inheritdoc />
    public void ClearTextureMip(GpuTextureRef texture, int mipLevel)
    {
        RequireContext();
        var image = texture.RequireImage(mipLevel);
        var target = texture.Target;
        var encoding = OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(image.Format), TextureFormatHelper.GetPixelType(image.Format));
        System.Span<byte> zero = stackalloc byte[encoding.BytesPerPixel];
        zero.Clear();
        int height = target == OpenTK.Graphics.OpenGL.TextureTarget.Texture1DArray ? 1 : image.Height;
        int depth = target == OpenTK.Graphics.OpenGL.TextureTarget.TextureCubeMap ? 6
            : target == OpenTK.Graphics.OpenGL.TextureTarget.Texture1DArray ? image.Height : image.Depth;
        ClearTexture(texture, new(new(mipLevel), 0, 0, 0, image.Width, height, depth), encoding, zero);
    }
    #endregion
}
