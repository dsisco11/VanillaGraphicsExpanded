using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Preferred name for <see cref="GpuBufferView"/>: a texture buffer (<c>GL_TEXTURE_BUFFER</c>) that exposes a buffer as texels.
/// </summary>
public sealed class GpuBufferTexture : GpuBufferView
{
    /// <summary>Declares an unpublished buffer texture for manager initialization.</summary>
    private GpuBufferTexture() { }

    /// <summary>
    /// Creates a new buffer texture that exposes the entire buffer as a <c>GL_TEXTURE_BUFFER</c>.
    /// </summary>
    public static new GpuBufferTexture CreateWholeBuffer(
        int bufferId,
        SizedInternalFormat format,
        string? debugName = null)
    {
        if (bufferId == 0)
        {
            throw new ArgumentException("Buffer id must be non-zero.", nameof(bufferId));
        }

        var view = new GpuBufferTexture();
        view.InitializeBufferView(bufferId, format, 0, 0, debugName);
        return view;
    }

    /// <summary>
    /// Creates a new buffer texture that exposes a subrange of a buffer as a <c>GL_TEXTURE_BUFFER</c>.
    /// </summary>
    /// <remarks>
    /// Requires <c>GL_ARB_texture_buffer_range</c> (or core GL 4.3+).
    /// </remarks>
    public static new GpuBufferTexture CreateRange(
        int bufferId,
        SizedInternalFormat format,
        nint offsetBytes,
        nint sizeBytes,
        string? debugName = null)
    {
        if (bufferId == 0)
        {
            throw new ArgumentException("Buffer id must be non-zero.", nameof(bufferId));
        }

        if (offsetBytes < 0 || sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), "Offset must be >= 0 and size must be > 0.");
        }

        if (!GpuSupport.Graphics.SupportsTextureBufferRange)
        {
            throw new NotSupportedException("Texture buffer ranges require GL_ARB_texture_buffer_range.");
        }

        var view = new GpuBufferTexture();
        view.InitializeBufferView(bufferId, format, offsetBytes, sizeBytes, debugName);
        return view;
    }
}
