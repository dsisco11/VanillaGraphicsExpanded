using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Encapsulates a color OpenGL 2D texture with lifecycle management.
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
public class DynamicTexture2D : GpuTexture
{
    #region Properties

    /// <summary>
    /// Number of mip levels allocated for this texture (>= 1).
    /// </summary>
    public int MipLevels => StorageMipLevels;

    #endregion

    #region Constructor (private - use factory methods)

    internal DynamicTexture2D() { }

    /// <summary>Initializes storage for a specialized two-dimensional texture.</summary>
    protected DynamicTexture2D(int width, int height, PixelInternalFormat format, TextureFilterMode filter, string? debugName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        filterMode = filter;
        this.debugName = debugName;
        AllocateOrReallocate2DTexture(width, height, format, mipLevels: 1);
    }

    #endregion

    #region Factory Methods







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
        OpenGLImageBindings.UnbindTexture(StateCache.Current, TextureTarget.Texture2D, unit);
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
        RequireMutableStorage();
        if (!((GpuResource)this).IsValid)
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

        // Reallocate storage while preserving the existing texture id.
        Reallocate2DStorageInPlace(newWidth, newHeight, StorageMipLevels);

        return true;
    }

    /// <summary>
    /// Clears the texture to the specified color (requires binding to FBO first).
    /// This method should be called when the texture is attached to a bound framebuffer.
    /// </summary>
    public virtual void Clear()
    {
        if (!((GpuResource)this).IsValid)
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
        if (!((GpuResource)this).IsValid)
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        if (!((GpuResource)this).IsValid)
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), pixelType), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        if (!((GpuResource)this).IsValid)
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(internalFormat), PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
    }

    internal void EnqueueUploadData(float[] data, int priority = 0, int mipLevel = 0)
        => EnqueueUploadData(data, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), priority, mipLevel);

    internal void EnqueueUploadData(float[] data, int x, int y, int regionWidth, int regionHeight, int priority = 0, int mipLevel = 0)
    {
        if (!((GpuResource)this).IsValid)
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
            unpackAlignment: 4, textureOwner: this);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[DynamicTexture] EnqueueUploadData(float[]) staged rejected: {result.RejectReason}");
        }
    }

    internal void EnqueueUploadData(ushort[] data, int priority = 0, int mipLevel = 0)
        => EnqueueUploadData(data, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), priority, mipLevel);

    internal void EnqueueUploadData(ushort[] data, int x, int y, int regionWidth, int regionHeight, int priority = 0, int mipLevel = 0)
    {
        if (!((GpuResource)this).IsValid)
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
            unpackAlignment: 2, textureOwner: this);

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
        if (!((GpuResource)this).IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        int channelCount = GetChannelCount();
        float[] data = new float[width * height * channelCount];

        // Create temporary FBO for readback
        using var tempFbo = DynamicGpuFramebuffer.CreateEmpty("VGE_DynamicTexture2D_Readback_FBO");
        tempFbo.Bind();
        AttachForReadback(tempFbo, 0);

        // Explicitly select the attachment as the read source.
        // Some drivers/context states may otherwise read from an undefined buffer.
        GL.ReadBuffer(ReadbackBuffer);

        // Read pixels
        GL.ReadPixels(0, 0, width, height,
            ReadbackFormat,
            ReadbackType,
            data);

        // Cleanup
        GpuFramebuffer.Unbind();

        return data;
    }

    /// <summary>
    /// Reads pixel data from a sub-region of the texture.
    /// Creates a temporary FBO for readback (best-effort; avoid calling frequently at runtime).
    /// </summary>
    public override float[] ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight)
    {
        if (!((GpuResource)this).IsValid)
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

        using var tempFbo = DynamicGpuFramebuffer.CreateEmpty("VGE_DynamicTexture2D_Readback_FBO");
        tempFbo.Bind();
        AttachForReadback(tempFbo, 0);

        GL.ReadBuffer(ReadbackBuffer);
        GL.ReadPixels(
            x,
            y,
            regionWidth,
            regionHeight,
            ReadbackFormat,
            ReadbackType,
            data);

        GpuFramebuffer.Unbind();

        return data;
    }

    /// <summary>
    /// Reads pixel data from a specific mip level of the texture.
    /// </summary>
    public override float[] ReadPixels(int mipLevel)
    {
        if (!((GpuResource)this).IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to read pixels from disposed or invalid texture");
            return [];
        }

        mipLevel = Math.Clamp(mipLevel, 0, Math.Max(0, StorageMipLevels - 1));

        int mipWidth = Math.Max(1, width >> mipLevel);
        int mipHeight = Math.Max(1, height >> mipLevel);
        int channelCount = GetChannelCount();
        float[] data = new float[mipWidth * mipHeight * channelCount];

        using var tempFbo = DynamicGpuFramebuffer.CreateEmpty("VGE_DynamicTexture2D_Readback_FBO");
        tempFbo.Bind();
        AttachForReadback(tempFbo, mipLevel);

        GL.ReadBuffer(ReadbackBuffer);
        GL.ReadPixels(0, 0, mipWidth, mipHeight,
            ReadbackFormat,
            ReadbackType,
            data);

        GpuFramebuffer.Unbind();

        return data;
    }

    #endregion

    #region Protected API

    /// <summary>Attaches this texture to a temporary readback framebuffer.</summary>
    protected virtual void AttachForReadback(GpuFramebuffer framebuffer, int mipLevel) =>
        framebuffer.Attach(textureId, attachmentIndex: 0, mipLevel);

    /// <summary>Gets the pixel format used for floating-point readback.</summary>
    protected virtual PixelFormat ReadbackFormat => TextureFormatHelper.GetPixelFormat(internalFormat);

    /// <summary>Gets the pixel type used for floating-point readback.</summary>
    protected virtual PixelType ReadbackType => PixelType.Float;

    /// <summary>Gets the read buffer selected for readback.</summary>
    protected virtual ReadBufferMode ReadbackBuffer => ReadBufferMode.ColorAttachment0;

    #endregion

    #region Private Methods


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
