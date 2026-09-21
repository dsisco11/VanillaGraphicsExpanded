using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for GPU texture wrappers.
/// By default, upload APIs use streamed <c>StageCopy</c> (deep-copy immediately, GPU upload deferred).
/// "Dynamic" textures can override upload methods to default to immediate GL uploads.
/// </summary>
public abstract class GpuTexture : GpuResource, IDisposable
{
    protected int textureId;
    protected int width;
    protected int height;
    protected int depth = 1;
    protected PixelInternalFormat internalFormat;
    protected TextureTarget textureTarget;
    protected TextureFilterMode filterMode;
    protected string? debugName;

    public int TextureId => textureId;
    public int Width => width;
    public int Height => height;
    public int Depth => depth;
    public PixelInternalFormat InternalFormat => internalFormat;
    public TextureTarget TextureTarget => textureTarget;
    public TextureFilterMode FilterMode => filterMode;
    public string? DebugName => debugName;

    protected override nint ResourceId
    {
        get => textureId;
        set => textureId = (int)value;
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
        if (!IsValid)
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
        GL.BindImageTexture(unit, textureId, level, layered, layer, access, sizedFormat);
    }

    public BindingScope BindScope(int unit)
    {
        var gl = GlStateCache.Current;

        // Ensure sampler-object state doesn't override texture-object parameters.
        // Use GlStateCache scopes so binding restore is PSO-owned and cache-consistent.
        var textureScope = gl.BindTextureScope(textureTarget, unit, textureId);
        var samplerScope = gl.BindSamplerScope(unit, samplerId: 0);

        return new BindingScope(textureScope, samplerScope);
    }

    #region Allocation Helpers

    protected void AllocateOrReallocate2DTexture(int mipLevels)
    {
        if (textureTarget != TextureTarget.Texture2D && textureTarget != TextureTarget.TextureRectangle)
        {
            throw new InvalidOperationException($"2D allocation is not supported for target {textureTarget}.");
        }

        if (mipLevels < 1)
        {
            mipLevels = 1;
        }

        if (textureTarget == TextureTarget.TextureRectangle && mipLevels != 1)
        {
            throw new InvalidOperationException("TextureRectangle does not support mipmaps.");
        }

        DeleteTextureIfAllocated();

        textureId = GL.GenTexture();
        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);

        Allocate2DStorageBound(mipLevels);

