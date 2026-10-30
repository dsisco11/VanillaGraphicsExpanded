using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns native texture allocation lowering for existing wrapper construction and resize policy.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void AllocateTextureOwner(GpuTexture owner, in GpuTextureInitialization request, TextureFilterMode filter, string? label, bool replaceExisting)
    {
        RequireContext();
        var description = new RHITextureDescription { Dimension = request.Target,
            Format = request.InternalFormat, Width = request.Width, Height = request.Height,
            Depth = request.Depth, MipLevels = request.Levels, BaseLevel = request.BaseLevel, MaxLevel = request.MaxLevel,
            Storage = request.AllocationMode == GpuTextureAllocationMode.Immutable ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable,
            Filter = filter, Label = label };
        owner.RequireOriginalContext();
        owner.RequireMutableStorage();
        // Validate the frozen request before surrendering the previous allocation. One
        // completion spans retirement and projection so owner-following images see final
        // facts, while borrowers and views still retire with their original registration.
        _ = LowerTexture(description).Resolve();
        if (replaceExisting && owner.ResourceId != 0) owner.RequireRetirement();
        using var completion = replaceExisting ? owner.ResourceManager?.BeginChangeDelivery() : null;
        if (replaceExisting && owner.ResourceId != 0)
        {
            int previous = owner.TextureId;
            owner.ResourceManager?.ReplaceWrapperResource(owner);
            RetireResource(new(GpuResourceKind.Texture, previous));
            owner.SetCreationIdentity(0);
        }
        if (owner.ResourceId == 0)
        {
            InitializeTextureOwner(owner, description);
            // Allocation and filter policy are complete before dependents see replacement.
            // A missing image selection must not delete the successful owner allocation.
            RHIResourceManager.PublishOwnerImages(owner, true, reportMissingImages: true);
        }
        else AllocateTextureStorage(owner, description);
        // Delivery occurs outside initialization's failure cleanup. A consumer failure
        // cannot retire successfully established replacement storage.
        completion?.Complete();
    }
    /// <inheritdoc />
    public void ClearTextureMip(GpuTextureRef texture, int mipLevel)
    {
        RequireContext();
        var image = texture.RequireImage(mipLevel);
        var target = texture.Target;
        var encoding = new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(image.Format), TextureFormatHelper.GetPixelType(image.Format));
        System.Span<byte> zero = stackalloc byte[encoding.BytesPerPixel];
        zero.Clear();
        int height = target == TextureDimension.OneArray ? 1 : image.Height;
        int depth = target == TextureDimension.Cube ? 6
            : target == TextureDimension.OneArray ? image.Height : image.Depth;
        ClearTexture(texture, new(new(mipLevel), 0, 0, 0, image.Width, height, depth), encoding, zero);
    }
    #endregion
}
