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
public sealed class DynamicTexture3D : IDisposable
{
    #region Constants

    /// <summary>
    /// Octahedral map resolution (8×8 = 64 directions per probe).
    /// </summary>
    public const int OctahedralSize = 8;

    #endregion

    #region Fields

    private int textureId;
    private int width;
    private int height;
    private int depth;
    private PixelInternalFormat internalFormat;
    private TextureFilterMode filterMode;
    private TextureTarget textureTarget;
    private bool isDisposed;

    #endregion

    #region Properties

    /// <summary>
    /// OpenGL texture ID. Returns 0 if disposed or not created.
    /// </summary>
    public int TextureId => textureId;

    /// <summary>
    /// Texture width in pixels.
    /// </summary>
    public int Width => width;

    /// <summary>
    /// Texture height in pixels.
    /// </summary>
    public int Height => height;

    /// <summary>
    /// Texture depth (number of slices/layers).
    /// </summary>
    public int Depth => depth;

    /// <summary>
    /// Internal pixel format of the texture.
    /// </summary>
    public PixelInternalFormat InternalFormat => internalFormat;

    /// <summary>
    /// Filtering mode used for sampling.
    /// </summary>
    public TextureFilterMode FilterMode => filterMode;

    /// <summary>
    /// The OpenGL texture target (Texture3D or Texture2DArray).
    /// </summary>
    public TextureTarget TextureTarget => textureTarget;

    /// <summary>
    /// Whether this texture has been disposed.
    /// </summary>
    public bool IsDisposed => isDisposed;

    /// <summary>
    /// Whether this is a valid, allocated texture.
    /// </summary>
    public bool IsValid => textureId != 0 && !isDisposed;

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

        texture.AllocateGpu();
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
    public void Bind(int unit)
    {
        if (isDisposed || textureId == 0)
        {
            Debug.WriteLine($"[DynamicTexture3D] Attempted to bind disposed or invalid texture");
            return;
        }

        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(textureTarget, textureId);
    }

    /// <summary>
    /// Unbinds this texture from the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit index.</param>
    public void Unbind(int unit)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(textureTarget, 0);
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

        AllocateGpu();
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
        if (isDisposed || textureId == 0)
            return;

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

    #endregion

    #region Private Methods

    private void AllocateGpu()
    {
        // Delete existing texture if any
        if (textureId != 0)
        {
            GL.DeleteTexture(textureId);
            textureId = 0;
        }

        // Generate new texture
        textureId = GL.GenTexture();
        GL.BindTexture(textureTarget, textureId);

        // Allocate storage
        GL.TexImage3D(
            textureTarget,
            0,  // mipmap level
            internalFormat,
            width,
            height,
            depth,
            0,  // border (must be 0)
            TextureFormatHelper.GetPixelFormat(internalFormat),
            TextureFormatHelper.GetPixelType(internalFormat),
            IntPtr.Zero);  // no initial data

        // Set filtering
        var glFilter = filterMode == TextureFilterMode.Linear
            ? TextureMinFilter.Linear
            : TextureMinFilter.Nearest;
        var glMagFilter = filterMode == TextureFilterMode.Linear
            ? TextureMagFilter.Linear
            : TextureMagFilter.Nearest;

        GL.TexParameter(textureTarget, TextureParameterName.TextureMinFilter, (int)glFilter);
        GL.TexParameter(textureTarget, TextureParameterName.TextureMagFilter, (int)glMagFilter);

        // Clamp to edge
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        if (textureTarget == TextureTarget.Texture3D)
        {
            GL.TexParameter(textureTarget, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        }

        GL.BindTexture(textureTarget, 0);

        var typeStr = textureTarget == TextureTarget.Texture2DArray ? "2D array" : "3D";
        Debug.WriteLine($"[DynamicTexture3D] Allocated {width}x{height}x{depth} {typeStr} {internalFormat}");
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases GPU resources.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
            return;

        if (textureId != 0)
        {
            GL.DeleteTexture(textureId);
            textureId = 0;
        }

        isDisposed = true;
    }

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