    #if DEBUG
        // Label only after storage exists; some drivers reject labeling of "generated" names.
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
    #endif
        Apply2DTextureObjectParamsBound(mipLevels);
    }

    protected void Reallocate2DStorageInPlace(int mipLevels)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("Cannot reallocate texture storage: texture is not valid.");
        }

        if (textureTarget != TextureTarget.Texture2D && textureTarget != TextureTarget.TextureRectangle)
        {
            throw new InvalidOperationException($"2D allocation is not supported for target {textureTarget}.");
        }

        if (mipLevels < 1)
        {
            mipLevels = 1;
        }

        if (textureTarget == TextureTarget.TextureRectangle && mipLevels != 1)
        {
            throw new InvalidOperationException("TextureRectangle does not support mipmaps.");
        }

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        Allocate2DStorageBound(mipLevels);
        Apply2DTextureObjectParamsBound(mipLevels);
    }

    protected void AllocateOrReallocate3DTexture()
    {
        Ensure3DLike();

        DeleteTextureIfAllocated();

        textureId = GL.GenTexture();
        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);

        Allocate3DStorageBound();

    #if DEBUG
        // Label only after storage exists; some drivers reject labeling of "generated" names.
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
    #endif
        Apply3DTextureObjectParamsBound();
    }

    private void DeleteTextureIfAllocated()
    {
        if (textureId != 0)
        {
            GL.DeleteTexture(textureId);
            textureId = 0;
        }
    }

    private void Allocate2DStorageBound(int mipLevels)
    {
        if (mipLevels <= 1)
        {
            GL.TexImage2D(
                textureTarget,
                level: 0,
                internalformat: internalFormat,
                width: width,
                height: height,
                border: 0,
                format: TextureFormatHelper.GetPixelFormat(internalFormat),
                type: TextureFormatHelper.GetPixelType(internalFormat),
                pixels: IntPtr.Zero);

            return;
        }

        for (int level = 0; level < mipLevels; level++)
        {
            int lw = Math.Max(1, width >> level);
            int lh = Math.Max(1, height >> level);
            GL.TexImage2D(
                textureTarget,
                level,
                internalFormat,
                lw,
                lh,
                0,
                TextureFormatHelper.GetPixelFormat(internalFormat),
                TextureFormatHelper.GetPixelType(internalFormat),
                IntPtr.Zero);
        }
    }

    private void Allocate3DStorageBound()
    {
        GL.TexImage3D(
            textureTarget,
            level: 0,
            internalformat: internalFormat,
            width: width,
            height: height,
            depth: depth,
            border: 0,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: TextureFormatHelper.GetPixelType(internalFormat),
            pixels: IntPtr.Zero);
    }

    private void Apply2DTextureObjectParamsBound(int mipLevels)
    {
        // When a sampler object is bound to the unit, it overrides these parameters.
        // GpuTexture.Bind/TryBind explicitly unbinds the sampler to ensure these apply.

        TextureMinFilter min;
        TextureMagFilter mag;
        if (mipLevels <= 1)
        {
            min = filterMode == TextureFilterMode.Linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest;
            mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
        }
        else
        {
            // Keep parity with the legacy sampler-object path.
            min = TextureMinFilter.NearestMipmapNearest;
            mag = TextureMagFilter.Nearest;
        }

        GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, (int)min);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, (int)mag);

        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        int maxLevel = Math.Max(0, mipLevels - 1);
        GL.TexParameter(textureTarget, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMaxLevel, maxLevel);
    }

    private void Apply3DTextureObjectParamsBound()
    {
        TextureMinFilter min = filterMode == TextureFilterMode.Linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest;
        TextureMagFilter mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;

        GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, (int)min);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, (int)mag);

        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        if (textureTarget == TextureTarget.Texture3D)
        {
            GL.TexParameter(textureTarget, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        }

        GL.TexParameter(textureTarget, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMaxLevel, 0);
    }

    #endregion

    public virtual void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to bind disposed or invalid texture");
            return;
        }

        GlStateCache.Current.BindTexture(textureTarget, unit, textureId);
        // Ensure sampler-object state doesn't override texture-object parameters.
        GlStateCache.Current.UnbindSampler(unit);
    }

    public bool TryBind(int unit)
    {
        if (!IsValid)
        {
            return false;
        }

        GlStateCache.Current.BindTexture(textureTarget, unit, textureId);
        // Ensure sampler-object state doesn't override texture-object parameters.
        GlStateCache.Current.UnbindSampler(unit);
        return true;
    }

    public virtual void Unbind(int unit)
    {
        GlStateCache.Current.BindTexture(textureTarget, unit, 0);
        GlStateCache.Current.UnbindSampler(unit);
    }

    #region Texture-Object State Helpers (TexParameter)

    /// <summary>
    /// Sets the mipmap range on the texture object itself via <c>glTexParameter</c>.
    /// This is important when a texture is sampled by raw texture ID without a sampler object bound.
    /// </summary>
    public void SetMipRange(int baseLevel, int maxLevel)
    {
        if (!IsValid)
        {
            return;
        }

        try
        {
            using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
            GL.TexParameter(textureTarget, TextureParameterName.TextureBaseLevel, baseLevel);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMaxLevel, maxLevel);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Disables mipmapping on the texture object itself (base/max level forced to 0).
    /// </summary>
    public void DisableMipmaps()
    {
        SetMipRange(baseLevel: 0, maxLevel: 0);
    }

    /// <summary>
    /// Sets min/mag filtering on the texture object itself via <c>glTexParameter</c>.
    /// </summary>
    public void SetTexFilter(TextureMinFilter minFilter, TextureMagFilter magFilter)
    {
        if (!IsValid)
        {
            return;
        }

        try
        {
            using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, (int)minFilter);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, (int)magFilter);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Sets wrapping on the texture object itself via <c>glTexParameter</c>.
    /// </summary>
    public void SetTexWrap(TextureWrapMode wrapS, TextureWrapMode wrapT, TextureWrapMode? wrapR = null)
    {
        if (!IsValid)
        {
            return;
        }

        try
        {
            using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
            GL.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)wrapS);
            GL.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)wrapT);

            if (wrapR.HasValue && textureTarget == TextureTarget.Texture3D)
            {
                GL.TexParameter(textureTarget, TextureParameterName.TextureWrapR, (int)wrapR.Value);
            }
        }
        catch
        {
        }
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
        if (!IsValid)
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
            unpackAlignment: 4);

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
        if (!IsValid)
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
            unpackAlignment: 4);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[GpuTexture] UploadData(region) staged rejected: {result.RejectReason}");
        }

        _ = result;
    }

    public virtual void UploadDataStreamed(ushort[] data, int priority = 0, int mipLevel = 0)
    {
        if (!IsValid)
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
            unpackAlignment: 1);

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
        if (!IsValid)
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
            unpackAlignment: 4);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[GpuTexture] UploadData3D staged rejected: {result.RejectReason}");
        }

        _ = result;
    }

    public virtual void UploadDataImmediate(float[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int mipLevel = 0)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.TexSubImage3D(
            textureTarget,
            mipLevel,
            x,
            y,
            z,
            regionWidth,
            regionHeight,
            regionDepth,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);
    }

    /// <summary>Uploads tightly packed byte data to a 3D region, preserving the caller's unpack alignment.</summary>
    public virtual void UploadDataImmediate(byte[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int mipLevel = 0)
    {
        if (!IsValid)
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

        using var binding = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        // Single-channel regions need not have four-byte-wide rows.
        GL.GetInteger(GetPName.UnpackAlignment, out int previousAlignment);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        try
        {
            GL.TexSubImage3D(textureTarget, mipLevel, x, y, z, regionWidth, regionHeight, regionDepth,
                TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.UnsignedByte, data);
        }
        finally
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment);
        }
    }

    public virtual void UploadDataImmediate(uint[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int mipLevel = 0)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.TexSubImage3D(
            textureTarget,
            mipLevel,
            x,
            y,
            z,
            regionWidth,
            regionHeight,
            regionDepth,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            pixelType,
            data);
    }

    /// <summary>Uploads native normalized or integer bytes to a complete 2D texture without float staging.</summary>
    public virtual void UploadDataImmediate(byte[] data)
        => UploadDataImmediate(data, 0, 0, width, height);

    /// <summary>Uploads native bytes to a bounded 2D region while preserving pixel-store alignment.</summary>
    public virtual void UploadDataImmediate(byte[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(GpuTexture));
        ArgumentNullException.ThrowIfNull(data);
        Ensure2DLike();
        if (TextureFormatHelper.GetPixelType(internalFormat) != PixelType.UnsignedByte)
            throw new InvalidOperationException("Byte upload requires an unsigned-byte texture format.");
        if (x < 0 || y < 0 || regionWidth <= 0 || regionHeight <= 0 || x + regionWidth > width || y + regionHeight > height)
            throw new ArgumentOutOfRangeException(nameof(regionWidth));
        if (data.Length != checked(regionWidth * regionHeight * GetChannelCount())) throw new ArgumentException("Incorrect byte payload size.", nameof(data));
        using var binding = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.GetInteger(GetPName.UnpackAlignment, out int previousAlignment);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        try { GL.TexSubImage2D(textureTarget, 0, x, y, regionWidth, regionHeight, TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.UnsignedByte, data); }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment); }
    }

    /// <summary>Uploads a full texture immediately from floating-point input.</summary>
    public virtual void UploadDataImmediate(float[] data)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.TexSubImage2D(
            textureTarget,
            level: 0,
            xoffset: 0,
            yoffset: 0,
            width: width,
            height: height,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: PixelType.Float,
            pixels: data);
    }

    public virtual void UploadDataImmediate(uint[] data)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.TexSubImage2D(
            textureTarget,
            level: 0,
            xoffset: 0,
            yoffset: 0,
            width: width,
            height: height,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: pixelType,
            pixels: data);
    }

    public virtual void UploadDataImmediate(float[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.TexSubImage2D(
            textureTarget,
            level: 0,
            xoffset: x,
            yoffset: y,
            width: regionWidth,
            height: regionHeight,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: PixelType.Float,
            pixels: data);
    }

    public virtual void UploadDataImmediate(uint[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.TexSubImage2D(
            textureTarget,
            level: 0,
            xoffset: x,
            yoffset: y,
            width: regionWidth,
            height: regionHeight,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: pixelType,
            pixels: data);
    }

    public virtual void UploadDataImmediate(ushort[] data)
    {
        if (!IsValid)
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

        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexSubImage2D(
            textureTarget,
            level: 0,
            xoffset: 0,
            yoffset: 0,
            width: width,
            height: height,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: pixelType,
            pixels: data);
    }

    #region Readback

    /// <summary>
    /// Reads pixel data from the full texture.
    /// Requires the texture to be a 2D-like target; uses a temporary FBO for readback.
    /// </summary>
    public virtual float[] ReadPixels()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        Ensure2DLike();

        int channelCount = GetChannelCount();
        float[] data = new float[checked(width * height * channelCount)];

        using var tempFbo = GpuFramebuffer.CreateEmpty("VGE_GpuTexture_Readback_FBO");
        tempFbo.Bind();
        tempFbo.AttachColorTextureId(textureId, attachmentIndex: 0, mipLevel: 0);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(
            0,
            0,
            width,
            height,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GpuFramebuffer.Unbind();

        return data;
    }

    /// <summary>
    /// Reads pixel data from a sub-region of the texture.
    /// Creates a temporary FBO for readback (best-effort; avoid calling frequently at runtime).
    /// </summary>
    public virtual float[] ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        Ensure2DLike();

        if (x < 0 || y < 0 || x + regionWidth > width || y + regionHeight > height)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({width}×{height})");
        }

        int channelCount = GetChannelCount();
        float[] data = new float[checked(regionWidth * regionHeight * channelCount)];

        using var tempFbo = GpuFramebuffer.CreateEmpty("VGE_GpuTexture_Readback_FBO");
        tempFbo.Bind();
        tempFbo.AttachColorTextureId(textureId, attachmentIndex: 0, mipLevel: 0);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(
            x,
            y,
            regionWidth,
            regionHeight,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GpuFramebuffer.Unbind();

        return data;
    }

    /// <summary>
    /// Reads pixel data from a specific mip level of the texture.
    /// </summary>
    public virtual float[] ReadPixels(int mipLevel)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        Ensure2DLike();

        if (textureTarget != TextureTarget.Texture2D)
        {
            throw new InvalidOperationException($"Mip readback is not supported for target {textureTarget}.");
        }

        mipLevel = Math.Max(0, mipLevel);

        int mipWidth = Math.Max(1, width >> mipLevel);
        int mipHeight = Math.Max(1, height >> mipLevel);
        int channelCount = GetChannelCount();
        float[] data = new float[checked(mipWidth * mipHeight * channelCount)];

        using var tempFbo = GpuFramebuffer.CreateEmpty("VGE_GpuTexture_Readback_FBO");
        tempFbo.Bind();
        tempFbo.AttachColorTextureId(textureId, attachmentIndex: 0, mipLevel: mipLevel);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(
            0,
            0,
            mipWidth,
            mipHeight,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GpuFramebuffer.Unbind();

        return data;
    }

    #endregion

    public override string ToString()
    {
        return $"{GetType().Name}(id={textureId}, target={textureTarget}, size={width}x{height}x{depth}, format={internalFormat}, name={debugName}, disposed={IsDisposed})";
    }

    public unsafe bool TryClearToZero(int mipLevel = 0)
    {
        if (!IsValid)
        {
            return false;
        }

        if (!GlExtensions.Supports("GL_ARB_clear_texture"))
        {
            return false;
        }

        PixelFormat format = TextureFormatHelper.GetPixelFormat(internalFormat);
        PixelType type = TextureFormatHelper.GetPixelType(internalFormat);
        int componentCount = GetClearComponentCount(format);
        int typeSizeBytes = GetPixelTypeSizeBytes(type);
        int byteCount = checked(componentCount * typeSizeBytes);

        // For a clear-to-zero, a zero-initialized byte buffer works for all scalar types
        // (including float/half-float) and packed depth-stencil types.
        Span<byte> zero = stackalloc byte[byteCount];

        try
        {
            fixed (byte* ptr = zero)
            {
                GL.ClearTexImage(textureId, level: mipLevel, format, type, (IntPtr)ptr);
            }

            return true;
        }
        catch
        {
            return false;
        }
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

    protected int GetChannelCount()
    {
        return internalFormat switch
        {
            PixelInternalFormat.R16f or PixelInternalFormat.R32f or PixelInternalFormat.R16 or PixelInternalFormat.R8 => 1,
            PixelInternalFormat.R8ui or PixelInternalFormat.R16ui or PixelInternalFormat.R32ui => 1,
            PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f or PixelInternalFormat.Rg16 or PixelInternalFormat.Rg8 => 2,
            PixelInternalFormat.Rg8ui or PixelInternalFormat.Rg16ui or PixelInternalFormat.Rg32ui => 2,
            PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb32f or PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb => 3,
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
        private readonly GlStateCache.TextureScope textureScope;
        private readonly GlStateCache.SamplerScope samplerScope;

        private readonly TextureTarget target;
        private readonly int unit;
        private readonly int previousBinding;
        private readonly int previousSampler;
        private readonly int previousActiveUnit;
        private readonly bool restoreActive;

        private readonly bool useCacheScopes;

        internal BindingScope(GlStateCache.TextureScope textureScope, GlStateCache.SamplerScope samplerScope)
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

            GlStateCache.Current.BindTexture(target, unit, previousBinding);
            GlStateCache.Current.BindSampler(unit, previousSampler);

            if (restoreActive)
            {
                GlStateCache.Current.ActiveTexture(previousActiveUnit);
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
