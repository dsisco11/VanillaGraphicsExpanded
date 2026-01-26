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
    private GpuSampler? sampler;

    public int TextureId => textureId;
    public int Width => width;
    public int Height => height;
    public int Depth => depth;
    public PixelInternalFormat InternalFormat => internalFormat;
    public TextureTarget TextureTarget => textureTarget;
    public TextureFilterMode FilterMode => filterMode;
    public string? DebugName => debugName;

    internal int SamplerId => sampler?.SamplerId ?? 0;

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

        if (sampler is not null && sampler.IsValid)
        {
            sampler.SetDebugName(debugName is null ? null : $"{debugName}.Sampler");
        }
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
        int previousActive = 0;
        bool hasPreviousActive = false;
        try
        {
            previousActive = GL.GetInteger(GetPName.ActiveTexture);
            hasPreviousActive = true;
        }
        catch
        {
            previousActive = 0;
        }

        int previousBinding = 0;
        int previousSampler = 0;
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            if (TryGetBindingQuery(textureTarget, out GetPName bindingQuery))
            {
                GL.GetInteger(bindingQuery, out previousBinding);
            }

            try
            {
                previousSampler = GL.GetInteger(GetPName.SamplerBinding);
            }
            catch
            {
                previousSampler = 0;
            }
        }
        catch
        {
            previousBinding = 0;
            previousSampler = 0;
        }
        finally
        {
            if (hasPreviousActive)
            {
                try { GL.ActiveTexture((TextureUnit)previousActive); } catch { }
            }
        }

        GlStateCache.Current.BindTexture(textureTarget, unit, textureId);
        int samplerId = SamplerId;
        if (samplerId != 0)
        {
            GlStateCache.Current.BindSampler(unit, samplerId);
        }

        return new BindingScope(textureTarget, unit, previousBinding, previousSampler, previousActive, hasPreviousActive);
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

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
#endif

        Allocate2DStorageBound(mipLevels);
        Apply2DSamplerParamsBound(mipLevels);
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
        Apply2DSamplerParamsBound(mipLevels);
    }

    protected void AllocateOrReallocate3DTexture()
    {
        Ensure3DLike();

        DeleteTextureIfAllocated();

        textureId = GL.GenTexture();
        using var _ = GlStateCache.Current.BindTextureScope(textureTarget, unit: 0, textureId);

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
#endif

        Allocate3DStorageBound();
        Apply3DSamplerParamsBound();
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

    private void Apply2DSamplerParamsBound(int mipLevels)
    {
        EnsureSamplerCreated();
        ConfigureSampler(mipLevels, is3D: false);

        if (mipLevels > 1)
        {
            GL.TexParameter(textureTarget, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMaxLevel, mipLevels - 1);
        }
    }

    private void Apply3DSamplerParamsBound()
    {
        EnsureSamplerCreated();
        ConfigureSampler(mipLevels: 1, is3D: true);
    }

    private void EnsureSamplerCreated()
    {
        if (sampler is not null && sampler.IsValid)
        {
            return;
        }

        sampler?.Dispose();
        sampler = GpuSampler.Create(debugName is null ? null : $"{debugName}.Sampler");
    }

    private void ConfigureSampler(int mipLevels, bool is3D)
    {
        if (sampler is null || !sampler.IsValid)
        {
            return;
        }

        if (mipLevels <= 1)
        {
            TextureMinFilter min = filterMode == TextureFilterMode.Linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest;
            TextureMagFilter mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
            sampler.SetFilter(min, mag);
        }
        else
        {
            // Keep parity with the legacy texture-parameter path.
            sampler.SetFilter(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);
        }

        sampler.SetWrap(
            TextureWrapMode.ClampToEdge,
            TextureWrapMode.ClampToEdge,
            is3D && textureTarget == TextureTarget.Texture3D ? TextureWrapMode.ClampToEdge : null);
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
        int samplerId = SamplerId;
        if (samplerId != 0)
        {
            GlStateCache.Current.BindSampler(unit, samplerId);
        }
    }

    public bool TryBind(int unit)
    {
        if (!IsValid)
        {
            return false;
        }

        GlStateCache.Current.BindTexture(textureTarget, unit, textureId);
        int samplerId = SamplerId;
        if (samplerId != 0)
        {
            GlStateCache.Current.BindSampler(unit, samplerId);
        }
        return true;
    }

    public virtual void Unbind(int unit)
    {
        GlStateCache.Current.BindTexture(textureTarget, unit, 0, sampler: null);
    }

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

    /// <summary>
    /// Uploads a full texture immediately (GL call).
    /// </summary>
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

    protected override void OnDetached(nint id)
    {
        sampler?.Dispose();
        sampler = null;
    }

    protected override void OnAfterDelete()
    {
        sampler?.Dispose();
        sampler = null;
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
            PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f or PixelInternalFormat.Rg16 or PixelInternalFormat.Rg8 => 2,
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
        private readonly TextureTarget target;
        private readonly int unit;
        private readonly int previousBinding;
        private readonly int previousSampler;
        private readonly int previousActive;
        private readonly bool restoreActive;

        public BindingScope(
            TextureTarget target,
            int unit,
            int previousBinding,
            int previousSampler,
            int previousActive,
            bool restoreActive)
        {
            this.target = target;
            this.unit = unit;
            this.previousBinding = previousBinding;
            this.previousSampler = previousSampler;
            this.previousActive = previousActive;
            this.restoreActive = restoreActive;
        }

        public void Dispose()
        {
            GlStateCache.Current.BindTexture(target, unit, previousBinding);
            GlStateCache.Current.BindSampler(unit, previousSampler);

            if (restoreActive)
            {
                try
                {
                    int prevUnit = previousActive - (int)TextureUnit.Texture0;
                    GlStateCache.Current.ActiveTexture(prevUnit);
                }
                catch
                {
                }
            }
        }
    }
}
