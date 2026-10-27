using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Linq;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for GPU texture wrappers.
/// By default, upload APIs use streamed <c>StageCopy</c> (deep-copy immediately, GPU upload deferred).
/// "Dynamic" textures can override upload methods to default to immediate GL uploads.
/// </summary>
public abstract partial class GpuTexture : GpuResource<GpuTextureMetadata>, IDisposable
{
    protected int textureId;
    /// <summary>Accesses the base image width through the typed metadata snapshot.</summary>
    protected int width => BaseImage.Width;
    /// <summary>Accesses the base image height through the typed metadata snapshot.</summary>
    protected int height => BaseImage.Height;
    /// <summary>Accesses the base image depth through the typed metadata snapshot.</summary>
    protected int depth => textureTarget == TextureTarget.TextureCubeMap ? 6 : BaseImage.Depth;
    /// <summary>Accesses the base image internalFormat through the typed metadata snapshot.</summary>
    protected PixelInternalFormat internalFormat => BaseImage.Format;
    /// <summary>Accesses the canonical native target independently of sampler policy.</summary>
    protected TextureTarget textureTarget => metadata.Target;
    protected TextureFilterMode filterMode;
    protected string? debugName;

    public int TextureId => textureId;
    public int Width => width;
    public int Height => height;
    public int Depth => depth;
    public PixelInternalFormat InternalFormat => internalFormat;
    public TextureTarget TextureTarget => textureTarget;
    /// <summary>Reports the mip levels allocated by this texture owner without querying driver storage.</summary>
    internal int StorageMipLevels
    {
        get => metadata.Levels.IsDefaultOrEmpty ? 0 : metadata.Levels.Max(image => image.Level) + 1;
    }
    /// <summary>Exposes the published image facts for source availability checks.</summary>
    internal ImmutableArray<GpuTextureLevelMetadata> StorageImages => metadata.Levels;

    /// <summary>Retains the manager-published local storage certificate independently of unavailable image facts.</summary>
    internal object UntrackedStorageIdentity { get; set; } = new();

    /// <summary>Gets the canonical storage revision independently of native handle reuse.</summary>
    internal ulong StorageGeneration => ResourceManager?.GetResourceRegistration(this)?.StorageRevision ?? 0;
    /// <summary>Selects immutable layered allocation so separate texture objects can share its storage.</summary>
    internal bool ImmutableLayeredStorage => metadata.ImmutableStorage;
    public TextureFilterMode FilterMode => filterMode;
    public string? DebugName => debugName;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => textureId;
        protected set => textureId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;

