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
    private int mipLevels = 1;
    private PixelInternalFormat internalFormat;
    private TextureFilterMode filterMode;
    private string? debugName;
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
    /// Number of mip levels allocated for this texture (>= 1).
    /// </summary>
    public int MipLevels => mipLevels;

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

    public string? DebugName => debugName;

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

        var texture = new DynamicTexture
        {
            width = width,
            height = height,
            mipLevels = 1,
            internalFormat = format,
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
    public static DynamicTexture CreateMipmapped(
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

        var texture = new DynamicTexture
        {
            width = width,
            height = height,
            mipLevels = mipLevels,
            internalFormat = format,
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
    public static DynamicTexture CreateDepth(
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
    public static DynamicTexture CreateWithData(
        int width,
        int height,
        PixelInternalFormat format,
        float[] data,
        TextureFilterMode filter = TextureFilterMode.Nearest,
        string? debugName = null)
    {
        var texture = Create(width, height, format, filter, debugName);
        texture.UploadData(data);
        return texture;
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

        if (mipLevels <= 1)
        {
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
        }
        else
        {
            for (int level = 0; level < mipLevels; level++)
            {
                int lw = Math.Max(1, width >> level);
                int lh = Math.Max(1, height >> level);
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    level,
                    internalFormat,
                    lw,
                    lh,
                    0,
                    TextureFormatHelper.GetPixelFormat(internalFormat),
                    TextureFormatHelper.GetPixelType(internalFormat),
                    IntPtr.Zero);
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mipLevels - 1);
        }

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

    /// <summary>
    /// Uploads pixel data to the texture, replacing existing contents.
    /// Data array must match texture dimensions and format.
    /// </summary>
    /// <param name="data">Float array containing pixel data (RGBA order for RGBA formats).</param>
    /// <exception cref="ArgumentException">Thrown if data array size doesn't match texture dimensions.</exception>
    public void UploadData(float[] data)
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
    /// Uploads pixel data to a sub-region of the texture.
    /// </summary>
    /// <param name="data">Float array containing pixel data for the sub-region.</param>
    /// <param name="x">X offset in pixels.</param>
    /// <param name="y">Y offset in pixels.</param>
    /// <param name="regionWidth">Width of the sub-region in pixels.</param>
    /// <param name="regionHeight">Height of the sub-region in pixels.</param>
    /// <exception cref="ArgumentException">Thrown if data array size doesn't match region dimensions.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if region extends beyond texture bounds.</exception>
    public void UploadData(float[] data, int x, int y, int regionWidth, int regionHeight)
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

    /// <summary>
    /// Reads pixel data from the texture.
    /// Requires the texture to be bound to an FBO for readback.
    /// </summary>
    /// <returns>Float array containing pixel data (RGBA order for RGBA formats).</returns>
    /// <remarks>
    /// This method binds the texture to a temporary FBO for readback.
    /// For frequent readback operations, consider using a persistent FBO.
    /// </remarks>
    public float[] ReadPixels()
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
    /// Reads pixel data from a specific mip level of the texture.
    /// </summary>
    public float[] ReadPixels(int mipLevel)
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
    private int GetChannelCount()
    {
        return internalFormat switch
        {
            PixelInternalFormat.R16f or PixelInternalFormat.R32f => 1,
            PixelInternalFormat.Rg16f or PixelInternalFormat.Rg32f => 2,
            PixelInternalFormat.Rgb16f or PixelInternalFormat.Rgb32f or
            PixelInternalFormat.Rgb8 or PixelInternalFormat.Rgb => 3,
            _ => 4 // RGBA formats and default
        };
    }

    #endregion

    #region Private Methods

    private void AllocateTexture()
    {
        textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

#if DEBUG
        GlDebug.TryLabelTexture2D(textureId, debugName);
#endif

        if (mipLevels <= 1)
        {
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
        }
        else
        {
            // Allocate all mip levels explicitly
            for (int level = 0; level < mipLevels; level++)
            {
                int lw = Math.Max(1, width >> level);
                int lh = Math.Max(1, height >> level);
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    level,
                    internalFormat,
                    lw,
                    lh,
                    0,
                    TextureFormatHelper.GetPixelFormat(internalFormat),
                    TextureFormatHelper.GetPixelType(internalFormat),
                    IntPtr.Zero);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mipLevels - 1);

            // Mipmapped sampling is explicit via texelFetch/textureLod.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapNearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }

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
