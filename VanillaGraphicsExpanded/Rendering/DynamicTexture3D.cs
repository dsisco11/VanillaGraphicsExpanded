using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Encapsulates an OpenGL 3D texture or 2D texture array with lifecycle management.
/// Used for LumOn octahedral radiance cache storage.
/// Dimensions: (width, height, depth/layers) where each layer is one probe's octahedral map.
/// </summary>
/// <remarks>
/// For octahedral radiance cache, use Texture2DArray to avoid interpolation between probes.
/// For volumetric data where 3D filtering is desired, use Texture3D.
/// 
/// Usage:
/// <code>
/// using var texture = DynamicTexture3D.Create(8, 8, probeCount, PixelInternalFormat.Rgba16f, textureType: Texture3DType.Texture2DArray);
/// texture.Bind(0); // Bind to texture unit 0
/// // ... use texture ...
/// </code>
/// </remarks>
public sealed class DynamicTexture3D : GpuTexture
{
    #region Constants

    /// <summary>
    /// Octahedral map resolution (8×8 = 64 directions per probe).
    /// </summary>
    public const int OctahedralSize = 8;

    #endregion

    #region Fields

    // All common texture state lives in GpuTexture.

    #endregion

    #region Properties

    // Public properties are inherited from GpuTexture.

    #endregion

    #region Constructor (private - use factory methods)

    private DynamicTexture3D() { }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a new 3D texture or 2D texture array with the specified parameters.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="depth">Texture depth (slices for 3D, layers for array).</param>
    /// <param name="format">Internal pixel format (e.g., Rgba16f).</param>
    /// <param name="filter">Filtering mode for sampling. Default is Linear.</param>
    /// <param name="textureTarget">OpenGL texture target. Default is Texture2DArray (recommended for probes).</param>
    /// <returns>A new DynamicTexture3D instance.</returns>
    public static DynamicTexture3D Create(
        int width,
        int height,
        int depth,
        PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Linear,
        TextureTarget textureTarget = TextureTarget.Texture2DArray)
    {
        if (width <= 0)
        {
            Debug.WriteLine($"[DynamicTexture3D] Invalid width {width}, defaulting to 1");
            width = 1;
        }
        if (height <= 0)
        {
            Debug.WriteLine($"[DynamicTexture3D] Invalid height {height}, defaulting to 1");
            height = 1;
        }
        if (depth <= 0)
        {
            Debug.WriteLine($"[DynamicTexture3D] Invalid depth {depth}, defaulting to 1");
            depth = 1;
        }

        var texture = new DynamicTexture3D
        {
            width = width,
            height = height,
            depth = depth,
            internalFormat = format,
            filterMode = filter,
            textureTarget = textureTarget
        };

        texture.AllocateOrReallocate3DTexture();

        var typeStr = textureTarget == TextureTarget.Texture2DArray ? "2D array" : "3D";
        Debug.WriteLine($"[DynamicTexture3D] Allocated {width}x{height}x{depth} {typeStr} {format}");
        return texture;
    }

    /// <summary>
    /// Creates a texture sized for LumOn octahedral radiance cache.
    /// Uses 8×8 octahedral resolution per probe.
    /// </summary>
    /// <param name="probeCount">Total number of probes (probeCountX × probeCountY).</param>
    /// <param name="format">Internal pixel format. Default is Rgba16f.</param>
    /// <param name="filter">Filtering mode. Default is Linear for smooth direction sampling.</param>
    /// <param name="textureTarget">OpenGL texture target. Default is Texture2DArray (recommended for probes).</param>
    /// <returns>A new DynamicTexture3D instance sized for octahedral cache.</returns>
    public static DynamicTexture3D CreateOctahedralCache(
        int probeCount,
        PixelInternalFormat format = PixelInternalFormat.Rgba16f,
        TextureFilterMode filter = TextureFilterMode.Linear,
        TextureTarget textureTarget = TextureTarget.Texture2DArray)
    {
        return Create(OctahedralSize, OctahedralSize, probeCount, format, filter, textureTarget);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Binds this texture to the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit index (0-15 typically).</param>
    public override void Bind(int unit)
    {
        base.Bind(unit);
    }

    /// <summary>
    /// Unbinds this texture from the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit index.</param>
    public override void Unbind(int unit)
    {
        base.Unbind(unit);
    }

    /// <summary>
    /// Resizes the texture if dimensions have changed.
    /// </summary>
    /// <param name="newWidth">New width.</param>
    /// <param name="newHeight">New height.</param>
    /// <param name="newDepth">New depth/layers.</param>
    /// <returns>True if texture was reallocated, false if dimensions unchanged.</returns>
    public bool Resize(int newWidth, int newHeight, int newDepth)
    {
        if (newWidth == width && newHeight == height && newDepth == depth)
            return false;

        width = Math.Max(1, newWidth);
        height = Math.Max(1, newHeight);
        depth = Math.Max(1, newDepth);

        AllocateOrReallocate3DTexture();

        var typeStr = textureTarget == TextureTarget.Texture2DArray ? "2D array" : "3D";
        Debug.WriteLine($"[DynamicTexture3D] Allocated {width}x{height}x{depth} {typeStr} {internalFormat}");
        return true;
    }

    /// <summary>
    /// Clears the texture to the specified color.
    /// </summary>
    /// <param name="r">Red component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="b">Blue component.</param>
    /// <param name="a">Alpha component.</param>
    public void Clear(float r = 0f, float g = 0f, float b = 0f, float a = 0f)
    {
        ClearImmediate(r, g, b, a);
    }

    public override void UploadData(float[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth)
    {
        UploadDataImmediate(data, x, y, z, regionWidth, regionHeight, regionDepth);
    }

    public void ClearImmediate(float r = 0f, float g = 0f, float b = 0f, float a = 0f)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture3D] Attempted to clear disposed or invalid texture");
            return;
        }

