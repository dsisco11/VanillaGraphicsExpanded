using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL texture-buffer view (<c>GL_TEXTURE_BUFFER</c>).
/// A buffer view is a texture object that exposes a buffer object's data as texels for sampling in shaders.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
public class GpuBufferView : GpuResource<GpuTextureMetadata>, IDisposable
{
    private int textureId;
    /// <summary>Gets the native buffer-view bufferId from the canonical texture snapshot.</summary>
    private int bufferId => checked((int)(metadata.Buffer?.BufferId ?? 0));
    /// <summary>Gets the native buffer-view format from the canonical texture snapshot.</summary>
    private SizedInternalFormat format => metadata.Buffer?.Format ?? 0;
    /// <summary>Gets the native buffer-view offsetBytes from the canonical texture snapshot.</summary>
    private nint offsetBytes => metadata.Buffer?.Offset ?? 0;
    /// <summary>Gets the native buffer-view sizeBytes from the canonical texture snapshot.</summary>
    private nint sizeBytes => metadata.Buffer is { WholeBuffer: false } data ? data.Size : 0;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => textureId;
        protected set => textureId = (int)value;
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
    /// Reports a live texture association whose tracked backing metadata remains available.
    /// </summary>
    public new bool IsValid => base.IsValid && metadata.Buffer is { BufferId: not 0 }
        && (ResourceManager?.GetResourceRegistration(this) is null || ResourceManager.IsResourceMetadataAvailable(this));

    /// <summary>Configures a view using its known creation path, then receives the accepted native metadata.</summary>
    /// <param name="textureId">Texture name owned by the view.</param>
    /// <param name="bufferId">Backing buffer owned by the caller.</param>
    /// <param name="format">Sized format used to interpret buffer texels.</param>
    /// <param name="offsetBytes">First byte of a ranged view.</param>
    /// <param name="sizeBytes">Range size, or zero for a whole-buffer view.</param>
    /// <param name="createdWithDsa">Whether typed DSA creation already initialized this texture; generated names use bound initialization.</param>
    protected GpuBufferView(int textureId, int bufferId, SizedInternalFormat format, nint offsetBytes, nint sizeBytes,
        bool createdWithDsa = false)
    {
        this.textureId = textureId;
        // The view retains its DSA/bound fallback strategy; StateCache owns command execution and publication.
        var cache = StateCache.Current;
        // Creation already tells us whether named commands can initialize the association.
        // Generated names supplied by derived wrappers use the bound path without querying GL.
        if (createdWithDsa)
        {
            bool configured = sizeBytes == 0
                ? cache.TryTextureBuffer(this, bufferId, format)
                : cache.TryTextureBufferRange(this, bufferId, format, offsetBytes, sizeBytes);
            if (configured) return;
        }

        using var binding = cache.BindTextureScope(TextureTarget.TextureBuffer, 0, textureId);
        if (sizeBytes == 0)
        {
            cache.TexBuffer(this, bufferId, format);
        }
        else
            cache.TexBufferRange(this, bufferId, format, offsetBytes, sizeBytes);
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

        int id = CreateTextureId(out bool createdWithDsa);

        try
        {
            var view = new GpuBufferView(id, bufferId, format, offsetBytes: 0, sizeBytes: 0, createdWithDsa: createdWithDsa);
            view.SetDebugName(debugName);
            return view;
        }
        catch
        {
            try { StateCache.Current.DeleteTexture(id); } catch { }
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
        if (!GpuSupport.Graphics.SupportsTextureBufferRange)
        {
            throw new NotSupportedException("Texture buffer ranges require GL_ARB_texture_buffer_range.");
        }

        int id = CreateTextureId(out bool createdWithDsa);

        try
        {
            var view = new GpuBufferView(id, bufferId, format, offsetBytes, sizeBytes, createdWithDsa);
            view.SetDebugName(debugName);
            return view;
        }
        catch
        {
            try { StateCache.Current.DeleteTexture(id); } catch { }
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

        StateCache.Current.BindTexture(TextureTarget.TextureBuffer, unit, textureId);
    }

    /// <summary>
    /// Binds this buffer texture to an image unit via <c>glBindImageTexture</c>.
    /// </summary>
    /// <remarks>
    /// Buffer textures always bind with <c>level=0</c>, <c>layered=false</c>, <c>layer=0</c>.
    /// </remarks>
    public void BindImageUnit(int unit, TextureAccess access = TextureAccess.ReadOnly)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferView] Attempted to bind disposed or invalid view as image");
            return;
        }

        if (unit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Image unit must be >= 0.");
        }

        StateCache.Current.BindImageTexture(unit, textureId, level: 0, layered: false, layer: 0, access: access, format: format);
    }

    #region Protected API
    /// <summary>Creates a texture and reports whether typed DSA creation initialized it, without querying the resulting object.</summary>
    protected static int CreateTextureId(out bool createdWithDsa)
    {
        var cache = StateCache.Current;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess
            && cache.TryCreateTexture(TextureTarget.TextureBuffer, out int texture))
        {
            createdWithDsa = true;
            return texture;
        }
        createdWithDsa = false;
        int generated = cache.GenTexture();
        if (generated == 0) throw new InvalidOperationException("Texture object creation failed.");
        return generated;
    }
    #endregion
}
