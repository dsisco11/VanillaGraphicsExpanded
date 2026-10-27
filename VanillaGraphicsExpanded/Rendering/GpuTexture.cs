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

    /// <summary>
    /// Binds this texture to an image unit via <c>glBindImageTexture</c>.
    /// </summary>
    /// <remarks>
    /// The <paramref name="format"/> must be a sized internal format compatible with the texture storage.
    /// If not specified, this uses <see cref="InternalFormat"/> cast to <see cref="SizedInternalFormat"/>.
    /// </remarks>
    public void BindImageUnit(
        int unit,
        TextureAccess access = TextureAccess.ReadOnly,
        int level = 0,
        bool layered = false,
        int layer = 0,
        SizedInternalFormat? format = null)
    {
        if (!base.IsValid || metadata.Levels.IsDefaultOrEmpty)
        {
            Debug.WriteLine("[GpuTexture] Attempted to bind disposed or invalid texture as image");
            return;
        }

        if (unit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Image unit must be >= 0.");
        }

        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be >= 0.");
        }

        if (layer < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "Layer must be >= 0.");
        }

        var sizedFormat = format ?? (SizedInternalFormat)internalFormat;
        OpenGLImageSlotBindings.BindImageTexture(StateCache.Current, unit, textureId, level, layered, layer, access, sizedFormat);
    }

    /// <summary>Binds this live allocation and restores the previous binding when the scope ends.</summary>
    public BindingScope BindScope(int unit)
    {
        ObjectDisposedException.ThrowIf(!base.IsValid, this);
        if (metadata.Levels.IsDefaultOrEmpty)
            throw new InvalidOperationException("Texture image metadata is unavailable; refresh before binding.");
        var gl = StateCache.Current;

        // Ensure sampler-object state doesn't override texture-object parameters.
        // Use GlStateCache scopes so binding restore is PSO-owned and cache-consistent.
        var textureScope = gl.BindTextureScope(textureTarget, unit, textureId);
        var samplerScope = gl.BindSamplerScope(unit, samplerId: 0);

        return new BindingScope(textureScope, samplerScope);
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

    /// <summary>Changes existing storage and receives its resulting snapshot through StateCache publication.</summary>
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
        var request = new RHITextureDescription
        {
            Dimension = RHI.Backends.OpenGL.OpenGLImageValues.Dimension(target), Format = RHI.Backends.OpenGL.OpenGLImageValues.Format(format),
            Width = width, Height = height, Depth = depth, MipLevels = 1, Storage = immutableStorage ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable,
            Filter = filterMode, BaseLevel = 0, MaxLevel = 0, Label = debugName
        };
        try { RHICommandContext.Current.OpenGL.InitializeTextureOwner(this, request); }
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
        RHICommandContext.Current.OpenGL.DeleteTexture(textureId);
        textureId = 0;
    }
    #endregion

    public virtual void Bind(int unit)
    {
        if (!base.IsValid || metadata.Levels.IsDefaultOrEmpty)
        {
            Debug.WriteLine("[GpuTexture] Attempted to bind disposed or invalid texture");
            return;
        }

        OpenGLImageBindings.BindTexture(StateCache.Current, textureTarget, unit, textureId);
        // Ensure sampler-object state doesn't override texture-object parameters.
        OpenGLImageBindings.UnbindSampler(StateCache.Current, unit);
    }

    public bool TryBind(int unit)
    {
        if (!base.IsValid || metadata.Levels.IsDefaultOrEmpty)
        {
            return false;
        }

        OpenGLImageBindings.BindTexture(StateCache.Current, textureTarget, unit, textureId);
        // Ensure sampler-object state doesn't override texture-object parameters.
        OpenGLImageBindings.UnbindSampler(StateCache.Current, unit);
        return true;
    }

    public virtual void Unbind(int unit)
    {
        OpenGLImageBindings.BindTexture(StateCache.Current, textureTarget, unit, 0);
        OpenGLImageBindings.UnbindSampler(StateCache.Current, unit);
    }

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
            MapUploadTarget(textureTarget),
            TextureUploadRegion.For2D(0, 0, width, height, mipLevel: mipLevel),
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
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
            MapUploadTarget(textureTarget),
            TextureUploadRegion.For2D(x, y, regionWidth, regionHeight, mipLevel: mipLevel),
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
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
            MapUploadTarget(textureTarget),
            TextureUploadRegion.For2D(0, 0, width, height, mipLevel: mipLevel),
            TextureFormatHelper.GetPixelFormat(internalFormat),
            pixelType,
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
            MapUploadTarget(textureTarget),
            new TextureUploadRegion(x, y, z, regionWidth, regionHeight, regionDepth, mipLevel),
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        
            RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.UnsignedByte), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
        
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
        
            RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.UnsignedShort), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
        
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(mipLevel), x, y, z, regionWidth, regionHeight, regionDepth), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), pixelType), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
         RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.UnsignedByte), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan())); 
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), pixelType), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), pixelType), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), pixelType), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
            new(new(0), 0, 0, 0, width, height, 1), OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float)));
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
            new(new(0), x, y, 0, regionWidth, regionHeight, 1), OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float)));
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
            new(new(mipLevel), 0, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), 1), OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float)));
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
        GpuTextureRef reference = this;
        var image = reference.RequireImage(mipLevel);
        var encoding = RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(image.Format), TextureFormatHelper.GetPixelType(image.Format));
        Span<byte> zero = stackalloc byte[RHI.Backends.OpenGL.OpenGLPixelValues.PixelBytes(encoding)];
        zero.Clear();
        int height = textureTarget == TextureTarget.Texture1DArray ? 1 : image.Height;
        int depth = textureTarget == TextureTarget.TextureCubeMap ? 6 : textureTarget == TextureTarget.Texture1DArray ? image.Height : image.Depth;
        RHICommandContext.Current.ClearTexture(this, new(new(mipLevel), 0, 0, 0, image.Width, height, depth), encoding, zero);
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

    internal static TextureUploadTarget MapUploadTarget(TextureTarget textureTarget)
    {
        return textureTarget switch
        {
            TextureTarget.Texture1D => TextureUploadTarget.For1D(),
            TextureTarget.Texture1DArray => TextureUploadTarget.For1DArray(),
            TextureTarget.Texture2D => TextureUploadTarget.For2D(),
            TextureTarget.Texture2DArray => TextureUploadTarget.For2DArray(),
            TextureTarget.Texture3D => TextureUploadTarget.For3D(),
            TextureTarget.TextureRectangle => TextureUploadTarget.ForRectangle(),
            TextureTarget.TextureCubeMapArray => TextureUploadTarget.ForCubeArray(),
            _ => new TextureUploadTarget(textureTarget, textureTarget)
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

    private static bool TryGetBindingQuery(TextureTarget target, out GetPName pname)
    {
        pname = target switch
        {
            TextureTarget.Texture1D => GetPName.TextureBinding1D,
            TextureTarget.Texture1DArray => GetPName.TextureBinding1DArray,
            TextureTarget.Texture2D => GetPName.TextureBinding2D,
            TextureTarget.Texture2DArray => GetPName.TextureBinding2DArray,
            TextureTarget.Texture3D => GetPName.TextureBinding3D,
            TextureTarget.TextureRectangle => GetPName.TextureBindingRectangle,
            TextureTarget.TextureCubeMap => GetPName.TextureBindingCubeMap,
            _ => default
        };

        return pname != default;
    }

    public readonly struct BindingScope : IDisposable
    {
        private readonly StateCache.TextureScope textureScope;
        private readonly StateCache.SamplerScope samplerScope;

        private readonly TextureTarget target;
        private readonly int unit;
        private readonly int previousBinding;
        private readonly int previousSampler;
        private readonly int previousActiveUnit;
        private readonly bool restoreActive;

        private readonly bool useCacheScopes;

        internal BindingScope(StateCache.TextureScope textureScope, StateCache.SamplerScope samplerScope)
        {
            this.textureScope = textureScope;
            this.samplerScope = samplerScope;

            target = default;
            unit = 0;
            previousBinding = 0;
            previousSampler = 0;
            previousActiveUnit = 0;
            restoreActive = false;
            useCacheScopes = true;
        }

        public BindingScope(
            TextureTarget target,
            int unit,
            int previousBinding,
            int previousSampler,
            int previousActiveUnit,
            bool restoreActive)
        {
            textureScope = default;
            samplerScope = default;
            this.target = target;
            this.unit = unit;
            this.previousBinding = previousBinding;
            this.previousSampler = previousSampler;
            this.previousActiveUnit = previousActiveUnit;
            this.restoreActive = restoreActive;
            useCacheScopes = false;
        }

        public void Dispose()
        {
            if (useCacheScopes)
            {
                samplerScope.Dispose();
                textureScope.Dispose();
                return;
            }

            OpenGLImageBindings.BindTexture(StateCache.Current, target, unit, previousBinding);
            OpenGLImageBindings.BindSampler(StateCache.Current, unit, previousSampler);

            if (restoreActive)
            {
                OpenGLImageBindings.ActiveTexture(StateCache.Current, previousActiveUnit);
            }
        }
    }

    private static int GetClearComponentCount(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Red or PixelFormat.RedInteger => 1,
            PixelFormat.Rg or PixelFormat.RgInteger => 2,
            PixelFormat.Rgb => 3,
            PixelFormat.Rgba or PixelFormat.RgbaInteger => 4,
            PixelFormat.DepthComponent => 1,

            // Packed depth-stencil types (UnsignedInt248/Float32UnsignedInt248Rev) are provided as a single value.
            PixelFormat.DepthStencil => 1,

            _ => 4
        };
    }

    private static int GetPixelTypeSizeBytes(PixelType type)
    {
        return type switch
        {
            PixelType.UnsignedByte or PixelType.Byte => 1,
            PixelType.UnsignedShort or PixelType.Short or PixelType.HalfFloat => 2,
            PixelType.UnsignedInt or PixelType.Int or PixelType.Float or PixelType.UnsignedInt248 => 4,
            PixelType.Float32UnsignedInt248Rev => 8,
            _ => 4
        };
    }
}
