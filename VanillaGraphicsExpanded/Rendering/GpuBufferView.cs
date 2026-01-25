using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL texture-buffer view (<c>GL_TEXTURE_BUFFER</c>).
/// A buffer view is a texture object that exposes a buffer object's data as texels for sampling in shaders.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
public sealed class GpuBufferView : GpuResource, IDisposable
{
    private int textureId;
    private int bufferId;
    private readonly SizedInternalFormat format;
    private readonly nint offsetBytes;
    private readonly nint sizeBytes;

    protected override nint ResourceId
    {
        get => textureId;
        set => textureId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;

    /// <summary>
    /// Gets the OpenGL texture id backing this view.
    /// </summary>
    public int TextureId => textureId;

    /// <summary>
    /// Gets the OpenGL buffer id currently referenced by this view.
    /// </summary>
    public int BufferId => bufferId;

    /// <summary>
    /// Gets the sized internal format used to interpret buffer texels.
    /// </summary>
    public SizedInternalFormat Format => format;

    /// <summary>
    /// Gets the byte offset into <see cref="BufferId"/> for this view.
    /// </summary>
    public nint OffsetBytes => offsetBytes;

    /// <summary>
    /// Gets the byte size of the view. When created with whole-buffer binding, this may be 0.
    /// </summary>
    public nint SizeBytes => sizeBytes;

    /// <summary>
    /// Returns <c>true</c> when the view has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => textureId != 0 && !IsDisposed;

    private GpuBufferView(int textureId, int bufferId, SizedInternalFormat format, nint offsetBytes, nint sizeBytes)
    {
        this.textureId = textureId;
        this.bufferId = bufferId;
        this.format = format;
        this.offsetBytes = offsetBytes;
        this.sizeBytes = sizeBytes;
    }

    /// <summary>
    /// Sets the debug label for this buffer view (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (textureId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
        }
#endif
    }

    /// <summary>
    /// Creates a new buffer view that exposes the entire buffer as a <c>GL_TEXTURE_BUFFER</c>.
    /// </summary>
    public static GpuBufferView CreateWholeBuffer(
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
            // DSA path.
            GL.TextureBuffer(id, format, bufferId);

            var view = new GpuBufferView(id, bufferId, format, offsetBytes: 0, sizeBytes: 0);
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
    /// Creates a new buffer view that exposes a subrange of a buffer as a <c>GL_TEXTURE_BUFFER</c>.
    /// </summary>
    /// <remarks>
    /// Requires <c>GL_ARB_texture_buffer_range</c> (or core GL 4.3+).
    /// </remarks>
    public static GpuBufferView CreateRange(
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

        // Best-effort capability check (some drivers expose this in core without the extension string).
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
            // DSA path.
            GL.TextureBufferRange(id, format, bufferId, (IntPtr)offsetBytes, (IntPtr)sizeBytes);

            var view = new GpuBufferView(id, bufferId, format, offsetBytes, sizeBytes);
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
    /// Binds this view to a texture unit for sampling.
    /// </summary>
    public void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferView] Attempted to bind disposed or invalid view");
            return;
        }

        try
        {
            GL.BindTextureUnit(unit, textureId);
        }
        catch
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            GL.BindTexture(TextureTarget.TextureBuffer, textureId);
        }
    }
}

