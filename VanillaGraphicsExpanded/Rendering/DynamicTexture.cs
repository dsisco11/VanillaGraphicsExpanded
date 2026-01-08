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
public sealed class DynamicTexture : IDisposable
{
    #region Fields

    private int textureId;
    private int width;
    private int height;
    private PixelInternalFormat internalFormat;
    private TextureFilterMode filterMode;
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
    /// Internal pixel format of the texture.
    /// </summary>
    public PixelInternalFormat InternalFormat => internalFormat;

    /// <summary>
    /// Filtering mode used for sampling.
    /// </summary>
    public TextureFilterMode FilterMode => filterMode;

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

    private DynamicTexture() { }

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
    public static DynamicTexture Create(
        int width,
        int height,
        PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest)
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

        var texture = new DynamicTexture
        {
            width = width,
            height = height,
            internalFormat = format,
            filterMode = filter
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
    public static DynamicTexture CreateDepth(
        int width,
        int height,
        PixelInternalFormat format = PixelInternalFormat.DepthComponent24)
    {
        if (!TextureFormatHelper.IsDepthFormat(format))
        {
            Debug.WriteLine($"[DynamicTexture] Format {format} is not a depth format, defaulting to DepthComponent24");
            format = PixelInternalFormat.DepthComponent24;
        }

        return Create(width, height, format, TextureFilterMode.Nearest);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Binds this texture to the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit (0-31 typically).</param>
    public void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[DynamicTexture] Attempted to bind disposed or invalid texture");
            return;
        }
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
    }

    /// <summary>
    /// Unbinds any texture from the specified texture unit.
    /// </summary>
    /// <param name="unit">Texture unit to unbind.</param>
    public static void Unbind(int unit)
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

        // Reallocate with new dimensions
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            width,
            height,
            0,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            TextureFormatHelper.GetPixelType(internalFormat),
            IntPtr.Zero);
        GL.BindTexture(TextureTarget.Texture2D, 0);

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

    #endregion

    #region Private Methods

    private void AllocateTexture()
    {
        textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        // Allocate storage
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            width,
            height,
            0,
            TextureFormatHelper.GetPixelFormat(internalFormat),
            TextureFormatHelper.GetPixelType(internalFormat),
            IntPtr.Zero);

        // Set filtering parameters
        int filterParam = TextureFormatHelper.GetFilterParameter(filterMode);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filterParam);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filterParam);

        // Set wrap mode to clamp (standard for render targets)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases the GPU texture resource.
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
    /// Allows implicit conversion to int for use with existing APIs expecting texture IDs.
    /// </summary>
    public static implicit operator int(DynamicTexture texture)
    {
        return texture?.textureId ?? 0;
    }

    #endregion
}
