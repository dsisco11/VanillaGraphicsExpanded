using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes manager-authorized external framebuffer observation.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public bool ReadFramebufferMetadata(nint id, out GpuFramebufferMetadata metadata) => new OpenGLFramebufferQueries(this).TryRead(id, out metadata);
    #endregion
}
