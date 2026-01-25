using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Preferred name for <see cref="GpuBufferView"/>: a texture buffer (<c>GL_TEXTURE_BUFFER</c>) that exposes a buffer as texels.
/// </summary>
public sealed class GpuBufferTexture : GpuBufferView
{
    private GpuBufferTexture(int textureId, int bufferId, SizedInternalFormat format, nint offsetBytes, nint sizeBytes)
        : base(textureId, bufferId, format, offsetBytes, sizeBytes)
    {
    }

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

        int id = GL.GenTexture();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenTextures failed.");
        }

        try
        {
            using var _ = GlStateCache.Current.BindTextureScope(TextureTarget.TextureBuffer, unit: 0, id);

            // Prefer DSA when available, otherwise fall back to bind-to-edit.
            try
            {
                GL.TextureBuffer(id, format, bufferId);
            }
            catch
            {
                GL.TexBuffer(TextureBufferTarget.TextureBuffer, format, bufferId);
            }

            var view = new GpuBufferTexture(id, bufferId, format, offsetBytes: 0, sizeBytes: 0);
            view.SetDebugName(debugName);
            return view;
        }
        catch
        {
            try { GL.DeleteTexture(id); } catch { }
            throw;
        }
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

        if (!GlExtensions.Supports("GL_ARB_texture_buffer_range"))
        {
            throw new NotSupportedException("Texture buffer ranges require GL_ARB_texture_buffer_range.");
        }

        int id = GL.GenTexture();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenTextures failed.");
        }

        try
        {
            using var _ = GlStateCache.Current.BindTextureScope(TextureTarget.TextureBuffer, unit: 0, id);

            // Prefer DSA when available, otherwise fall back to bind-to-edit.
            try
            {
                GL.TextureBufferRange(id, format, bufferId, (IntPtr)offsetBytes, (IntPtr)sizeBytes);
            }
            catch
            {
                int size = checked((int)sizeBytes);
                GL.TexBufferRange(TextureBufferTarget.TextureBuffer, format, bufferId, (IntPtr)offsetBytes, size);
            }

            var view = new GpuBufferTexture(id, bufferId, format, offsetBytes, sizeBytes);
            view.SetDebugName(debugName);
            return view;
        }
        catch
        {
            try { GL.DeleteTexture(id); } catch { }
            throw;
        }
    }
}