        var clearData = new float[width * height * depth * 4];
        for (int i = 0; i < clearData.Length; i += 4)
        {
            clearData[i] = r;
            clearData[i + 1] = g;
            clearData[i + 2] = b;
            clearData[i + 3] = a;
        }

        GL.BindTexture(textureTarget, textureId);
        GL.TexSubImage3D(
            textureTarget,
            0, 0, 0, 0,
            width, height, depth,
            PixelFormat.Rgba,
            PixelType.Float,
            clearData);
        GL.BindTexture(textureTarget, 0);
    }

    internal void EnqueueUploadData(float[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int priority = 0, int mipLevel = 0)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture3D] Attempted to enqueue upload for disposed or invalid texture");
            return;
        }

        if (data is null) throw new ArgumentNullException(nameof(data));

        int mipWidth = Math.Max(1, width >> mipLevel);
        int mipHeight = Math.Max(1, height >> mipLevel);
        int mipDepth = textureTarget == TextureTarget.Texture3D ? Math.Max(1, depth >> mipLevel) : depth;

        if (x < 0 || y < 0 || z < 0 ||
            x + regionWidth > mipWidth ||
            y + regionHeight > mipHeight ||
            z + regionDepth > mipDepth)
        {
            throw new ArgumentOutOfRangeException(
                $"Region ({x}, {y}, {z}, {regionWidth}, {regionHeight}, {regionDepth}) exceeds bounds ({mipWidth}x{mipHeight}x{mipDepth}) at mip {mipLevel}.");
        }

        PixelFormat pixelFormat = TextureFormatHelper.GetPixelFormat(internalFormat);
        int bytesPerPixel = TextureStreamingUtils.GetBytesPerPixel(pixelFormat, PixelType.Float);
        if (bytesPerPixel <= 0 || (bytesPerPixel % sizeof(float)) != 0)
        {
            throw new InvalidOperationException($"Unsupported format/type combination for float upload: {pixelFormat}/{PixelType.Float}.");
        }

        int channelCount = bytesPerPixel / sizeof(float);
        int expectedSize = checked(regionWidth * regionHeight * regionDepth * channelCount);

        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Data array size {data.Length} doesn't match expected size {expectedSize} " +
                $"({regionWidth}x{regionHeight}x{regionDepth}x{channelCount} channels)",
                nameof(data));
        }

        TextureUploadTarget target = GpuTexture.MapUploadTarget(textureTarget);
        TextureUploadPriority uploadPriority = GpuTexture.MapUploadPriority(priority);

        TextureStageResult result = TextureStreamingSystem.StageCopy(
            textureId,
            target,
            new TextureUploadRegion(x, y, z, regionWidth, regionHeight, regionDepth, mipLevel),
            pixelFormat,
            PixelType.Float,
            data,
            uploadPriority,
            unpackAlignment: 4);

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            Debug.WriteLine($"[DynamicTexture3D] EnqueueUploadData staged rejected: {result.RejectReason}");
        }
    }

    #endregion

    #region Private Methods

    // Allocation handled by GpuTexture.

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases GPU resources.
    /// </summary>
    // Uses base GpuTexture.Dispose() / GpuResource.Dispose().

    #endregion

    #region Implicit Conversion

    /// <summary>
    /// Allows using DynamicTexture3D directly where an int texture ID is expected.
    /// </summary>
    public static implicit operator int(DynamicTexture3D texture)
    {
        return texture?.TextureId ?? 0;
    }

    #endregion
}
