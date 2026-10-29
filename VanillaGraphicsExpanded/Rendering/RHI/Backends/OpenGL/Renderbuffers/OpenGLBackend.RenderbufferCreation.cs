using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates a completed existing owner through manager identity and snapshot handoff.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuRenderbuffer> CreateRenderbuffer(in RHIRenderbufferDescription description)
    {
        var request = description;
        RequireContext();
        var initialization = LowerRenderbuffer(request);
        GpuResourceManagerSystem.EnsureCurrentContext();
        var owner = new GpuRenderbuffer(0, true, request.Label);
        try
        {
            if (!GpuResourceManager.CreateRenderbuffer(initialization, owner, out _).HasValue)
                return new(null, "Renderbuffer initialization did not complete.");
            owner.SetDebugName(request.Label);
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
}
