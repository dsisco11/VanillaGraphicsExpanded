using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Encapsulates an OpenGL 2D texture with lifecycle management.
/// Handles creation, binding, resizing, and disposal of GPU texture resources.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// using var texture = DynamicTexture.Create(1920, 1080, PixelInternalFormat.Rgba16f);
/// texture.Bind(0); // Bind to texture unit 0
/// // ... use texture ...
/// </code>
/// </remarks>
public sealed class DynamicTexture2D : GpuTexture
{
    #region Fields
    private int mipLevels = 1;

    #endregion

    #region Properties

    /// <summary>
    /// Number of mip levels allocated for this texture (>= 1).
    /// </summary>
    public int MipLevels => mipLevels;

    #endregion

    #region Constructor (private - use factory methods)

    private DynamicTexture2D() { }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a new 2D texture with the specified parameters.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="format">Internal pixel format (e.g., Rgba16f, Rgba8).</param>
    /// <param name="filter">Filtering mode for sampling. Default is Nearest.</param>
    /// <returns>A new DynamicTexture instance.</returns>
    public static DynamicTexture2D Create(
        int width,
        int height,
        PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest,
        string? debugName = null)
    {
        if (width <= 0)
        {
            Debug.WriteLine($"[DynamicTexture] Invalid width {width}, defaulting to 1");
            width = 1;
        }
        if (height <= 0)
        {
            Debug.WriteLine($"[DynamicTexture] Invalid height {height}, defaulting to 1");
            height = 1;
        }

        var texture = new DynamicTexture2D
        {
            width = width,
            height = height,
            depth = 1,
            mipLevels = 1,
            internalFormat = format,
            textureTarget = TextureTarget.Texture2D,
            filterMode = filter,
            debugName = debugName
        };

        texture.AllocateTexture();
        return texture;
    }

    /// <summary>
    /// Creates a new 2D texture with an explicit mip chain allocated.
    /// Intended for hierarchical buffers like HZB.
    /// </summary>
    public static DynamicTexture2D CreateMipmapped(
        int width,
        int height,
        PixelInternalFormat format,
        int mipLevels,
        string? debugName = null)
    {
        if (width <= 0)
        {
            Debug.WriteLine($"[DynamicTexture] Invalid width {width}, defaulting to 1");
            width = 1;
        }
        if (height <= 0)
        {
            Debug.WriteLine($"[DynamicTexture] Invalid height {height}, defaulting to 1");
            height = 1;
        }

        if (mipLevels < 1)
            mipLevels = 1;

        var texture = new DynamicTexture2D
        {
            width = width,
            height = height,
            depth = 1,
            mipLevels = mipLevels,
            internalFormat = format,
            textureTarget = TextureTarget.Texture2D,
            filterMode = TextureFilterMode.Nearest,
            debugName = debugName
        };

        texture.AllocateTexture();
        return texture;
    }

    /// <summary>
    /// Creates a depth texture with the specified parameters.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="format">Depth format (default: DepthComponent24).</param>
    /// <returns>A new DynamicTexture instance configured for depth.</returns>
    public static DynamicTexture2D CreateDepth(
        int width,
        int height,
        PixelInternalFormat format = PixelInternalFormat.DepthComponent24,
        string? debugName = null)
    {
        if (!TextureFormatHelper.IsDepthFormat(format))
        {
            Debug.WriteLine($"[DynamicTexture] Format {format} is not a depth format, defaulting to DepthComponent24");
            format = PixelInternalFormat.DepthComponent24;
        }

        return Create(width, height, format, TextureFilterMode.Nearest, debugName);
    }