    public override void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (textureId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
        }
#endif
    }


    #region Allocation Helpers
    /// <summary>Replaces owned two-dimensional storage using creation arguments rather than editing a snapshot.</summary>
    protected void AllocateOrReallocate2DTexture(int width, int height, PixelInternalFormat format, int mipLevels,
        TextureTarget target = TextureTarget.Texture2D)
    {
        RequireMutableStorage();
        Validate2DAllocation(target, width, height, mipLevels);
        DeleteTextureIfAllocated();
        try { Allocate2DStorage(target, format, width, height, mipLevels); }
        catch
        {
            try { DeleteTextureIfAllocated(); } catch { }
            throw;
        }
        GpuFramebufferAttachmentObservers.NotifyChanged(this);
    }

    /// <summary>Changes existing storage and receives its resulting snapshot through OpenGlStateCache publication.</summary>
    protected void Reallocate2DStorageInPlace(int width, int height, int mipLevels)
    {
        RequireMutableStorage();
        if (!base.IsValid) throw new InvalidOperationException("Cannot reallocate retired texture storage.");
        Validate2DAllocation(textureTarget, width, height, mipLevels);
        Allocate2DStorage(textureTarget, internalFormat, width, height, mipLevels);
        if (ResourceManager?.GetResourceRegistration(this) is null)
            GpuFramebufferAttachmentObservers.NotifyChanged(this);
    }

    /// <summary>Replaces layered storage while retaining requested dimensions only as command arguments.</summary>
    protected void AllocateOrReallocate3DTexture(int width, int height, int depth, PixelInternalFormat format,
        TextureTarget target, bool immutableStorage = false)
    {
        RequireMutableStorage();
        if (target is not (TextureTarget.Texture3D or TextureTarget.Texture2DArray))
            throw new InvalidOperationException($"Layered allocation is not supported for {target}.");
        DeleteTextureIfAllocated();
        var request = new GpuTextureInitialization { Target = target, InternalFormat = format,
            Width = width, Height = height, Depth = depth, Levels = 1, BaseLevel = 0, MaxLevel = 0,
            AllocationMode = immutableStorage ? GpuTextureAllocationMode.Immutable : GpuTextureAllocationMode.Mutable };
        try { RHICommandContext.Current.AllocateTextureOwner(this, request, filterMode, debugName); }
        catch
        {
            try { DeleteTextureIfAllocated(); } catch { }
            throw;
        }
        GpuFramebufferAttachmentObservers.NotifyChanged(this);
    }

    /// <summary>Withdraws the previous owning association before deleting its native name.</summary>
    private void DeleteTextureIfAllocated()
    {
        if (textureId == 0) return;
        RequireRetirement();
        ResourceManager?.ReplaceWrapperResource(this);
        RHICommandContext.Current.RetireResource(new(GpuResourceKind.Texture, textureId));
        textureId = 0;
    }
    #endregion


    #region Texture-Object State Helpers (TexParameter)

    /// <summary>
    /// Sets the mipmap range on the texture object itself via <c>glTexParameter</c>.
    /// This is important when a texture is sampled by raw texture ID without a sampler object bound.
    /// </summary>
    public void SetMipRange(int baseLevel, int maxLevel)
    {
        // Parameter operations can explicitly restore withdrawn facts. Admission uses native
        // lifetime; consumer usability remains manager-owned and requires available metadata.
        if (!base.IsValid)
        {
            return;
        }

        RHICommandContext.Current.SetMipRange(this, baseLevel, maxLevel);
    }

    /// <summary>
    /// Disables mipmapping on the texture object itself (base/max level forced to 0).
    /// </summary>
    public void DisableMipmaps()
    {
        SetMipRange(baseLevel: 0, maxLevel: 0);
    }

    /// <summary>Applies owner-selected texture filtering without temporarily selecting a texture unit.</summary>
    public void SetTexFilter(SamplerFilter minFilter, SamplerFilter magFilter)
    {
        ObjectDisposedException.ThrowIf(!base.IsValid, this);
        RHICommandContext.Current.ConfigureTexture(this, new RHISamplerDescription { MinFilter = minFilter, MagFilter = magFilter });
    }

    /// <summary>Applies the supplied address axes through the named backend operation.</summary>
    public void SetTexWrap(SamplerAddressMode wrapS, SamplerAddressMode wrapT, SamplerAddressMode? wrapR = null)
    {
        ObjectDisposedException.ThrowIf(!base.IsValid, this);
        var request = new RHISamplerDescription { AddressS = wrapS, AddressT = wrapT };
        if (wrapR.HasValue) request.AddressR = wrapR.Value;
        RHICommandContext.Current.ConfigureTexture(this, request);
    }

    #endregion

    public virtual void UploadData(float[] data)
    {
        UploadDataStreamed(data);
    }

    public virtual void UploadData(float[] data, int x, int y, int regionWidth, int regionHeight)
    {
        UploadDataStreamed(data, x, y, regionWidth, regionHeight);
    }

    public virtual void UploadData(ushort[] data)
    {
        UploadDataStreamed(data);
    }

    public virtual void UploadData(float[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth)
    {
        UploadDataStreamed3D(data, x, y, z, regionWidth, regionHeight, regionDepth);
    }

    public virtual void UploadDataStreamed(float[] data, int priority = 0, int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        int channels = GetChannelCount();
        int expected = checked(width * height * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({width}×{height}×{channels} channels)",
                nameof(data));
        }

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            new TextureUploadTarget(RHICommandContext.Current.DescribeTexture(this).Dimension),
            TextureUploadRegion.For2D(0, 0, width, height, mipLevel: mipLevel),
            RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float).Channels,
            PixelComponentType.Float,
            data,
            MapUploadPriority(priority),
            unpackAlignment: 4, textureOwner: this);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[GpuTexture] UploadData staged rejected: {result.RejectReason}");
        }

        _ = result;
    }

    /// <summary>
    /// Stages a sub-region upload via <c>StageCopy</c>.
    /// </summary>
    public virtual void UploadDataStreamed(float[] data, int x, int y, int regionWidth, int regionHeight, int priority = 0, int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        if (x < 0 || y < 0 || x + regionWidth > width || y + regionHeight > height)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({width}×{height})");
        }

        int channels = GetChannelCount();
        int expected = checked(regionWidth * regionHeight * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({regionWidth}×{regionHeight}×{channels} channels)",
                nameof(data));
        }

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            new TextureUploadTarget(RHICommandContext.Current.DescribeTexture(this).Dimension),
            TextureUploadRegion.For2D(x, y, regionWidth, regionHeight, mipLevel: mipLevel),
            RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float).Channels,
            PixelComponentType.Float,
            data,
            MapUploadPriority(priority),
            unpackAlignment: 4, textureOwner: this);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[GpuTexture] UploadData(region) staged rejected: {result.RejectReason}");
        }

        _ = result;
    }

    public virtual void UploadDataStreamed(ushort[] data, int priority = 0, int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedShort)
        {
            throw new InvalidOperationException($"UploadDataStreamed(ushort[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedShort)}, but format is {internalFormat} -> {pixelType}.");
        }

        int channels = GetChannelCount();
        int expected = checked(width * height * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({width}×{height}×{channels} channels)",
                nameof(data));
        }

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            new TextureUploadTarget(RHICommandContext.Current.DescribeTexture(this).Dimension),
            TextureUploadRegion.For2D(0, 0, width, height, mipLevel: mipLevel),
            RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float).Channels,
            PixelComponentType.UnsignedShort,
            data,
            MapUploadPriority(priority),
            unpackAlignment: 1, textureOwner: this);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[GpuTexture] UploadData(ushort[]) staged rejected: {result.RejectReason}");
        }

        _ = result;
    }

    public virtual void UploadDataStreamed3D(
        float[] data,
        int x,
        int y,
        int z,
        int regionWidth,
        int regionHeight,
        int regionDepth,
        int priority = 0,
        int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        int channels = GetChannelCount();
        int expected = checked(regionWidth * regionHeight * regionDepth * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({regionWidth}×{regionHeight}×{regionDepth}×{channels} channels)",
                nameof(data));
        }

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            new TextureUploadTarget(RHICommandContext.Current.DescribeTexture(this).Dimension),
            new TextureUploadRegion(x, y, z, regionWidth, regionHeight, regionDepth, mipLevel),
            RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float).Channels,
            PixelComponentType.Float,
            data,
            MapUploadPriority(priority),
            unpackAlignment: 4, textureOwner: this);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[GpuTexture] UploadData3D staged rejected: {result.RejectReason}");
        }

        _ = result;
    }

    public virtual void UploadDataImmediate(float[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure3DLike();

        int channels = GetChannelCount();
        int expected = checked(regionWidth * regionHeight * regionDepth * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({regionWidth}×{regionHeight}×{regionDepth}×{channels} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    /// <summary>Uploads tightly packed byte data to a 3D region, preserving the caller's unpack alignment.</summary>
    public virtual void UploadDataImmediate(byte[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }
        ArgumentNullException.ThrowIfNull(data);
        Ensure3DLike();
        if (TextureFormatHelper.GetPixelType(internalFormat) != PixelType.UnsignedByte)
            throw new InvalidOperationException($"Byte upload requires an unsigned-byte texture format, got {internalFormat}.");
        int expected = checked(regionWidth * regionHeight * regionDepth * GetChannelCount());
        if (data.Length != expected)
            throw new ArgumentException($"Expected {expected} tightly packed bytes, got {data.Length}.", nameof(data));
        // Single-channel regions need not have four-byte-wide rows.

            RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedByte), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));

    }

    /// <summary>Uploads tightly packed UNORM16 or unsigned integer nodes to a layered region without float conversion.</summary>
    public virtual void UploadDataImmediate(ushort[] data,int x,int y,int z,int regionWidth,int regionHeight,int regionDepth,int mipLevel=0)
    {
        ObjectDisposedException.ThrowIf(!base.IsValid,this);
        ArgumentNullException.ThrowIfNull(data);
        Ensure3DLike();
        if(TextureFormatHelper.GetPixelType(internalFormat)!=PixelType.UnsignedShort)
            throw new InvalidOperationException($"Unsigned-short upload requires a matching texture format, got {internalFormat}.");
        if(data.Length!=checked(regionWidth*regionHeight*regionDepth*GetChannelCount()))
            throw new ArgumentException("Unsigned-short data does not match the texture region.",nameof(data));

            RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedShort), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));

    }

    public virtual void UploadDataImmediate(uint[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int mipLevel = 0)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure3DLike();

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedInt)
        {
            throw new InvalidOperationException($"UploadDataImmediate(uint[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedInt)}, but format is {internalFormat} -> {pixelType}.");
        }

        int channels = GetChannelCount();
        int expected = checked(regionWidth * regionHeight * regionDepth * channels);
        if (data.Length < expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} is smaller than expected size {expected} ({regionWidth}×{regionHeight}×{regionDepth}×{channels} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedInt), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    /// <summary>Uploads native normalized or integer bytes to a complete 2D texture without float staging.</summary>
    public virtual void UploadDataImmediate(byte[] data)
        => UploadDataImmediate(data, 0, 0, width, height);

    /// <summary>Uploads native bytes to a bounded 2D region while preserving pixel-store alignment.</summary>
    public virtual void UploadDataImmediate(byte[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!base.IsValid) throw new ObjectDisposedException(nameof(GpuTexture));
        ArgumentNullException.ThrowIfNull(data);
        Ensure2DLike();
        if (TextureFormatHelper.GetPixelType(internalFormat) != PixelType.UnsignedByte)
            throw new InvalidOperationException("Byte upload requires an unsigned-byte texture format.");
        if (x < 0 || y < 0 || regionWidth <= 0 || regionHeight <= 0 || x + regionWidth > width || y + regionHeight > height)
            throw new ArgumentOutOfRangeException(nameof(regionWidth));
        if (data.Length != checked(regionWidth * regionHeight * GetChannelCount())) throw new ArgumentException("Incorrect byte payload size.", nameof(data));
         RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedByte), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    /// <summary>Uploads a full texture immediately from floating-point input.</summary>
    public virtual void UploadDataImmediate(float[] data)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        int channels = GetChannelCount();
        int expected = checked(width * height * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({width}×{height}×{channels} channels)",
                nameof(data));
        }

        Ensure2DLike();
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    public virtual void UploadDataImmediate(uint[] data)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedInt)
        {
            throw new InvalidOperationException($"UploadDataImmediate(uint[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedInt)}, but format is {internalFormat} -> {pixelType}.");
        }

        int channels = GetChannelCount();
        int expected = checked(width * height * channels);
        if (data.Length < expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} is smaller than expected size {expected} ({width}×{height}×{channels} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedInt), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    public virtual void UploadDataImmediate(float[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        if (x < 0 || y < 0 || x + regionWidth > width || y + regionHeight > height)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({width}×{height})");
        }

        int channels = GetChannelCount();
        int expected = checked(regionWidth * regionHeight * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({regionWidth}×{regionHeight}×{channels} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    public virtual void UploadDataImmediate(uint[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        if (x < 0 || y < 0 || x + regionWidth > width || y + regionHeight > height)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({width}×{height})");
        }

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedInt)
        {
            throw new InvalidOperationException($"UploadDataImmediate(uint[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedInt)}, but format is {internalFormat} -> {pixelType}.");
        }

        int channels = GetChannelCount();
        int expected = checked(regionWidth * regionHeight * channels);
        if (data.Length < expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} is smaller than expected size {expected} ({regionWidth}×{regionHeight}×{channels} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedInt), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    public virtual void UploadDataImmediate(ushort[] data)
    {
        if (!base.IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        Ensure2DLike();

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedShort)
        {
            throw new InvalidOperationException($"UploadDataImmediate(ushort[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedShort)}, but format is {internalFormat} -> {pixelType}.");
        }

        int channels = GetChannelCount();
        int expected = checked(width * height * channels);
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expected} ({width}×{height}×{channels} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedShort), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    #region Readback

    /// <summary>
    /// Reads pixel data from the full texture.
    /// Reads through private staging and explicit GPU completion.
    /// </summary>
    public virtual float[] ReadPixels()
    {
        if (!((GpuResource)this).IsValid) return [];
        // The request owns staging independently; only completed bytes are exposed to CPU readers.
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(0), 0, 0, 0, width, height, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Texture readback did not complete.", request.Error);
        using var mapping = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    /// <summary>
    /// Reads pixel data from a sub-region of the texture.
    /// Uses an explicit selected-region readback request.
    /// </summary>
    public virtual float[] ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight)
    {
        if (!((GpuResource)this).IsValid) return [];
        // The request owns staging independently; only completed bytes are exposed to CPU readers.
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(0), x, y, 0, regionWidth, regionHeight, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Texture readback did not complete.", request.Error);
        using var mapping = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    /// <summary>
    /// Reads pixel data from a specific mip level of the texture.
    /// </summary>
    public virtual float[] ReadPixels(int mipLevel)
    {
        if (!((GpuResource)this).IsValid) return [];
        if (mipLevel < 0) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        // The request owns staging independently; only completed bytes are exposed to CPU readers.
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(mipLevel), 0, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Texture readback did not complete.", request.Error);
        using var mapping = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    #endregion

    public override string ToString()
    {
        return $"{GetType().Name}(id={textureId}, target={textureTarget}, size={width}x{height}x{depth}, format={internalFormat}, name={debugName}, disposed={IsDisposed})";
    }

    /// <summary>Clears an allocated mip directly, without a capability fallback or full-image CPU allocation.</summary>
    public void ClearToZero(int mipLevel = 0)
    {
        RHICommandContext.Current.ClearTextureMip(this, mipLevel);
    }

    protected override void OnDetached(nint id)
    {
    }

    protected override void OnAfterDelete()
    {
    }

    internal static TextureUploadPriority MapUploadPriority(int priority)
    {
        return priority switch
        {
            <= -1 => TextureUploadPriority.Low,
            >= 1 => TextureUploadPriority.High,
            _ => TextureUploadPriority.Normal
        };
    }


    /// <summary>Returns the number of upload components required by the texture's pixel format.</summary>
    protected int GetChannelCount()
    {
        return internalFormat switch
        {
            PixelInternalFormat.R16f or PixelInternalFormat.R32f or PixelInternalFormat.R16 or PixelInternalFormat.R8 => 1,
            PixelInternalFormat.DepthComponent or PixelInternalFormat.DepthComponent16 or PixelInternalFormat.DepthComponent24 or PixelInternalFormat.DepthComponent32f => 1,
            PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8 => 1,
            PixelInternalFormat.R8ui or PixelInternalFormat.R16ui or PixelInternalFormat.R32ui => 1,
            PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f or PixelInternalFormat.Rg16 or PixelInternalFormat.Rg8 => 2,
            PixelInternalFormat.Rg8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.Rg32ui => 2,
            PixelInternalFormat.R11fG11fB10f or PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb32f or PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb => 3,
            _ => 4
        };
    }

    private void Ensure2DLike()
    {
        if (textureTarget != TextureTarget.Texture2D && textureTarget != TextureTarget.TextureRectangle)
        {
            // Most users of the 2D overloads should be on Texture2D/Rectangle.
            // Array/cube-array targets should use 3D overloads.
            throw new InvalidOperationException($"2D upload is not supported for target {textureTarget}.");
        }

        if (depth != 1)
        {
            throw new InvalidOperationException("2D upload requires Depth == 1.");
        }
    }

    private void Ensure3DLike()
    {
        // Targets that upload via TexSubImage3D.
        if (textureTarget != TextureTarget.Texture3D
            && textureTarget != TextureTarget.Texture2DArray
            && textureTarget != TextureTarget.TextureCubeMapArray)
        {
            throw new InvalidOperationException($"3D upload is not supported for target {textureTarget}.");
        }
    }


}
