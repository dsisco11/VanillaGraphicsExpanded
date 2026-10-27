using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes manager-authorized external framebuffer observation.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public bool IsFramebufferResource(nint id) => new OpenGLFramebufferQueries(this).IsResource(id);
    /// <inheritdoc />
    public bool ReadFramebufferMetadata(nint id, out GpuFramebufferMetadata metadata) => new OpenGLFramebufferQueries(this).TryRead(id, out metadata);
    /// <inheritdoc />
    public bool ReadFramebufferField(nint id, GpuFramebufferMetadata previous, FramebufferAttachment? slot, FramebufferDefaultParameter? parameter, out GpuFramebufferMetadata metadata) => new OpenGLFramebufferQueries(this).TryReadAffected(id, previous, slot, parameter, out metadata);
    #endregion
}
