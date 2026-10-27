using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Owns an OpenGL continuous 3D volume or an array of discrete two-dimensional layers.
/// Optional immutable allocation supports texture views sharing restricted layer ranges.
/// </summary>
/// <remarks>
/// For octahedral radiance cache, use Texture2DArray to avoid interpolation between probes.
/// For volumetric data where 3D filtering is desired, use Texture3D.
/// 
/// Usage:
/// <code>
/// using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), Depth = System.Math.Max(1, probeCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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

    internal DynamicTexture3D() { }

    #endregion

    #region Factory Methods





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
        RequireMutableStorage();
        if (newWidth == width && newHeight == height && newDepth == depth)
            return false;

        AllocateOrReallocate3DTexture(Math.Max(1, newWidth), Math.Max(1, newHeight), Math.Max(1, newDepth),
            internalFormat, textureTarget, ImmutableLayeredStorage);

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
        ObjectDisposedException.ThrowIf(!IsValid, this);
        if (r == 0 && g == 0 && b == 0 && a == 0) { ClearToZero(); return; }
        Span<float> values = stackalloc float[4] { r, g, b, a };
        var format = TextureFormatHelper.GetPixelFormat(internalFormat);
        var encoding = RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(format, PixelType.Float);
        int count = GetChannelCount();
        if (format is PixelFormat.RedInteger or PixelFormat.RgInteger or PixelFormat.RgbInteger or PixelFormat.RgbaInteger)
            throw new InvalidOperationException("Nonzero integer clears require an explicitly encoded integer clear value.");
        RHICommandContext.Current.ClearTexture(this, new(new(), 0, 0, 0, width, height, depth), encoding, System.Runtime.InteropServices.MemoryMarshal.AsBytes(values[..count]));
    }

    internal void EnqueueUploadData(float[] data, int x, int y, int z, int regionWidth, int regionHeight, int regionDepth, int priority = 0, int mipLevel = 0)
    {
        if (!((GpuResource)this).IsValid)
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
            unpackAlignment: 4, textureOwner: this);

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
