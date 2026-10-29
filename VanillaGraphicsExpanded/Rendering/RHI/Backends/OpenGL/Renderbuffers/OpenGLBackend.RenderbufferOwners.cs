using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Completes existing renderbuffer owner declarations through manager-controlled allocation.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void AllocateRenderbufferOwner(GpuRenderbuffer owner, in GpuRenderbufferInitialization request)
    {
        RequireContext();
        if (!RHIResourceManager.CreateRenderbuffer(request, owner, out _).HasValue)
            throw new InvalidOperationException("Renderbuffer initialization failed.");
    }
    #endregion
}
