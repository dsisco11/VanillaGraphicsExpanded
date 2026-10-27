using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Encapsulates a color OpenGL 2D texture with lifecycle management.
/// Handles creation, binding, resizing, and disposal of GPU texture resources.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// using var texture = DynamicTexture.Create(1920, 1080, TextureFormat.Rgba16f);
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
    protected DynamicTexture2D(int width, int height, TextureFormat format, TextureFilterMode filter, string? debugName)
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        if (pixelType != PixelComponentType.UnsignedShort)
        {
            throw new InvalidOperationException($"UploadDataImmediate(ushort[]) requires {nameof(PixelComponentType)}.{nameof(PixelComponentType.UnsignedShort)}, but format is {internalFormat} -> {pixelType}.");
        }

        int expectedSize = width * height * GetChannelCount();
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({width}×{height}×{GetChannelCount()} channels)",
                nameof(data));
        }
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), 0, 0, 0, width, height, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.UnsignedShort), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
        RHICommandContext.Current.UploadTexture(this, new RHITextureRegion(new(0), x, y, 0, regionWidth, regionHeight, 1), RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()));
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
            RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float).Channels,
            PixelComponentType.Float,
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
        if (pixelType != PixelComponentType.UnsignedShort)
        {
            throw new InvalidOperationException($"EnqueueUploadData(ushort[]) requires {nameof(PixelComponentType)}.{nameof(PixelComponentType.UnsignedShort)}, but format is {internalFormat} -> {pixelType}.");
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
            RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float).Channels,
            PixelComponentType.UnsignedShort,
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
    /// Preserves surrounding framebuffer and pixel-pack selectors.
    /// </summary>
    /// <returns>Float array containing pixel data (RGBA order for RGBA formats).</returns>
    public override float[] ReadPixels()
    {
        if (!((GpuResource)this).IsValid) return [];
        // The request owns staging independently; only completed bytes are exposed to CPU readers.
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(0), 0, 0, 0, width, height, 1), ReadbackEncoding));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Texture readback did not complete.", request.Error);
        using var mapping = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    /// <summary>
    /// Reads pixel data from a sub-region of the texture.
    /// Uses an explicit selected-region readback request.
    /// </summary>
    public override float[] ReadPixelsRegion(int x, int y, int regionWidth, int regionHeight)
    {
        if (!((GpuResource)this).IsValid) return [];
        // The request owns staging independently; only completed bytes are exposed to CPU readers.
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(0), x, y, 0, regionWidth, regionHeight, 1), ReadbackEncoding));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Texture readback did not complete.", request.Error);
        using var mapping = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    /// <summary>
    /// Reads pixel data from a specific mip level of the texture.
    /// </summary>
    public override float[] ReadPixels(int mipLevel)
    {
        if (!((GpuResource)this).IsValid) return [];
        mipLevel = Math.Clamp(mipLevel, 0, Math.Max(0, StorageMipLevels - 1));
        // The request owns staging independently; only completed bytes are exposed to CPU readers.
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(this,
            new(new(mipLevel), 0, 0, 0, Math.Max(1, width >> mipLevel), Math.Max(1, height >> mipLevel), 1), ReadbackEncoding));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Texture readback did not complete.", request.Error);
        using var mapping = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    #endregion

    #region Protected API

/// <summary>Gets the pixel format used for floating-point readback.</summary>
    protected virtual RHIPixelEncoding ReadbackEncoding => RHICommandContext.Current.GetTextureEncoding(this, PixelComponentType.Float);

    /// <summary>Gets the pixel type used for floating-point readback.</summary>
    protected virtual PixelComponentType ReadbackType => PixelComponentType.Float;

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