    /// <summary>
    /// Creates a new 2D texture with initial pixel data.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="format">Internal pixel format (e.g., Rgba16f, Rgba8).</param>
    /// <param name="data">Float array containing initial pixel data.</param>
    /// <param name="filter">Filtering mode for sampling. Default is Nearest.</param>
    /// <returns>A new DynamicTexture instance with uploaded data.</returns>
    /// <exception cref="ArgumentException">Thrown if data array size doesn't match texture dimensions.</exception>
    public static DynamicTexture2D CreateWithData(
        int width,
        int height,
        PixelInternalFormat format,
        float[] data,
        TextureFilterMode filter = TextureFilterMode.Nearest,
        string? debugName = null)
    {
        var texture = Create(width, height, format, filter, debugName);
        texture.UploadDataImmediate(data);
        return texture;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Binds this texture to the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit (0-31 typically).</param>
    public override void Bind(int unit)
    {
        base.Bind(unit);
    }

    /// <summary>
    /// Unbinds any texture from the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit to unbind.</param>
    public static new void Unbind(int unit)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Resizes the texture, reallocating GPU memory.
    /// Contents are lost after resize.
    /// </summary>
    /// <param name="newWidth">New width in pixels.</param>
    /// <param name="newHeight">New height in pixels.</param>
    /// <returns>True if resize occurred, false if dimensions unchanged.</returns>
    public bool Resize(int newWidth, int newHeight)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to resize disposed or invalid texture");
            return false;
        }

        if (newWidth <= 0)
        {
            Debug.WriteLine($"[DynamicTexture] Invalid resize width {newWidth}, defaulting to 1");
            newWidth = 1;
        }
        if (newHeight <= 0)
        {
            Debug.WriteLine($"[DynamicTexture] Invalid resize height {newHeight}, defaulting to 1");
            newHeight = 1;
        }

        if (newWidth == width && newHeight == height)
            return false;

        width = newWidth;
        height = newHeight;

        // Reallocate storage while preserving the existing texture id.
        Reallocate2DStorageInPlace(mipLevels);

