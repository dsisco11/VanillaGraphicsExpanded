using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
    public new bool IsValid => GpuResourceManager.TryGetTextureReferenceMetadata(this, out _);

    /// <summary>Creates a consumer-only value without transferring backing ownership.</summary>
    public static implicit operator GpuTextureRef(GpuBufferView? value) => new(value);

    /// <summary>Declares an unpublished view before manager-owned creation.</summary>
    protected GpuBufferView() { }


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
    /// Binds this view to a texture unit for sampling.
    /// </summary>
    public void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferView] Attempted to bind disposed or invalid view");
            return;
        }

        OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.TextureBuffer, unit, textureId);
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

        OpenGLImageSlotBindings.BindImageTexture(StateCache.Current, unit, textureId, level: 0, layered: false, layer: 0, access: access, format: format);
    }

}
