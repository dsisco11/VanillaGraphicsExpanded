using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for GPU texture wrappers.
/// By default, upload APIs use streamed <c>StageCopy</c> (deep-copy immediately, GPU upload deferred).
/// "Dynamic" textures can override upload methods to default to immediate GL uploads.
/// </summary>
public abstract class GpuTexture : IDisposable
{
    protected int textureId;
    protected int width;
    protected int height;
    protected int depth = 1;
    protected PixelInternalFormat internalFormat;
    protected TextureTarget textureTarget;
    protected TextureFilterMode filterMode;
    protected string? debugName;
    protected bool isDisposed;

    public int TextureId => textureId;
    public int Width => width;
    public int Height => height;
    public int Depth => depth;
    public PixelInternalFormat InternalFormat => internalFormat;
    public TextureTarget TextureTarget => textureTarget;
    public TextureFilterMode FilterMode => filterMode;
    public string? DebugName => debugName;

    public bool IsDisposed => isDisposed;
    public bool IsValid => textureId != 0 && !isDisposed;

    public int Detach()
    {
        if (isDisposed)
        {
            return 0;
        }

        int id = textureId;
        textureId = 0;
        isDisposed = true;
        return id;
    }

    public int ReleaseHandle()
    {
        return Detach();
    }

    public void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (textureId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
        }
#endif
    }

    public BindingScope BindScope(int unit)
    {
        int previousActive = 0;
        bool hasPreviousActive = false;
        try
        {
            GL.GetInteger(GetPName.ActiveTexture, out previousActive);
            hasPreviousActive = true;
        }
        catch
        {
            previousActive = 0;
        }

        int previousBinding = 0;
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            if (TryGetBindingQuery(textureTarget, out GetPName bindingQuery))
            {
                GL.GetInteger(bindingQuery, out previousBinding);
            }
        }
        catch
        {
            previousBinding = 0;
        }

        Bind(unit);
        return new BindingScope(textureTarget, unit, previousBinding, previousActive, hasPreviousActive);
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
        GL.BindTexture(textureTarget, textureId);

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
#endif

        Allocate2DStorageBound(mipLevels);
        Apply2DSamplerParamsBound(mipLevels);

        GL.BindTexture(textureTarget, 0);
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

        GL.BindTexture(textureTarget, textureId);
        Allocate2DStorageBound(mipLevels);
        Apply2DSamplerParamsBound(mipLevels);
        GL.BindTexture(textureTarget, 0);
    }

    protected void AllocateOrReallocate3DTexture()
    {
        Ensure3DLike();

        DeleteTextureIfAllocated();

        textureId = GL.GenTexture();
        GL.BindTexture(textureTarget, textureId);

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
#endif

        Allocate3DStorageBound();
        Apply3DSamplerParamsBound();

        GL.BindTexture(textureTarget, 0);
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
        if (mipLevels <= 1)
        {
            int filterParam = TextureFormatHelper.GetFilterParameter(filterMode);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, filterParam);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, filterParam);
        }
        else
        {
            GL.TexParameter(textureTarget, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMaxLevel, mipLevels - 1);

            // Mipmapped sampling is typically explicit (texelFetch/textureLod) for these usage patterns.
            GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapNearest);
            GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }

        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    private void Apply3DSamplerParamsBound()
    {
        int filterParam = TextureFormatHelper.GetFilterParameter(filterMode);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, filterParam);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, filterParam);
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        if (textureTarget == TextureTarget.Texture3D)
        {
            GL.TexParameter(textureTarget, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        }
    }

    #endregion

    public virtual void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTexture] Attempted to bind disposed or invalid texture");
            return;
        }

        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(textureTarget, textureId);
    }

    public virtual void Unbind(int unit)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(textureTarget, 0);
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

        GL.BindTexture(textureTarget, textureId);
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
        GL.BindTexture(textureTarget, 0);
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

        GL.BindTexture(textureTarget, textureId);
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
        GL.BindTexture(textureTarget, 0);
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

        GL.BindTexture(textureTarget, textureId);
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
        GL.BindTexture(textureTarget, 0);
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

        GL.BindTexture(textureTarget, textureId);
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
        GL.BindTexture(textureTarget, 0);
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

        int tempFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            textureTarget,
            textureId,
            0);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(
            0,
            0,
            width,
            height,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(tempFbo);

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

        int tempFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            textureTarget,
            textureId,
            0);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(
            x,
            y,
            regionWidth,
            regionHeight,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(tempFbo);

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

        int tempFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            textureId,
            mipLevel);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(
            0,
            0,
            mipWidth,
            mipHeight,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(tempFbo);

        return data;
    }

    #endregion

    public virtual void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        if (textureId != 0)
        {
            GL.DeleteTexture(textureId);
            textureId = 0;
        }

        isDisposed = true;
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
        private readonly int previousActive;
        private readonly bool restoreActive;

        public BindingScope(TextureTarget target, int unit, int previousBinding, int previousActive, bool restoreActive)
        {
            this.target = target;
            this.unit = unit;
            this.previousBinding = previousBinding;
            this.previousActive = previousActive;
            this.restoreActive = restoreActive;
        }

        public void Dispose()
        {
            if (restoreActive)
            {
                try
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + unit);
                    GL.BindTexture(target, previousBinding);
                }
                finally
                {
                    GL.ActiveTexture((TextureUnit)previousActive);
                }
            }
            else
            {
                GL.ActiveTexture(TextureUnit.Texture0 + unit);
                GL.BindTexture(target, previousBinding);
            }
        }
    }
}