        return true;
    }

    /// <summary>
    /// Clears the texture to the specified color (requires binding to FBO first).
    /// This method should be called when the texture is attached to a bound framebuffer.
    /// </summary>
    public void Clear()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to clear disposed or invalid texture");
            return;
        }
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>
    /// Uploads pixel data to the texture, replacing existing contents.
    /// Data array must match texture dimensions and format.
    /// </summary>
    /// <param name="data">Float array containing pixel data (RGBA order for RGBA formats).</param>
    /// <exception cref="ArgumentException">Thrown if data array size doesn't match texture dimensions.</exception>
    public override void UploadData(float[] data)
    {
        UploadDataImmediate(data);
    }

    /// <summary>
    /// Uploads pixel data to the texture immediately (GL call on the current thread).
    /// Use this only when the updated texture must be visible right away.
    /// </summary>
    public override void UploadDataImmediate(float[] data)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        int expectedSize = width * height * GetChannelCount();
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({width}×{height}×{GetChannelCount()} channels)",
                nameof(data));
        }

        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            0, 0,
            width, height,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Uploads pixel data to the texture, replacing existing contents.
    /// Intended for UNorm integer textures (e.g., RG16).
    /// </summary>
    /// <param name="data">UShort array containing pixel data (interleaved channels).</param>
    /// <exception cref="ArgumentException">Thrown if data array size doesn't match texture dimensions.</exception>
    public override void UploadData(ushort[] data)
    {
        UploadDataImmediate(data);
    }

    /// <summary>
    /// Uploads pixel data to the texture immediately (GL call on the current thread).
    /// Use this only when the updated texture must be visible right away.
    /// </summary>
    public override void UploadDataImmediate(ushort[] data)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        if (data is null) throw new ArgumentNullException(nameof(data));

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedShort)
        {
            throw new InvalidOperationException($"UploadDataImmediate(ushort[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedShort)}, but format is {internalFormat} -> {pixelType}.");
        }

        int expectedSize = width * height * GetChannelCount();
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({width}×{height}×{GetChannelCount()} channels)",
                nameof(data));
        }

        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            0, 0,
            width, height,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            pixelType,
            data);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>
    /// Uploads pixel data to a sub-region of the texture.
    /// </summary>
    /// <param name="data">Float array containing pixel data for the sub-region.</param>
    /// <param name="x">X offset in pixels.</param>
    /// <param name="y">Y offset in pixels.</param>
    /// <param name="regionWidth">Width of the sub-region in pixels.</param>
    /// <param name="regionHeight">Height of the sub-region in pixels.</param>
    /// <exception cref="ArgumentException">Thrown if data array size doesn't match region dimensions.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if region extends beyond texture bounds.</exception>
    public override void UploadData(float[] data, int x, int y, int regionWidth, int regionHeight)
    {
        UploadDataImmediate(data, x, y, regionWidth, regionHeight);
    }

    /// <summary>
    /// Uploads pixel data to a sub-region of the texture immediately (GL call on the current thread).
    /// Use this only when the updated texture must be visible right away.
    /// </summary>
    public override void UploadDataImmediate(float[] data, int x, int y, int regionWidth, int regionHeight)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to upload data to disposed or invalid texture");
            return;
        }

        if (x < 0 || y < 0 || x + regionWidth > width || y + regionHeight > height)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({width}×{height})");
        }

        int expectedSize = regionWidth * regionHeight * GetChannelCount();
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({regionWidth}×{regionHeight}×{GetChannelCount()} channels)",
                nameof(data));
        }

        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.TexSubImage2D(
            TextureTarget.Texture2D,
            0,
            x, y,
            regionWidth, regionHeight,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    internal void EnqueueUploadData(float[] data, int priority = 0, int mipLevel = 0)
        => EnqueueUploadData(data, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), priority, mipLevel);

    internal void EnqueueUploadData(float[] data, int x, int y, int regionWidth, int regionHeight, int priority = 0, int mipLevel = 0)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to enqueue upload for disposed or invalid texture");
            return;
        }

        if (data is null) throw new ArgumentNullException(nameof(data));

        int mipWidth = Math.Max(1, width >> mipLevel);
        int mipHeight = Math.Max(1, height >> mipLevel);

        if (x < 0 || y < 0 || x + regionWidth > mipWidth || y + regionHeight > mipHeight)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({mipWidth}x{mipHeight}) at mip {mipLevel}");
        }

        int expectedSize = checked(regionWidth * regionHeight * GetChannelCount());
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({regionWidth}x{regionHeight}x{GetChannelCount()} channels)",
                nameof(data));
        }

        TextureUploadPriority uploadPriority = GpuTexture.MapUploadPriority(priority);

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            TextureUploadTarget.For2D(),
            new TextureUploadRegion(x, y, 0, regionWidth, regionHeight, Depth: 1, MipLevel: mipLevel),
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data,
            uploadPriority,
            unpackAlignment: 4);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[DynamicTexture] EnqueueUploadData(float[]) staged rejected: {result.RejectReason}");
        }
    }

    internal void EnqueueUploadData(ushort[] data, int priority = 0, int mipLevel = 0)
        => EnqueueUploadData(data, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), priority, mipLevel);

    internal void EnqueueUploadData(ushort[] data, int x, int y, int regionWidth, int regionHeight, int priority = 0, int mipLevel = 0)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to enqueue upload for disposed or invalid texture");
            return;
        }

        if (data is null) throw new ArgumentNullException(nameof(data));

        var pixelType = TextureFormatHelper.GetPixelType(internalFormat);
        if (pixelType != PixelType.UnsignedShort)
        {
            throw new InvalidOperationException($"EnqueueUploadData(ushort[]) requires {nameof(PixelType)}.{nameof(PixelType.UnsignedShort)}, but format is {internalFormat} -> {pixelType}.");
        }

        int mipWidth = Math.Max(1, width >> mipLevel);
        int mipHeight = Math.Max(1, height >> mipLevel);

        if (x < 0 || y < 0 || x + regionWidth > mipWidth || y + regionHeight > mipHeight)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({mipWidth}x{mipHeight}) at mip {mipLevel}");
        }

        int expectedSize = checked(regionWidth * regionHeight * GetChannelCount());
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({regionWidth}x{regionHeight}x{GetChannelCount()} channels)",
                nameof(data));
        }

        TextureUploadPriority uploadPriority = GpuTexture.MapUploadPriority(priority);

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            TextureUploadTarget.For2D(),
            new TextureUploadRegion(x, y, 0, regionWidth, regionHeight, Depth: 1, MipLevel: mipLevel),
            TextureFormatHelper.GetPixelFormat(internalFormat),
            pixelType,
            data,
            uploadPriority,
            unpackAlignment: 2);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[DynamicTexture] EnqueueUploadData(ushort[]) staged rejected: {result.RejectReason}");
        }
    }
    /// <summary>
    /// Reads pixel data from the texture.
    /// Requires the texture to be bound to an FBO for readback.
    /// </summary>
    /// <returns>Float array containing pixel data (RGBA order for RGBA formats).</returns>
    /// <remarks>
    /// This method binds the texture to a temporary FBO for readback.
    /// For frequent readback operations, consider using a persistent FBO.
    /// </remarks>
    public override float[] ReadPixels()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        int channelCount = GetChannelCount();
        float[] data = new float[width * height * channelCount];

        // Create temporary FBO for readback
        int tempFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            textureId,
            0);

        // Explicitly select the attachment as the read source.
        // Some drivers/context states may otherwise read from an undefined buffer.
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        // Read pixels
        GL.ReadPixels(0, 0, width, height,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        // Cleanup
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(tempFbo);

        return data;
    }

    /// <summary>
    /// Reads pixel data from a sub-region of the texture.
    /// Creates a temporary FBO for readback (best-effort; avoid calling frequently at runtime).
    /// </summary>
    public override float[] ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        if (x < 0 || y < 0 || x + regionWidth > width || y + regionHeight > height)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {regionWidth}, {regionHeight}) extends beyond texture bounds ({width}×{height})");
        }

        int channelCount = GetChannelCount();
        float[] data = new float[regionWidth * regionHeight * channelCount];

        int tempFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
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
    public override float[] ReadPixels(int mipLevel)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        mipLevel = Math.Clamp(mipLevel, 0, Math.Max(0, mipLevels - 1));

        int mipWidth = Math.Max(1, width >> mipLevel);
        int mipHeight = Math.Max(1, height >> mipLevel);
        int channelCount = GetChannelCount();
        float[] data = new float[mipWidth * mipHeight * channelCount];

        int tempFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, tempFbo);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            textureId,
            mipLevel);

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(0, 0, mipWidth, mipHeight,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            PixelType.Float,
            data);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteFramebuffer(tempFbo);

        return data;
    }

    /// <summary>
    /// Gets the number of channels for the current internal format.
    /// </summary>
    private new int GetChannelCount()
    {
        return internalFormat switch
        {
            PixelInternalFormat.R16f or PixelInternalFormat.R32f or
            PixelInternalFormat.R16 or PixelInternalFormat.R8 => 1,
            PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f or
            PixelInternalFormat.Rg16 or PixelInternalFormat.Rg8 => 2,
            PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb32f or
            PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb => 3,
            _ => 4 // RGBA formats and default
        };
    }

    #endregion

    #region Private Methods

    private void AllocateTexture()
    {
        AllocateOrReallocate2DTexture(mipLevels);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases the GPU texture resource.
    /// </summary>
    // Uses base GpuTexture.Dispose() / GpuResource.Dispose().

    #endregion

    #region Implicit Conversion

    /// <summary>
    /// Allows implicit conversion to int for use with existing APIs expecting texture IDs.
    /// </summary>
    public static implicit operator int(DynamicTexture2D texture)
    {
        return texture?.textureId ?? 0;
    }

    #endregion
}
